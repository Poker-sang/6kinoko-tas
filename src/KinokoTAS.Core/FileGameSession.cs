using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
namespace KinokoTAS.Core;

public sealed record SessionState(long Sequence,long Completed,long Total,string Phase);
public sealed record PreviewFrame(long Completed,int Width,int Height,byte[] Pixels);
public sealed record InitialFile(string Path,string Sha256);
public sealed record SessionMetadata(int Version,string Identity,string EngineSha256,string SourceReplay,InitialFile[] InitialFiles);

/// <summary>Version 1 local directory transport, one owned child process. No game code in the UI.</summary>
public sealed class FileGameSession : IGameSession {
    readonly string executable,root,identity;
    readonly string? source;
    readonly SemaphoreSlim commands=new(1,1);
    Process? process;
    string run="",bridge="";
    long sequence,inputSequence;
    public string BranchPath {get;private set;}="";
    public string SessionDirectory=>root;
    public bool IsLive {get;private set;}
    public string EngineHash {get;}
    public static string HashFile(string path){using var s=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();}
    public FileGameSession(string exe,string sessionRoot,string? replayPath,string initialDirectory,string replayIdentity) {
        executable=Path.GetFullPath(exe);root=Path.GetFullPath(sessionRoot);identity=replayIdentity;
        if(Directory.Exists(root))throw new IOException("会话目录已存在。");
        foreach(var name in new[]{"6kinoko_a.dat","6kinoko_b.dat","6kinoko_c.dat"})if(!File.Exists(Path.Combine(Path.GetDirectoryName(executable)!,name)))throw new IOException("游戏程序旁缺少 "+name);
        Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"initial"));
        EngineHash=HashFile(executable);
        foreach(var file in Directory.EnumerateFiles(initialDirectory)) {
            var name=Path.GetFileName(file);
            if((name.EndsWith(".dat",StringComparison.OrdinalIgnoreCase) && !name.StartsWith("6kinoko_",StringComparison.OrdinalIgnoreCase)) || name=="input-actions.cfg")File.Copy(file,Path.Combine(root,"initial",name));
        }
        if(replayPath is not null){source=Path.Combine(root,"source.krec");File.Copy(replayPath,source);if(Replay.Load(source).Identity!=identity)throw new InvalidDataException("录制身份不一致。");}
        var files=Directory.GetFiles(Path.Combine(root,"initial")).Order().Select(p=>new InitialFile(Path.GetFileName(p),HashFile(p))).ToArray();
        File.WriteAllText(Path.Combine(root,"session.json"),JsonSerializer.Serialize(new SessionMetadata(1,identity,EngineHash,replayPath??"",files),new JsonSerializerOptions{WriteIndented=true}));
    }
    public async Task StartAsync(CancellationToken ct=default) {
        if(process is not null)throw new InvalidOperationException("会话已启动。");
        if(HashFile(executable)!=EngineHash)throw new IOException("游戏程序发生变化，请重新创建会话。");
        run=Path.Combine(root,"run-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..6]);
        bridge=Path.Combine(run,"bridge");Directory.CreateDirectory(bridge);var saves=Path.Combine(run,"saves");Directory.CreateDirectory(saves);
        foreach(var f in Directory.GetFiles(Path.Combine(root,"initial")))File.Copy(f,Path.Combine(saves,Path.GetFileName(f)));
        BranchPath=Path.Combine(run,"branch.krec");sequence=0;IsLive=source is null;
        var start=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)!};
        foreach(var arg in new[]{"--save-dir",saves,source is null?"--record":"--replay",source??BranchPath,"--replay-status",Path.Combine(run,"replay-status.txt"),"--replay-identity",identity,"--tas-dir",bridge,"--tas-output",BranchPath})start.ArgumentList.Add(arg);
        start.Environment.Remove("KINOKO_REPLAY_MODE");start.Environment["KINOKO_TRACE"]="0";
        process=Process.Start(start)??throw new IOException("游戏进程启动失败。");
        try {await WaitAsync(s=>s.Completed>=1 && s.Phase.EndsWith("paused"),TimeSpan.FromSeconds(30),ct);}
        catch{await DisposeAsync();throw;}
    }
    static byte[] ReadShared(string path) {using var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
    public SessionState? ReadState() {
        if(File.Exists(Path.Combine(bridge,"error.txt")))throw new InvalidDataException(System.Text.Encoding.UTF8.GetString(ReadShared(Path.Combine(bridge,"error.txt"))));
        try {
            var words=System.Text.Encoding.UTF8.GetString(ReadShared(Path.Combine(bridge,"state.txt"))).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if(words.Length!=5 || words[0]!="KTAS1")return null;
            return new(long.Parse(words[1]),long.Parse(words[2]),long.Parse(words[3]),words[4]);
        }catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}catch(FormatException){return null;}
    }
    public PreviewFrame? ReadPreview() {
        try {
            byte[] data=ReadShared(Path.Combine(bridge,"image.rgba"));
            if(data.Length<24 || !data.AsSpan(0,8).SequenceEqual("KTASIMG1"u8))return null;
            long count=System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(8));
            int w=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16)),h=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(20));
            if(w<=0||h<=0||w>4096||h>4096||data.Length!=24+(long)w*h*4)return null;
            return new(count,w,h,data[24..]);
        }catch(IOException){return null;}
    }
    public void Input(uint mask) {
        if(process is null||process.HasExited)return;
        AtomicFile.Write(Path.Combine(bridge,"input.txt"),s=>{using var w=new StreamWriter(s,leaveOpen:true);w.Write($"{++inputSequence} {mask}\n");});
    }
    async Task<SessionState> WaitAsync(Func<SessionState,bool> predicate,TimeSpan timeout,CancellationToken ct) {
        var watch=Stopwatch.StartNew();
        while(watch.Elapsed<timeout){ct.ThrowIfCancellationRequested();var state=ReadState();if(state is not null&&predicate(state))return state;if(process is null||process.HasExited)throw new IOException("游戏已退出，请查看会话日志。");await Task.Delay(10,ct);}
        throw new TimeoutException("引擎未确认操作。请确认选择的是支持 KTAS1 的新版游戏程序。");
    }
    async Task<SessionState> SendAsync(string verb,long arg,Func<SessionState,bool> predicate,CancellationToken ct) {
        await commands.WaitAsync(ct);
        try {long id=++sequence;AtomicFile.Write(Path.Combine(bridge,"command.txt"),s=>{using var w=new StreamWriter(s,leaveOpen:true);w.Write($"{id} {verb} {arg}\n");});return await WaitAsync(s=>s.Sequence==id&&predicate(s),TimeSpan.FromMinutes(30),ct);}
        finally{commands.Release();}
    }
    static GameState State(SessionState s,string id)=>new(s.Completed,s.Phase.EndsWith("paused"),id);
    public async Task<GameState> PauseAsync(CancellationToken ct)=>State(await SendAsync("pause",0,s=>s.Phase.EndsWith("paused"),ct),identity);
    public async Task<GameState> StepAsync(IReadOnlyList<bool> actions,CancellationToken ct) {
        if(actions.Count!=19)throw new ArgumentException("19 actions required");uint mask=0;for(int i=0;i<19;i++)if(actions[i])mask|=1u<<i;Input(mask);
        await PauseAsync(ct);var s=ReadState()??throw new IOException("无引擎状态");
        if(!IsLive && s.Completed>=s.Total)throw new InvalidOperationException("录制已到末尾，请接管或重新定位。");
        return State(await SendAsync("target",s.Completed+1,x=>x.Completed>=s.Completed+1&&x.Phase.EndsWith("paused"),ct),identity);
    }
    public async Task<GameState> ResumeAsync(double speed,CancellationToken ct) {
        var current=ReadState();if(!IsLive && current is not null && current.Completed>=current.Total)throw new InvalidOperationException("录制已到末尾，请接管或重新定位。");
        if(speed!=1)throw new NotSupportedException("首版仅支持正常速度。");
        return State(await SendAsync("run",0,s=>!s.Phase.EndsWith("paused"),ct),identity);
    }
    public async Task SeekAsync(long frame,CancellationToken ct=default) {
        if(source is null||IsLive)throw new InvalidOperationException("接管/新录制中请先结束并保存分支，再打开分支定位。");
        long target=frame+1;var replay=Replay.Load(source);if(target<1||target>replay.Count)throw new ArgumentOutOfRangeException(nameof(frame));
        await PauseAsync(ct);var state=ReadState()!;
        if(state.Completed>target){await StopAsync();await StartAsync(ct);}
        await SendAsync("target",target,s=>s.Completed==target&&s.Phase.EndsWith("paused"),ct);
    }
    public async Task TakeoverAsync(CancellationToken ct=default) {await PauseAsync(ct);Input(0);await SendAsync("takeover",0,s=>s.Phase=="live-paused",ct);IsLive=true;}
    public async Task<string> StopAsync() {
        if(process is null)return BranchPath;
        try {
            long id=++sequence;AtomicFile.Write(Path.Combine(bridge,"command.txt"),s=>{using var w=new StreamWriter(s,leaveOpen:true);w.Write($"{id} stop 0\n");});
            using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(15));await process.WaitForExitAsync(limit.Token);
        }catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw new IOException("游戏未正常结束，录制文件可能未完成。");}
        finally{process.Dispose();process=null;}
        _=Replay.Load(BranchPath);return BranchPath;
    }
    public async ValueTask DisposeAsync(){if(process is null)return;try{await StopAsync();}catch{if(process is not null){if(!process.HasExited)process.Kill(true);process.Dispose();process=null;}}}
}
