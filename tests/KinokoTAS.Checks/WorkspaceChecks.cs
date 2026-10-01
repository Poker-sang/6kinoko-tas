using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KinokoTAS.Core;

internal static class WorkspaceChecks
{
    public static async Task RunAsync(Replay replay, string engine, string output, Action<bool, string> check)
    {
        var workspace = new RecordingWorkspace(Path.Combine(output, "workspace"));
        var initial = Path.Combine(output, "initial");
        var packed = Path.Combine(output, "workspace-package.krec");
        RecordingPackage.Save(packed, replay, initial, [new(2, "重点")]);
        var original = File.ReadAllBytes(packed);
        var first = workspace.FindInitialDirectory(packed)!;
        var second = workspace.FindInitialDirectory(packed)!;
        check(first != second && File.ReadAllText(Path.Combine(first, "marisaA.dat")) == "original",
            "workspace extracts independent initial snapshots from saved recording");
        check(File.ReadAllBytes(packed).AsSpan().SequenceEqual(original), "workspace leaves source recording unchanged");

        var draft = Path.Combine(output, "workspace-draft.ktas");
        new TasProject(replay, "source.krec").Save(draft, initial, null);
        check(File.ReadAllText(Path.Combine(workspace.FindInitialDirectory(draft)!, "marisaA.dat")) == "original",
            "workspace resolves self-contained draft without folder picker");

        await using (var playback = workspace.CreatePlaybackSession(engine, new(replay, "source.krec"), first, true))
            check(playback.ExternalWindow && Replay.Load(playback.PlaybackSource!).Bytes.Span.SequenceEqual(replay.Bytes.Span),
                "workspace prepares exact playback input and external window mode");

        await using (var recording = workspace.CreateRecordingSession(engine, false))
            check(!recording.ExternalWindow && recording.PlaybackSource is null,
                "workspace creates new recording independently of open replay");

        var root = Path.Combine(output, "workspace-validation");
        var saves = Path.Combine(root, "initial");
        Directory.CreateDirectory(saves);
        var save = Path.Combine(saves, "marisaA.dat");
        File.WriteAllText(save, "before");
        var metadata = new SessionMetadata(1, replay.Identity, "unused", "source.krec", [new("marisaA.dat", FileGameSession.HashFile(save))]);
        File.WriteAllText(Path.Combine(root, "session.json"), JsonSerializer.Serialize(metadata, RecordingJsonContext.Default.SessionMetadata));
        RecordingWorkspace.VerifyInitial(saves, engine);
        File.WriteAllText(save, "after");
        var rejected = false;
        try
        {
            RecordingWorkspace.VerifyInitial(saves, engine);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        check(rejected, "workspace rejects altered initial save before launching game");
        var video = await workspace.ExportVideoAsync(packed, engine, Path.Combine(output, "workspace-video.mp4"),
            2, 4, engine, null, CancellationToken.None);
        check(video.Frames == 3 && new FileInfo(video.Path).Length == 12 && File.ReadAllBytes(packed).AsSpan().SequenceEqual(original),
            "workspace video export uses isolated saved package (synthetic encoder)");
    }
}
