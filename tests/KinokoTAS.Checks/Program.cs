using Avalonia; using Avalonia.Controls; using Avalonia.Headless; using Avalonia.Input; using Avalonia.Threading;
using KinokoTAS.Core; using KinokoTAS.App;
internal static class Program {
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Reject(byte[] b,string name){try{Replay.Parse(b);}catch(InvalidDataException){Console.WriteLine("PASS "+name);return;}throw new Exception(name);}
 static byte[] Fixture(int count=180) {
  using var s=new MemoryStream();using var w=new BinaryWriter(s);
  w.Write("KINORPL1"u8);w.Write(1u);w.Write(19u);w.Write(System.Text.Encoding.ASCII.GetBytes(new string('a',64)));
  ulong chain=14695981039346656037;
  for(int f=0;f<count;f++) {
   using var frame=new MemoryStream();using var fw=new BinaryWriter(frame);fw.Write((ulong)f);
   for(int a=0;a<19;a++)fw.Write(a==4 && f<60?f+1:a==1?f+1:0);
   for(int a=0;a<19;a++)fw.Write((byte)(a==4 && f==60?1:0));
   fw.Write(0);fw.Write(0);for(int a=0;a<6;a++)fw.Write(0);
   fw.Write(new byte[4]);for(int a=0;a<10;a++)fw.Write(a==0?258:0);
   fw.Write((uint)(1000+f*1000/60));fw.Write(42u);fw.Write(42u);fw.Write((ulong)f*17);
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
   string fakeExe=ProtocolCheck(output,replayPath).GetAwaiter().GetResult();
   AppBuilder.Configure<App>().UseSkia().UseHeadless(new(){UseHeadlessDrawing=false}).SetupWithoutStarting();
   var window=new MainWindow();window.Show();var open=window.OpenPathAsync(replayPath);
   while(!open.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}open.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();
   Check(window.Project?.Source.Count==180,"UI loads source");
   var timeline=window.FindControl<TimelineControl>("Timeline")!;
   var point=timeline.TranslatePoint(new Point(TimelineControl.FrameWidth+4*TimelineControl.CellWidth+20,TimelineControl.HeaderHeight+10),window)!.Value;
   window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);Dispatcher.UIThread.RunJobs();
   Check(window.Project!.IsEdited(0,4) && !window.Project.Down(0,4),"timeline click edits selected action");
   var uiSession=new FileGameSession(fakeExe,Path.Combine(output,"ui-session"),replayPath,Path.Combine(output,"initial"),new string('a',64));
   var connect=window.AttachGameSessionAsync(uiSession);
   while(!connect.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}connect.GetAwaiter().GetResult();
   var previewDeadline=DateTime.UtcNow.AddSeconds(10);
   while(window.FindControl<Image>("GameImage")!.Source is null && DateTime.UtcNow<previewDeadline){Dispatcher.UIThread.RunJobs();Thread.Sleep(10);}
   Check(window.FindControl<Image>("GameImage")!.Source is not null,"embedded preview arrives in UI");
   using(var screenshot=window.CaptureRenderedFrame()??throw new Exception("No rendered UI"))screenshot.Save(Path.Combine(output,"editor.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
   var stop=window.StopGameSessionAsync();while(!stop.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}stop.GetAwaiter().GetResult();
   window.Project.Undo();Check(window.Project.EditCount==0,"UI undo");
   Console.WriteLine("All checks passed. Artifacts: "+output);return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }

 static int FakeEngine(string[] args) {
  string Arg(string name)=>args[Array.IndexOf(args,name)+1];
  string bridge=Arg("--tas-dir"),output=Arg("--tas-output");long seq=0,count=0,target=1;bool live=args.Contains("--record"),run=false;
  for(int tick=0;tick<15000;tick++) {
   try{var parts=File.ReadAllText(Path.Combine(bridge,"command.txt")).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
    if(parts.Length==3 && long.Parse(parts[0])>seq){seq=long.Parse(parts[0]);switch(parts[1]){
     case "stop":File.WriteAllBytes(output,Fixture((int)count));return 0;
     case "pause":run=false;target=count;break;
     case "target":target=long.Parse(parts[2]);run=false;break;
     case "run":run=true;break;
     case "takeover":live=true;run=false;target=count;break;
    }}
   }catch(IOException){}
   if(run||count<target)count++;
   string phase=(run||count<target)?(live?"live":"playing"):(live?"live-paused":"paused");
   AtomicFile.Write(Path.Combine(bridge,"state.txt"),s=>{using var w=new StreamWriter(s,leaveOpen:true);w.Write($"KTAS1 {seq} {count} 180 {phase}\n");});
   AtomicFile.Write(Path.Combine(bridge,"image.rgba"),s=>{using var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true);w.Write("KTASIMG1"u8);w.Write(count);w.Write(1);w.Write(1);w.Write(new byte[]{10,20,30,255});});
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
  string branch=await session.StopAsync();Check(Replay.Load(branch).Count==4,"branch finalized before load");
  Check(File.ReadAllText(Path.Combine(initial,"marisaA.dat"))=="original","initial save untouched");
  return exe;
 }
}
