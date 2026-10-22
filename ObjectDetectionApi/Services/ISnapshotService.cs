using SkiaSharp;

namespace ObjectDetectionApi.Services;

public interface ISnapshotService
{
    Task<SKBitmap> CaptureAsync(CancellationToken cancellationToken = default);
}
