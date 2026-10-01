using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KinokoTAS.App;
using KinokoTAS.Core;

internal static class Program
{
    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new Exception(name);

        Console.WriteLine("PASS " + name);
    }

    private static void Reject(byte[] b, string name)
    {
        try
        {
            Replay.Parse(b);
        }
        catch (InvalidDataException)
        {
            Console.WriteLine("PASS " + name);
            return;
        }

        throw new Exception(name);
    }

    private static byte[] Fixture(int count = 180, uint[]? masks = null)
    {
        using var s = new MemoryStream();
        using var w = new BinaryWriter(s);
        w.Write("KINORPL1"u8);
        w.Write(1u);
        w.Write(19u);
        w.Write(System.Text.Encoding.ASCII.GetBytes(new string('a', 64)));
        var chain = 14695981039346656037;
        var held = new int[19];
        for (var f = 0; f < count; f++)
        {
            using var frame = new MemoryStream();
            using var fw = new BinaryWriter(frame);
            fw.Write((ulong) f);
            var previous = (int[]) held.Clone();
            for (var a = 0; a < 19; a++)
            {
                var down = masks is null ? (a == 4 && f < 60 || a == 1) : (masks[f] & (1u << a)) != 0;
                held[a] = down ? held[a] + 1 : 0;
                fw.Write(held[a]);
            }

            for (var a = 0; a < 19; a++)
                fw.Write((byte) (previous[a] > 0 && held[a] == 0 ? 1 : 0));

            fw.Write(0);
            fw.Write(0);
            for (var a = 0; a < 6; a++)
                fw.Write(0);

            fw.Write(new byte[4]);
            for (var a = 0; a < 10; a++)
                fw.Write(a == 0 ? 258 : 0);

            fw.Write((uint) (1000 + f * 1000 / 60));
            fw.Write(42u);
            fw.Write(42u);
            fw.Write((ulong) f * 17 + (masks is null ? 0 : masks[f]));
            var b = frame.ToArray();
            if (b.Length != 199)
                throw new Exception("fixture size");

            w.Write((byte) 1);
            w.Write(b);
            w.Write(Replay.Hash(b));
            chain = Replay.Hash(b, chain);
        }

        w.Write((byte) 0);
        w.Write((ulong) count);
        w.Write(chain);
        return s.ToArray();
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("-pixel_format"))
        {
            using var input = Console.OpenStandardInput();
            using var output = File.Create(args[^1]);
            input.CopyTo(output);
            if (args[^1].Contains("encoder-fail"))
            {
                Console.Error.WriteLine("injected encoder failure");
                return 9;
            }

            return 0;
        }

        if (args.Contains("--tas-dir"))
            return FakeEngine(args);

        try
        {
            var output = Path.GetFullPath(args.Length > 0
                ? args[0]
                : "artifacts/checks-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            if (Directory.Exists(output))
                throw new Exception("Use a fresh output directory");

            Directory.CreateDirectory(output);
            var data = Fixture();
            var replayPath = Path.Combine(output, "synthetic.krec");
            File.WriteAllBytes(replayPath, data);
            var replay = Replay.Parse(data);
            FrameEditingChecks.Run(replay, output);
            Check(replay.Count == 180 && replay.Held(59, 4) == 60 && replay.Released(60, 4),
                "wire values and releases");
            Check(replay.Clock(60) == 2000 && replay.RandomBefore(60) == 42 && replay.Checkpoint(60) == 1020,
                "wire diagnostic offsets");
            var corrupt = (byte[]) data.Clone();
            corrupt[100] ^= 1;
            Reject(corrupt, "corrupt frame rejected");
            Reject(data[..^1], "truncation rejected");
            Reject([.. data, 0], "trailing bytes rejected");
            corrupt = (byte[]) data.Clone();
            corrupt[12] = 18;
            Reject(corrupt, "schema mismatch rejected");
            var project = new TasProject(replay, "synthetic.krec");
            project.SetRange(5, 9, 4, false);
            Check(project.EditCount == 5 && !project.Down(5, 4) && project.InvalidFrom == 5, "range edit");
            project.Undo();
            Check(project.EditCount == 0 && project.Down(5, 4), "undo batch");
            project.Redo();
            Check(project.EditCount == 5, "redo batch");
            var path = Path.Combine(output, "edited.ktas");
            project.Save(path);
            var loaded = TasProject.Load(path);
            Check(loaded.EditCount == 5 && !loaded.Down(9, 4) && loaded.Source.Bytes.Span.SequenceEqual(data),
                "project preserves exact source");
            loaded.ExportSource(Path.Combine(output, "export.krec"));
            Check(File.ReadAllBytes(Path.Combine(output, "export.krec")).SequenceEqual(data), "export exact original");
            var atomic = Path.Combine(output, "atomic.txt");
            File.WriteAllText(atomic, "keep");
            try
            {
                AtomicFile.Write(atomic, s => throw new IOException("injected"));
            }
            catch (IOException)
            {
            }

            Check(File.ReadAllText(atomic) == "keep", "failed save preserves old file");
            if (args.Length > 1)
            {
                var real = Replay.Load(args[1]);
                Check(real.Count > 0, "user recording read-only: " + real.Count + " frames");
            }

            var liveFile = Path.Combine(output, "partial.krec");
            File.WriteAllBytes(liveFile, data[..(Replay.HeaderSize + Replay.RecordSize + 17)]);
            var liveReader = new LiveTimeline();
            liveReader.Read(liveFile, 2);
            Check(liveReader.Masks.Count == 1, "live reader ignores incomplete record");
            File.WriteAllBytes(liveFile, data[..(Replay.HeaderSize + 2 * Replay.RecordSize)]);
            liveReader.Read(liveFile, 2);
            Check(liveReader.Masks.Count == 2 && (liveReader.Masks[1] & (1u << 4)) != 0,
                "live reader incrementally appends validated input");
            var fakeExe = ProtocolCheck(output, replayPath).GetAwaiter().GetResult();
            WorkspaceChecks.RunAsync(replay, fakeExe, output, Check).GetAwaiter().GetResult();
            VideoChecks(fakeExe, output, replayPath).GetAwaiter().GetResult();
            var marks = new[] { new FrameBookmark(2, "Boss 前"), new FrameBookmark(10, "重点") };
            var bundle = RecordingLibrary.SaveBundle(output, replay, Path.Combine(output, "initial"), marks);
            Check(Replay.Load(bundle).Bytes.Span.SequenceEqual(data), "saved bundle preserves exact replay");
            Check(
                File.ReadAllText(Path.Combine(Path.GetDirectoryName(bundle)!, "initial", "marisaA.dat")) == "original",
                "saved bundle includes initial saves");
            Check(RecordingLibrary.LoadBookmarks(bundle + ".bookmarks.json", replay.Count).SequenceEqual(marks),
                "named bookmarks survive bundle save and reload");
            var packed = Path.Combine(output, "single.krec");
            RecordingPackage.Save(packed, replay, Path.Combine(output, "initial"), marks);
            var package = RecordingPackage.Load(packed);
            Check(Replay.Load(packed).Bytes.Span.SequenceEqual(data) && package.Bookmarks.SequenceEqual(marks),
                "single-file replay and bookmarks roundtrip");
            var unpack = Path.Combine(output, "unpacked-initial");
            package.ExtractInitial(unpack);
            Check(File.ReadAllText(Path.Combine(unpack, "marisaA.dat")) == "original",
                "single-file initial saves roundtrip");
            Check(new FileInfo(packed).Length < data.Length, "replay package compression reduces fixture size");
            var badPack = Path.Combine(output, "invalid.krec");
            File.Copy(packed, badPack);
            using (var zip = System.IO.Compression.ZipFile.Open(badPack, System.IO.Compression.ZipArchiveMode.Update))
                zip.CreateEntry("../escape.dat");

            var rejected = false;
            try
            {
                RecordingPackage.Load(badPack);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Check(rejected, "unexpected package entry rejected before extraction");
            var broken = Path.Combine(output, "damaged.krec");
            File.Copy(packed, broken);
            using (var zip = System.IO.Compression.ZipFile.Open(broken, System.IO.Compression.ZipArchiveMode.Update))
            {
                zip.GetEntry("initial/marisaA.dat")!.Delete();
                using var changed = zip.CreateEntry("initial/marisaA.dat").Open();
                changed.WriteByte(1);
            }

            rejected = false;
            try
            {
                RecordingPackage.Load(broken);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Check(rejected, "changed packaged save rejected by hash");
            var packedSession = new FileGameSession(fakeExe, Path.Combine(output, "packed-session"), packed, unpack,
                replay.Identity);
            packedSession.StartAsync().GetAwaiter().GetResult();
            Check(File.ReadAllBytes(Path.Combine(packedSession.SessionDirectory, "source.krec")).SequenceEqual(data),
                "game receives unpacked legacy wire format");
            packedSession.StopAsync().GetAwaiter().GetResult();
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new() { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var confirmation = window.ConfirmContentAsync("未保存的修改", "放弃未保存的输入草稿？原始录制不会被修改。", "放弃修改", "返回编辑");
            Dispatcher.UIThread.RunJobs();
            for (var frame = 0; frame < 5; frame++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Thread.Sleep(20);
            }

            foreach (var c in window.GetVisualDescendants().OfType<KinokoTAS.App.Controls.ContentDialogHost>())
                Console.WriteLine($"Dialog host: {c.Bounds} visible={c.IsVisible}");

            using (var dialogImage = window.CaptureRenderedFrame() ?? throw new Exception("No dialog image"))
                dialogImage.Save(Path.Combine(output, "dialog.png"),
                    new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

            Check(!(window.Content as Control)!.IsEnabled, "dialog blocks background controls");
            var primary = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_PrimaryButton");
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialogDeadline = DateTime.UtcNow.AddSeconds(5);
            while (!confirmation.IsCompleted && DateTime.UtcNow < dialogDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(
                confirmation is { IsCompletedSuccessfully: true, Result: true } &&
                (window.Content as Control)!.IsEnabled,
                "dialog primary result restores background controls");
            var cancellation = window.ConfirmContentAsync("验证录制", "出现不同步时停止。", "开始回放", "取消");
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_CloseButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialogDeadline = DateTime.UtcNow.AddSeconds(5);
            while (!cancellation.IsCompleted && DateTime.UtcNow < dialogDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(cancellation is { IsCompletedSuccessfully: true, Result: false }, "dialog cancel returns false");
            var open = window.OpenPathAsync(replayPath);
            while (!open.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            open.GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Check(window.Project?.Source.Count == 180, "UI loads source");
            var timeline = window.FindControl<TimelineControl>("Timeline")!;
            var point = timeline.TranslatePoint(
                new Point(TimelineControl.FrameWidth + 5,
                    TimelineControl.HeaderHeight + 4 * TimelineControl.RowHeight + 5), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Check(window.Project!.IsEdited(0, 4) && !window.Project.Down(0, 4), "timeline click edits selected action");
            var paintStart =
                timeline.TranslatePoint(
                    new Point(TimelineControl.FrameWidth + 2 * TimelineControl.CellWidth + 5,
                        TimelineControl.HeaderHeight + 5 * TimelineControl.RowHeight + 5), window)!.Value;
            var paintEnd =
                timeline.TranslatePoint(
                    new Point(TimelineControl.FrameWidth + 6 * TimelineControl.CellWidth + 5,
                        TimelineControl.HeaderHeight + 5 * TimelineControl.RowHeight + 5), window)!.Value;
            window.MouseDown(paintStart, MouseButton.Left);
            window.MouseMove(paintEnd, RawInputModifiers.LeftMouseButton);
            window.MouseUp(paintEnd, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Check(window.Project.Down(2, 5) && window.Project.Down(6, 5), "pointer drag paints every crossed frame");
            window.Project.Undo();
            Check(!window.Project.Down(2, 5) && !window.Project.Down(6, 5) && window.Project.EditCount == 1,
                "one undo reverses the complete pointer stroke");
            var uiSession = new FileGameSession(fakeExe, Path.Combine(output, "ui-session"), replayPath,
                Path.Combine(output, "initial"), new string('a', 64));
            var connect = window.AttachGameSessionAsync(uiSession);
            window.PauseOnDeactivateAsync().GetAwaiter().GetResult();
            Check(!Directory.GetFiles(uiSession.SessionDirectory, "command.txt", SearchOption.AllDirectories).Any(),
                "startup focus loss does not pause before first frame");
            while (!connect.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            connect.GetAwaiter().GetResult();
            if (OperatingSystem.IsWindows())
            {
                var statePath = Directory.GetFiles(uiSession.SessionDirectory, "state.txt", SearchOption.AllDirectories)
                    .Single();
                var heldState = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.None);
                var releaseState = Task.Run(() =>
                {
                    Thread.Sleep(10);
                    heldState.Dispose();
                });
                Check(uiSession.ReadState() is not null, "transient mailbox sharing conflict retries successfully");
                releaseState.GetAwaiter().GetResult();
                using (var blockedState = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var reported = false;
                    try
                    {
                        uiSession.ReadState();
                    }
                    catch (IOException)
                    {
                        reported = true;
                    }

                    Check(reported, "persistent mailbox access conflict remains visible after bounded retries");
                }
            }

            var previewDeadline = DateTime.UtcNow.AddSeconds(10);
            while (window.FindControl<Image>("GameImage")!.Source is null && DateTime.UtcNow < previewDeadline)
            {
                window.RefreshGameView();
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            Check(window.FindControl<Image>("GameImage")!.Source is not null, "embedded preview arrives in UI");
            Check(
                window.FindControl<Control>("PlayGameButton")!.IsVisible &&
                !window.FindControl<Control>("PauseGameButton")!.IsVisible,
                "paused session shows only playback command");
            var addMark = window.AddBookmarkAsync("测试重点");
            while (!addMark.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            addMark.GetAwaiter().GetResult();
            Check(window.FindControl<ListBox>("BookmarkList")!.SelectedItem is FrameBookmark { Frame: 0 },
                "bookmark captures completed game frame");
            string pointerName = "pointer-" + Path.GetFileName(output),
                renamedName = "renamed-" + Path.GetFileName(output);
            window.AddBookmarkAt(12, pointerName);
            Dispatcher.UIThread.RunJobs();
            var bookmarkList = window.FindControl<ListBox>("BookmarkList")!;
            var bookmarkItem = bookmarkList.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.Content is FrameBookmark mark && mark.Name == pointerName);
            var bookmarkPoint = bookmarkItem.TranslatePoint(new Point(8, 8), window)!.Value;
            window.MouseDown(bookmarkPoint, MouseButton.Left);
            window.MouseUp(bookmarkPoint, MouseButton.Left);
            window.MouseDown(bookmarkPoint, MouseButton.Left);
            window.MouseUp(bookmarkPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Check(timeline.SelectedFrame == 12 && uiSession.ReadState()!.Completed == 1,
                "bookmark double click selects timeline without seeking engine");
            Dispatcher.UIThread.RunJobs();
            bookmarkItem = bookmarkList.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.Content is FrameBookmark mark && mark.Name == pointerName);
            bookmarkPoint = bookmarkItem.TranslatePoint(new Point(8, 8), window)!.Value;
            window.MouseDown(bookmarkPoint, MouseButton.Right);
            window.MouseUp(bookmarkPoint, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine(
                $"Bookmark context: selected={bookmarkList.SelectedItem}, open={bookmarkList.ContextMenu?.IsOpen}, point={bookmarkPoint}");
            Check(
                bookmarkList.SelectedItem is FrameBookmark clicked && clicked.Name == pointerName &&
                bookmarkList.ContextMenu?.IsOpen == true, "bookmark right click targets clicked item");
            var renameMark = (FrameBookmark) bookmarkList.SelectedItem!;
            bookmarkList.ContextMenu!.Close();
            var renameTask = window.RenameBookmarkAsync(renameMark);
            Dispatcher.UIThread.RunJobs();
            var renameDeadline = DateTime.UtcNow.AddSeconds(5);
            while (!window.GetVisualDescendants().OfType<KinokoTAS.App.Controls.ContentDialog>().Any() &&
                   !renameTask.IsCompleted && DateTime.UtcNow < renameDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            var renameInput =
                window.GetVisualDescendants().OfType<KinokoTAS.App.Controls.ContentDialog>().Single()
                    .Content as TextBox;
            renameInput!.Text = renamedName;
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_PrimaryButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (!renameTask.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            renameTask.GetAwaiter().GetResult();
            Check(
                bookmarkList.SelectedItem is FrameBookmark { Frame: 12 } renamed &&
                renamed.Name == renamedName, "bookmark rename preserves frame");
            var renamedItem = bookmarkList.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.Content is FrameBookmark mark && mark.Name == renamedName);
            bookmarkPoint = renamedItem.TranslatePoint(new Point(8, 8), window)!.Value;
            window.MouseDown(bookmarkPoint, MouseButton.Right);
            window.MouseUp(bookmarkPoint, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            var deleteMark = bookmarkList.ContextMenu!.ItemsSource!.Cast<MenuItem>()
                .Single(item => Equals(item.Header, "删除重点"));
            deleteMark.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            bookmarkList.ContextMenu.Close();
            Dispatcher.UIThread.RunJobs();
            Check(
                bookmarkList.Items.OfType<FrameBookmark>().All(mark => mark.Name != renamedName) &&
                bookmarkList.Items.OfType<FrameBookmark>().Any(mark => mark.Name == "测试重点"),
                "context deletion preserves other bookmarks");
            using (var screenshot = window.CaptureRenderedFrame() ?? throw new Exception("No rendered UI"))
                screenshot.Save(Path.Combine(output, "editor.png"),
                    new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

            var stop = window.StopGameSessionAsync();
            while (!stop.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            stop.GetAwaiter().GetResult();
            window.Project.Undo();
            Check(window.Project.EditCount == 0, "UI undo");
            string layoutMark = "layout-" + Path.GetFileName(output),
                insertedMark = "inserted-" + Path.GetFileName(output);
            window.AddBookmarkAt(5, layoutMark);
            window.InsertEmptyFrames(3, 2);
            var markList = window.FindControl<ListBox>("BookmarkList")!;
            Check(
                window.Project.FrameCount == 182 &&
                markList.Items.OfType<FrameBookmark>().Single(mark => mark.Name == layoutMark).Frame == 7,
                "bookmark follows insertion and timeline uses draft length");
            window.AddBookmarkAt(3, insertedMark);
            window.Project.Undo();
            Check(
                markList.Items.OfType<FrameBookmark>().All(mark => mark.Name != insertedMark) &&
                markList.Items.OfType<FrameBookmark>().Single(mark => mark.Name == layoutMark).Frame == 5,
                "undo insertion removes inserted-frame bookmarks and remaps survivors");
            window.Project.Redo();
            Check(markList.Items.OfType<FrameBookmark>().Any(mark => mark.Name == insertedMark && mark.Frame == 3),
                "redo insertion recovers newly added bookmark");
            window.Project.Undo();
            window.DeleteFrameRange(5, 5);
            Check(markList.Items.OfType<FrameBookmark>().All(mark => mark.Name != layoutMark),
                "deleting bookmark frame removes its mark");
            window.Project.Undo();
            Check(markList.Items.OfType<FrameBookmark>().Single(mark => mark.Name == layoutMark).Frame == 5,
                "undo deletion recovers bookmark at original frame");
            window.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            using (var frameTools = window.CaptureRenderedFrame() ?? throw new Exception("No frame tools image"))
                frameTools.Save(Path.Combine(output, "frame-tools.png"),
                    new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

            var editingSession = new FileGameSession(fakeExe, Path.Combine(output, "ui-edit-session"), replayPath,
                Path.Combine(output, "initial"), new string('a', 64));
            var editConnect = window.AttachGameSessionAsync(editingSession);
            while (!editConnect.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            editConnect.GetAwaiter().GetResult();
            window.InsertEmptyFrames(3, 1);
            var blocked = false;
            try
            {
                window.ReplayAllAsync().GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                blocked = true;
            }

            Check(blocked && editingSession.ReadState()!.Completed == 1,
                "unapplied layout cannot play the old source under shifted frame numbers");
            window.Project!.Undo();
            window.FindControl<ComboBox>("SpeedPicker")!.SelectedIndex = 2;
            var fractionalDeadline = DateTime.UtcNow.AddSeconds(5);
            while (editingSession.PlaybackSpeed != 0.75 && DateTime.UtcNow < fractionalDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(editingSession.PlaybackSpeed == 0.75, "0.75x picker forwards correct speed to engine");
            window.FindControl<ComboBox>("SpeedPicker")!.SelectedIndex = 5;
            var speedDeadline = DateTime.UtcNow.AddSeconds(5);
            while (editingSession.PlaybackSpeed != 4 && DateTime.UtcNow < speedDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(editingSession.PlaybackSpeed == 4, "speed picker controls connected engine");
            window.Project.SetRange(0, 0, 4, false);
            timeline.SelectedFrame = 7;
            window.RefreshGameView();
            Check(window.FindControl<Control>("ApplyEditsButton")!.IsEnabled,
                "apply command enabled for pending inputs");
            Check(
                window.FindControl<CommandBar>("TimelineCommands")!.PrimaryCommands.Contains(
                    window.FindControl<CommandBarButton>("ApplyEditsButton")!),
                "apply command is directly on timeline toolbar");
            Check(
                window.Title!.Contains(" *") &&
                window.FindControl<TextBlock>("EditWorkflowLabel")!.Text!.Contains("待应用"),
                "pending timeline edits show unsaved star and explicit workflow state");
            Check(window.FindControl<CommandBarButton>("SaveRecordingButton")!.Label == "应用并保存录制",
                "save clearly includes applying pending edits");
            var portableDraftPath = Path.Combine(output, "ui-portable.ktas");
            var exportPortableDraft = window.SaveDraftToAsync(portableDraftPath);
            while (!exportPortableDraft.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            exportPortableDraft.GetAwaiter().GetResult();
            Check(
                window.HasUnsavedChanges && window.Title!.Contains(" *") &&
                !File.Exists(portableDraftPath + ".bookmarks.json"),
                "self-contained draft export does not clear krec unsaved state or create sidecar");
            var isolatedFolder = Path.Combine(output, "ui-isolated");
            Directory.CreateDirectory(isolatedFolder);
            var isolatedDraft = Path.Combine(isolatedFolder, "portable.ktas");
            File.Copy(portableDraftPath, isolatedDraft);
            var portableWindow = new MainWindow();
            portableWindow.Show();
            var openPortable = portableWindow.OpenPathAsync(isolatedDraft);
            while (!openPortable.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            openPortable.GetAwaiter().GetResult();
            var resavePortable = portableWindow.SaveDraftToAsync(Path.Combine(isolatedFolder, "exported.ktas"));
            Check(resavePortable.IsCompletedSuccessfully,
                "opening and resaving moved draft needs no initial-folder picker");
            Check(
                TasProject.Load(Path.Combine(isolatedFolder, "exported.ktas")).EmbeddedRecording!.Initial.ContainsKey(
                    "marisaA.dat"), "editor resaved draft retains initial save without game session");
            portableWindow.Hide();
            var pendingApply = window.Project;
            window.FindControl<Control>("ApplyEditsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var applyDeadline = DateTime.UtcNow.AddSeconds(20);
            while (ReferenceEquals(window.Project, pendingApply) && DateTime.UtcNow < applyDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(window.Project.EditCount == 0 && window.Project.Source.Held(0, 4) == 0,
                "UI adopts verified edited recording");
            Check(window.Project.InvalidFrom is null && !window.Project.IsEdited(0, 4),
                "successful apply clears yellow pending cell marker");
            Check(timeline is { Playhead: 7, SelectedFrame: 7 },
                "apply returns verified game to selected frame");
            Check(window.HasUnsavedChanges, "applied recording still requires saving to disk");
            var restore = window.RestoreOverwriteAsync();
            while (!restore.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            restore.GetAwaiter().GetResult();
            Check(window.Project.Source.Held(0, 4) == 1 && window.Project.EditCount == 1,
                "restore recovers original and retained draft");
            window.Project.Undo();
            window.Project.SetRange(1, 1, 4, false);
            timeline.SelectedFrame = 1;
            window.RefreshGameView();
            Check(!window.FindControl<Control>("RestartGameButton")!.IsEnabled,
                "game restart no longer doubles as timeline apply");
            var autoSavePath = Path.Combine(output, "auto-applied-save.krec");
            var autoApplySave = window.SaveRecordingToAsync(autoSavePath);
            while (!autoApplySave.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            autoApplySave.GetAwaiter().GetResult();
            Check(
                window.Project.Source.Held(1, 4) == 0 && window.Project.InvalidFrom is null &&
                !window.Project.IsEdited(1, 4), "saving automatically applies and clears pending timeline edits");
            Check(
                RecordingPackage.Load(autoSavePath).Replay.Held(1, 4) == 0 && !window.HasUnsavedChanges &&
                !window.Title!.Contains(" *"), "save writes applied krec and clears unsaved title star");
            Check(File.ReadAllBytes(replayPath).SequenceEqual(data), "save-as retains original recording bytes");
            using (var editWorkflowImage =
                   window.CaptureRenderedFrame() ?? throw new Exception("No edit workflow image"))
                editWorkflowImage.Save(Path.Combine(output, "timeline-edit-workflow.png"),
                    new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

            var beforeCover = window.Project.Source.Bytes.ToArray();
            var cover = window.ToggleRecordingAsync();
            while (!cover.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            cover.GetAwaiter().GetResult();
            Check(window.FindControl<Border>("GamePanel")!.IsFocused, "record takeover focuses embedded game preview");
            Check(
                !window.FindControl<Control>("PlayGameButton")!.IsVisible &&
                window.FindControl<Control>("PauseGameButton")!.IsVisible,
                "recording session shows only pause command");
            var savePath = Path.Combine(output, "ui-current-save.krec");
            var saveLive = window.SaveRecordingToAsync(savePath);
            while (!saveLive.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            saveLive.GetAwaiter().GetResult();
            Check(window.RecordingSavePath == Path.GetFullPath(savePath), "save destination retained");
            Check(RecordingPackage.Load(savePath).Replay.Count > 0, "live save produces complete package");
            var saveAgain = window.SaveRecordingAsync();
            while (!saveAgain.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            saveAgain.GetAwaiter().GetResult();
            Check(window.RecordingSavePath == Path.GetFullPath(savePath),
                "subsequent save reuses destination without picker");
            var beforeVideoProcess = window.CurrentGameProcessId;
            var beforeVideoBytes = File.ReadAllBytes(savePath);
            var uiVideo = window.ExportVideoToAsync(Path.Combine(output, "ui-video.mp4"), 0, 0, fakeExe, fakeExe);
            while (!uiVideo.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            uiVideo.GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Check(
                beforeVideoProcess > 0 && window.CurrentGameProcessId == beforeVideoProcess &&
                File.ReadAllBytes(savePath).SequenceEqual(beforeVideoBytes) && !window.IsVideoExporting,
                "UI video export preserves current game and saved recording");
            Check(!window.HasUnsavedChanges, "successful save marks current live prefix clean");
            window.AddBookmarkAt(0, "未保存重点");
            Check(window.HasUnsavedChanges, "bookmark changes require saving");
            Check(window.Title!.Contains(" *"), "bookmark edits immediately show unsaved star in current krec title");
            var newCancelled = window.NewRecordingAsync();
            var promptDeadline = DateTime.UtcNow.AddSeconds(5);
            while (!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_CloseButton") &&
                   DateTime.UtcNow < promptDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(
                window.GetVisualDescendants().OfType<Button>()
                    .Any(b => b is { Name: "PART_SecondaryButton", IsVisible: true }),
                "new recording uses three-choice save dialog");
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_CloseButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (!newCancelled.IsCompleted && DateTime.UtcNow < promptDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(newCancelled.IsCompletedSuccessfully && window.HasUnsavedChanges,
                "cancel new recording preserves unsaved document");
            window.Close();
            Dispatcher.UIThread.RunJobs();
            promptDeadline = DateTime.UtcNow.AddSeconds(5);
            while (window.GetVisualDescendants().OfType<Button>().All(b => b.Name != "PART_CloseButton") &&
                   DateTime.UtcNow < promptDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_CloseButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var wait = 0; wait < 40; wait++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(window is { IsVisible: true, HasUnsavedChanges: true },
                "cancel exit keeps window and unsaved contents");
            var undoCover = window.RestoreOverwriteAsync();
            while (!undoCover.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            undoCover.GetAwaiter().GetResult();
            Check(window.Project.Source.Bytes.Span.SequenceEqual(beforeCover),
                "record takeover undo restores whole source tail");
            // Shortcuts are routed through the preview, while text undo remains local.
            var panel = window.FindControl<Border>("GamePanel")!;
            panel.Focus();
            window.KeyPress(Key.F10, RawInputModifiers.None, PhysicalKey.F10, null);
            var keyDeadline = DateTime.UtcNow.AddSeconds(5);
            while (window.FindControl<TimelineControl>("Timeline")!.Playhead < 1 && DateTime.UtcNow < keyDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                window.RefreshGameView();
                Thread.Sleep(5);
            }

            Check(window.FindControl<TimelineControl>("Timeline")!.Playhead == 1, "F10 steps once with preview focus");
            window.KeyRelease(Key.F10, RawInputModifiers.None, PhysicalKey.F10, null);
            var editingStop = window.StopGameSessionAsync();
            while (!editingStop.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            editingStop.GetAwaiter().GetResult();
            window.Project!.SetRange(0, 0, 4, false);
            var fresh = new FileGameSession(fakeExe, Path.Combine(output, "fresh-after-edits"), null,
                Path.Combine(output, "initial"), new string('a', 64), true);
            var newDoc = window.AttachGameSessionAsync(fresh);
            while (!newDoc.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            newDoc.GetAwaiter().GetResult();
            Check(window.Project is null, "new recording cannot inherit stale input draft");
            var freshStop = window.StopGameSessionAsync();
            while (!freshStop.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            freshStop.GetAwaiter().GetResult();
            window.Hide();
            window = new MainWindow();
            window.Show();
            var externalSession = new FileGameSession(fakeExe, Path.Combine(output, "external-session"), null,
                Path.Combine(output, "initial"), new string('a', 64), true);
            var externalConnect = window.AttachGameSessionAsync(externalSession);
            while (!externalConnect.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            externalConnect.GetAwaiter().GetResult();
            var resume = externalSession.ResumeAsync(1, CancellationToken.None);
            while (!resume.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            resume.GetAwaiter().GetResult();
            var before = externalSession.ReadState()!.Sequence;
            window.PauseOnDeactivateAsync().GetAwaiter().GetResult();
            Check(externalSession.ReadState()!.Sequence == before,
                "external window focus transfer does not pause recording");
            Check(File.Exists(Path.Combine(externalSession.SessionDirectory, "external-window.txt")),
                "external mode launch argument reaches child");
            ClickPlaybackThroughRefresh(window, "PauseGameButton");
            var playbackDeadline = DateTime.UtcNow.AddSeconds(5);
            while (externalSession.ReadState()?.Phase != "live-paused" && DateTime.UtcNow < playbackDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(externalSession.ReadState()?.Phase == "live-paused",
                "pause click survives refresh while live recording runs");
            window.RefreshGameView();
            ClickPlaybackThroughRefresh(window, "PlayGameButton");
            playbackDeadline = DateTime.UtcNow.AddSeconds(5);
            while (externalSession.ReadState()?.Phase != "live" && DateTime.UtcNow < playbackDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(externalSession.ReadState()?.Phase == "live",
                "play click survives refresh and resumes live recording");
            var liveMark = window.AddBookmarkAsync("重新挑战");
            while (!liveMark.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            liveMark.GetAwaiter().GetResult();
            window.RefreshGameView();
            // Resume now also awaits the focus bridge; live phase alone does not mean
            // the asynchronous click handler has released its command guard.
            playbackDeadline = DateTime.UtcNow.AddSeconds(5);
            while (!window.FindControl<Control>("PlayGameButton")!.IsEnabled && DateTime.UtcNow < playbackDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
                window.RefreshGameView();
            }

            Check(window.FindControl<Control>("PlayGameButton")!.IsEnabled, "bookmark pause enables live resume");
            ClickPlaybackThroughRefresh(window, "PlayGameButton");
            playbackDeadline = DateTime.UtcNow.AddSeconds(5);
            while (externalSession.ReadState()?.Phase != "live" && DateTime.UtcNow < playbackDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(externalSession.ReadState()?.Phase == "live",
                "recording resumes after bookmark without changing recording mode");
            var bookmarkPause = externalSession.PauseAsync(CancellationToken.None);
            while (!bookmarkPause.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            bookmarkPause.GetAwaiter().GetResult();
            var marked = (FrameBookmark) window.FindControl<ListBox>("BookmarkList")!.SelectedItem!;
            var advance = externalSession.StepAsync(new bool[19], CancellationToken.None);
            while (!advance.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            advance.GetAwaiter().GetResult();
            var goBack = window.ReturnSelectedBookmarkAsync();
            while (!goBack.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            goBack.GetAwaiter().GetResult();
            Check(window.FindControl<NumericUpDown>("JumpFrame")!.Value == marked.Frame,
                "live bookmark return saves then reopens at selected frame");
            Check(Replay.Load(externalSession.LastRecoveryPath!).Count > marked.Frame + 1,
                "return preserves input after bookmarked frame in original branch");
            window.RefreshGameView();
            Check(window.FindControl<TimelineControl>("Timeline")!.Playhead == marked.Frame,
                "playhead follows seek independently of selection");
            var overwrite = externalSession.TakeoverAsync();
            while (!overwrite.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            overwrite.GetAwaiter().GetResult();
            var newFrame = externalSession.StepAsync(new bool[19], CancellationToken.None);
            while (!newFrame.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            newFrame.GetAwaiter().GetResult();
            window.RefreshGameView();
            Check(window.FindControl<TimelineControl>("Timeline")!.FrameCount == marked.Frame + 2,
                "live timeline replaces old tail with new completed frames");
            var all = window.ReplayAllAsync();
            while (!all.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            all.GetAwaiter().GetResult();
            Check(!externalSession.IsLive, "replay all seals recording and starts playback without manual reopen");
            var externalStop = window.StopGameSessionAsync();
            while (!externalStop.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            externalStop.GetAwaiter().GetResult();
            var closingWindow = new MainWindow();
            closingWindow.Show();
            var savedPackage = RecordingPackage.Load(savePath);
            var staleCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "6kinokoTAS", "bookmarks",
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(savedPackage.Replay.Bytes.Span)) +
                ".json");
            var previousCache = File.Exists(staleCache) ? File.ReadAllBytes(staleCache) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(staleCache)!);
            File.WriteAllText(staleCache, "[]");
            var closeOpen = closingWindow.OpenPathAsync(savePath);
            while (!closeOpen.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            closeOpen.GetAwaiter().GetResult();
            Check(
                closingWindow.FindControl<ListBox>("BookmarkList")!.Items.Cast<FrameBookmark>()
                    .SequenceEqual(savedPackage.Bookmarks), "packaged bookmarks override stale empty local cache");
            if (previousCache is not null)
                File.WriteAllBytes(staleCache, previousCache);

            Check(!closingWindow.HasUnsavedChanges, "opening saved package starts clean");
            closingWindow.AddBookmarkAt(0, "退出前保存");
            closingWindow.Close();
            ClickSaveChoice(closingWindow, "PART_PrimaryButton");
            var closeDeadline = DateTime.UtcNow.AddSeconds(5);
            while (closingWindow.IsVisible && DateTime.UtcNow < closeDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(!closingWindow.IsVisible && RecordingPackage.Load(savePath).Bookmarks.Any(m => m.Name == "退出前保存"),
                "save on exit writes bookmarks before closing");
            var cleanWindow = new MainWindow();
            cleanWindow.Show();
            var cleanOpen = cleanWindow.OpenPathAsync(savePath);
            while (!cleanOpen.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            cleanOpen.GetAwaiter().GetResult();
            cleanWindow.Close();
            Dispatcher.UIThread.RunJobs();
            Check(!cleanWindow.IsVisible, "clean close is deferred past original cancelled closing event");
            var discardWindow = new MainWindow();
            discardWindow.Show();
            var discardOpen = discardWindow.OpenPathAsync(savePath);
            while (!discardOpen.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            discardOpen.GetAwaiter().GetResult();
            var packageBeforeDiscard = File.ReadAllBytes(savePath);
            discardWindow.AddBookmarkAt(0, "放弃退出");
            discardWindow.Close();
            ClickSaveChoice(discardWindow, "PART_SecondaryButton");
            closeDeadline = DateTime.UtcNow.AddSeconds(5);
            while (discardWindow.IsVisible && DateTime.UtcNow < closeDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(!discardWindow.IsVisible && File.ReadAllBytes(savePath).SequenceEqual(packageBeforeDiscard),
                "discard on exit leaves saved package untouched");
            var shortcutWindow = new MainWindow();
            shortcutWindow.Show();
            var shortcutSession = new FileGameSession(fakeExe, Path.Combine(output, "shortcut-session"), replayPath,
                Path.Combine(output, "initial"), new string('a', 64), true);
            var shortcutConnect = shortcutWindow.AttachGameSessionAsync(shortcutSession);
            while (!shortcutConnect.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            shortcutConnect.GetAwaiter().GetResult();
            var shortcutBridge = Directory
                .GetFiles(shortcutSession.SessionDirectory, "state.txt", SearchOption.AllDirectories).Single();
            shortcutBridge = Path.GetDirectoryName(shortcutBridge)!;
            File.WriteAllText(Path.Combine(shortcutBridge, "shortcut-1.txt"), "KTASKEY1 1 toggle-recording\n");
            var shortcutTakeover = shortcutWindow.ProcessGameRequestsAsync();
            while (!shortcutTakeover.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            shortcutTakeover.GetAwaiter().GetResult();
            Check(shortcutSession.IsLive && shortcutSession.ReadState()?.Phase == "live",
                "game F8 request invokes editor takeover and resumes recording");
            Check(!File.Exists(Path.Combine(shortcutBridge, "shortcut-1.txt")), "game shortcut is consumed once");
            var noRepeat = shortcutWindow.ProcessGameRequestsAsync();
            noRepeat.GetAwaiter().GetResult();
            Check(shortcutSession.IsLive, "polling consumed shortcut does not toggle twice");
            File.WriteAllText(Path.Combine(shortcutBridge, "shortcut-2.txt"), "KTASKEY1 2 toggle-recording\n");
            var shortcutPlayback = shortcutWindow.ProcessGameRequestsAsync();
            while (!shortcutPlayback.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            shortcutPlayback.GetAwaiter().GetResult();
            Check(
                !shortcutSession.IsLive && shortcutSession.ReadState()?.Phase == "paused" &&
                shortcutSession.LastRecoveryPath is not null,
                "game F8 seals recording and returns to playback with recovery retained");
            Check(File.ReadAllBytes(replayPath).SequenceEqual(data),
                "game shortcut takeover leaves original recording unchanged");
            var shortcutStop = shortcutWindow.StopGameSessionAsync();
            while (!shortcutStop.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            shortcutStop.GetAwaiter().GetResult();
            shortcutWindow.Hide();
            var saveFailureWindow = new MainWindow();
            saveFailureWindow.Show();
            var saveFailureOpen = saveFailureWindow.OpenPathAsync(replayPath);
            while (!saveFailureOpen.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            saveFailureOpen.GetAwaiter().GetResult();
            var saveFailureSession = new FileGameSession(fakeExe, Path.Combine(output, "save-failure-session"),
                replayPath, Path.Combine(output, "initial"), new string('a', 64));
            var saveFailureConnect = saveFailureWindow.AttachGameSessionAsync(saveFailureSession);
            while (!saveFailureConnect.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            saveFailureConnect.GetAwaiter().GetResult();
            var failFlag = Path.Combine(saveFailureSession.SessionDirectory, "initial", "fail-verification.dat");
            File.WriteAllText(failFlag, "fixture");
            saveFailureWindow.Project!.SetRange(1, 1, 4, false);
            var retainedDraft = saveFailureWindow.Project;
            var protectedSave = Path.Combine(output, "protected-existing-save.krec");
            File.Copy(savePath, protectedSave);
            var protectedBytes = File.ReadAllBytes(protectedSave);
            var failSave = saveFailureWindow.SaveRecordingToAsync(protectedSave);
            while (!failSave.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            var saveRejected = false;
            try
            {
                failSave.GetAwaiter().GetResult();
            }
            catch (InvalidDataException)
            {
                saveRejected = true;
            }

            Check(saveRejected && File.ReadAllBytes(protectedSave).SequenceEqual(protectedBytes),
                "failed apply-and-save preserves existing krec exactly");
            Check(
                ReferenceEquals(retainedDraft, saveFailureWindow.Project) && saveFailureWindow.HasUnsavedChanges &&
                saveFailureWindow.Title!.Contains(" *") && saveFailureSession.IsRunning,
                "failed save retains draft, live session and unsaved star");
            File.Delete(failFlag);
            var retrySave = saveFailureWindow.SaveRecordingToAsync(protectedSave);
            while (!retrySave.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            retrySave.GetAwaiter().GetResult();
            Check(RecordingPackage.Load(protectedSave).Replay.Held(1, 4) == 0 && !saveFailureWindow.HasUnsavedChanges,
                "retry applies pending edits and saves playable krec");
            saveFailureWindow.Project!.SetRange(2, 2, 4, false);
            saveFailureWindow.Close();
            ClickSaveChoice(saveFailureWindow, "PART_PrimaryButton");
            closeDeadline = DateTime.UtcNow.AddSeconds(20);
            while (saveFailureWindow.IsVisible && DateTime.UtcNow < closeDeadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Check(!saveFailureWindow.IsVisible && RecordingPackage.Load(protectedSave).Replay.Held(2, 4) == 0,
                "close-save applies pending edits to krec and exits instead of exporting ktas");
            Check(File.ReadAllBytes(replayPath).SequenceEqual(data),
                "all apply-and-save checks leave original fixture unchanged");
            Console.WriteLine("All checks passed. Artifacts: " + output);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void ClickSaveChoice(MainWindow window, string name)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == name) && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void ClickPlaybackThroughRefresh(MainWindow window, string name)
    {
        window.RefreshGameView();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var button = window.FindControl<Control>(name)!;
        Check(button is { IsVisible: true, IsEnabled: true }, name + " available before pointer press");
        var resets = 0;

        void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Visual.IsVisibleProperty || args.Property == InputElement.IsEnabledProperty)
                resets++;
        }

        button.PropertyChanged += Changed;
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        for (var refresh = 0; refresh < 5; refresh++)
        {
            window.RefreshGameView();
            Dispatcher.UIThread.RunJobs();
        }

        button.PropertyChanged -= Changed;
        Check(resets == 0, name + " stays visible and enabled across held-click refreshes");
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static int FakeEngine(string[] args)
    {
        string Arg(string name) => args[Array.IndexOf(args, name) + 1];
        string bridge = Arg("--tas-dir"), output = Arg("--tas-output");
        long seq = 0, count = 0, target = 1;
        bool live = args.Contains("--record"), run = false;
        if (args.Contains("--tas-window"))
            File.WriteAllText(
                Path.Combine(Directory.GetParent(Directory.GetParent(bridge)!.FullName)!.FullName,
                    "external-window.txt"), "yes");

        var source = live ? null : Replay.Load(Arg("--replay"));
        long total = source?.Count ?? 0;
        uint[]? plan = null;
        var first = -1;
        var planPath = Path.Combine(bridge, "edit.bin");
        if (File.Exists(planPath))
        {
            using var reader = new BinaryReader(File.OpenRead(planPath));
            var version = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8));
            var length = reader.ReadInt32();
            first = reader.ReadInt32();
            if (version == "KTASED02")
                reader.ReadInt32();

            plan = new uint[length];
            for (var i = 0; i < length; i++)
                plan[i] = reader.ReadUInt32();

            total = length;
        }

        if (!File.Exists(Path.Combine(Arg("--save-dir"), "no-edits.dat")))
            File.WriteAllText(Path.Combine(bridge, "capabilities.txt"),
                "KTAS1 edits-v1 edits-v2 pacing-v1 pacing-075-v1 snapshot-v1 focus-v1" +
                (File.Exists(Path.Combine(Arg("--save-dir"), "legacy-pacing.dat")) ? "" : " seek-fast-v1"));

        var recorded = new List<uint>();
        byte[] Current() => Fixture((int) count, [.. recorded]);
        for (var tick = 0; tick < 15000; tick++)
        {
            try
            {
                var parts = File.ReadAllText(Path.Combine(bridge, "command.txt"))
                    .Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && long.Parse(parts[0]) > seq)
                {
                    seq = long.Parse(parts[0]);
                    switch (parts[1])
                    {
                        case "stop":
                            File.WriteAllBytes(output, Current());
                            return 0;
                        case "pause":
                            run = false;
                            target = count;
                            break;
                        case "snapshot":
                            run = false;
                            target = count;
                            File.WriteAllBytes(Path.Combine(bridge, "recording.krec"), Current());
                            break;
                        case "focus":
                            File.WriteAllText(Path.Combine(bridge, "focus-observed.txt"), "yes");
                            break;
                        case "target":
                            target = long.Parse(parts[2]);
                            run = false;
                            break;
                        case "seek":
                            target = long.Parse(parts[2]);
                            run = false;
                            File.WriteAllText(Path.Combine(bridge, "seek-observed.txt"), parts[2]);
                            break;
                        case "run":
                            run = true;
                            break;
                        case "speed":
                            File.WriteAllText(Path.Combine(bridge, "speed-observed.txt"), parts[2]);
                            File.AppendAllText(Path.Combine(bridge, "speed-history.txt"), parts[2] + "\n");
                            break;
                        case "takeover":
                            live = true;
                            run = false;
                            target = count;
                            break;
                    }
                }
            }
            catch (IOException)
            {
            }

            if ((run || count < target) && (live || count < total))
            {
                if (plan is not null && count >= first)
                    live = true;

                uint value = 0;
                if (plan is not null)
                    value = plan[count];

                else if (!live && source is not null)
                    for (var a = 0; a < 19; a++)
                        if (source.Held((int) count, a) > 0)
                            value |= 1u << a;

                        else
                            try
                            {
                                var input = File.ReadAllText(Path.Combine(bridge, "input.txt")).Split(' ');
                                value = uint.Parse(input[1]);
                            }
                            catch (IOException)
                            {
                            }

                recorded.Add(value);
                count++;
            }

            if (plan is null && source?.Checkpoint(0) > 0 && count >= 2 &&
                File.Exists(Path.Combine(Arg("--save-dir"), "fail-verification.dat")))
            {
                File.WriteAllText(Path.Combine(bridge, "error.txt"), "Injected checkpoint divergence");
                return 3;
            }

            if (!live && count >= total)
            {
                run = false;
                target = count;
            }

            var phase = (run || count < target) ? (live ? "live" : "playing") : (live ? "live-paused" : "paused");
            try
            {
                AtomicFile.Write(output, s =>
                {
                    var bytes = Current();
                    s.Write(bytes.AsSpan(0, bytes.Length - 17));
                });
                AtomicFile.Write(Path.Combine(bridge, "state.txt"), s =>
                {
                    using var w = new StreamWriter(s, leaveOpen: true);
                    w.Write($"KTAS1 {seq} {count} {total} {phase}\n");
                });
                AtomicFile.Write(Path.Combine(bridge, "image.rgba"), s =>
                {
                    using var w = new BinaryWriter(s, System.Text.Encoding.UTF8, true);
                    w.Write("KTASIMG1"u8);
                    w.Write(count);
                    w.Write(1);
                    w.Write(1);
                    w.Write(new byte[] { 10, 20, 30, 255 });
                });
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            Thread.Sleep(2);
        }

        return 2;
    }

    private sealed class ExportProgress(Action<VideoExportProgress> report) : IProgress<VideoExportProgress>
    {
        public void Report(VideoExportProgress value) => report(value);
    }

    private static async Task VideoChecks(string exe, string output, string replay)
    {
        FileGameSession Session(string name) => new(exe, Path.Combine(output, name), replay,
            Path.Combine(output, "initial"), new string('a', 64), false, true);

        var frames = new List<int>();
        var video = Path.Combine(output, "fake-video.mp4");
        var result = await VideoExporter.ExportAsync(Session("video-session"), exe, video, 2, 4, new ExportProgress(p =>
        {
            if (p.Completed > 0)
                frames.Add(p.Frame);
        }));
        Check(result.Frames == 3 && File.ReadAllBytes(video).Length == 12 && frames.SequenceEqual([2, 3, 4]),
            "video export inclusive range and exact frame delivery (fake encoder)");
        var cancelled = Path.Combine(output, "cancel-video.mp4");
        File.WriteAllText(cancelled, "keep");
        using var cancel = new CancellationTokenSource();
        var stopped = false;
        try
        {
            await VideoExporter.ExportAsync(Session("cancel-video-session"), exe, cancelled, 0, 20,
                new ExportProgress(p =>
                {
                    if (p.Completed == 1)
                        cancel.Cancel();
                }), cancel.Token);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }

        Check(stopped && File.ReadAllText(cancelled) == "keep", "cancelled export preserves existing video");
        var failed = Path.Combine(output, "encoder-fail.mp4");
        File.WriteAllText(failed, "keep");
        var rejected = false;
        try
        {
            await VideoExporter.ExportAsync(Session("failed-video-session"), exe, failed, 0, 1);
        }
        catch (IOException)
        {
            rejected = true;
        }

        Check(
            rejected && File.ReadAllText(failed) == "keep" && File
                .ReadAllText(Path.Combine(output, "failed-video-session", "ffmpeg.log")).Contains("injected"),
            "encoder failure preserves destination and diagnostic");
        var encoder = VideoExporter.FindEncoder();
        if (encoder is not null)
        {
            var real = await VideoExporter.ExportAsync(Session("real-video-session"), encoder,
                Path.Combine(output, "real-video.mp4"), 0, 4);
            Check(real.Frames == 5 && new FileInfo(real.Path).Length > 100,
                "real FFmpeg MP4 encoding with synthetic game frames");
        }
    }

    private static async Task<string> ProtocolCheck(string output, string replay)
    {
        var fake = Path.Combine(output, "fake-engine");
        Directory.CreateDirectory(fake);
        foreach (var f in Directory.GetFiles(AppContext.BaseDirectory))
            File.Copy(f, Path.Combine(fake, Path.GetFileName(f)));

        foreach (var n in new[] { "6kinoko_a.dat", "6kinoko_b.dat", "6kinoko_c.dat" })
            File.WriteAllText(Path.Combine(fake, n), "fixture");

        var initial = Path.Combine(output, "initial");
        Directory.CreateDirectory(initial);
        File.WriteAllText(Path.Combine(initial, "marisaA.dat"), "original");
        var exe = Path.Combine(fake, Path.GetFileName(Environment.ProcessPath!));
        await using var session = new FileGameSession(exe, Path.Combine(output, "protocol-session"), replay, initial,
            new string('a', 64));
        await session.StartAsync();
        Check(session.ReadState()?.Completed == 1, "engine handshake and initial pause");
        await session.SetSpeedAsync(0.5);
        Check(session.PlaybackSpeed == 0.5, "slow playback speed acknowledged");
        await session.SeekAsync(10);
        Check(session.ReadState()?.Completed == 11, "forward seek acknowledged");
        var speedFile = Directory
            .GetFiles(session.SessionDirectory, "speed-observed.txt", SearchOption.AllDirectories)
            .OrderBy(File.GetLastWriteTimeUtc).Last();
        Check(File.ReadAllText(speedFile) == "50", "accelerated seek restores chosen playback speed");
        Check(File.ReadAllText(Path.Combine(Path.GetDirectoryName(speedFile)!, "seek-observed.txt")) == "11",
            "capable engine receives unlimited seek command");
        Check(!File.ReadAllLines(Path.Combine(Path.GetDirectoryName(speedFile)!, "speed-history.txt")).Contains("400"),
            "unlimited seek never changes selected playback speed");
        await session.SeekAsync(2);
        Check(session.ReadState()?.Completed == 3, "backward seek restarts and restores");
        var legacyInitial = Path.Combine(output, "legacy-initial");
        Directory.CreateDirectory(legacyInitial);
        File.WriteAllText(Path.Combine(legacyInitial, "legacy-pacing.dat"), "fixture");
        await using (var legacy = new FileGameSession(exe, Path.Combine(output, "legacy-session"), replay,
                         legacyInitial, new string('a', 64)))
        {
            await legacy.StartAsync();
            await legacy.SetSpeedAsync(0.5);
            await legacy.SeekAsync(10);
            var history = Directory
                .GetFiles(legacy.SessionDirectory, "speed-history.txt", SearchOption.AllDirectories).Single();
            Check(
                File.ReadAllLines(history).TakeLast(2).SequenceEqual(["400", "50"]) &&
                legacy.ReadState()!.Completed == 11, "older engine uses 4x fallback and restores speed");
        }

        await session.TakeoverAsync();
        Check(session.IsLive && session.ReadState()?.Phase == "live-paused", "takeover acknowledgment");
        var liveProcess = session.GameProcessId;
        var captured = await session.CaptureRecordingAsync();
        Check(
            captured.Count == 3 && session is { IsRunning: true, IsLive: true } && session.GameProcessId == liveProcess,
            "live snapshot leaves process and mode intact");
        await session.StepAsync(new bool[19], CancellationToken.None);
        Check(session.ReadState()?.Completed == 4, "single frame acknowledgment");
        Check((await session.CaptureRecordingAsync()).Count == 4 && session.GameProcessId == liveProcess,
            "recording continues after repeated save snapshot");
        var stateFile = Directory.GetFiles(session.SessionDirectory, "state.txt", SearchOption.AllDirectories)
            .OrderBy(File.GetLastWriteTimeUtc).Last();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(stateFile)!, "command.txt"), "999 stop 0\n");
        var exitDeadline = DateTime.UtcNow.AddSeconds(10);
        while (session.IsRunning && DateTime.UtcNow < exitDeadline)
            await Task.Delay(10);

        Check(!session.IsRunning, "external game close detected");
        await session.RestartAsync();
        Check(session.IsRunning && session.ReadState()?.Completed == 4 && !session.IsLive,
            "restart restores closed live recording at last completed frame");
        var branch = await session.StopAsync();
        Check(Replay.Load(branch).Count == 4, "branch finalized before load");
        Check(File.ReadAllText(Path.Combine(initial, "marisaA.dat")) == "original", "initial save untouched");
        await using var editSession = new FileGameSession(exe, Path.Combine(output, "edit-session"), replay, initial,
            new string('a', 64));
        await editSession.StartAsync();
        var edited = new TasProject(Replay.Load(replay), "edit.krec");
        edited.SetRange(0, 0, 4, false);
        edited.SetRange(5, 9, 1, false);
        await using var result = await editSession.ResimulateAsync(edited);
        var generated = Replay.Load(result.PlaybackSource!);
        Check(
            generated.Count == 180 && generated.Held(0, 4) == 0 && generated.Held(1, 4) == 1 &&
            generated.Released(5, 1) && generated.Held(10, 1) == 1,
            "edited input resimulation includes frame zero, releases and held duration");
        Check(generated.Checkpoint(0) != edited.Source.Checkpoint(0), "edited checksums are regenerated by engine");
        Check(result.ReadState()?.Completed == 180 && result.ReadState()!.Phase == "paused",
            "generated recording replay verified through last frame");
        Check(Replay.Load(replay).Held(0, 4) == 1 && editSession.IsRunning && edited.EditCount == 6,
            "original session and edit intentions survive transaction");
        var longer = new TasProject(Replay.Load(replay), "longer.krec");
        longer.InsertFrames(2, 3);
        await using var longerSession = await editSession.ResimulateAsync(longer);
        var longerReplay = Replay.Load(longerSession.PlaybackSource!);
        Check(longerReplay.Count == 183 && longerReplay.Released(2, 4) && longerReplay.Held(5, 4) == 1,
            "inserted blank frames regenerate release and duration checkpoints");
        var appended = new TasProject(Replay.Load(replay), "appended.krec");
        appended.InsertFrames(appended.FrameCount, 2);
        await using var appendedSession = await editSession.ResimulateAsync(appended);
        Check(Replay.Load(appendedSession.PlaybackSource!).Count == 182,
            "appending past original EOF takes over instead of stopping");
        var shorter = new TasProject(Replay.Load(replay), "shorter.krec");
        shorter.DeleteFrames(0, 3);
        await using var shorterSession = await editSession.ResimulateAsync(shorter);
        Check(Replay.Load(shorterSession.PlaybackSource!).Count == 177,
            "deleted frames produce a verified shorter recording");
        using var cancelled = new CancellationTokenSource();
        try
        {
            await editSession.ResimulateAsync(edited, new TestProgress(p =>
            {
                if (p.Completed >= 3)
                    cancelled.Cancel();
            }), cancelled.Token);
            throw new Exception("Cancellation ignored");
        }
        catch (OperationCanceledException)
        {
        }

        Check(editSession.IsRunning && editSession.ReadState()!.Phase == "paused" && edited.EditCount == 6,
            "cancelled resimulation keeps old recording and draft");
        using var seekCancel = new CancellationTokenSource();

        void CancelSeek(SessionState state)
        {
            if (state.Completed >= 3)
                seekCancel.Cancel();
        }

        editSession.Progress += CancelSeek;
        try
        {
            await editSession.SeekAsync(170, seekCancel.Token);
            throw new Exception("Seek cancellation ignored");
        }
        catch (OperationCanceledException)
        {
        }

        editSession.Progress -= CancelSeek;
        Check(editSession.ReadState()!.Phase == "paused", "cancelled seek pauses engine at acknowledged boundary");
        using var verifyCancel = new CancellationTokenSource();
        try
        {
            await editSession.ResimulateAsync(edited, new TestProgress(p =>
            {
                if (p is { Stage: "回放验证", Completed: >= 3 })
                    verifyCancel.Cancel();
            }), verifyCancel.Token);
            throw new Exception("Verification cancellation ignored");
        }
        catch (OperationCanceledException)
        {
        }

        Check(editSession.IsRunning && edited.EditCount == 6, "cancelling verification preserves original and draft");
        File.WriteAllText(Path.Combine(editSession.SessionDirectory, "initial", "fail-verification.dat"), "fixture");
        try
        {
            await editSession.ResimulateAsync(edited);
            throw new Exception("Verification failure ignored");
        }
        catch (InvalidDataException)
        {
        }

        Check(editSession.IsRunning && edited.EditCount == 6,
            "failed verification cannot replace original or clear edits");
        File.WriteAllText(Path.Combine(editSession.SessionDirectory, "initial", "no-edits.dat"), "fixture");
        try
        {
            await editSession.ResimulateAsync(edited);
            throw new Exception("Old engine accepted");
        }
        catch (NotSupportedException)
        {
        }

        Check(editSession.IsRunning, "unsupported engine fails explicitly and preserves original session");
        return exe;
    }

    private sealed class TestProgress(Action<SimulationProgress> callback) : IProgress<SimulationProgress>
    {
        public void Report(SimulationProgress value) => callback(value);
    }
}
