using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KinokoTAS.Core;

public sealed record VideoExportProgress(int Completed, int Total, int Frame, string Stage);

public sealed record VideoExportResult(string Path, int Frames, int Width, int Height);

public static class VideoExporter
{
    public static string? FindEncoder()
    {
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var directories = new[]
            {
                AppContext.BaseDirectory, Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft",
                    "WinGet", "Links")
            }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries));
        return directories.Select(dir => Path.Combine(dir.Trim('"'), name)).FirstOrDefault(File.Exists);
    }

    public static async Task<VideoExportResult> ExportAsync(FileGameSession session, string encoder, string output,
        int first, int last, IProgress<VideoExportProgress>? progress = null, CancellationToken ct = default)
    {
        if (session.ExternalWindow || !session.Silent || session.PlaybackSource is null)
            throw new ArgumentException("需要独立、静音的画面回放会话。");

        var count = Replay.Load(session.PlaybackSource).Count;
        if (first < 0 || last < first || last >= count)
            throw new ArgumentOutOfRangeException(nameof(first));

        output = Path.GetFullPath(output);
        if (!Path.GetExtension(output).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请使用 .mp4 扩展名。");

        var temporary = Path.Combine(Path.GetDirectoryName(output)!,
            $".{Path.GetFileNameWithoutExtension(output)}-{Guid.NewGuid():N}.partial.mp4");
        Process? process = null;
        Task<string>? errors = null;
        try
        {
            progress?.Report(new(0, last - first + 1, first, "定位起始帧"));
            await session.StartAsync(ct);
            var preview = await session.ReadVideoFrameAsync(first, ct);
            int width = preview.Width, height = preview.Height;
            var start = new ProcessStartInfo(encoder)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[]
                     {
                         "-hide_banner", "-loglevel", "error", "-nostats", "-f", "rawvideo", "-pixel_format",
                         "rgba", "-video_size", $"{preview.Width}x{preview.Height}", "-framerate", "60", "-i",
                         "pipe:0", "-an", "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-vf",
                         "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-y",
                         temporary
                     })
                start.ArgumentList.Add(arg);

            File.WriteAllLines(Path.Combine(session.SessionDirectory, "ffmpeg-arguments.txt"), start.ArgumentList);
            process = Process.Start(start) ?? throw new IOException("FFmpeg 启动失败。");
            errors = process.StandardError.ReadToEndAsync();
            for (var frame = first; frame <= last; frame++)
            {
                ct.ThrowIfCancellationRequested();
                if (frame != first)
                    preview = await session.ReadVideoFrameAsync(frame, ct);

                if (preview.Width != width || preview.Height != height)
                    throw new InvalidDataException("导出期间画面尺寸变化。");

                await process.StandardInput.BaseStream.WriteAsync(preview.Pixels, ct);
                progress?.Report(new(frame - first + 1, last - first + 1, frame, "编码画面"));
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync(ct);
            var diagnostic = await errors;
            if (process.ExitCode != 0)
                throw new IOException("FFmpeg 编码失败：" + diagnostic);

            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new IOException("FFmpeg 未生成视频。");

            ct.ThrowIfCancellationRequested();
            File.Move(temporary, output, true);
            return new(output, last - first + 1, preview.Width, preview.Height);
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync();
                }

                if (errors is not null)
                    File.WriteAllText(Path.Combine(session.SessionDirectory, "ffmpeg.log"), await errors);

                process.Dispose();
            }

            // Session artifacts and partial MP4 are retained for diagnosis.
            await session.DisposeAsync();
        }
    }
}
