using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KinokoTAS.Core;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private async Task OpenPicker()
    {
        if (_busy || _gameCommand)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "打开录制或 TAS 项目",
            AllowMultiple = false,
            FileTypeFilter = [new("6kinoko TAS") { Patterns = ["*.krec", "*.ktas"] }]
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } p)
        {
            var previous = Project;
            await OpenPathAsync(p);
            if (!ReferenceEquals(previous, Project) && _sourcePath == Path.GetFullPath(p) && Project?.Source.Count > 0)
                await Operate(() => LaunchGame(false));
        }
    }

    public async Task OpenPathAsync(string path)
    {
        Timeline.FinishPainting();
        if (_busy || !await ConfirmSaveChangesAsync())
            return;

        _busy = true;
        StatusLabel.Text = "正在校验并读取录制…";
        try
        {
            var loaded = await Task.Run(() =>
                Path.GetExtension(path).Equals(".ktas", StringComparison.OrdinalIgnoreCase)
                    ? TasProject.Load(path)
                    : new TasProject(Replay.Load(path), Path.GetFileName(path)));
            if (_game is not null)
                await EndGame(false);

            Project?.Changed -= OnChanged;

            _bookmarkLayouts.Clear();
            _recoveries.Clear();
            Project = loaded;
            _sourcePath = Path.GetFullPath(path);
            _dirty = false;
            _documentUnsaved = false;
            Project.Changed += OnChanged;
            RecordingSavePath = Path.GetExtension(path).Equals(".krec", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(path)
                : null;
            LoadBookmarksFor(Project.Source, _sourcePath, Project.FrameCount);
            Timeline.LiveMasks = null;
            Timeline.Project = Project;
            Timeline.FirstFrame = 0;
            Timeline.SelectedFrame = 0;
            FrameScroll.Value = 0;
            FrameScroll.Maximum = Math.Max(0, Project.FrameCount - 1);
            FrameScroll.ViewportSize = 20;
            JumpFrame.Maximum = RangeStart.Maximum = RangeEnd.Maximum = Math.Max(0, Project.FrameCount - 1);
            RangeStart.Value = RangeEnd.Value = JumpFrame.Value = 0;
            SaveButton.IsEnabled = ExportButton.IsEnabled = true;
            HoldButton.IsEnabled = ReleaseButton.IsEnabled = Project.Source.Count > 0;
            StatusLabel.Text = $"已打开 {Project.FrameCount:N0} 帧 · 黄色标记表示待应用编辑 · Ctrl+S 保存 .krec";
            Refresh();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "打开失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SaveProject(bool duringConfirmation = false)
    {
        if (Project is null || _busy || (_gameCommand && !duringConfirmation))
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "导出编辑草稿（不是可回放录制）",
            SuggestedFileName = Path.GetFileNameWithoutExtension(Project.SourceName) + ".ktas",
            DefaultExtension = "ktas",
            FileTypeChoices = [new("编辑草稿（待应用）") { Patterns = ["*.ktas"] }]
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;

        if (!Path.GetExtension(path).Equals(".ktas", StringComparison.OrdinalIgnoreCase))
        {
            StatusLabel.Text = "项目必须使用 .ktas 扩展名。";
            return;
        }

        try
        {
            await SaveDraftToAsync(path);
            StatusLabel.Text = "编辑草稿已导出（含初始存档和重点）：" + path + "；录制仍需用 Ctrl+S 保存为 .krec。";
            Refresh();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "草稿导出失败：" + ex.Message;
        }
    }

    public async Task SaveDraftToAsync(string path)
    {
        if (Project is null)
            throw new InvalidOperationException("先打开录制。");

        if (!Path.GetExtension(path).Equals(".ktas", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("草稿必须使用 .ktas 扩展名。");

        var initial = _game is not null
            ? Path.Combine(_game.SessionDirectory, "initial")
            : await InitialDirectory(_sourcePath);
        if (initial is null)
            return;

        Project.Save(path, initial, _bookmarks);
    }

    private async void ExportClick(object? s, RoutedEventArgs e)
    {
        if (Project is null || _busy || _gameCommand)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "导出未修改的原始录制",
            SuggestedFileName = "original.krec",
            DefaultExtension = "krec"
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;

        if (Path.GetFullPath(path).Equals(_sourcePath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            StatusLabel.Text = "请使用不同路径，原始文件保持只读。";
            return;
        }

        try
        {
            Project.ExportSource(path);
            StatusLabel.Text = "已导出原始录制（不包含项目中的输入编辑）。";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "导出失败：" + ex.Message;
        }
    }

    private async void OpenClick(object? s, RoutedEventArgs e) => await OpenPicker();

    private async void SaveClick(object? s, RoutedEventArgs e) => await SaveProject();

    private async void SaveRecordingClick(object? s, RoutedEventArgs e) => await Operate(() => SaveRecordingAsync());

    private async void SaveRecordingAsClick(object? s, RoutedEventArgs e) =>
        await Operate(() => SaveRecordingAsync(true));

    public async Task SaveRecordingAsync(bool saveAs = false)
    {
        if (_game?.IsRunning == true)
            await _game.PauseAsync(CancellationToken.None);

        if (_game is null && Project is null)
            throw new InvalidOperationException("先新建或打开录制。");

        var output = saveAs ? null : RecordingSavePath;
        if (output is null)
        {
            var selected = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = saveAs ? "另存为录制" : "保存录制",
                SuggestedFileName =
                    RecordingSavePath is null
                        ? "录制-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".krec"
                        : Path.GetFileName(RecordingSavePath),
                DefaultExtension = "krec",
                FileTypeChoices = [new("完整录制") { Patterns = ["*.krec"] }]
            });
            output = selected?.TryGetLocalPath();
            if (output is null)
                return;
        }

        await SaveRecordingToAsync(output);
    }

    public async Task SaveRecordingToAsync(string output)
    {
        if (_game is null && Project is null)
            throw new InvalidOperationException("先新建或打开录制。");

        if (!Path.GetExtension(output).Equals(".krec", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请使用 .krec 扩展名。");

        output = Path.GetFullPath(output);
        if (_game is not null && output.StartsWith(
                Path.GetFullPath(_game.SessionDirectory) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("请选择会话目录外的保存位置。");

        // Saving always means a playable .krec, never an implicit .ktas fallback.
        // Simulation/verification must succeed before the destination is touched.
        if (Project?.InvalidFrom is not null)
            await ApplyEditsAsync();

        var initial = _game is not null
            ? Path.Combine(_game.SessionDirectory, "initial")
            : await InitialDirectory(_sourcePath);
        if (initial is null)
            return;

        Replay replay;
        if (_game is not null)
            replay = await _game.CaptureRecordingAsync();

        else
            replay = Project!.Source;

        RecordingPackage.Save(output, replay, initial, _bookmarks.Where(m => m.Frame < replay.Count));
        ShowSaved(output);
        RecordingSavePath = Path.GetFullPath(output);
        _dirty = false;
        _documentUnsaved = false;
        _saveRevision++;
        _savedLiveBranch = _game?.BranchPath;
        _savedLiveFrames = replay.Count;
        Refresh();
        StatusLabel.Text = "已保存 .krec：录制、初始存档及重点。";
    }

    private void OpenSavedClick(object? s, RoutedEventArgs e)
    {
        try
        {
            if (_lastSaved is not null)
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(_lastSaved)!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "打开目录失败：" + ex.Message;
        }
    }
}
