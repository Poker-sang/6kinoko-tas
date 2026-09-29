using Avalonia.VisualTree; using Avalonia.Interactivity;
using Avalonia; using Avalonia.Controls; using Avalonia.Headless; using Avalonia.Input; using Avalonia.Threading;
using KinokoTAS.Core; using KinokoTAS.App;
internal static class Program {
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Reject(byte[] b,string name){try{Replay.Parse(b);}catch(InvalidDataException){Console.WriteLine("PASS "+name);return;}throw new Exception(name);}
 static byte[] Fixture(int count=180,uint[]? masks=null) {
  using var s=new MemoryStream();using var w=new BinaryWriter(s);
  w.Write("KINORPL1"u8);w.Write(1u);w.Write(19u);w.Write(System.Text.Encoding.ASCII.GetBytes(new string('a',64)));
  ulong chain=14695981039346656037;
  int[] held=new int[19];
  for(int f=0;f<count;f++) {
   using var frame=new MemoryStream();using var fw=new BinaryWriter(frame);fw.Write((ulong)f);
   var previous=(int[])held.Clone();
   for(int a=0;a<19;a++){bool down=masks is null?(a==4&&f<60||a==1):(masks[f]&(1u<<a))!=0;held[a]=down?held[a]+1:0;fw.Write(held[a]);}
   for(int a=0;a<19;a++)fw.Write((byte)(previous[a]>0&&held[a]==0?1:0));
   fw.Write(0);fw.Write(0);for(int a=0;a<6;a++)fw.Write(0);
   fw.Write(new byte[4]);for(int a=0;a<10;a++)fw.Write(a==0?258:0);
   fw.Write((uint)(1000+f*1000/60));fw.Write(42u);fw.Write(42u);fw.Write((ulong)f*17+(masks is null?0:masks[f]));
   var b=frame.ToArray();if(b.Length!=199)throw new Exception("fixture size");
   w.Write((byte)1);w.Write(b);w.Write(Replay.Hash(b));chain=Replay.Hash(b,chain);
  }
  w.Write((byte)0);w.Write((ulong)count);w.Write(chain);return s.ToArray();
 }
 [STAThread] static int Main(string[] args) {
  if(args.Contains("--tas-dir"))return FakeEngine(args);
  try {
   string output=Path.GetFullPath(args.Length>0?args[0]:"artifacts/checks-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
   if(Directory.Exists(output))throw new Exception("Use a fresh output directory");Directory.CreateDirectory(output);
   byte[] data=Fixture();string replayPath=Path.Combine(output,"synthetic.krec");File.WriteAllBytes(replayPath,data);
   var replay=Replay.Parse(data);Check(replay.Count==180 && replay.Held(59,4)==60 && replay.Released(60,4),"wire values and releases");
   Check(replay.Clock(60)==2000 && replay.RandomBefore(60)==42 && replay.Checkpoint(60)==1020,"wire diagnostic offsets");
   var corrupt=(byte[])data.Clone();corrupt[100]^=1;Reject(corrupt,"corrupt frame rejected");Reject(data[..^1],"truncation rejected");Reject([..data,0],"trailing bytes rejected");
   corrupt=(byte[])data.Clone();corrupt[12]=18;Reject(corrupt,"schema mismatch rejected");
   var project=new TasProject(replay,"synthetic.krec");project.SetRange(5,9,4,false);Check(project.EditCount==5 && !project.Down(5,4) && project.InvalidFrom==5,"range edit");
   project.Undo();Check(project.EditCount==0 && project.Down(5,4),"undo batch");project.Redo();Check(project.EditCount==5,"redo batch");
   string path=Path.Combine(output,"edited.ktas");project.Save(path);var loaded=TasProject.Load(path);
   Check(loaded.EditCount==5 && !loaded.Down(9,4) && loaded.Source.Bytes.Span.SequenceEqual(data),"project preserves exact source");
   loaded.ExportSource(Path.Combine(output,"export.krec"));Check(File.ReadAllBytes(Path.Combine(output,"export.krec")).SequenceEqual(data),"export exact original");
   string atomic=Path.Combine(output,"atomic.txt");File.WriteAllText(atomic,"keep");try{AtomicFile.Write(atomic,s=>throw new IOException("injected"));}catch(IOException){}
   Check(File.ReadAllText(atomic)=="keep","failed save preserves old file");
   if(args.Length>1){var real=Replay.Load(args[1]);Check(real.Count>0,"user recording read-only: "+real.Count+" frames");}
   string liveFile=Path.Combine(output,"partial.krec");File.WriteAllBytes(liveFile,data[..(Replay.HeaderSize+Replay.RecordSize+17)]);
   var liveReader=new LiveTimeline();liveReader.Read(liveFile,2);Check(liveReader.Masks.Count==1,"live reader ignores incomplete record");
   File.WriteAllBytes(liveFile,data[..(Replay.HeaderSize+2*Replay.RecordSize)]);liveReader.Read(liveFile,2);Check(liveReader.Masks.Count==2 && (liveReader.Masks[1]&(1u<<4))!=0,"live reader incrementally appends validated input");
   string fakeExe=ProtocolCheck(output,replayPath).GetAwaiter().GetResult();
   var marks=new[]{new FrameBookmark(2,"Boss 前"),new FrameBookmark(10,"重点")};
   var bundle=RecordingLibrary.SaveBundle(output,replay,Path.Combine(output,"initial"),marks);
   Check(Replay.Load(bundle).Bytes.Span.SequenceEqual(data),"saved bundle preserves exact replay");
   Check(File.ReadAllText(Path.Combine(Path.GetDirectoryName(bundle)!,"initial","marisaA.dat"))=="original","saved bundle includes initial saves");
   Check(RecordingLibrary.LoadBookmarks(bundle+".bookmarks.json",replay.Count).SequenceEqual(marks),"named bookmarks survive bundle save and reload");
   string packed=Path.Combine(output,"single.krec");RecordingPackage.Save(packed,replay,Path.Combine(output,"initial"),marks);
   var package=RecordingPackage.Load(packed);
   Check(Replay.Load(packed).Bytes.Span.SequenceEqual(data)&&package.Bookmarks.SequenceEqual(marks),"single-file replay and bookmarks roundtrip");
   var unpack=Path.Combine(output,"unpacked-initial");package.ExtractInitial(unpack);
   Check(File.ReadAllText(Path.Combine(unpack,"marisaA.dat"))=="original","single-file initial saves roundtrip");
   Check(new FileInfo(packed).Length<data.Length,"replay package compression reduces fixture size");
   var badPack=Path.Combine(output,"invalid.krec");File.Copy(packed,badPack);
   using(var zip=System.IO.Compression.ZipFile.Open(badPack,System.IO.Compression.ZipArchiveMode.Update)){zip.CreateEntry("../escape.dat");}
   bool rejected=false;try{RecordingPackage.Load(badPack);}catch(InvalidDataException){rejected=true;}Check(rejected,"unexpected package entry rejected before extraction");
   var broken=Path.Combine(output,"damaged.krec");File.Copy(packed,broken);
   using(var zip=System.IO.Compression.ZipFile.Open(broken,System.IO.Compression.ZipArchiveMode.Update)){zip.GetEntry("initial/marisaA.dat")!.Delete();using var changed=zip.CreateEntry("initial/marisaA.dat").Open();changed.WriteByte(1);}
   rejected=false;try{RecordingPackage.Load(broken);}catch(InvalidDataException){rejected=true;}Check(rejected,"changed packaged save rejected by hash");
   var packedSession=new FileGameSession(fakeExe,Path.Combine(output,"packed-session"),packed,unpack,replay.Identity);
   packedSession.StartAsync().GetAwaiter().GetResult();
   Check(File.ReadAllBytes(Path.Combine(packedSession.SessionDirectory,"source.krec")).SequenceEqual(data),"game receives unpacked legacy wire format");
   packedSession.StopAsync().GetAwaiter().GetResult();
   AppBuilder.Configure<App>().UseSkia().UseHeadless(new(){UseHeadlessDrawing=false}).SetupWithoutStarting();
   var window=new MainWindow();window.Show();Dispatcher.UIThread.RunJobs();
   var confirmation=window.ConfirmContentAsync("未保存的修改","放弃未保存的输入草稿？原始录制不会被修改。","放弃修改","返回编辑");
   Dispatcher.UIThread.RunJobs();
   for(int frame=0;frame<5;frame++){Dispatcher.UIThread.RunJobs();window.UpdateLayout();Thread.Sleep(20);}
   foreach(var c in window.GetVisualDescendants().OfType<KinokoTAS.App.Controls.ContentDialogHost>())Console.WriteLine($"Dialog host: {c.Bounds} visible={c.IsVisible}");
   using(var dialogImage=window.CaptureRenderedFrame()??throw new Exception("No dialog image"))dialogImage.Save(Path.Combine(output,"dialog.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
   Check(!(window.Content as Control)!.IsEnabled,"dialog blocks background controls");
   var primary=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="PART_PrimaryButton");
   primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   var dialogDeadline=DateTime.UtcNow.AddSeconds(5);while(!confirmation.IsCompleted && DateTime.UtcNow<dialogDeadline){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}
   Check(confirmation.IsCompletedSuccessfully && confirmation.Result && (window.Content as Control)!.IsEnabled,"dialog primary result restores background controls");
   var cancellation=window.ConfirmContentAsync("验证录制","出现不同步时停止。","开始回放","取消");Dispatcher.UIThread.RunJobs();
   window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="PART_CloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   dialogDeadline=DateTime.UtcNow.AddSeconds(5);while(!cancellation.IsCompleted && DateTime.UtcNow<dialogDeadline){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}
   Check(cancellation.IsCompletedSuccessfully && !cancellation.Result,"dialog cancel returns false");
   var open=window.OpenPathAsync(replayPath);
   while(!open.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}open.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();
   Check(window.Project?.Source.Count==180,"UI loads source");
   var timeline=window.FindControl<TimelineControl>("Timeline")!;
   var point=timeline.TranslatePoint(new Point(TimelineControl.FrameWidth+5,TimelineControl.HeaderHeight+4*TimelineControl.RowHeight+5),window)!.Value;
   window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);Dispatcher.UIThread.RunJobs();
   Check(window.Project!.IsEdited(0,4) && !window.Project.Down(0,4),"timeline click edits selected action");
   var uiSession=new FileGameSession(fakeExe,Path.Combine(output,"ui-session"),replayPath,Path.Combine(output,"initial"),new string('a',64));
   var connect=window.AttachGameSessionAsync(uiSession);
   window.PauseOnDeactivateAsync().GetAwaiter().GetResult();
   Check(!Directory.GetFiles(uiSession.SessionDirectory,"command.txt",SearchOption.AllDirectories).Any(),"startup focus loss does not pause before first frame");
   while(!connect.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}connect.GetAwaiter().GetResult();
   var previewDeadline=DateTime.UtcNow.AddSeconds(10);
   while(window.FindControl<Image>("GameImage")!.Source is null && DateTime.UtcNow<previewDeadline){window.RefreshGameView();Dispatcher.UIThread.RunJobs();Thread.Sleep(10);}
   Check(window.FindControl<Image>("GameImage")!.Source is not null,"embedded preview arrives in UI");
   var addMark=window.AddBookmarkAsync("测试重点");while(!addMark.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}addMark.GetAwaiter().GetResult();
   Check(window.FindControl<ListBox>("BookmarkList")!.SelectedItem is FrameBookmark {Frame:0},"bookmark captures completed game frame");
   using(var screenshot=window.CaptureRenderedFrame()??throw new Exception("No rendered UI"))screenshot.Save(Path.Combine(output,"editor.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
   var stop=window.StopGameSessionAsync();while(!stop.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}stop.GetAwaiter().GetResult();
   window.Project.Undo();Check(window.Project.EditCount==0,"UI undo");
   var editingSession=new FileGameSession(fakeExe,Path.Combine(output,"ui-edit-session"),replayPath,Path.Combine(output,"initial"),new string('a',64),true);
   var editConnect=window.AttachGameSessionAsync(editingSession);while(!editConnect.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}editConnect.GetAwaiter().GetResult();
   window.Project.SetRange(0,0,4,false);
   var apply=window.ApplyEditsAsync();while(!apply.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}apply.GetAwaiter().GetResult();
   Check(window.Project.EditCount==0 && window.Project.Source.Held(0,4)==0,"UI adopts verified edited recording");
   var restore=window.RestoreOverwriteAsync();while(!restore.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}restore.GetAwaiter().GetResult();
   Check(window.Project.Source.Held(0,4)==1 && window.Project.EditCount==1,"restore recovers original and retained draft");
   window.Project.Undo();
   var beforeCover=window.Project.Source.Bytes.ToArray();
   var cover=window.ToggleRecordingAsync();while(!cover.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}cover.GetAwaiter().GetResult();
   var undoCover=window.RestoreOverwriteAsync();while(!undoCover.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}undoCover.GetAwaiter().GetResult();
   Check(window.Project.Source.Bytes.Span.SequenceEqual(beforeCover),"record takeover undo restores whole source tail");
   var editingStop=window.StopGameSessionAsync();while(!editingStop.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}editingStop.GetAwaiter().GetResult();
   window.Hide();window=new MainWindow();window.Show();
   var externalSession=new FileGameSession(fakeExe,Path.Combine(output,"external-session"),null,Path.Combine(output,"initial"),new string('a',64),true);
   var externalConnect=window.AttachGameSessionAsync(externalSession);
   while(!externalConnect.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}externalConnect.GetAwaiter().GetResult();
   var resume=externalSession.ResumeAsync(1,default);while(!resume.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}resume.GetAwaiter().GetResult();
   var before=externalSession.ReadState()!.Sequence;
   window.PauseOnDeactivateAsync().GetAwaiter().GetResult();
   Check(externalSession.ReadState()!.Sequence==before,"external window focus transfer does not pause recording");
   Check(File.Exists(Path.Combine(externalSession.SessionDirectory,"external-window.txt")),"external mode launch argument reaches child");
   var liveMark=window.AddBookmarkAsync("重新挑战");while(!liveMark.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}liveMark.GetAwaiter().GetResult();
   var marked=(FrameBookmark)window.FindControl<ListBox>("BookmarkList")!.SelectedItem!;
   var advance=externalSession.StepAsync(new bool[19],default);while(!advance.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}advance.GetAwaiter().GetResult();
   var goBack=window.ReturnSelectedBookmarkAsync();while(!goBack.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}goBack.GetAwaiter().GetResult();
   Check(window.FindControl<NumericUpDown>("JumpFrame")!.Value==marked.Frame,"live bookmark return saves then reopens at selected frame");
   Check(Replay.Load(externalSession.LastRecoveryPath!).Count>marked.Frame+1,"return preserves input after bookmarked frame in original branch");
   window.RefreshGameView();
   Check(window.FindControl<TimelineControl>("Timeline")!.Playhead==marked.Frame,"playhead follows seek independently of selection");
   var overwrite=externalSession.TakeoverAsync();while(!overwrite.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}overwrite.GetAwaiter().GetResult();
   var newFrame=externalSession.StepAsync(new bool[19],default);while(!newFrame.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}newFrame.GetAwaiter().GetResult();
   window.RefreshGameView();
   Check(window.FindControl<TimelineControl>("Timeline")!.FrameCount==marked.Frame+2,"live timeline replaces old tail with new completed frames");
   var all=window.ReplayAllAsync();while(!all.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}all.GetAwaiter().GetResult();
   Check(!externalSession.IsLive,"replay all seals recording and starts playback without manual reopen");
   var externalStop=window.StopGameSessionAsync();while(!externalStop.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}externalStop.GetAwaiter().GetResult();
   Console.WriteLine("All checks passed. Artifacts: "+output);return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }

 static int FakeEngine(string[] args) {
  string Arg(string name)=>args[Array.IndexOf(args,name)+1];
  string bridge=Arg("--tas-dir"),output=Arg("--tas-output");long seq=0,count=0,target=1;bool live=args.Contains("--record"),run=false;
  if(args.Contains("--tas-window"))File.WriteAllText(Path.Combine(Directory.GetParent(Directory.GetParent(bridge)!.FullName)!.FullName,"external-window.txt"),"yes");
  var source=live?null:Replay.Load(Arg("--replay"));long total=source?.Count??0;
  uint[]? plan=null;int first=-1;
  var planPath=Path.Combine(bridge,"edit.bin");
  if(File.Exists(planPath)){using var reader=new BinaryReader(File.OpenRead(planPath));reader.ReadBytes(8);int length=reader.ReadInt32();first=reader.ReadInt32();plan=new uint[length];for(int i=0;i<length;i++)plan[i]=reader.ReadUInt32();}
  File.WriteAllText(Path.Combine(bridge,"capabilities.txt"),"KTAS1 edits-v1");
  var recorded=new List<uint>();
  byte[] Current()=>Fixture((int)count,recorded.ToArray());
  for(int tick=0;tick<15000;tick++) {
   try{var parts=File.ReadAllText(Path.Combine(bridge,"command.txt")).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
    if(parts.Length==3 && long.Parse(parts[0])>seq){seq=long.Parse(parts[0]);switch(parts[1]){
     case "stop":File.WriteAllBytes(output,Current());return 0;
     case "pause":run=false;target=count;break;
     case "target":target=long.Parse(parts[2]);run=false;break;
     case "run":run=true;break;
     case "takeover":live=true;run=false;target=count;break;
    }}
   }catch(IOException){}
   if((run||count<target)&&(live||count<total)){
    if(plan is not null&&count>=first)live=true;
    uint value=0;if(plan is not null)value=plan[count];
    else if(!live&&source is not null){for(int a=0;a<19;a++)if(source.Held((int)count,a)>0)value|=1u<<a;}
    else {try{var input=File.ReadAllText(Path.Combine(bridge,"input.txt")).Split(' ');value=uint.Parse(input[1]);}catch(IOException){}}
    recorded.Add(value);count++;
   }
   if(!live && count>=total){run=false;target=count;}
   string phase=(run||count<target)?(live?"live":"playing"):(live?"live-paused":"paused");
   try {AtomicFile.Write(output,s=>{var bytes=Current();s.Write(bytes.AsSpan(0,bytes.Length-17));});
   AtomicFile.Write(Path.Combine(bridge,"state.txt"),s=>{using var w=new StreamWriter(s,leaveOpen:true);w.Write($"KTAS1 {seq} {count} {total} {phase}\n");});
   AtomicFile.Write(Path.Combine(bridge,"image.rgba"),s=>{using var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true);w.Write("KTASIMG1"u8);w.Write(count);w.Write(1);w.Write(1);w.Write(new byte[]{10,20,30,255});});}catch(IOException){}catch(UnauthorizedAccessException){}
   Thread.Sleep(2);
  }return 2;
 }
 static async Task<string> ProtocolCheck(string output,string replay) {
  var fake=Path.Combine(output,"fake-engine");Directory.CreateDirectory(fake);
  foreach(string f in Directory.GetFiles(AppContext.BaseDirectory))File.Copy(f,Path.Combine(fake,Path.GetFileName(f)));
  foreach(var n in new[]{"6kinoko_a.dat","6kinoko_b.dat","6kinoko_c.dat"})File.WriteAllText(Path.Combine(fake,n),"fixture");
  string initial=Path.Combine(output,"initial");Directory.CreateDirectory(initial);File.WriteAllText(Path.Combine(initial,"marisaA.dat"),"original");
  string exe=Path.Combine(fake,Path.GetFileName(Environment.ProcessPath!));
  await using var session=new FileGameSession(exe,Path.Combine(output,"protocol-session"),replay,initial,new string('a',64));
  await session.StartAsync();Check(session.ReadState()?.Completed==1,"engine handshake and initial pause");
  await session.SeekAsync(10);Check(session.ReadState()?.Completed==11,"forward seek acknowledged");
  await session.SeekAsync(2);Check(session.ReadState()?.Completed==3,"backward seek restarts and restores");
  await session.TakeoverAsync();Check(session.IsLive && session.ReadState()?.Phase=="live-paused","takeover acknowledgment");
  await session.StepAsync(new bool[19],default);Check(session.ReadState()?.Completed==4,"single frame acknowledgment");
  string stateFile=Directory.GetFiles(session.SessionDirectory,"state.txt",SearchOption.AllDirectories).OrderBy(File.GetLastWriteTimeUtc).Last();
  File.WriteAllText(Path.Combine(Path.GetDirectoryName(stateFile)!,"command.txt"),"999 stop 0\n");
  var exitDeadline=DateTime.UtcNow.AddSeconds(10);while(session.IsRunning && DateTime.UtcNow<exitDeadline)await Task.Delay(10);
  Check(!session.IsRunning,"external game close detected");
  await session.RestartAsync();Check(session.IsRunning && session.ReadState()?.Completed==4 && !session.IsLive,"restart restores closed live recording at last completed frame");
  string branch=await session.StopAsync();Check(Replay.Load(branch).Count==4,"branch finalized before load");
  Check(File.ReadAllText(Path.Combine(initial,"marisaA.dat"))=="original","initial save untouched");
  await using var editSession=new FileGameSession(exe,Path.Combine(output,"edit-session"),replay,initial,new string('a',64));
  await editSession.StartAsync();
  var edited=new TasProject(Replay.Load(replay),"edit.krec");edited.SetRange(0,0,4,false);edited.SetRange(5,9,1,false);
  await using var result=await editSession.ResimulateAsync(edited);
  var generated=Replay.Load(result.PlaybackSource!);
  Check(generated.Count==180 && generated.Held(0,4)==0 && generated.Held(1,4)==1 && generated.Released(5,1) && generated.Held(10,1)==1,"edited input resimulation includes frame zero, releases and held duration");
  Check(generated.Checkpoint(0)!=edited.Source.Checkpoint(0),"edited checksums are regenerated by engine");
  Check(result.ReadState()?.Completed==180 && result.ReadState()!.Phase=="paused","generated recording replay verified through last frame");
  Check(Replay.Load(replay).Held(0,4)==1 && editSession.IsRunning && edited.EditCount==6,"original session and edit intentions survive transaction");
  using var cancelled=new CancellationTokenSource();
  try{await editSession.ResimulateAsync(edited,new TestProgress(p=>{if(p.Completed>=3)cancelled.Cancel();}),cancelled.Token);throw new Exception("Cancellation ignored");}
  catch(OperationCanceledException){}
  Check(editSession.IsRunning && editSession.ReadState()!.Phase=="paused" && edited.EditCount==6,"cancelled resimulation keeps old recording and draft");
  using var seekCancel=new CancellationTokenSource();
  void CancelSeek(SessionState state){if(state.Completed>=3)seekCancel.Cancel();}
  editSession.Progress+=CancelSeek;
  try{await editSession.SeekAsync(170,seekCancel.Token);throw new Exception("Seek cancellation ignored");}catch(OperationCanceledException){}
  editSession.Progress-=CancelSeek;
  Check(editSession.ReadState()!.Phase=="paused","cancelled seek pauses engine at acknowledged boundary");
  return exe;
 }
 sealed class TestProgress(Action<SimulationProgress> callback):IProgress<SimulationProgress>{public void Report(SimulationProgress value)=>callback(value);}

}
