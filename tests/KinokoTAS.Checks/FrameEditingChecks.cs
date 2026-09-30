using System.IO.Compression;
using System.Text.Json;
using KinokoTAS.Core;
internal static class FrameEditingChecks {
    static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
    public static void Run(Replay replay,string output) {
        var project=new TasProject(replay,"layout.krec");project.SetRange(5,9,4,false);project.InsertFrames(2,2);
        Check(project.FrameCount==182 && project.SourceFrame(2)==-1 && project.SourceFrame(4)==2 && !project.Down(7,4),"insert preserves shifted input edits and original mapping");
        var before=Enumerable.Range(0,project.FrameCount).Select(project.Mask).ToArray();
        project.BeginPaint();project.SetRange(2,5,5,true);project.SetRange(4,7,5,true);project.EndPaint();project.Undo();
        Check(Enumerable.Range(0,project.FrameCount).Select(project.Mask).SequenceEqual(before),"overlapping drag painting undoes as one complete stroke");
        project.Redo();Check(project.Down(2,5)&&project.Down(7,5),"drag stroke redo restores full span");project.Undo();
        project.DeleteFrames(3,4);Check(project.FrameCount==178,"delete changes draft length");project.Undo();
        Check(Enumerable.Range(0,project.FrameCount).Select(project.Mask).SequenceEqual(before),"delete undo restores masks and shifted source frames");
        string draft=Path.Combine(output,"layout.ktas");project.Save(draft);var loaded=TasProject.Load(draft);
        Check(loaded.FrameCount==182 && loaded.SourceFrame(2)==-1 && loaded.Mask(7)==project.Mask(7) && loaded.Source.Bytes.Span.SequenceEqual(replay.Bytes.Span),"layout draft roundtrip preserves immutable original");
        string plan=Path.Combine(output,"layout.bin");loaded.WriteEditPlan(plan);
        using(var reader=new BinaryReader(File.OpenRead(plan)))Check(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8))=="KTASED02"&&reader.ReadInt32()==182&&reader.ReadInt32()==2&&reader.ReadInt32()==180,"variable-length plan encodes first edit and original length");
        project.Undo();project.Undo();Check(project.FrameCount==180&&project.EditCount==0&&project.InvalidFrom is null,"mixed structural and input undo returns exact original intent");
        bool rejected=false;try{project.DeleteFrames(0,180);}catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected,"cannot delete every frame");
        string legacy=Path.Combine(output,"legacy-draft.ktas");
        using(var zip=ZipFile.Open(legacy,ZipArchiveMode.Create)){
            using(var raw=zip.CreateEntry("source.krec").Open())raw.Write(replay.Bytes.Span);
            using var json=zip.CreateEntry("project.json").Open();JsonSerializer.Serialize(json,new ProjectManifest(1,"legacy.krec",[new(2,4,false)]),RecordingJsonContext.Default.ProjectManifest);
        }
        Check(TasProject.Load(legacy).EditCount==1,"version-one input draft remains compatible");
        var invalid=Path.Combine(output,"invalid-layout.ktas");File.Copy(draft,invalid);
        using(var zip=ZipFile.Open(invalid,ZipArchiveMode.Update)){
            zip.GetEntry("project.json")!.Delete();using var json=zip.CreateEntry("project.json").Open();
            JsonSerializer.Serialize(json,new ProjectManifest(2,"invalid.krec",[],[new(2,0),new(1,0)]),RecordingJsonContext.Default.ProjectManifest);
        }
        rejected=false;try{TasProject.Load(invalid);}catch(InvalidDataException){rejected=true;}Check(rejected,"non-monotonic source mappings rejected");
    }
}
