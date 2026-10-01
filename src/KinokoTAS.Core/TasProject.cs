using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace KinokoTAS.Core;

public readonly record struct Cell(int Frame, int Action);

public sealed record Edit(int Frame, int Action, bool Down);

public readonly record struct FrameIntent(int SourceFrame, uint Mask);

public sealed record ProjectManifest(
    int Version,
    string SourceName,
    Edit[] Edits,
    FrameIntent[]? Frames = null,
    FrameBookmark[]? Bookmarks = null);

public sealed record FrameLayoutChange(long Id, int First, int Removed, int Inserted, bool Forward);

public sealed class TasProject
{
    public Replay Source { get; }

    public string SourceName { get; }

    public RecordingPackage? EmbeddedRecording { get; private set; }

    public FrameBookmark[]? EmbeddedBookmarks { get; private set; }

    private readonly List<FrameIntent> _frames;
    private readonly SortedSet<int> _editedFrames = [];
    private readonly Stack<Action<bool>> _undo = [], _redo = [];
    private int _inputEditCount;
    private int? _layoutFrom;
    private long _nextLayoutId;
    private Dictionary<int, uint>? _paintingBefore;

    public int FrameCount => _frames.Count;

    public int EditCount => _inputEditCount + (_layoutFrom.HasValue ? 1 : 0);

    public bool HasLayoutChanges => _layoutFrom.HasValue;

    public bool IsPainting => _paintingBefore is not null;

    public bool CanUndo => _undo.Count > 0 && !IsPainting;

    public bool CanRedo => _redo.Count > 0 && !IsPainting;

    public int? InvalidFrom
    {
        get
        {
            int? first = _editedFrames.Count > 0 ? _editedFrames.Min : null;
            if (_layoutFrom is { } layout)
                first = first is { } input ? Math.Min(layout, input) : layout;

            return first is { } index ? Math.Min(index, FrameCount - 1) : null;
        }
    }

    public event Action? Changed;

    public event Action<FrameLayoutChange>? LayoutChanged;

    public TasProject(Replay source, string sourceName)
    {
        Source = source;
        SourceName = sourceName;
        _frames = new(source.Count);
        for (var frame = 0; frame < source.Count; frame++)
            _frames.Add(new(frame, SourceMask(frame)));
    }

    private uint SourceMask(int frame)
    {
        if (frame < 0)
            return 0;

        uint mask = 0;
        for (var action = 0; action < Replay.ActionCount; action++)
            if (Source.Held(frame, action) > 0)
                mask |= 1u << action;

        return mask;
    }

    public int SourceFrame(int frame) => _frames[frame].SourceFrame;

    public bool IsEdited(int frame, int action) => _frames[frame].SourceFrame != frame ||
                                                   (_frames[frame].Mask & (1u << action)) !=
                                                   (SourceMask(_frames[frame].SourceFrame) & (1u << action));

    public bool Down(int frame, int action) => (_frames[frame].Mask & (1u << action)) != 0;

    public uint Mask(int frame) => _frames[frame].Mask;

    public void WriteEditPlan(string path)
    {
        if (InvalidFrom is not { } first || first < 0)
            throw new InvalidOperationException("没有待执行的输入修改。");

        AtomicFile.Write(path, stream =>
        {
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            writer.Write(FrameCount == Source.Count ? "KTASED01"u8 : "KTASED02"u8);
            writer.Write(FrameCount);
            writer.Write(first);
            if (FrameCount != Source.Count)
                writer.Write(Source.Count);

            foreach (var frame in _frames)
                writer.Write(frame.Mask);
        });
    }

    private void SetMask(int frame, uint mask)
    {
        var previous = _frames[frame];
        var baseline = SourceMask(previous.SourceFrame);
        _inputEditCount += BitOperations.PopCount(mask ^ baseline) - BitOperations.PopCount(previous.Mask ^ baseline);
        _frames[frame] = previous with { Mask = mask };
        if (mask == baseline)
            _editedFrames.Remove(frame);

        else
            _editedFrames.Add(frame);
    }

