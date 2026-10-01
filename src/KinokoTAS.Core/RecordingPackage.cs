using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace KinokoTAS.Core;

public sealed record RecordingPackage(Replay Replay, FrameBookmark[] Bookmarks, Dictionary<string, byte[]> Initial)
{
    private const long ReplayLimit = Replay.HeaderSize + (long) Replay.RecordSize * Replay.MaxFrames + 17;

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static bool SaveName(string name) => name.Length is > 0 and < 128 && !name.Contains('/') &&
                                                 !name.Contains('\\') && !name.Contains(':') && name != "." &&
                                                 name != ".." &&
                                                 !name.StartsWith("6kinoko_", StringComparison.OrdinalIgnoreCase) &&
                                                 (name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                                                  name == "input-actions.cfg");

    public static bool IsPackage(string path)
    {
        using var s = File.OpenRead(path);
        return s.ReadByte() == 80 && s.ReadByte() == 75;
    }

    public static RecordingPackage Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Load(stream);
    }

    public static RecordingPackage Load(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, true);
        if (zip.Entries.Count < 2 || zip.Entries.Count > 130 ||
            zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
            throw new InvalidDataException("录制包条目无效或重复。");

        byte[] Read(ZipArchiveEntry entry, long limit)
        {
            if (entry.Length > limit)
                throw new InvalidDataException("录制包内容超过限制。");

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            var block = new byte[81920];
            int n;
            while ((n = stream.Read(block)) > 0)
            {
                if (memory.Length + n > limit)
                    throw new InvalidDataException("解压内容超过限制。");

                memory.Write(block, 0, n);
            }

            return memory.ToArray();
        }

        var metadata = zip.GetEntry("recording.json") ?? throw new InvalidDataException("缺少录制包清单。");
        var manifest =
            JsonSerializer.Deserialize(Read(metadata, 1024 * 1024), RecordingJsonContext.Default.RecordingManifest) ??
            throw new InvalidDataException("无效清单。");
        if (manifest.Version != 1 || manifest.Sha256 is null || manifest.Bookmarks is null ||
            manifest.Sha256.Count != zip.Entries.Count - 1)
            throw new InvalidDataException("不支持的录制包版本或清单。");

        byte[]? raw = null;
        var initial = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry == metadata)
                continue;

            var isReplay = entry.FullName == "replay.krec";
            if (!isReplay && (!entry.FullName.StartsWith("initial/", StringComparison.Ordinal) ||
                              !SaveName(entry.FullName[8..])))
                throw new InvalidDataException("录制包包含无效路径。");

            var bytes = Read(entry, isReplay ? ReplayLimit : 16 * 1024 * 1024);
            if (!manifest.Sha256.TryGetValue(entry.FullName, out var hash) || Hash(bytes) != hash)
                throw new InvalidDataException("录制包文件校验失败。");

            if (isReplay)
                raw = bytes;

            else
            {
                total += bytes.Length;
                if (total > 64 * 1024 * 1024)
                    throw new InvalidDataException("初始存档过大。");

                initial.Add(entry.FullName[8..], bytes);
            }
        }

        var replay = Replay.Parse(raw ?? throw new InvalidDataException("缺少回放数据。"));
        if (manifest.Bookmarks.Any(m =>
                m is null || m.Frame < 0 || m.Frame >= replay.Count || string.IsNullOrWhiteSpace(m.Name)))
            throw new InvalidDataException("无效书签。");

        return new(replay, manifest.Bookmarks, initial);
    }

    public static void Save(string path, Replay replay, string initial, IEnumerable<FrameBookmark> bookmarks)
    {
        var package = Capture(replay, initial, bookmarks);
        AtomicFile.Write(path, package.WriteTo);
    }

    public static RecordingPackage Capture(Replay replay, string initial, IEnumerable<FrameBookmark> bookmarks)
    {
        var saves = new Dictionary<string, byte[]>();
        foreach (var file in Directory.GetFiles(initial))
        {
            var name = Path.GetFileName(file);
            if (!SaveName(name))
                throw new InvalidDataException("初始存档文件名不支持：" + name);

            if (new FileInfo(file).Length > 16 * 1024 * 1024)
                throw new InvalidDataException("初始文件过大。");

            saves.Add(name, File.ReadAllBytes(file));
        }

        return new(replay, [.. bookmarks], saves);
    }

    public void WriteTo(Stream stream)
    {
        var replay = Replay;
        var files = new Dictionary<string, byte[]> { { "replay.krec", replay.Bytes.ToArray() } };
        foreach (var file in Initial)
        {
            if (!SaveName(file.Key) || file.Value.Length > 16 * 1024 * 1024)
                throw new InvalidDataException("初始存档无效或过大。");

            files.Add("initial/" + file.Key, file.Value);
        }

        var marks = Bookmarks;
        if (files.Count > 129 ||
            files.Where(f => f.Key != "replay.krec").Sum(f => (long) f.Value.Length) > 64 * 1024 * 1024 ||
            marks.Any(m => m.Frame < 0 || m.Frame >= replay.Count || string.IsNullOrWhiteSpace(m.Name)))
            throw new InvalidDataException("录制包内容无效。");

        var manifest = new RecordingManifest(1, files.ToDictionary(f => f.Key, f => Hash(f.Value)), marks);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, true);
        foreach (var file in files)
        {
            using var entry = zip.CreateEntry(file.Key, CompressionLevel.Optimal).Open();
            entry.Write(file.Value);
        }

        using var json = zip.CreateEntry("recording.json", CompressionLevel.Optimal).Open();
        JsonSerializer.Serialize(json, manifest, RecordingJsonContext.Default.RecordingManifest);
    }

    public void ExtractInitial(string directory)
    {
        if (Directory.Exists(directory))
            throw new IOException("初始存档目标已存在。");

        Directory.CreateDirectory(directory);
        foreach (var file in Initial)
            File.WriteAllBytes(Path.Combine(directory, file.Key), file.Value);
    }
}
