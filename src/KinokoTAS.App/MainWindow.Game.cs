using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Interactivity;
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
    private bool _gameCommand;
    private CancellationTokenSource? _seeking;
    private nint _orderedGameWindow;
    private int _orderedProcess;
    private string? _gameExe;
    private string? _operationError;

    private void SaveSettings() => _settings.Write(new(_gameExe, EmbeddedOption.IsChecked == true, _ffmpegExe));

    private async void ChangeGameClick(object? sender, RoutedEventArgs e) => await Operate(async () =>
    {
        _gameExe = null;
        await PickGame();
    });

    private void InitializeGamePanel()
    {
        if (_settings.Read() is { } settings)
        {
            _gameExe = settings.GameExe;
            EmbeddedOption.IsChecked = settings.Embedded;
            _ffmpegExe = settings.FfmpegExe;
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
            _preview.Dispose();
        };
    }

    public async Task PauseOnDeactivateAsync()
    {
        _input.Clear();
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
        if (_gameCommand || _busy || _seeking is not null || _dialogs.IsOpen)
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
        var initial = _workspace.FindInitialDirectory(replay);
        if (initial is not null)
            return initial;

        var folders = await StorageProvider.OpenFolderPickerAsync(new()
        {
            Title = "选择录制开始前的 initial 存档目录（没有存档时选择空目录）",
            AllowMultiple = false
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private async Task<bool> ConfirmReplayEngine()
    {
        return await ConfirmContentAsync("验证录制", "将逐帧验证录制，出现不同步时停止。输入草稿不会改变本次回放。", "开始回放", "取消");
    }

    private async Task LaunchGame(bool recording)
    {
        var executable = await PickGame();
        if (executable is null)
            return;

        SaveSettings();
        FileGameSession session;
        if (recording)
            session = _workspace.CreateRecordingSession(executable, EmbeddedOption.IsChecked != true);
        else
        {
            if (Project is null || Project.Source.Count == 0)
                throw new InvalidOperationException("先打开一个非空录制。");

            var initial = await InitialDirectory(_sourcePath);
            if (initial is null)
                return;

            RecordingWorkspace.VerifyInitial(initial, executable);
            if (!await ConfirmReplayEngine())
                return;

            session = _workspace.CreatePlaybackSession(executable, Project, initial, EmbeddedOption.IsChecked != true);
        }

        await AttachGameSessionAsync(session);
    }

    public async Task AttachGameSessionAsync(FileGameSession session)
    {
        await EndGame(false);
        _recoveries.Clear();
        GameStatus.Text = "启动引擎，等待首帧…";
        _preview.Reset();
        _input.Clear();
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
        _input.Clear();
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
}
