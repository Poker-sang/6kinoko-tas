using Avalonia; using Avalonia.Controls; using Avalonia.Input; using Avalonia.Interactivity; using Avalonia.Media.Imaging; using Avalonia.Platform; using Avalonia.Platform.Storage; using Avalonia.Threading;
using KinokoTAS.Core; using System.Runtime.InteropServices; using System.Text.Json;
namespace KinokoTAS.App;
public partial class MainWindow {
    FileGameSession? game;
    readonly DispatcherTimer gameTimer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    readonly HashSet<Key> gameKeys=[];
    WriteableBitmap? bitmap;
    bool gameCommand;
    CancellationTokenSource? seeking;
    long previewCount=-1;
    nint orderedGameWindow;
    int orderedProcess;
    string? gameExe;
    string? operationError;
    static string SettingsPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KinokoTAS","settings.json");
    void SaveSettings(){Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);File.WriteAllText(SettingsPath,JsonSerializer.Serialize(new{GameExe=gameExe,Embedded=EmbeddedOption.IsChecked==true}));}
    async void ChangeGameClick(object? sender,RoutedEventArgs e)=>await Operate(async()=>{gameExe=null;await PickGame();});
    void InitializeGamePanel() {
        try {if(File.Exists(SettingsPath)){using var settings=JsonDocument.Parse(File.ReadAllText(SettingsPath));gameExe=settings.RootElement.GetProperty("GameExe").GetString();EmbeddedOption.IsChecked=settings.RootElement.GetProperty("Embedded").GetBoolean();}}catch{gameExe=null;}
        gameTimer.Tick+=(_,_)=>RefreshGameView();gameTimer.Start();
        Deactivated+=async (_,_)=>await PauseOnDeactivateAsync();
        Closed+=(_,_)=>{gameTimer.Stop();bitmap?.Dispose();};
    }
    public async Task PauseOnDeactivateAsync() {
        gameKeys.Clear();var active=game;
        if(active is null)return;
        if(active.ExternalWindow){active.Input(0);return;}
        active.Input(0);try{await active.PauseAsync(default);}catch(Exception ex){GameStatus.Text=ex.Message;}
    }
    async Task Operate(Func<Task> action) {
        Timeline.FinishPainting();
        if(gameCommand)return;gameCommand=true;
        try{operationError=null;await action();}catch(OperationCanceledException){operationError="操作已取消，原录制与草稿保留。";GameStatus.Text=operationError;}catch(Exception ex){operationError="操作失败："+ex.Message;GameStatus.Text=operationError;}
        finally{gameCommand=false;}
    }
    async Task<string?> PickGame() {
        if(gameExe is not null && File.Exists(gameExe))return gameExe;
        var paths=await StorageProvider.OpenFilePickerAsync(new(){Title="选择游戏程序 kinoko_modern_gpu.exe（不是 .ktas 录制项目）",AllowMultiple=false});
        if(paths.Count==0)return null;gameExe=paths[0].TryGetLocalPath();SaveSettings();UpdateGamePath();return gameExe;
    }
    async Task<string?> InitialDirectory(string? replay) {
        if(replay is not null) {
            if(Path.GetExtension(replay).Equals(".krec",StringComparison.OrdinalIgnoreCase) && RecordingPackage.IsPackage(replay)) {
                var package=RecordingPackage.Load(replay);var directory=Path.Combine(AppContext.BaseDirectory,"sessions","unpacked-"+Guid.NewGuid().ToString("N"),"initial");package.ExtractInitial(directory);return directory;
            }
            var parent=Directory.GetParent(replay);
            foreach(var root in new[]{parent?.FullName,parent?.Parent?.FullName}) {
                if(root is not null && Directory.Exists(Path.Combine(root,"initial")))return Path.Combine(root,"initial");
            }
        }
        var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title="选择录制开始前的 initial 存档目录（没有存档时选择空目录）",AllowMultiple=false});
        return folders.Count==0?null:folders[0].TryGetLocalPath();
    }
    static void VerifyInitial(string initial,string exe) {
        var root=Directory.GetParent(initial)!.FullName;
        string manifest=Path.Combine(root,"session-manifest.json");
        if(File.Exists(manifest)) {
            using var json=JsonDocument.Parse(File.ReadAllText(manifest));
            foreach(var entry in json.RootElement.GetProperty("files").EnumerateArray()) {
                string name=entry.GetProperty("path").GetString()!;
                var path=Path.GetFullPath(Path.Combine(initial,name));
                if(!path.StartsWith(Path.GetFullPath(initial)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||FileGameSession.HashFile(path)!=entry.GetProperty("sha256").GetString())throw new InvalidDataException("初始存档/配置校验失败。");
            }
            foreach(var entry in json.RootElement.GetProperty("runtime").EnumerateArray()) {
                string name=entry.GetProperty("path").GetString()!;
                if(name.StartsWith("6kinoko_",StringComparison.OrdinalIgnoreCase) && FileGameSession.HashFile(Path.Combine(Path.GetDirectoryName(exe)!,name))!=entry.GetProperty("sha256").GetString())throw new InvalidDataException("游戏资源与录制不匹配。");
            }
        }
        manifest=Path.Combine(root,"session.json");
        if(File.Exists(manifest)) {
            var metadata=JsonSerializer.Deserialize<SessionMetadata>(File.ReadAllText(manifest))!;
            foreach(var entry in metadata.InitialFiles)if(Path.GetFileName(entry.Path)!=entry.Path || FileGameSession.HashFile(Path.Combine(initial,entry.Path))!=entry.Sha256)throw new InvalidDataException("分支初始存档校验失败。");
        }
    }
    async Task<bool> ConfirmReplayEngine() {
        return await ConfirmContentAsync("验证录制","将逐帧验证录制，出现不同步时停止。输入草稿不会改变本次回放。","开始回放","取消");
    }
    async Task LaunchGame(bool recording) {
        var exe=await PickGame();if(exe is null)return;SaveSettings();
        string? replay=null;string initial;string identity;
        var sessions=Path.Combine(AppContext.BaseDirectory,"sessions");Directory.CreateDirectory(sessions);
        if(recording) {
            initial=Path.Combine(sessions,"empty-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(initial);
            // Stable identity for this runtime and the empty initial state.
            var text=FileGameSession.HashFile(exe);
            foreach(var name in new[]{"6kinoko_a.dat","6kinoko_b.dat","6kinoko_c.dat"})text+=FileGameSession.HashFile(Path.Combine(Path.GetDirectoryName(exe)!,name));
            identity=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        }else {
            if(Project is null||Project.Source.Count==0)throw new InvalidOperationException("先打开一个非空录制。");
            initial=await InitialDirectory(sourcePath)??"";if(initial.Length==0)return;VerifyInitial(initial,exe);
            if(!await ConfirmReplayEngine())return;
            replay=Path.Combine(sessions,"source-"+Guid.NewGuid().ToString("N")+".krec");Project.ExportSource(replay);identity=Project.Source.Identity;
        }
        var session=new FileGameSession(exe,Path.Combine(sessions,"session-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]),replay,initial,identity,EmbeddedOption.IsChecked!=true);
        await AttachGameSessionAsync(session);
    }
    public async Task AttachGameSessionAsync(FileGameSession session) {
        await EndGame(false);recoveries.Clear();
        GameStatus.Text="启动引擎，等待首帧…";previewCount=-1;gameKeys.Clear();
        try {await session.StartAsync();if(selectedSpeed!=1)await session.SetSpeedAsync(selectedSpeed);}
        catch {await session.DisposeAsync();EngineLabel.Text="启动失败";throw;}
        if(session.IsLive){
            recordingSavePath=null;
            lastSaved=null;OpenSavedButton.IsEnabled=false;SavedPathLabel.Text="保存位置：尚未保存";
            if(Project is not null)Project.Changed-=OnChanged;
            Project=null;Timeline.Project=null;sourcePath=null;dirty=false;documentUnsaved=true;savedLiveBranch=null;
            bookmarks.Clear();bookmarkFile=Path.Combine(session.SessionDirectory,"bookmarks.json");
        }
        liveTimeline=new();timelineSource=null;
        game=session;GameImage.IsVisible=!session.ExternalWindow;ExternalHint.IsVisible=session.ExternalWindow;EmbeddedOption.IsEnabled=false;EngineLabel.Text=session.IsLive?"新录制 · 已暂停":"回放 · 已暂停";
    }
    uint CurrentMask() {
        if(!GamePanel.IsFocused)return 0;uint m=0;
        void Set(bool value,params int[] actions){if(value)foreach(int action in actions)m|=1u<<action;}
        Set(gameKeys.Contains(Key.Left),0,15);Set(gameKeys.Contains(Key.Right),1,16);
        Set(gameKeys.Contains(Key.Up),2,8,9,17);Set(gameKeys.Contains(Key.Down),3,10,18);
        Set(gameKeys.Contains(Key.Z),4,11);Set(gameKeys.Contains(Key.X),5,6,7);
        Set(gameKeys.Contains(Key.A),12,13);Set(gameKeys.Contains(Key.C),14);
        Set(gameKeys.Contains(Key.Space),4);Set(gameKeys.Contains(Key.Enter),11);Set(gameKeys.Contains(Key.Escape),13);return m;
    }
    public void RefreshGameView() {
        UpdatePlaybackButtons(null);
        BindLayoutProject();
        CancelOperationButton.IsEnabled=seeking is not null;
        ApplyEditsButton.IsEnabled=!gameCommand && !busy && Project?.InvalidFrom is not null && game?.IsLive!=true;
        RestoreOverwriteButton.IsEnabled=!gameCommand && !busy && game is not null && recoveries.Count>0;
        RestartGameButton.IsEnabled=game is not null?!game.IsRunning:Project is not null;
        if(game is null)return;
        if(!game.IsRunning){EngineLabel.Text="游戏已关闭";GameStatus.Text=operationError??"点击“重新启动游戏”恢复当前录制。";return;}
        try {
            if(game.ExternalWindow && OperatingSystem.IsWindows()) {
                if(orderedProcess!=game.GameProcessId){orderedProcess=game.GameProcessId;orderedGameWindow=0;}
                var handle=game.GameWindowHandle;
                if(handle!=0 && handle!=orderedGameWindow && GameWindowOrder.Attach(handle,TryGetPlatformHandle()?.Handle??0))orderedGameWindow=handle;
            }
            game.Input(CurrentMask());var state=game.ReadState();if(state is null)return;
            UpdatePlaybackButtons(state);
            RefreshTimeline(state);RecordToggle.IsChecked=game.IsLive;
            var frame=game.ExternalWindow?null:game.ReadPreview();
            if(frame is not null && frame.Completed!=previewCount) {
                if(bitmap is null||bitmap.PixelSize.Width!=frame.Width||bitmap.PixelSize.Height!=frame.Height){bitmap?.Dispose();bitmap=new(new(frame.Width,frame.Height),new(96,96),PixelFormat.Rgba8888,AlphaFormat.Opaque);GameImage.Source=bitmap;}
                using(var buffer=bitmap.Lock())for(int row=0;row<frame.Height;row++)Marshal.Copy(frame.Pixels,row*frame.Width*4,buffer.Address+row*buffer.RowBytes,frame.Width*4);
                previewCount=frame.Completed;GameImage.InvalidateVisual();
            }
            var phase=state.Phase switch {"paused" or "live-paused"=>"已暂停","live"=>"正在录制","playing"=>"正在回放","finished"=>"已结束","failed"=>"运行失败",_=>"正在启动"};
            EngineLabel.Text=$"{phase} · 已完成 {state.Completed} 帧";
            FrameLabel.Text=Math.Max(0,state.Completed-1).ToString("D6");FrameDetails.Text=$"时间 {Math.Max(0,state.Completed-1)/60.0:F2} 秒";
            if(seeking is not null){GameStatus.Text=operationProgress??$"正在定位：{state.Completed:N0} / {seekTarget:N0} 帧（Esc 取消）";return;}
            if(operationError is not null){GameStatus.Text=operationError;return;}
            if(game.ExternalWindow){GameStatus.Text=$"{phase} · 在独立游戏窗口操作，F9 播放/暂停，F10 前进一帧。切换窗口不会自动暂停。";return;}
            GameStatus.Text=$"画面帧 {previewCount-1} / 逻辑帧 {state.Completed-1} · "+(game.IsLive?"接管输入：方向键、Z 跳跃/确认、X 攻击/加速/搬运、A 暂停、C 道具、F10 执行一帧。点击画面获取焦点。":"双击时间轴帧号或使用定位按钮查看。回退会从头重播，请等待。");
        }catch(Exception ex){EngineLabel.Text="引擎错误";GameStatus.Text=ex.Message;}
    }
    void UpdatePlaybackButtons(SessionState? state) {
        bool running=state?.Phase is "playing" or "live";
        PlayGameButton.IsVisible=!running;PauseGameButton.IsVisible=running;
        PlayGameButton.IsEnabled=game?.IsRunning==true && state?.Phase.EndsWith("paused")==true
            && (game.IsLive || state.Completed<state.Total) && !gameCommand && !busy && seeking is null
            && Project?.HasLayoutChanges!=true;
        PauseGameButton.IsEnabled=running;
    }
    async Task SeekGame(int frame) {if(game is not null)await Operate(async()=>{
        if(Project?.HasLayoutChanges==true)throw new InvalidOperationException("帧布局已修改，请先应用修改，再定位游戏画面。");
        using var token=new CancellationTokenSource();seeking=token;seekTarget=frame+1;
        try{GameStatus.Text="正在重播定位…";await game.SeekAsync(frame,token.Token);UpdatePlaybackProject();}finally{seeking=null;RefreshGameView();}
    });}
    public Task StopGameSessionAsync()=>EndGame(false);
    async Task EndGame(bool load) {
        if(game is null)return;var old=game;game=null;gameKeys.Clear();EmbeddedOption.IsEnabled=true;
        try {string branch=old.CurrentRecordingPath;await old.StopAsync();
            var savedReplay=Replay.Load(branch);var retained=bookmarks.Where(m=>m.Frame<savedReplay.Count).ToArray();
            RecordingLibrary.SaveBookmarks(BookmarkCache(savedReplay),retained);
            RecordingLibrary.SaveBookmarks(branch+".bookmarks.json",retained);
            EngineLabel.Text="录制已保存";GameStatus.Text=branch;ShowSaved(branch);
            if(load)await OpenPathAsync(branch);
        }
        finally {await old.DisposeAsync();}
    }
    
    public async Task NewRecordingAsync(){if(await ConfirmSaveChangesAsync())await LaunchGame(true);}
    async void NewRecordingClick(object? s,RoutedEventArgs e)=>await Operate(NewRecordingAsync);
    async void PlayGameClick(object? s,RoutedEventArgs e)=>await Operate(async()=>{RequireAppliedLayout();if(game is null)throw new InvalidOperationException("先启动会话。");await game.ResumeAsync(selectedSpeed,default);GamePanel.Focus();});
    async void PauseGameClick(object? s,RoutedEventArgs e){seeking?.Cancel();try{if(game is not null)await game.PauseAsync(default);}catch(Exception ex){GameStatus.Text=ex.Message;}}
    async void StepGameClick(object? s,RoutedEventArgs e)=>await Operate(async()=>{RequireAppliedLayout();if(game is not null){uint mask=CurrentMask();await game.StepAsync(Enumerable.Range(0,19).Select(i=>(mask&(1u<<i))!=0).ToArray(),default);}});
    async void TakeoverClick(object? s,RoutedEventArgs e)=>await Operate(ToggleRecordingAsync);
    
    void GamePointerPressed(object? s,PointerPressedEventArgs e){GamePanel.Focus();e.Handled=true;}
    void GameKeyDown(object? s,KeyEventArgs e){if(e.Handled)return;gameKeys.Add(e.Key);e.Handled=true;}
    void GameKeyUp(object? s,KeyEventArgs e){gameKeys.Remove(e.Key);e.Handled=true;}
    void GameLostFocus(object? s,RoutedEventArgs e){gameKeys.Clear();game?.Input(0);}
}
