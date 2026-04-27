using ObjectDetectionApi.Models;
using SkiaSharp;
using YoloDotNet;
using YoloDotNet.Enums;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Extensions;
using YoloDotNet.Models;
using YoloDotNet.Trackers;
using YoloDotNet.Video;

namespace ObjectDetectionApi.Services;

public class ObjectDetectionService : IObjectDetectionService
{
    private readonly Onnx _onnx;
    private readonly Video _video;
    private readonly ILogger<ObjectDetectionService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public ObjectDetectionService(Onnx onnx, Video video, ILogger<ObjectDetectionService> logger)
    {
        _onnx = onnx;
        _video = video;
        _logger = logger;
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
        // Max count of each label seen simultaneously in a single frame
        var maxPerLabel = new Dictionary<string, int>();

        _logger.LogInformation("Loading ONNX model: {ModelPath}", _onnx.ModelPath);

        using var yolo = new Yolo(new YoloOptions
        {
            ExecutionProvider = new CpuExecutionProvider(_onnx.ModelPath),
            ImageResize = ImageResize.Proportional,
            SamplingOptions = new(SKFilterMode.Nearest, SKMipmapMode.None)
        });

        _logger.LogInformation("Model loaded: {ModelInfo}", yolo.ModelInfo);

        var videoOptions = new VideoOptions
        {
            VideoInput = _video.Input,
            FrameRate = FrameRate.AUTO,
            Width = 416,
            Height = -2,
            CompressionQuality = 30,
            VideoChunkDuration = 0,
            FrameInterval = 5,
        };

        if (_video.NeedOutput)
        {
            Directory.CreateDirectory(_video.OutputPath);
            var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.mp4";
            videoOptions.VideoOutput = Path.Combine(_video.OutputPath, fileName);
            RotateOutputFiles(_video.OutputPath, _video.MaxFiles);
        }

        yolo.InitializeVideo(videoOptions);
        var metadata = yolo.GetVideoMetaData();
        _logger.LogInformation(
            "Video initialized: {Input} | {Width}x{Height} @ {Fps}fps | total frames: {TotalFrames}",
            _video.Input, metadata.Width, metadata.Height, metadata.FPS, metadata.TotalFrames);

        var sortTracker = new SortTracker(0.5f, 5, 60);

        DetectionDrawingOptions? drawingOptions = _video.NeedOutput ? new DetectionDrawingOptions
        {
            DrawBoundingBoxes = true,
            DrawConfidenceScore = true,
            DrawLabels = true,
            EnableFontShadow = true,
            Font = SKTypeface.Default,
            FontSize = 18,
            FontColor = SKColors.White,
            DrawLabelBackground = true,
            EnableDynamicScaling = true,
            BorderThickness = 2,
            BoundingBoxOpacity = 128,
            DrawTrackedTail = true,
            TailPaintColorStart = new SKColor(255, 105, 180),
            TailPaintColorEnd = SKColor.Empty.WithAlpha(0),
            TailThickness = 4,
        } : null;

        // With FrameInterval=5, frameIndex counts delivered frames, not original frames
        const int maxFramesToProcess = 20;

        yolo.OnVideoFrameReceived = (SKBitmap frame, long frameIndex) =>
        {
            var results = yolo.RunObjectDetection(frame, confidence: _onnx.Confidence, iou: _onnx.Iou).Track(sortTracker);

            foreach (var grp in results.GroupBy(r => r.Label.Name))
            {
                var count = grp.Count();
                if (!maxPerLabel.TryGetValue(grp.Key, out var prev) || count > prev)
                    maxPerLabel[grp.Key] = count;
            }

            if (drawingOptions is not null)
                frame.Draw(results, drawingOptions);

            if (frameIndex >= maxFramesToProcess)
                yolo.StopVideoProcessing();
        };

        _logger.LogInformation("Starting video processing...");
        await Task.Run(() => yolo.StartVideoProcessing(), cancellationToken);

        var processedAt = DateTimeOffset.UtcNow;

        var byLabel = maxPerLabel;
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

    private void RotateOutputFiles(string outputPath, int maxFiles)
    {
        var files = Directory.GetFiles(outputPath, "*.mp4")
            .OrderBy(File.GetCreationTimeUtc)
            .ToList();

        while (files.Count >= maxFiles)
        {
            File.Delete(files[0]);
            _logger.LogInformation("Deleted old video file: {File}", files[0]);
            files.RemoveAt(0);
        }
    }
}
