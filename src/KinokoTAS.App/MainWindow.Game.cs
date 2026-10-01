using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KinokoTAS.Core;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private FileGameSession? _game;
    private FileGameSession? _playbackSession;
    private SessionState? _playbackState;
    private readonly DispatcherTimer _gameTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly HashSet<Key> _gameKeys = [];
    private WriteableBitmap? _bitmap;
    private bool _gameCommand;
    private CancellationTokenSource? _seeking;
    private long _previewCount = -1;
    private nint _orderedGameWindow;
    private int _orderedProcess;
    private string? _gameExe;
    private string? _operationError;

    private static string SettingsPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var current = Path.Combine(root, "6kinokoTAS");
            var legacy = Path.Combine(root, "KinokoTAS");
            if (!Directory.Exists(current) && Directory.Exists(legacy))
            {
                Directory.CreateDirectory(current);
                foreach (var file in Directory.GetFiles(legacy, "*", SearchOption.AllDirectories))
                {
                    var destination = Path.Combine(current, Path.GetRelativePath(legacy, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination, false);
                }
            }

            return Path.Combine(current, "settings.json");
        }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath,
            JsonSerializer.Serialize(new EditorSettings(_gameExe, EmbeddedOption.IsChecked == true, _ffmpegExe),
                SettingsJsonContext.Default.EditorSettings));
    }

    private async void ChangeGameClick(object? sender, RoutedEventArgs e) => await Operate(async () =>
    {
        _gameExe = null;
        await PickGame();
    });

    private void InitializeGamePanel()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var settings = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                _gameExe = settings.RootElement.GetProperty("GameExe").GetString();
                EmbeddedOption.IsChecked = settings.RootElement.GetProperty("Embedded").GetBoolean();
                if (settings.RootElement.TryGetProperty("FfmpegExe", out var encoder))
                    _ffmpegExe = encoder.GetString();
            }
        }
        catch
        {
            _gameExe = null;
        }

        _gameTimer.Tick += async (_, _) =>
        {
            RefreshGameView();
            await ProcessGameRequestsAsync();
        };
        _gameTimer.Start();
        Deactivated += async (_, _) => await PauseOnDeactivateAsync();
        Closed += (_, _) =>
        {
            _gameTimer.Stop();
            _bitmap?.Dispose();
        };
    }

    public async Task PauseOnDeactivateAsync()
    {
        _gameKeys.Clear();
        var active = _game;
        if (active is null)
            return;

        if (active.ExternalWindow)
        {
            active.Input(0);
            return;
        }

        active.Input(0);
        try
        {
            await active.PauseAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            GameStatus.Text = ex.Message;
        }
    }

    private async Task Operate(Func<Task> action)
    {
        Timeline.FinishPainting();
        if (_gameCommand)
            return;

        _gameCommand = true;
        try
        {
            _operationError = null;
            await action();
        }
        catch (OperationCanceledException)
        {
            _operationError = "操作已取消，原录制与草稿保留。";
            GameStatus.Text = _operationError;
        }
        catch (Exception ex)
        {
            _operationError = "操作失败：" + ex.Message;
            GameStatus.Text = _operationError;
        }
        finally
        {
            _gameCommand = false;
            RefreshGameView();
        }
    }

    public async Task ProcessGameRequestsAsync()
    {
        // Leave queued requests untouched during dialogs, seeks and save/edit
        // transactions. Operate guards reentrant timer ticks while awaiting IPC.
        if (_gameCommand || _busy || _seeking is not null || _dialogHost?.IsOpen == true)
            return;

        try
        {
            if (_game?.TakeRecordingToggleRequest() == true)
                await Operate(ToggleRecordingAsync);
        }
        catch (Exception ex)
        {
            _operationError = "游戏快捷键失败：" + ex.Message;
            GameStatus.Text = _operationError;
        }
    }

    private async Task<string?> PickGame()
    {
        if (_gameExe is not null && File.Exists(_gameExe))
            return _gameExe;

        var paths = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "选择游戏程序 kinoko_modern_gpu.exe（不是 .ktas 录制项目）",
            AllowMultiple = false
        });
        if (paths.Count == 0)
            return null;

        _gameExe = paths[0].TryGetLocalPath();
        SaveSettings();
        UpdateGamePath();
        return _gameExe;
    }

    private async Task<string?> InitialDirectory(string? replay)
    {
        if (replay is not null)
        {
            if (Path.GetExtension(replay).Equals(".ktas", StringComparison.OrdinalIgnoreCase))
            {
                var package = TasProject.Load(replay).EmbeddedRecording!;
                var directory = Path.Combine(AppContext.BaseDirectory, "sessions",
                    "unpacked-" + Guid.NewGuid().ToString("N"), "initial");
                package.ExtractInitial(directory);
                return directory;
            }

            if (Path.GetExtension(replay).Equals(".krec", StringComparison.OrdinalIgnoreCase) &&
                RecordingPackage.IsPackage(replay))
            {
                var package = RecordingPackage.Load(replay);
                var directory = Path.Combine(AppContext.BaseDirectory, "sessions",
                    "unpacked-" + Guid.NewGuid().ToString("N"), "initial");
                package.ExtractInitial(directory);
                return directory;
            }

            var parent = Directory.GetParent(replay);
            foreach (var root in new[] { parent?.FullName, parent?.Parent?.FullName })
                if (root is not null && Directory.Exists(Path.Combine(root, "initial")))
                    return Path.Combine(root, "initial");
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new()
        {
            Title = "选择录制开始前的 initial 存档目录（没有存档时选择空目录）",
            AllowMultiple = false
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private static void VerifyInitial(string initial, string exe)
    {
        var root = Directory.GetParent(initial)!.FullName;
        var manifest = Path.Combine(root, "session-manifest.json");
        if (File.Exists(manifest))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            foreach (var entry in json.RootElement.GetProperty("files").EnumerateArray())
            {
                var name = entry.GetProperty("path").GetString()!;
                var path = Path.GetFullPath(Path.Combine(initial, name));
                if (!path.StartsWith(Path.GetFullPath(initial) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) ||
                    FileGameSession.HashFile(path) != entry.GetProperty("sha256").GetString())
                    throw new InvalidDataException("初始存档/配置校验失败。");
            }

            foreach (var entry in json.RootElement.GetProperty("runtime").EnumerateArray())
            {
                var name = entry.GetProperty("path").GetString()!;
                if (name.StartsWith("6kinoko_", StringComparison.OrdinalIgnoreCase) &&
                    FileGameSession.HashFile(Path.Combine(Path.GetDirectoryName(exe)!, name)) !=
                    entry.GetProperty("sha256").GetString())
                    throw new InvalidDataException("游戏资源与录制不匹配。");
            }
        }

        manifest = Path.Combine(root, "session.json");
        if (File.Exists(manifest))
        {
            var metadata =
                JsonSerializer.Deserialize(File.ReadAllText(manifest), RecordingJsonContext.Default.SessionMetadata)!;
            foreach (var entry in metadata.InitialFiles)
                if (Path.GetFileName(entry.Path) != entry.Path ||
                    FileGameSession.HashFile(Path.Combine(initial, entry.Path)) != entry.Sha256)
                    throw new InvalidDataException("分支初始存档校验失败。");
        }
    }

    private async Task<bool> ConfirmReplayEngine()
    {
        return await ConfirmContentAsync("验证录制", "将逐帧验证录制，出现不同步时停止。输入草稿不会改变本次回放。", "开始回放", "取消");
    }

    private async Task LaunchGame(bool recording)
    {
        var exe = await PickGame();
        if (exe is null)
            return;

        SaveSettings();
        string? replay = null;
        string initial;
        string identity;
        var sessions = Path.Combine(AppContext.BaseDirectory, "sessions");
        Directory.CreateDirectory(sessions);
        if (recording)
        {
            initial = Path.Combine(sessions, "empty-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(initial);
            // Stable identity for this runtime and the empty initial state.
            var text = FileGameSession.HashFile(exe);
            foreach (var s in _SourceArray)
                text += FileGameSession.HashFile(Path.Combine(Path.GetDirectoryName(exe)!, s));

            identity = Convert
                .ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))
                .ToLowerInvariant();
        }
        else
        {
            if (Project is null || Project.Source.Count == 0)
                throw new InvalidOperationException("先打开一个非空录制。");

            initial = await InitialDirectory(_sourcePath) ?? "";
            if (initial.Length == 0)
                return;

            VerifyInitial(initial, exe);
            if (!await ConfirmReplayEngine())
                return;

            replay = Path.Combine(sessions, "source-" + Guid.NewGuid().ToString("N") + ".krec");
            Project.ExportSource(replay);
            identity = Project.Source.Identity;
        }

        var session = new FileGameSession(exe,
            Path.Combine(sessions,
                "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]),
            replay, initial, identity, EmbeddedOption.IsChecked != true);
        await AttachGameSessionAsync(session);
    }

    private static readonly string[] _SourceArray = ["6kinoko_a.dat", "6kinoko_b.dat", "6kinoko_c.dat"];

    public async Task AttachGameSessionAsync(FileGameSession session)
    {
        await EndGame(false);
        _recoveries.Clear();
        GameStatus.Text = "启动引擎，等待首帧…";
        _previewCount = -1;
        _gameKeys.Clear();
        try
        {
            await session.StartAsync();
            if (_selectedSpeed != 1)
                await session.SetSpeedAsync(_selectedSpeed);
        }
        catch
        {
            await session.DisposeAsync();
            EngineLabel.Text = "启动失败";
            throw;
        }

        if (session.IsLive)
        {
            RecordingSavePath = null;
            _lastSaved = null;
            OpenSavedButton.IsEnabled = false;
            SavedPathLabel.Text = "保存位置：尚未保存";
            Project?.Changed -= OnChanged;

            Project = null;
            Timeline.Project = null;
            _sourcePath = null;
            _dirty = false;
            _documentUnsaved = true;
            _savedLiveBranch = null;
            _bookmarks.Clear();
            _bookmarkFile = Path.Combine(session.SessionDirectory, "bookmarks.json");
        }

        _liveTimeline = new();
        _timelineSource = null;
        _game = session;
        GameImage.IsVisible = !session.ExternalWindow;
        ExternalHint.IsVisible = session.ExternalWindow;
        EmbeddedOption.IsEnabled = false;
        EngineLabel.Text = session.IsLive ? "新录制 · 已暂停" : "回放 · 已暂停";
    }

    private uint CurrentMask()
    {
        if (!GamePanel.IsFocused)
            return 0;

        uint m = 0;

        Set(_gameKeys.Contains(Key.Left), 0, 15);
        Set(_gameKeys.Contains(Key.Right), 1, 16);
        Set(_gameKeys.Contains(Key.Up), 2, 8, 9, 17);
        Set(_gameKeys.Contains(Key.Down), 3, 10, 18);
        Set(_gameKeys.Contains(Key.Z), 4, 11);
        Set(_gameKeys.Contains(Key.X), 5, 6, 7);
        Set(_gameKeys.Contains(Key.A), 12, 13);
        Set(_gameKeys.Contains(Key.C), 14);
        Set(_gameKeys.Contains(Key.Space), 4);
        Set(_gameKeys.Contains(Key.Enter), 11);
        Set(_gameKeys.Contains(Key.Escape), 13);
        return m;

        void Set(bool value, params int[] actions)
        {
            if (value)
                foreach (var action in actions)
                    m |= 1u << action;
        }
    }

    public void RefreshGameView()
    {
        if (!ReferenceEquals(_playbackSession, _game))
        {
            _playbackSession = _game;
            _playbackState = null;
        }

        BindLayoutProject();
        CancelOperationButton.IsEnabled = _seeking is not null;
        RefreshEditWorkflow();
        RestoreOverwriteButton.IsEnabled = !_gameCommand && !_busy && _game is not null && _recoveries.Count > 0;
        RestartGameButton.IsEnabled = !_gameCommand && !_busy && Project?.InvalidFrom is null &&
                                      (!_game?.IsRunning ?? Project is not null);
        RestartGameButton.Label = "重新启动游戏";
        if (_game is null)
        {
            UpdatePlaybackButtons(null);
            return;
        }

        if (!_game.IsRunning)
        {
            _playbackState = null;
            UpdatePlaybackButtons(null);
            EngineLabel.Text = "游戏已关闭";
            GameStatus.Text = _operationError ?? "点击“重新启动游戏”恢复当前录制。";
            return;
        }

        try
        {
            if (_game.ExternalWindow && OperatingSystem.IsWindows())
            {
                if (_orderedProcess != _game.GameProcessId)
                {
                    _orderedProcess = _game.GameProcessId;
                    _orderedGameWindow = 0;
                }

                var handle = _game.GameWindowHandle;
                if (handle != 0 && handle != _orderedGameWindow &&
                    GameWindowOrder.Attach(handle, TryGetPlatformHandle()?.Handle ?? 0))
                    _orderedGameWindow = handle;
            }

            _game.Input(CurrentMask());
            var state = _game.ReadState();
            if (state is not null)
                _playbackState = state;

            UpdatePlaybackButtons(_playbackState);
            if (state is null)
                return;

            RefreshTimeline(state);
            RecordToggle.IsChecked = _game.IsLive;
            var frame = _game.ExternalWindow ? null : _game.ReadPreview();
            if (frame is not null && frame.Completed != _previewCount)
            {
                if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width ||
                    _bitmap.PixelSize.Height != frame.Height)
                {
                    _bitmap?.Dispose();
                    _bitmap = new(new(frame.Width, frame.Height), new(96, 96), PixelFormat.Rgba8888,
                        AlphaFormat.Opaque);
                    GameImage.Source = _bitmap;
                }

                using (var buffer = _bitmap.Lock())
                    for (var row = 0; row < frame.Height; row++)
                        Marshal.Copy(frame.Pixels, row * frame.Width * 4, buffer.Address + row * buffer.RowBytes,
                            frame.Width * 4);

                _previewCount = frame.Completed;
                GameImage.InvalidateVisual();
            }

            var phase = state.Phase switch
            {
                "paused" or "live-paused" => "已暂停",
                "live" => "正在录制",
                "playing" => "正在回放",
                "finished" => "已结束",
                "failed" => "运行失败",
                _ => "正在启动"
            };
            EngineLabel.Text = $"{phase} · 已完成 {state.Completed} 帧";
            FrameLabel.Text = Math.Max(0, state.Completed - 1).ToString("D6");
            FrameDetails.Text = $"时间 {Math.Max(0, state.Completed - 1) / 60.0:F2} 秒";
            if (_seeking is not null)
            {
                GameStatus.Text = _operationProgress ?? $"正在定位：{state.Completed:N0} / {_seekTarget:N0} 帧（Esc 取消）";
                return;
            }

            if (_operationError is not null)
            {
                GameStatus.Text = _operationError;
                return;
            }

            if (_game.ExternalWindow)
            {
                GameStatus.Text = $"{phase} · 在独立游戏窗口操作，F9 播放/暂停，F10 前进一帧。切换窗口不会自动暂停。";
                return;
            }

            GameStatus.Text = $"画面帧 {_previewCount - 1} / 逻辑帧 {state.Completed - 1} · " + (_game.IsLive
                ? "接管输入：方向键、Z 跳跃/确认、X 攻击/加速/搬运、A 暂停、C 道具、F10 执行一帧。点击画面获取焦点。"
                : "双击时间轴帧号或使用定位按钮查看。回退会从头重播，请等待。");
        }
        catch (Exception ex)
        {
            EngineLabel.Text = "引擎错误";
            GameStatus.Text = ex.Message;
        }
    }

    private void UpdatePlaybackButtons(SessionState? state)
    {
        var running = state?.Phase is "playing" or "live";
        PlayGameButton.IsVisible = !running;
        PauseGameButton.IsVisible = running;
        PlayGameButton.IsEnabled = _game is { IsRunning: true }
                                   && state is { Phase: "paused" or "live-paused" }
                                   && (_game.IsLive || state.Completed < state.Total)
                                   && !_gameCommand && !_busy && _seeking is null
                                   && Project?.InvalidFrom is null;
        PauseGameButton.IsEnabled = running;
    }

    private async Task SeekGame(int frame)
    {
        if (_game is not null)
            await Operate(async () =>
            {
                RequireAppliedLayout();
                using var token = new CancellationTokenSource();
                _seeking = token;
                _seekTarget = frame + 1;
                try
                {
                    GameStatus.Text = "正在重播定位…";
                    await _game.SeekAsync(frame, token.Token);
                    UpdatePlaybackProject();
                }
                finally
                {
                    _seeking = null;
                    RefreshGameView();
                }
            });
    }

    public Task StopGameSessionAsync() => EndGame(false);

    private async Task EndGame(bool load)
    {
        if (_game is null)
            return;

        var old = _game;
        _game = null;
        _gameKeys.Clear();
        EmbeddedOption.IsEnabled = true;
        try
        {
            var branch = old.CurrentRecordingPath;
            await old.StopAsync();
            var savedReplay = Replay.Load(branch);
            var retained = _bookmarks.Where(m => m.Frame < savedReplay.Count).ToArray();
            RecordingLibrary.SaveBookmarks(BookmarkCache(savedReplay), retained);
            RecordingLibrary.SaveBookmarks(branch + ".bookmarks.json", retained);
            EngineLabel.Text = "录制已保存";
            GameStatus.Text = branch;
            ShowSaved(branch);
            if (load)
                await OpenPathAsync(branch);
        }
        finally
        {
            await old.DisposeAsync();
        }
    }

    public async Task NewRecordingAsync()
    {
        if (await ConfirmSaveChangesAsync())
            await LaunchGame(true);
    }

    private async void NewRecordingClick(object? s, RoutedEventArgs e) => await Operate(NewRecordingAsync);

    private async Task FocusGameWindowAsync()
    {
        if (_game is null)
            return;

        if (!_game.ExternalWindow)
        {
            GamePanel.Focus();
            return;
        }

        GameWindowOrder.GrantActivation(_game.GameProcessId);
        await _game.FocusGameAsync();
        // A bridge acknowledgement confirms the request, not Windows foreground ownership.
        if (OperatingSystem.IsWindows() && !GameWindowOrder.Activate(_game.GameWindowHandle))
            GameStatus.Text = "Windows 未允许切换焦点，请点击游戏窗口。";
    }

    private async void PlayGameClick(object? s, RoutedEventArgs e) => await Operate(async () =>
    {
        RequireAppliedLayout();
        if (_game is null)
            throw new InvalidOperationException("先启动会话。");

        await _game.ResumeAsync(_selectedSpeed, CancellationToken.None);
        await FocusGameWindowAsync();
    });

    private async void PauseGameClick(object? s, RoutedEventArgs e)
    {
        _seeking?.Cancel();
        try
        {
            if (_game is not null)
                await _game.PauseAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            GameStatus.Text = ex.Message;
        }
        finally
        {
            RefreshGameView();
        }
    }

    private async void StepGameClick(object? s, RoutedEventArgs e) => await Operate(async () =>
    {
        RequireAppliedLayout();
        if (_game is not null)
        {
            var mask = CurrentMask();
            await _game.StepAsync([.. Enumerable.Range(0, 19).Select(i => (mask & (1u << i)) != 0)],
                CancellationToken.None);
        }
    });

    private async void TakeoverClick(object? s, RoutedEventArgs e) => await Operate(ToggleRecordingAsync);

    private void GamePointerPressed(object? s, PointerPressedEventArgs e)
    {
        GamePanel.Focus();
        e.Handled = true;
    }

    private void GameKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        _gameKeys.Add(e.Key);
        e.Handled = true;
    }

    private void GameKeyUp(object? s, KeyEventArgs e)
    {
        _gameKeys.Remove(e.Key);
        e.Handled = true;
    }

    private void GameLostFocus(object? s, RoutedEventArgs e)
    {
        _gameKeys.Clear();
        _game?.Input(0);
    }
}