    public void SetRange(int first, int last, int action, bool down)
    {
        if (first < 0 || last < first || last >= FrameCount || (uint) action >= Replay.ActionCount)
            throw new ArgumentOutOfRangeException(nameof(first));

        var changes = new List<(int Frame, uint Before, uint After)>();
        var bit = 1u << action;
        for (var frame = first; frame <= last; frame++)
        {
            uint before = Mask(frame), after = down ? before | bit : before & ~bit;
            if (before == after)
                continue;

            _paintingBefore?.TryAdd(frame, before);

            changes.Add((frame, before, after));
            SetMask(frame, after);
        }

        if (changes.Count == 0)
            return;

        if (_paintingBefore is null)
            Remember(forward =>
            {
                foreach (var change in changes)
                    SetMask(change.Frame, forward ? change.After : change.Before);
            });

        Changed?.Invoke();
    }

    private void Remember(Action<bool> change)
    {
        _undo.Push(change);
        _redo.Clear();
    }

    public void BeginPaint()
    {
        if (IsPainting)
            throw new InvalidOperationException("绘制尚未结束。");

        _paintingBefore = [];
    }

    public void EndPaint()
    {
        if (_paintingBefore is not { } original)
            return;

        _paintingBefore = null;
        var changes = original.Select(entry => (Frame: entry.Key, Before: entry.Value, After: Mask(entry.Key)))
            .Where(change => change.Before != change.After).ToArray();
        if (changes.Length > 0)
            Remember(forward =>
            {
                foreach (var change in changes)
                    SetMask(change.Frame, forward ? change.After : change.Before);
            });

        Changed?.Invoke();
    }

    private void Reindex()
    {
        _inputEditCount = 0;
        _editedFrames.Clear();
        _layoutFrom = null;
        for (var frame = 0; frame < FrameCount; frame++)
        {
            var intent = _frames[frame];
            var baseline = SourceMask(intent.SourceFrame);
            var edits = BitOperations.PopCount(intent.Mask ^ baseline);
            _inputEditCount += edits;
            if (edits > 0)
                _editedFrames.Add(frame);

            if (_layoutFrom is null && intent.SourceFrame != frame)
                _layoutFrom = frame;
        }

        if (_layoutFrom is null && FrameCount != Source.Count)
            _layoutFrom = FrameCount;
    }

    public void InsertFrames(int before, int count)
    {
        if (IsPainting || before < 0 || before > FrameCount || count < 1 || count > Replay.MaxFrames - FrameCount)
            throw new ArgumentOutOfRangeException(nameof(count));

        var inserted = Enumerable.Repeat(new FrameIntent(-1, 0), count).ToArray();
        var identifier = ++_nextLayoutId;
        _frames.InsertRange(before, inserted);
        Reindex();
        Remember(forward =>
        {
            if (forward)
                _frames.InsertRange(before, inserted);

            else
                _frames.RemoveRange(before, count);

            Reindex();
            LayoutChanged?.Invoke(new(identifier, before, 0, count, forward));
        });
        LayoutChanged?.Invoke(new(identifier, before, 0, count, true));
        Changed?.Invoke();
    }

