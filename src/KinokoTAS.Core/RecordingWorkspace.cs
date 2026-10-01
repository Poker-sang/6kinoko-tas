using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KinokoTAS.Core;

/// <summary>Owns editor-created session files; user recordings are read-only until explicitly saved.</summary>
public sealed class RecordingWorkspace(string rootDirectory)
{
    private static readonly string[] _Resources = ["6kinoko_a.dat", "6kinoko_b.dat", "6kinoko_c.dat"];

    private string Sessions => Path.Combine(rootDirectory, "sessions");

    public string? FindInitialDirectory(string? source)
    {
        if (source is null)
            return null;

        RecordingPackage? package = null;
        if (Path.GetExtension(source).Equals(".ktas", StringComparison.OrdinalIgnoreCase))
            package = TasProject.Load(source).EmbeddedRecording!;
        else if (Path.GetExtension(source).Equals(".krec", StringComparison.OrdinalIgnoreCase) &&
                 RecordingPackage.IsPackage(source))
            package = RecordingPackage.Load(source);

        if (package is not null)
        {
            var directory = Path.Combine(Sessions, "unpacked-" + Guid.NewGuid().ToString("N"), "initial");
            package.ExtractInitial(directory);
            return directory;
        }

        var parent = Directory.GetParent(source);
        foreach (var root in new[] { parent?.FullName, parent?.Parent?.FullName })
            if (root is not null && Directory.Exists(Path.Combine(root, "initial")))
                return Path.Combine(root, "initial");

        return null;
    }

    public FileGameSession CreateRecordingSession(string executable, bool externalWindow)
    {
        var initial = Path.Combine(Sessions, "empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(initial);
        var text = FileGameSession.HashFile(executable);
        foreach (var resource in _Resources)
            text += FileGameSession.HashFile(Path.Combine(Path.GetDirectoryName(executable)!, resource));

        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return CreateSession(executable, null, initial, identity, externalWindow);
    }

    public FileGameSession CreatePlaybackSession(string executable, TasProject project, string initial,
        bool externalWindow)
    {
        Directory.CreateDirectory(Sessions);
        var source = Path.Combine(Sessions, "source-" + Guid.NewGuid().ToString("N") + ".krec");
        project.ExportSource(source);
        return CreateSession(executable, source, initial, project.Source.Identity, externalWindow);
    }

    private FileGameSession CreateSession(string executable, string? source, string initial, string identity,
        bool externalWindow) =>
        new(executable,
            Path.Combine(Sessions,
                "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]),
            source, initial, identity, externalWindow);

    public static void VerifyInitial(string initial, string executable)
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
                        StringComparison.OrdinalIgnoreCase)
                    || FileGameSession.HashFile(path) != entry.GetProperty("sha256").GetString())
                    throw new InvalidDataException("初始存档/配置校验失败。");
            }

            foreach (var entry in json.RootElement.GetProperty("runtime").EnumerateArray())
            {
                var name = entry.GetProperty("path").GetString()!;
                if (name.StartsWith("6kinoko_", StringComparison.OrdinalIgnoreCase)
                    && FileGameSession.HashFile(Path.Combine(Path.GetDirectoryName(executable)!, name)) !=
                    entry.GetProperty("sha256").GetString())
                    throw new InvalidDataException("游戏资源与录制不匹配。");
            }
        }

        manifest = Path.Combine(root, "session.json");
        if (!File.Exists(manifest))
            return;

        var metadata =
            JsonSerializer.Deserialize(File.ReadAllText(manifest), RecordingJsonContext.Default.SessionMetadata)!;
        foreach (var entry in metadata.InitialFiles)
            if (Path.GetFileName(entry.Path) != entry.Path ||
                FileGameSession.HashFile(Path.Combine(initial, entry.Path)) != entry.Sha256)
                throw new InvalidDataException("分支初始存档校验失败。");
    }

    public Task<VideoExportResult> ExportVideoAsync(string saved, string executable, string output, int first, int last,
        string encoder, IProgress<VideoExportProgress>? progress, CancellationToken token) => Task.Run(async () =>
    {
        var package = RecordingPackage.Load(saved);
        var root = Path.Combine(rootDirectory, "video-exports",
            "export-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var initial = Path.Combine(root, "initial");
        package.ExtractInitial(initial);
        var source = Path.Combine(root, "source.krec");
        AtomicFile.Write(source, stream => stream.Write(package.Replay.Bytes.Span));
        await using var session = new FileGameSession(executable, Path.Combine(root, "session"), source, initial,
            package.Replay.Identity, false, true);
        return await VideoExporter.ExportAsync(session, encoder, output, first, last, progress, token)
            .ConfigureAwait(false);
    }, token);
}
