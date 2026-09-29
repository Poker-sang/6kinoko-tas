using System.IO.Compression;
using System.Text.Json;
namespace KinokoTAS.Core;
public readonly record struct Cell(int Frame,int Action);
public sealed record Edit(int Frame,int Action,bool Down);
public sealed record ProjectManifest(int Version,string SourceName,Edit[] Edits);

/// <summary>Edits are intentions, not fabricated verified replay checkpoints.</summary>
public sealed class TasProject(Replay source,string sourceName) {
    public Replay Source {get;}=source;
    public string SourceName {get;}=sourceName;
    private readonly Dictionary<Cell,bool> edits=[];
    private readonly Stack<Change[]> undo=[],redo=[];
    private sealed record Change(Cell Cell,bool? Before,bool? After);
    public int EditCount=>edits.Count;
    public bool CanUndo=>undo.Count>0;
    public bool CanRedo=>redo.Count>0;
    public int? InvalidFrom=>edits.Count==0 ? null : edits.Keys.Min(c=>c.Frame);
    public event Action? Changed;
    public bool IsEdited(int frame,int action)=>edits.ContainsKey(new(frame,action));
    public bool Down(int frame,int action)=>edits.TryGetValue(new(frame,action),out var v)?v:Source.Held(frame,action)>0;
    public uint Mask(int frame) {uint mask=0;for(int a=0;a<Replay.ActionCount;a++)if(Down(frame,a))mask|=1u<<a;return mask;}
    public void WriteEditPlan(string path) {
        if(InvalidFrom is not int first)throw new InvalidOperationException("没有待执行的输入修改。");
        AtomicFile.Write(path,s=>{using var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true);w.Write("KTASED01"u8);w.Write(Source.Count);w.Write(first);for(int f=0;f<Source.Count;f++)w.Write(Mask(f));});
    }
    public void SetRange(int first,int last,int action,bool down) {
        if(first<0 || last<first || last>=Source.Count || (uint)action>=Replay.ActionCount)throw new ArgumentOutOfRangeException(nameof(first));
        var changes=new List<Change>();
        for(int f=first;f<=last;f++) {
            var cell=new Cell(f,action);
            bool? before=edits.TryGetValue(cell,out bool v)?v:null;
            bool? after=(Source.Held(f,action)>0)==down?null:down;
            if(before!=after)changes.Add(new(cell,before,after));
        }
        if(changes.Count==0)return;
        var batch=changes.ToArray();Apply(batch,true);undo.Push(batch);redo.Clear();Changed?.Invoke();
    }
    private void Apply(Change[] changes,bool forward) {
        foreach(var c in changes) {var v=forward?c.After:c.Before;if(v.HasValue)edits[c.Cell]=v.Value;else edits.Remove(c.Cell);}
    }
    public void Undo() {if(undo.TryPop(out var b)){Apply(b,false);redo.Push(b);Changed?.Invoke();}}
    public void Redo() {if(redo.TryPop(out var b)){Apply(b,true);undo.Push(b);Changed?.Invoke();}}
    // Export the untouched source only. Edited intent is stored in .ktas, never passed to a verified replay reader.
    public void ExportSource(string path)=>AtomicFile.Write(path,s=>s.Write(Source.Bytes.Span));
    public void Save(string path)=>AtomicFile.Write(path,stream=> {
        using var zip=new ZipArchive(stream,ZipArchiveMode.Create,true);
        using(var raw=zip.CreateEntry("source.krec",CompressionLevel.Optimal).Open())raw.Write(Source.Bytes.Span);
        var manifest=new ProjectManifest(1,SourceName,edits.OrderBy(e=>e.Key.Frame).ThenBy(e=>e.Key.Action).Select(e=>new Edit(e.Key.Frame,e.Key.Action,e.Value)).ToArray());
        using var json=zip.CreateEntry("project.json",CompressionLevel.Optimal).Open();JsonSerializer.Serialize(json,manifest);
    });
    public static TasProject Load(string path) {
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Count!=2)throw new InvalidDataException("项目内容无效。");
        var raw=zip.GetEntry("source.krec")??throw new InvalidDataException("缺少原始录制。");
        var json=zip.GetEntry("project.json")??throw new InvalidDataException("缺少项目清单。");
        if(raw.Length>Replay.HeaderSize+(long)Replay.RecordSize*Replay.MaxFrames+17 || json.Length>128*1024*1024)throw new InvalidDataException("项目超过大小限制。");
        using var memory=new MemoryStream();using(var s=raw.Open())s.CopyTo(memory);
        var replay=Replay.Parse(memory.ToArray());
        using var metadata=json.Open();var m=JsonSerializer.Deserialize<ProjectManifest>(metadata)??throw new InvalidDataException("项目清单为空。");
        if(m.Version!=1 || m.Edits is null || string.IsNullOrWhiteSpace(m.SourceName))throw new InvalidDataException("不支持的项目版本。");
        var project=new TasProject(replay,m.SourceName);
        foreach(var edit in m.Edits) {
            if((uint)edit.Frame>=(uint)replay.Count || (uint)edit.Action>=Replay.ActionCount || !project.edits.TryAdd(new(edit.Frame,edit.Action),edit.Down))throw new InvalidDataException("项目含无效或重复编辑。");
            if((replay.Held(edit.Frame,edit.Action)>0)==edit.Down)throw new InvalidDataException("项目含冗余编辑。");
        }
        return project;
    }
}
public static class AtomicFile {
    public static void Write(string path,Action<Stream> write) {
        path=Path.GetFullPath(path);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var s=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){write(s);s.Flush(true);}
            File.Move(temp,path,true);
        }finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