    public void DeleteFrames(int first, int count)
    {
        if (IsPainting || first < 0 || count < 1 || first > FrameCount - count || count >= FrameCount)
            throw new ArgumentOutOfRangeException(nameof(count), "至少保留一帧。");

        var removed = _frames.GetRange(first, count);
        var identifier = ++_nextLayoutId;
        _frames.RemoveRange(first, count);
        Reindex();
        Remember(forward =>
        {
            if (forward)
                _frames.RemoveRange(first, count);

            else
                _frames.InsertRange(first, removed);

            Reindex();
            LayoutChanged?.Invoke(new(identifier, first, count, 0, forward));
        });
        LayoutChanged?.Invoke(new(identifier, first, count, 0, true));
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!IsPainting && _undo.TryPop(out var change))
        {
            change(false);
            _redo.Push(change);
            Changed?.Invoke();
        }
    }

    public void Redo()
    {
        if (!IsPainting && _redo.TryPop(out var change))
        {
            change(true);
            _undo.Push(change);
            Changed?.Invoke();
        }
    }

    public void ExportSource(string path) => AtomicFile.Write(path, stream => stream.Write(Source.Bytes.Span));

    public void Save(string path) => Save(path, null, null);

    public void Save(string path, string? initial, IEnumerable<FrameBookmark>? bookmarks)
    {
        var package = initial is not null
            ? RecordingPackage.Capture(Source, initial, [])
            : EmbeddedRecording ?? new RecordingPackage(Source, [], new());
        var marks = (bookmarks ?? EmbeddedBookmarks ?? []).ToArray();
        if (marks.Any(m => m.Frame < 0 || m.Frame >= FrameCount || string.IsNullOrWhiteSpace(m.Name)))
            throw new InvalidDataException("草稿重点无效。");

        AtomicFile.Write(path, stream =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, true);
            using (var raw = zip.CreateEntry("source.krec", CompressionLevel.Optimal).Open())
                new RecordingPackage(Source, [], package.Initial).WriteTo(raw);

            var manifest = new ProjectManifest(3, SourceName, [], [.. _frames], marks);
            using var json = zip.CreateEntry("project.json", CompressionLevel.Optimal).Open();
            JsonSerializer.Serialize(json, manifest, RecordingJsonContext.Default.ProjectManifest);
        });
    }

    public static TasProject Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count != 2)
            throw new InvalidDataException("项目内容无效。");

        var raw = zip.GetEntry("source.krec") ?? throw new InvalidDataException("缺少原始录制。");
        var json = zip.GetEntry("project.json") ?? throw new InvalidDataException("缺少项目清单。");
        if (raw.Length > Replay.HeaderSize + (long) Replay.RecordSize * Replay.MaxFrames + 65 * 1024 * 1024 ||
            json.Length > 128 * 1024 * 1024)
            throw new InvalidDataException("项目超过大小限制。");

        using var memory = new MemoryStream();
        using (var stream = raw.Open())
            stream.CopyTo(memory);

        using var metadata = json.Open();
        var manifest = JsonSerializer.Deserialize(metadata, RecordingJsonContext.Default.ProjectManifest) ??
                       throw new InvalidDataException("项目清单为空。");
        if (manifest.Version != 3 || manifest.Edits is null || string.IsNullOrWhiteSpace(manifest.SourceName))
            throw new InvalidDataException("请使用包含初始存档的新版 .ktas 草稿。");

        memory.Position = 0;
        var package = RecordingPackage.Load(memory);
        var replay = package.Replay;
        var project = new TasProject(replay, manifest.SourceName);
        {
            if (manifest.Frames is null || (manifest.Frames.Length < 1 && replay.Count > 0) ||
                manifest.Frames.Length > Replay.MaxFrames ||
                manifest.Edits.Length > 0)
                throw new InvalidDataException("项目帧布局无效。");

            var previousSource = -1;
            foreach (var frame in manifest.Frames)
            {
                if (frame.SourceFrame < -1 || frame.SourceFrame >= replay.Count ||
                    frame.Mask >> Replay.ActionCount != 0 ||
                    (frame.SourceFrame >= 0 && frame.SourceFrame <= previousSource))
                    throw new InvalidDataException("项目含无效或重复来源帧。");

                if (frame.SourceFrame >= 0)
                    previousSource = frame.SourceFrame;
            }

            project._frames.Clear();
            project._frames.AddRange(manifest.Frames);
            project.Reindex();
        }
        if (manifest.Bookmarks is null || manifest.Bookmarks.Any(m =>
                m is null || m.Frame < 0 || m.Frame >= project.FrameCount || string.IsNullOrWhiteSpace(m.Name)))
            throw new InvalidDataException("草稿重点无效。");

        project.EmbeddedRecording = package;
        project.EmbeddedBookmarks = manifest.Bookmarks;
        return project;
    }
}

public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write)
    {
        path = Path.GetFullPath(path);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }

            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
