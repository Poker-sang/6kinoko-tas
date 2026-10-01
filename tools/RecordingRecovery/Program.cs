using System.Security.Cryptography;
using KinokoTAS.Core;

// Read-only source handling; recovery always goes to a new destination.
if(args.Length==2 && args[0]=="draft") {
    var draft=TasProject.Load(args[1]);
    Console.WriteLine($"draft_frames={draft.FrameCount} source_frames={draft.Source.Count} edits={draft.EditCount} invalid_from={draft.InvalidFrom}");
    return;
}
if(args.Length!=4)throw new ArgumentException("Usage: RecordingRecovery <source> <new.krec> <initial-directory> <bookmarks.json-or-saved.krec>");
string source=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
if(File.Exists(output) || Directory.Exists(output))throw new IOException("Recovery destination already exists; originals must not be overwritten.");
var replay=Replay.Load(source);
var marks=Path.GetExtension(args[3]).Equals(".krec",StringComparison.OrdinalIgnoreCase)
    ?RecordingPackage.Load(args[3]).Bookmarks
    :RecordingLibrary.LoadBookmarks(args[3],replay.Count);
RecordingPackage.Save(output,replay,args[2],marks);
var verified=RecordingPackage.Load(output);
if(!verified.Replay.Bytes.Span.SequenceEqual(replay.Bytes.Span) || !verified.Bookmarks.SequenceEqual(marks))
    throw new InvalidDataException("Recovered replay or bookmarks differ from source.");
Console.WriteLine($"frames={verified.Replay.Count} bookmarks={verified.Bookmarks.Length} initial_files={verified.Initial.Count}");
Console.WriteLine($"replay_sha256={Convert.ToHexString(SHA256.HashData(replay.Bytes.Span))}");
Console.WriteLine($"output={output}");
