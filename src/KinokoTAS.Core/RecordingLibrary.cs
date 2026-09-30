using System.Text.Json;
namespace KinokoTAS.Core;
public sealed record FrameBookmark(int Frame,string Name) {
    public override string ToString()=>$"{Name}  ·  第 {Frame} 帧";
}
public static class RecordingLibrary {
    public static FrameBookmark[] LoadBookmarks(string path,int maxFrames) {
        if(!File.Exists(path))return [];
        var marks=JsonSerializer.Deserialize(File.ReadAllText(path),RecordingJsonContext.Default.FrameBookmarkArray)??[];
        if(marks.Any(m=>m.Frame<0 || m.Frame>=maxFrames || string.IsNullOrWhiteSpace(m.Name)))throw new InvalidDataException("书签包含无效帧号或名称。");
        return marks;
    }
    public static void SaveBookmarks(string path,IEnumerable<FrameBookmark> marks) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFile.Write(path,s=>JsonSerializer.Serialize(s,marks.ToArray(),RecordingJsonContext.Default.FrameBookmarkArray));
    }
    public static string SaveBundle(string parent,Replay replay,string initial,IEnumerable<FrameBookmark> marks) {
        var root=Path.Combine(parent,"录制-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(root);var saves=Path.Combine(root,"initial");Directory.CreateDirectory(saves);
        foreach(var file in Directory.GetFiles(initial))File.Copy(file,Path.Combine(saves,Path.GetFileName(file)));
        SaveBookmarks(Path.Combine(root,"session.krec.bookmarks.json"),marks);
        var output=Path.Combine(root,"session.krec");AtomicFile.Write(output,s=>s.Write(replay.Bytes.Span));
        return output;
    }
}
