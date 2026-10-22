using System.Diagnostics;
using ObjectDetectionApi.Models;
using SkiaSharp;

namespace ObjectDetectionApi.Services;

public class FfmpegSnapshotService : ISnapshotService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly Video _video;
    private readonly ILogger<FfmpegSnapshotService> _logger;

    public FfmpegSnapshotService(Video video, ILogger<FfmpegSnapshotService> logger)
    {
        _video = video;
        _logger = logger;
    }

    public async Task<SKBitmap> CaptureAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);

        var startInfo = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // -skip_frame nokey decodes only keyframes, so the first frame is never a half-decoded grey one
        foreach (var arg in new[]
                 {
                     "-hide_banner", "-loglevel", "error",
                     "-rtsp_transport", "tcp",
                     "-skip_frame", "nokey",
                     "-i", _video.Input,
                     "-an",
                     "-frames:v", "1",
                     "-f", "image2pipe", "-vcodec", "mjpeg",
                     "-",
                 })
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ffmpeg");

        try
        {
            using var output = new MemoryStream();
            var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(output, timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(timeoutCts.Token));

            if (process.ExitCode != 0 || output.Length == 0)
                throw new InvalidOperationException(
                    $"ffmpeg failed with exit code {process.ExitCode}: {stderrTask.Result.Trim()}");

            output.Position = 0;
            var bitmap = SKBitmap.Decode(output)
                ?? throw new InvalidOperationException("Failed to decode snapshot from ffmpeg");

            _logger.LogInformation("Snapshot captured: {Width}x{Height}", bitmap.Width, bitmap.Height);
            return bitmap;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"ffmpeg did not return a frame within {Timeout.TotalSeconds}s");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
