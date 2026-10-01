using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KinokoTAS.App.Services;
using KinokoTAS.Core;

namespace KinokoTAS.App;

public partial class MainWindow : Window
{
    public TasProject? Project { get; private set; }

    private bool _dirty;
    private bool _allowClose;
    private bool _busy;
    private string? _sourcePath;
    private readonly EditorSettingsStore _settings = new();
    private readonly RecordingWorkspace _workspace = new(AppContext.BaseDirectory);
    private readonly GameInputState _input = new();
    private readonly EditorDialogService _dialogs;
    private readonly GamePreviewPresenter _preview;

    public MainWindow()
    {
        InitializeComponent();
        _dialogs = new(this);
        _preview = new(GameImage);
        InitializeGamePanel();
        InitializeLibrary();
        InitializeFrameTools();
        ActionPicker.ItemsSource = Replay.Labels;
        Timeline.CellClicked += OnTimelineCellClicked;
        Timeline.FrameActivated += OnTimelineFrameActivated;
        Timeline.BookmarkRequested += OnTimelineBookmarkRequested;
        Timeline.Scrolled += OnTimelineScrolled;
        Closing += OnWindowClosing;
        AddHandler(KeyDownEvent, EditorShortcut, RoutingStrategies.Tunnel);
        KeyDown += OnDocumentShortcut;
    }

    private void OnChanged()
    {
        _dirty = true;
        Refresh();
    }
}
