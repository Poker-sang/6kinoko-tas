using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KinokoTAS.Core;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private readonly ObservableCollection<FrameBookmark> _bookmarks = [];
    private string? _bookmarkFile, _lastSaved;

    public string? RecordingSavePath { get; private set; }

    private void InitializeLibrary()
    {
        BookmarkList.ItemsSource = _bookmarks;
        Timeline.Bookmarks = _bookmarks;
        _bookmarks.CollectionChanged += (_, _) => Timeline.InvalidateVisual();
        FrameScroll.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, _) =>
        {
            if (!_followScroll)
                FollowLatest.IsChecked = false;
        }, RoutingStrategies.Tunnel, true);
        UpdateGamePath();
        BookmarkList.AddHandler(InputElement.PointerPressedEvent, BookmarkPointerPressed, RoutingStrategies.Tunnel,
            true);
        BookmarkList.ContextRequested += (_, e) =>
        {
            if (BookmarkList.ContextMenu is { } menu)
            {
                menu.Open(BookmarkList);
                e.Handled = true;
            }
        };
    }

    private void BookmarkPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_busy || _gameCommand)
            return;

        var visual = e.Source as Avalonia.Visual;
        var item = visual as ListBoxItem ?? visual?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (item?.Content is not FrameBookmark mark)
            return;

        var buttons = e.GetCurrentPoint(BookmarkList).Properties;
        if (buttons.IsRightButtonPressed)
        {
            BookmarkList.SelectedItem = mark;
            BookmarkList.ContextMenu?.Close();
            var remove = new MenuItem { Header = "删除重点" };
            remove.Click += (_, _) => RemoveBookmark(mark);
            var rename = new MenuItem { Header = "重命名重点" };
            rename.Click += async (_, _) => await Operate(() => RenameBookmarkAsync(mark));
            BookmarkList.ContextMenu = new ContextMenu { ItemsSource = new[] { rename, remove } };
        }
        else if (buttons.IsLeftButtonPressed && e.ClickCount == 2)
        {
            BookmarkList.SelectedItem = mark;
            FollowLatest.IsChecked = false;
            SelectFrame(mark.Frame);
            e.Handled = true;
        }
    }

    private void UpdateGamePath()
    {
        GamePathLabel.Text = _gameExe is null ? "当前游戏：尚未选择（首次开始时选择一次）" : "当前游戏：" + _gameExe;
        ToolTip.SetTip(GamePathLabel, _gameExe);
    }

    private void ShowSaved(string path)
    {
        _lastSaved = path;
        SavedPathLabel.Text = "已保存：" + path;
        OpenSavedButton.IsEnabled = true;
    }

    private string BookmarkCache(Replay replay) => Path.Combine(Path.GetDirectoryName(_settings.Path)!, "bookmarks",
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(replay.Bytes.Span)) + ".json");

    private void LoadBookmarksFor(Replay replay, string path, int? frameCount = null)
    {
        _bookmarkFile = BookmarkCache(replay);
        _bookmarks.Clear();
        var stored = File.Exists(_bookmarkFile) ? _bookmarkFile : path + ".bookmarks.json";
        var packed = Path.GetExtension(path).Equals(".krec", StringComparison.OrdinalIgnoreCase) &&
                     RecordingPackage.IsPackage(path);
        var marks = path.EndsWith(".ktas", StringComparison.OrdinalIgnoreCase) ? Project!.EmbeddedBookmarks! :
            packed ? RecordingPackage.Load(path).Bookmarks :
            File.Exists(stored) ? RecordingLibrary.LoadBookmarks(stored, frameCount ?? replay.Count) : [];
        foreach (var mark in marks)
            _bookmarks.Add(mark);
    }

    private void PersistBookmarks()
    {
        _documentUnsaved = true;
        if (_bookmarkFile is not null && Project?.HasLayoutChanges != true)
            RecordingLibrary.SaveBookmarks(_bookmarkFile, _bookmarks);

        RefreshEditWorkflow();
    }

    public async Task AddBookmarkAsync(string name)
    {
        int frame;
        if (Project?.HasLayoutChanges == true)
            frame = Timeline.SelectedFrame;

        else if (_game is not null)
        {
            await _game.PauseAsync(CancellationToken.None);
            frame = checked((int) (_game.ReadState()!.Completed - 1));
        }
        else if (Project?.Source.Count > 0)
            frame = Timeline.SelectedFrame;

        else
            throw new InvalidOperationException("先新建或打开录制。");

        if (frame < 0)
            throw new InvalidOperationException("请等待首帧完成。");

        AddBookmarkAt(frame, name);
    }

    public void AddBookmarkAt(int frame, string name)
    {
        if (frame < 0 || (frame >= Timeline.FrameCount && _game is null))
            throw new ArgumentOutOfRangeException(nameof(frame));

        var mark = new FrameBookmark(frame,
            string.IsNullOrWhiteSpace(name) ? $"重点 {_bookmarks.Count + 1}" : name.Trim());
        _bookmarks.Add(mark);
        PersistBookmarks();
        BookmarkList.SelectedItem = mark;
        StatusLabel.Text = $"已添加书签：{mark}";
    }

    private async void AddBookmarkClick(object? s, RoutedEventArgs e) =>
        await Operate(() => AddBookmarkAsync(BookmarkName.Text ?? ""));

    private async void ReturnBookmarkClick(object? s, RoutedEventArgs e) => await Operate(ReturnSelectedBookmarkAsync);

    public async Task ReturnSelectedBookmarkAsync()
    {
        RequireAppliedLayout();
        if (BookmarkList.SelectedItem is not FrameBookmark mark)
            return;

        if (_game is null)
        {
            await LaunchGame(false);
            if (_game is null)
                return;
        }

        using var cancel = new CancellationTokenSource();
        _seeking = cancel;
        _seekTarget = mark.Frame + 1;
        try
        {
            GameStatus.Text = $"正在重播返回：{mark.Name}";
            await _game.SeekAsync(mark.Frame, cancel.Token);
            UpdatePlaybackProject();
            RefreshGameView();
            SelectFrame(mark.Frame);
        }
        finally
        {
            _seeking = null;
        }
    }

    private void RemoveBookmark(FrameBookmark mark)
    {
        if (_busy || _gameCommand)
            return;

        try
        {
            _bookmarks.Remove(mark);
            PersistBookmarks();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
    }

    public async Task RenameBookmarkAsync(FrameBookmark mark)
    {
        if (!_bookmarks.Contains(mark))
            return;

        var name = await PromptNameAsync("重命名重点", mark.Name);
        if (name is null || !_bookmarks.Contains(mark))
            return;

        var index = _bookmarks.IndexOf(mark);
        var renamed = mark with { Name = name };
        _bookmarks[index] = renamed;
        PersistBookmarks();
        BookmarkList.SelectedItem = renamed;
    }

    private void RemoveBookmarkClick(object? s, RoutedEventArgs e)
    {
        if (BookmarkList.SelectedItem is FrameBookmark mark)
            RemoveBookmark(mark);
    }
}
