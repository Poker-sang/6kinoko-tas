using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KinokoTAS.Core;

public sealed record RecordingManifest(int Version, Dictionary<string, string> Sha256, FrameBookmark[] Bookmarks);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SessionMetadata))]
[JsonSerializable(typeof(ProjectManifest))]
[JsonSerializable(typeof(RecordingManifest))]
[JsonSerializable(typeof(FrameBookmark[]))]
public partial class RecordingJsonContext : JsonSerializerContext;
