using ObjectDetectionApi.Models;
using SkiaSharp;
using YoloDotNet;
using YoloDotNet.Enums;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Models;

namespace ObjectDetectionApi.Services;

public class ObjectDetectionService : IObjectDetectionService, IDisposable
{
    private readonly Onnx _onnx;
    private readonly Video _video;
    private readonly ISnapshotService _snapshotService;
    private readonly ILogger<ObjectDetectionService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Yolo _yolo;
    private long _runCount;

    public ObjectDetectionService(
        Onnx onnx,
        Video video,
        ISnapshotService snapshotService,
        ILogger<ObjectDetectionService> logger)
    {
        _onnx = onnx;
        _video = video;
        _snapshotService = snapshotService;
        _logger = logger;

        _logger.LogInformation("Loading ONNX model: {ModelPath}", _onnx.ModelPath);

        // Loaded once and reused; Yolo is not thread-safe, so every call goes through _semaphore
        _yolo = new Yolo(new YoloOptions
        {
            ExecutionProvider = new CpuExecutionProvider(_onnx.ModelPath),
            ImageResize = ImageResize.Proportional,
            SamplingOptions = new(SKFilterMode.Nearest, SKMipmapMode.None)
        });

        _logger.LogInformation("Model loaded: {ModelInfo}", _yolo.ModelInfo);
    }

    public async Task<DetectionResult> RunDetectionAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await RunDetectionCoreAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<DetectionResult> RunDetectionCoreAsync(CancellationToken cancellationToken)
    {
        using var frame = await _snapshotService.CaptureAsync(cancellationToken);

        if (_video.CollectDataset && _runCount++ % _video.DatasetSaveEvery == 0)
            SaveDatasetFrame(frame);

        var results = _yolo.RunObjectDetection(frame, confidence: _onnx.Confidence, iou: _onnx.Iou);

        var processedAt = DateTimeOffset.UtcNow;

        var byLabel = results
            .GroupBy(r => r.Label.Name)
            .ToDictionary(g => g.Key, g => g.Count());
        var detectedLabels = byLabel.Keys.ToArray();
        var totalDetections = byLabel.Values.Sum();

        var objectsFound = detectedLabels
            .Any(label => _onnx.ObjectsForSearch.Contains(label, StringComparer.OrdinalIgnoreCase));

        _logger.LogInformation(
            "Detection run complete. ObjectsFound={Found}, TotalDetections={Total}, ByLabel={ByLabel}",
            objectsFound,
            totalDetections,
            string.Join(", ", byLabel.Select(kv => $"{kv.Key}:{kv.Value}")));

        return new DetectionResult(
            ObjectsFound: objectsFound,
            DetectedLabels: detectedLabels,
            TotalDetections: totalDetections,
            DetectionsByLabel: byLabel,
            ProcessedAt: processedAt
        );
    }

    private void SaveDatasetFrame(SKBitmap frame)
    {
        Directory.CreateDirectory(_video.DatasetPath);

        var filePath = Path.Combine(_video.DatasetPath, $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.jpg");
        using var data = frame.Encode(SKEncodedImageFormat.Jpeg, 95);
        using var file = File.Create(filePath);
        data.SaveTo(file);

        _logger.LogInformation("Saved dataset frame: {File}", filePath);
    }

    public void Dispose()
    {
        _yolo.Dispose();
        _semaphore.Dispose();
    }
}
