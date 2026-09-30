using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
namespace KinokoTAS.Core;
public readonly record struct Cell(int Frame,int Action);
public sealed record Edit(int Frame,int Action,bool Down);
public readonly record struct FrameIntent(int SourceFrame,uint Mask);
public sealed record ProjectManifest(int Version,string SourceName,Edit[] Edits,FrameIntent[]? Frames=null);
public sealed record FrameLayoutChange(long Id,int First,int Removed,int Inserted,bool Forward);

public sealed class TasProject {
    public Replay Source {get;}
    public string SourceName {get;}
    readonly List<FrameIntent> frames;
    readonly SortedSet<int> editedFrames=[];
    readonly Stack<Action<bool>> undo=[],redo=[];
    int inputEditCount;
    int? layoutFrom;
    long nextLayoutId;
    Dictionary<int,uint>? paintingBefore;
    public int FrameCount=>frames.Count;
    public int EditCount=>inputEditCount+(layoutFrom.HasValue?1:0);
    public bool HasLayoutChanges=>layoutFrom.HasValue;
    public bool IsPainting=>paintingBefore is not null;
    public bool CanUndo=>undo.Count>0 && !IsPainting;
    public bool CanRedo=>redo.Count>0 && !IsPainting;
    public int? InvalidFrom {
        get {
            int? first=editedFrames.Count>0?editedFrames.Min:null;
            if(layoutFrom is int layout)first=first is int input?Math.Min(layout,input):layout;
            return first is int index?Math.Min(index,FrameCount-1):null;
        }
    }
    public event Action? Changed;
    public event Action<FrameLayoutChange>? LayoutChanged;
    public TasProject(Replay source,string sourceName) {
        Source=source;SourceName=sourceName;frames=new(source.Count);
        for(int frame=0;frame<source.Count;frame++)frames.Add(new(frame,SourceMask(frame)));
    }
    uint SourceMask(int frame) {
        if(frame<0)return 0;
        uint mask=0;for(int action=0;action<Replay.ActionCount;action++)if(Source.Held(frame,action)>0)mask|=1u<<action;
        return mask;
    }
    public int SourceFrame(int frame)=>frames[frame].SourceFrame;
    public bool IsEdited(int frame,int action)=>frames[frame].SourceFrame!=frame || (frames[frame].Mask&(1u<<action))!=(SourceMask(frames[frame].SourceFrame)&(1u<<action));
    public bool Down(int frame,int action)=>(frames[frame].Mask&(1u<<action))!=0;
    public uint Mask(int frame)=>frames[frame].Mask;
    public void WriteEditPlan(string path) {
        if(InvalidFrom is not int first || first<0)throw new InvalidOperationException("没有待执行的输入修改。");
        AtomicFile.Write(path,stream=>{
            using var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true);
            writer.Write(FrameCount==Source.Count?"KTASED01"u8:"KTASED02"u8);
            writer.Write(FrameCount);writer.Write(first);
            if(FrameCount!=Source.Count)writer.Write(Source.Count);
            foreach(var frame in frames)writer.Write(frame.Mask);
        });
    }
    void SetMask(int frame,uint mask) {
        var previous=frames[frame];uint baseline=SourceMask(previous.SourceFrame);
        inputEditCount+=BitOperations.PopCount(mask^baseline)-BitOperations.PopCount(previous.Mask^baseline);
        frames[frame]=previous with {Mask=mask};
        if(mask==baseline)editedFrames.Remove(frame);else editedFrames.Add(frame);
    }
    public void SetRange(int first,int last,int action,bool down) {
        if(first<0 || last<first || last>=FrameCount || (uint)action>=Replay.ActionCount)throw new ArgumentOutOfRangeException(nameof(first));
        var changes=new List<(int Frame,uint Before,uint After)>();uint bit=1u<<action;
        for(int frame=first;frame<=last;frame++) {
            uint before=Mask(frame),after=down?before|bit:before&~bit;
            if(before==after)continue;
            if(paintingBefore is not null)paintingBefore.TryAdd(frame,before);
            changes.Add((frame,before,after));SetMask(frame,after);
        }
        if(changes.Count==0)return;
        if(paintingBefore is null)Remember(forward=>{foreach(var change in changes)SetMask(change.Frame,forward?change.After:change.Before);});
        Changed?.Invoke();
    }
    void Remember(Action<bool> change){undo.Push(change);redo.Clear();}
    public void BeginPaint(){if(IsPainting)throw new InvalidOperationException("绘制尚未结束。");paintingBefore=[];}
    public void EndPaint() {
        if(paintingBefore is not { } original)return;
        paintingBefore=null;
        var changes=original.Select(entry=>(Frame:entry.Key,Before:entry.Value,After:Mask(entry.Key))).Where(change=>change.Before!=change.After).ToArray();
        if(changes.Length>0)Remember(forward=>{foreach(var change in changes)SetMask(change.Frame,forward?change.After:change.Before);});
        Changed?.Invoke();
    }
    void Reindex() {
        inputEditCount=0;editedFrames.Clear();layoutFrom=null;
        for(int frame=0;frame<FrameCount;frame++) {
            var intent=frames[frame];uint baseline=SourceMask(intent.SourceFrame);
            int edits=BitOperations.PopCount(intent.Mask^baseline);inputEditCount+=edits;
            if(edits>0)editedFrames.Add(frame);
            if(layoutFrom is null && intent.SourceFrame!=frame)layoutFrom=frame;
        }
        if(layoutFrom is null && FrameCount!=Source.Count)layoutFrom=FrameCount;
    }
    public void InsertFrames(int before,int count) {
        if(IsPainting || before<0 || before>FrameCount || count<1 || count>Replay.MaxFrames-FrameCount)throw new ArgumentOutOfRangeException(nameof(count));
        var inserted=Enumerable.Repeat(new FrameIntent(-1,0),count).ToArray();long identifier=++nextLayoutId;
        frames.InsertRange(before,inserted);Reindex();
        Remember(forward=>{
            if(forward)frames.InsertRange(before,inserted);else frames.RemoveRange(before,count);
            Reindex();LayoutChanged?.Invoke(new(identifier,before,0,count,forward));
        });
        LayoutChanged?.Invoke(new(identifier,before,0,count,true));Changed?.Invoke();
    }
    public void DeleteFrames(int first,int count) {
        if(IsPainting || first<0 || count<1 || first>FrameCount-count || count>=FrameCount)throw new ArgumentOutOfRangeException(nameof(count),"至少保留一帧。");
        var removed=frames.GetRange(first,count);long identifier=++nextLayoutId;
        frames.RemoveRange(first,count);Reindex();
        Remember(forward=>{
            if(forward)frames.RemoveRange(first,count);else frames.InsertRange(first,removed);
            Reindex();LayoutChanged?.Invoke(new(identifier,first,count,0,forward));
        });
        LayoutChanged?.Invoke(new(identifier,first,count,0,true));Changed?.Invoke();
    }
    public void Undo(){if(!IsPainting && undo.TryPop(out var change)){change(false);redo.Push(change);Changed?.Invoke();}}
    public void Redo(){if(!IsPainting && redo.TryPop(out var change)){change(true);undo.Push(change);Changed?.Invoke();}}
    public void ExportSource(string path)=>AtomicFile.Write(path,stream=>stream.Write(Source.Bytes.Span));
    public void Save(string path)=>AtomicFile.Write(path,stream=> {
        using var zip=new ZipArchive(stream,ZipArchiveMode.Create,true);
        using(var raw=zip.CreateEntry("source.krec",CompressionLevel.Optimal).Open())raw.Write(Source.Bytes.Span);
        var manifest=new ProjectManifest(2,SourceName,[],frames.ToArray());
        using var json=zip.CreateEntry("project.json",CompressionLevel.Optimal).Open();JsonSerializer.Serialize(json,manifest);
    });
    public static TasProject Load(string path) {
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Count!=2)throw new InvalidDataException("项目内容无效。");
        var raw=zip.GetEntry("source.krec")??throw new InvalidDataException("缺少原始录制。");
        var json=zip.GetEntry("project.json")??throw new InvalidDataException("缺少项目清单。");
        if(raw.Length>Replay.HeaderSize+(long)Replay.RecordSize*Replay.MaxFrames+17 || json.Length>128*1024*1024)throw new InvalidDataException("项目超过大小限制。");
        using var memory=new MemoryStream();using(var stream=raw.Open())stream.CopyTo(memory);
        var replay=Replay.Parse(memory.ToArray());
        using var metadata=json.Open();var manifest=JsonSerializer.Deserialize<ProjectManifest>(metadata)??throw new InvalidDataException("项目清单为空。");
        if(manifest.Version is not (1 or 2) || manifest.Edits is null || string.IsNullOrWhiteSpace(manifest.SourceName))throw new InvalidDataException("不支持的项目版本。");
        var project=new TasProject(replay,manifest.SourceName);
        if(manifest.Version==2) {
            if(manifest.Frames is null || manifest.Frames.Length<1 || manifest.Frames.Length>Replay.MaxFrames || manifest.Edits.Length>0)throw new InvalidDataException("项目帧布局无效。");
            int previousSource=-1;
            foreach(var frame in manifest.Frames) {
                if(frame.SourceFrame< -1 || frame.SourceFrame>=replay.Count || frame.Mask>>Replay.ActionCount!=0 || (frame.SourceFrame>=0 && frame.SourceFrame<=previousSource))throw new InvalidDataException("项目含无效或重复来源帧。");
                if(frame.SourceFrame>=0)previousSource=frame.SourceFrame;
            }
            project.frames.Clear();project.frames.AddRange(manifest.Frames);project.Reindex();
        } else {
            var seen=new HashSet<Cell>();
            foreach(var edit in manifest.Edits) {
                if((uint)edit.Frame>=(uint)replay.Count || (uint)edit.Action>=Replay.ActionCount || !seen.Add(new(edit.Frame,edit.Action)) || project.Down(edit.Frame,edit.Action)==edit.Down)throw new InvalidDataException("项目含无效、重复或冗余编辑。");
                project.SetMask(edit.Frame,edit.Down?project.Mask(edit.Frame)|(1u<<edit.Action):project.Mask(edit.Frame)&~(1u<<edit.Action));
            }
        }
        return project;
    }
}
public static class AtomicFile {
    public static void Write(string path,Action<Stream> write) {
        path=Path.GetFullPath(path);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){write(stream);stream.Flush(true);}
            File.Move(temp,path,true);
        }finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
