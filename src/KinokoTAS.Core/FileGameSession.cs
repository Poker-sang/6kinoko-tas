using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
namespace KinokoTAS.Core;

public sealed record SimulationProgress(string Stage,long Completed,long Total);
public sealed record SessionState(long Sequence,long Completed,long Total,string Phase);
public sealed record PreviewFrame(long Completed,int Width,int Height,byte[] Pixels);
public sealed record InitialFile(string Path,string Sha256);
public sealed record SessionMetadata(int Version,string Identity,string EngineSha256,string SourceReplay,InitialFile[] InitialFiles);

/// <summary>Version 1 local directory transport, one owned child process. No game code in the UI.</summary>
public sealed class FileGameSession : IGameSession {
    readonly string executable,root,identity;
    string? source;
    TasProject? editPlan;
    public double PlaybackSpeed {get;private set;}=1;
    bool Supports(string feature)=>File.Exists(Path.Combine(bridge,"capabilities.txt")) && File.ReadAllText(Path.Combine(bridge,"capabilities.txt")).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Contains(feature);
    public event Action<SessionState>? Progress;
    public string? PlaybackSource=>source;
    public string CurrentRecordingPath=>IsLive?BranchPath:source??BranchPath;
    public string? LastRecoveryPath {get;private set;}
    readonly SemaphoreSlim commands=new(1,1);
    Process? process;
    string run="",bridge="";
    long sequence,inputSequence,lastCompleted;
    public bool IsRunning=>process is not null && !process.HasExited;
    public string BranchPath {get;private set;}="";
    public string SessionDirectory=>root;
    public int GameProcessId=>process?.Id??0;
    public nint GameWindowHandle {get {if(!OperatingSystem.IsWindows() || process is null || process.HasExited)return 0;process.Refresh();return process.MainWindowHandle;}}
    public FileGameSession ReopenBranch()=>new(executable,root+"-return-"+Guid.NewGuid().ToString("N")[..8],BranchPath,Path.Combine(root,"initial"),identity,ExternalWindow);
    public bool IsLive {get;private set;}
    public bool ExternalWindow {get;}
    public string EngineHash {get;}
    public static string HashFile(string path){using var s=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();}
    public FileGameSession(string exe,string sessionRoot,string? replayPath,string initialDirectory,string replayIdentity,bool externalWindow=false) {
        ExternalWindow=externalWindow;
        executable=Path.GetFullPath(exe);root=Path.GetFullPath(sessionRoot);identity=replayIdentity;
        if(Directory.Exists(root))throw new IOException("会话目录已存在。");
        foreach(var name in new[]{"6kinoko_a.dat","6kinoko_b.dat","6kinoko_c.dat"})if(!File.Exists(Path.Combine(Path.GetDirectoryName(executable)!,name)))throw new IOException("游戏程序旁缺少 "+name);
        Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"initial"));
        EngineHash=HashFile(executable);
        foreach(var file in Directory.EnumerateFiles(initialDirectory)) {
            var name=Path.GetFileName(file);
            if((name.EndsWith(".dat",StringComparison.OrdinalIgnoreCase) && !name.StartsWith("6kinoko_",StringComparison.OrdinalIgnoreCase)) || name=="input-actions.cfg")File.Copy(file,Path.Combine(root,"initial",name));
        }
        if(replayPath is not null){source=Path.Combine(root,"source.krec");AtomicFile.Write(source,s=>s.Write(Replay.Load(replayPath).Bytes.Span));if(Replay.Load(source).Identity!=identity)throw new InvalidDataException("录制身份不一致。");}
        var files=Directory.GetFiles(Path.Combine(root,"initial")).Order().Select(p=>new InitialFile(Path.GetFileName(p),HashFile(p))).ToArray();
        File.WriteAllText(Path.Combine(root,"session.json"),JsonSerializer.Serialize(new SessionMetadata(1,identity,EngineHash,replayPath??"",files),new JsonSerializerOptions{WriteIndented=true}));
    }
    public FileGameSession CreatePlaybackSession(string replayPath)=>new(executable,Path.Combine(Path.GetDirectoryName(root)!,"session-"+Guid.NewGuid().ToString("N")),replayPath,Path.Combine(root,"initial"),identity,ExternalWindow){PlaybackSpeed=PlaybackSpeed};
    public async Task<FileGameSession> ResimulateAsync(TasProject project,IProgress<SimulationProgress>? progress=null,CancellationToken ct=default) {
        if(project.InvalidFrom is null)throw new InvalidOperationException("没有待执行的输入修改。");
        if(IsLive)throw new InvalidOperationException("请先暂停录制并切换回放，再修改输入。");
        if(source is null || !Replay.Load(source).Bytes.Span.SequenceEqual(project.Source.Bytes.Span))throw new InvalidOperationException("输入草稿与当前会话来源不一致。");
        await PauseAsync(ct);
        string draft=Path.Combine(root,"edit-"+Guid.NewGuid().ToString("N")+".ktas");project.Save(draft);
        var frozen=TasProject.Load(draft);
        await using var generated=CreatePlaybackSession(source);
        generated.editPlan=frozen;
        generated.Progress+=s=>progress?.Report(new("重新模拟",s.Completed,frozen.FrameCount));
        await generated.StartAsync(ct);
        await generated.TargetAsync(frozen.FrameCount,ct);
        string result=await generated.StopAsync();
        var replay=Replay.Load(result);
        if(replay.Count!=frozen.FrameCount || replay.Identity!=frozen.Source.Identity)throw new InvalidDataException("重新模拟的录制长度或身份不匹配。");
        for(int f=0;f<replay.Count;f++)for(int a=0;a<Replay.ActionCount;a++)
            if((replay.Held(f,a)>0)!=frozen.Down(f,a))throw new InvalidDataException($"重新模拟未执行预期输入：帧 {f}，动作 {a}。");
        var verified=CreatePlaybackSession(result);
        try {
            verified.Progress+=ReportVerification;
            await verified.StartAsync(ct);
            await verified.SeekAsync(replay.Count-1,ct);
            verified.Progress-=ReportVerification;
            return verified;
        }catch{await verified.DisposeAsync();throw;}
        void ReportVerification(SessionState s)=>progress?.Report(new("回放验证",s.Completed,replay.Count));
    }
    public async Task RestartAsync(CancellationToken ct=default) {
        if(IsRunning)throw new InvalidOperationException("游戏仍在运行。");
        long target=lastCompleted;
        if(process is not null){process.Dispose();process=null;}
        if(IsLive){
            var recorded=Replay.Load(BranchPath); // Never resume a truncated or corrupt recording as valid.
            source=BranchPath;LastRecoveryPath=BranchPath;target=recorded.Count;
        }
        var replay=source is not null?Replay.Load(source):null;
        await StartAsync(ct);
        if(replay?.Count>0)await SeekAsync(Math.Clamp(target-1,0,replay.Count-1),ct);
    }
    public async Task StartAsync(CancellationToken ct=default) {
        if(process is not null)throw new InvalidOperationException("会话已启动。");
        if(HashFile(executable)!=EngineHash)throw new IOException("游戏程序发生变化，请重新创建会话。");
        run=Path.Combine(root,"run-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..6]);
        bridge=Path.Combine(run,"bridge");Directory.CreateDirectory(bridge);var saves=Path.Combine(run,"saves");Directory.CreateDirectory(saves);
        foreach(var f in Directory.GetFiles(Path.Combine(root,"initial")))File.Copy(f,Path.Combine(saves,Path.GetFileName(f)));
        BranchPath=Path.Combine(run,"branch.krec");sequence=0;IsLive=source is null;
        editPlan?.WriteEditPlan(Path.Combine(bridge,"edit.bin"));
        var start=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)!};
        foreach(var arg in new[]{"--save-dir",saves,source is null?"--record":"--replay",source??BranchPath,"--replay-status",Path.Combine(run,"replay-status.txt"),"--replay-identity",identity,"--tas-dir",bridge,"--tas-output",BranchPath})start.ArgumentList.Add(arg);
        if(ExternalWindow)start.ArgumentList.Add("--tas-window");
        start.Environment.Remove("KINOKO_REPLAY_MODE");start.Environment["KINOKO_TRACE"]="0";
        process=Process.Start(start)??throw new IOException("游戏进程启动失败。");
        try {
            await WaitAsync(s=>s.Completed>=1 && s.Phase.EndsWith("paused"),TimeSpan.FromSeconds(30),ct);
            if(editPlan is not null && !Supports(editPlan.FrameCount==editPlan.Source.Count?"edits-v1":"edits-v2"))
                throw new NotSupportedException("游戏版本不支持输入重新模拟，请用新版游戏程序重新打开录制。");
            if(PlaybackSpeed!=1)await EngineSpeedAsync(PlaybackSpeed,ct);
        }
        catch{await DisposeAsync();throw;}
    }
    static byte[] ReadShared(string path) {using var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
    public SessionState? ReadState() {
        if(File.Exists(Path.Combine(bridge,"error.txt")))throw new InvalidDataException(System.Text.Encoding.UTF8.GetString(ReadShared(Path.Combine(bridge,"error.txt"))));
        try {
            var words=System.Text.Encoding.UTF8.GetString(ReadShared(Path.Combine(bridge,"state.txt"))).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if(words.Length!=5 || words[0]!="KTAS1")return null;
            lastCompleted=long.Parse(words[2]);
            return new(long.Parse(words[1]),lastCompleted,long.Parse(words[3]),words[4]);
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
    static void Mailbox(string path,Action<Stream> write) {
        for(int attempt=0;;attempt++)try{AtomicFile.Write(path,write);return;}
        catch(IOException)when(attempt<12){Thread.Sleep(2);}
        catch(UnauthorizedAccessException)when(attempt<12){Thread.Sleep(2);}
    }
    public void Input(uint mask) {
        if(process is null||process.HasExited)return;
        Mailbox(Path.Combine(bridge,"input.txt"),s=>{using var w=new StreamWriter(s,leaveOpen:true);w.Write($"{++inputSequence} {mask}\n");});
    }
    async Task<SessionState> WaitAsync(Func<SessionState,bool> predicate,TimeSpan timeout,CancellationToken ct) {
        var watch=Stopwatch.StartNew();
        while(watch.Elapsed<timeout){ct.ThrowIfCancellationRequested();var state=ReadState();if(state is not null)Progress?.Invoke(state);if(state is not null&&predicate(state))return state;if(process is null||process.HasExited)throw new IOException("游戏已退出，请查看会话日志。");await Task.Delay(10,ct);}
        throw new TimeoutException("引擎未确认操作。请确认选择的是支持 KTAS1 的新版游戏程序。");
    }
    async Task<SessionState> SendAsync(string verb,long arg,Func<SessionState,bool> predicate,CancellationToken ct) {
        await commands.WaitAsync(ct);
        try {long id=++sequence;Mailbox(Path.Combine(bridge,"command.txt"),s=>{using var w=new StreamWriter(s,leaveOpen:true);w.Write($"{id} {verb} {arg}\n");});return await WaitAsync(s=>s.Sequence==id&&predicate(s),TimeSpan.FromMinutes(30),ct);}
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
    async Task EngineSpeedAsync(double speed,CancellationToken ct) {
        if(speed is not (0.25 or 0.5 or 1 or 2 or 4))throw new ArgumentOutOfRangeException(nameof(speed));
        if(!Supports("pacing-v1"))throw new NotSupportedException("游戏版本不支持倍速，请更新游戏程序并重新打开录制。");
        await SendAsync("speed",(long)(speed*100),state=>true,ct);
    }
    public async Task SetSpeedAsync(double speed,CancellationToken ct=default) {
        if(speed!=1 || Supports("pacing-v1"))await EngineSpeedAsync(speed,ct);
        PlaybackSpeed=speed;
    }
    public async Task<GameState> ResumeAsync(double speed,CancellationToken ct) {
        var current=ReadState();if(!IsLive && current is not null && current.Completed>=current.Total)throw new InvalidOperationException("录制已到末尾，请接管或重新定位。");
        await SetSpeedAsync(speed,ct);
        return State(await SendAsync("run",0,s=>!s.Phase.EndsWith("paused"),ct),identity);
    }
    public async Task SeekAsync(long frame,CancellationToken ct=default) {
        if(IsLive)await SealLiveAsync(ct);
        if(source is null)throw new InvalidOperationException("尚无录制内容。");
        long target=frame+1;var replay=Replay.Load(source);if(target<1||target>replay.Count)throw new ArgumentOutOfRangeException(nameof(frame));
        await PauseAsync(ct);var state=ReadState()!;
        if(state.Completed>target){await StopAsync();await StartAsync(ct);}
        await TargetAsync(target,ct);
    }
    async Task TargetAsync(long target,CancellationToken ct) {
        bool fast=Supports("seek-fast-v1");
        bool accelerated=!fast && Supports("pacing-v1");
        try {
            if(accelerated)await EngineSpeedAsync(4,ct);
            await SendAsync(fast?"seek":"target",target,s=>s.Completed==target&&s.Phase.EndsWith("paused"),ct);
        }
        catch(OperationCanceledException){if(IsRunning)await PauseAsync(CancellationToken.None);throw;}
        finally {if(accelerated && IsRunning)await EngineSpeedAsync(PlaybackSpeed,CancellationToken.None);}
    }
    async Task SealLiveAsync(CancellationToken ct) {
        if(!IsLive)return;
        await PauseAsync(ct);var saved=await StopAsync();LastRecoveryPath=saved;source=saved;await StartAsync(ct);
    }
    public async Task SwitchToPlaybackAsync(CancellationToken ct=default) {
        if(!IsLive)return;await PauseAsync(ct);long frame=ReadState()!.Completed-1;
        await SealLiveAsync(ct);await SeekAsync(Math.Max(0,frame),ct);
    }
    public async Task ReplayAllAsync(CancellationToken ct=default){await SeekAsync(0,ct);if(ReadState()!.Total>1)await ResumeAsync(PlaybackSpeed,ct);}
    public async Task TakeoverAsync(CancellationToken ct=default) {if(IsLive)return;await PauseAsync(ct);Input(0);await SendAsync("takeover",0,s=>s.Phase=="live-paused",ct);IsLive=true;}
    public async Task<Replay> CaptureRecordingAsync(CancellationToken ct=default) {
        if(!IsRunning)return Replay.Load(CurrentRecordingPath);
        await PauseAsync(ct);
        if(!IsLive)return Replay.Load(source??throw new InvalidOperationException("尚无录制来源。"));
        if(!Supports("snapshot-v1"))throw new NotSupportedException("此游戏版本不支持保持进程保存，请更换新版游戏程序。");
        var acknowledged=await SendAsync("snapshot",0,state=>state.Phase=="live-paused",ct);
        var saved=Replay.Load(Path.Combine(bridge,"recording.krec"));
        if(saved.Identity!=identity || saved.Count!=acknowledged.Completed)throw new InvalidDataException("保存副本与引擎帧边界不一致。");
        return saved;
    }
    public async Task<bool> FocusGameAsync(CancellationToken ct=default) {
        if(!ExternalWindow || !Supports("focus-v1"))return false;
        await SendAsync("focus",0,state=>true,ct);return true;
    }
    public async Task<string> StopAsync() {
        if(process is null)return BranchPath;
        try {
            long id=++sequence;Mailbox(Path.Combine(bridge,"command.txt"),s=>{using var w=new StreamWriter(s,leaveOpen:true);w.Write($"{id} stop 0\n");});
            using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(15));await process.WaitForExitAsync(limit.Token);
        }catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw new IOException("游戏未正常结束，录制文件可能未完成。");}
        finally{process.Dispose();process=null;}
        _=Replay.Load(BranchPath);return BranchPath;
    }
    public async ValueTask DisposeAsync(){if(process is null)return;try{await StopAsync();}catch{if(process is not null){if(!process.HasExited)process.Kill(true);process.Dispose();process=null;}}}
}
