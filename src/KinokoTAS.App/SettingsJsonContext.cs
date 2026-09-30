using System.Text.Json.Serialization;
namespace KinokoTAS.App;

internal sealed record EditorSettings(string? GameExe,bool Embedded);
[JsonSerializable(typeof(EditorSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;
