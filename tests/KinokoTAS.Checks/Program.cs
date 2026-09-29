using Avalonia; using Avalonia.Controls; using Avalonia.Headless; using Avalonia.Input; using Avalonia.Threading;
using KinokoTAS.Core; using KinokoTAS.App;
internal static class Program {
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Reject(byte[] b,string name){try{Replay.Parse(b);}catch(InvalidDataException){Console.WriteLine("PASS "+name);return;}throw new Exception(name);}
 static byte[] Fixture() {
  using var s=new MemoryStream();using var w=new BinaryWriter(s);
  w.Write("KINORPL1"u8);w.Write(1u);w.Write(19u);w.Write(System.Text.Encoding.ASCII.GetBytes(new string('a',64)));
  ulong chain=14695981039346656037;
  for(int f=0;f<180;f++) {
   using var frame=new MemoryStream();using var fw=new BinaryWriter(frame);fw.Write((ulong)f);
   for(int a=0;a<19;a++)fw.Write(a==4 && f<60?f+1:a==1?f+1:0);
   for(int a=0;a<19;a++)fw.Write((byte)(a==4 && f==60?1:0));
   fw.Write(0);fw.Write(0);for(int a=0;a<6;a++)fw.Write(0);
   fw.Write(new byte[4]);for(int a=0;a<10;a++)fw.Write(a==0?258:0);
   fw.Write((uint)(1000+f*1000/60));fw.Write(42u);fw.Write(42u);fw.Write((ulong)f*17);
   var b=frame.ToArray();if(b.Length!=199)throw new Exception("fixture size");
   w.Write((byte)1);w.Write(b);w.Write(Replay.Hash(b));chain=Replay.Hash(b,chain);
  }
  w.Write((byte)0);w.Write(180ul);w.Write(chain);return s.ToArray();
 }
 [STAThread] static int Main(string[] args) {
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
   AppBuilder.Configure<App>().UseSkia().UseHeadless(new(){UseHeadlessDrawing=false}).SetupWithoutStarting();
   var window=new MainWindow();window.Show();var open=window.OpenPathAsync(replayPath);
   while(!open.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}open.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();
   Check(window.Project?.Source.Count==180,"UI loads source");
   var timeline=window.FindControl<TimelineControl>("Timeline")!;
   var point=timeline.TranslatePoint(new Point(TimelineControl.FrameWidth+4*TimelineControl.CellWidth+20,TimelineControl.HeaderHeight+10),window)!.Value;
   window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);Dispatcher.UIThread.RunJobs();
   Check(window.Project!.IsEdited(0,4) && !window.Project.Down(0,4),"timeline click edits selected action");
   using(var screenshot=window.CaptureRenderedFrame()??throw new Exception("No rendered UI"))screenshot.Save(Path.Combine(output,"editor.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
   window.Project.Undo();Check(window.Project.EditCount==0,"UI undo");
   Console.WriteLine("All checks passed. Artifacts: "+output);return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
