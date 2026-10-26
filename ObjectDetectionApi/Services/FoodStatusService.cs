using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ObjectDetectionApi.Models;
using SkiaSharp;

namespace ObjectDetectionApi.Services;

public class FoodStatusService : IFoodStatusService, IDisposable
{
    // Same gray as training (prepare_frames.py); the frame is padded to a square, not cropped
    private static readonly SKColor PadColor = new(114, 114, 114);

    private readonly Video _video;
    private readonly ISnapshotService _snapshotService;
    private readonly ILogger<FoodStatusService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _imageSize;
    private readonly FoodStatus[] _statusByClassIndex;
    private long _runCount;

    public FoodStatusService(
        Onnx onnx,
        Video video,
        ISnapshotService snapshotService,
        ILogger<FoodStatusService> logger)
    {
        _video = video;
        _snapshotService = snapshotService;
        _logger = logger;

        _logger.LogInformation("Loading ONNX model: {ModelPath}", onnx.ModelPath);

        // Loaded once and reused; captures are serialized through _semaphore
        _session = new InferenceSession(onnx.ModelPath);
        _inputName = _session.InputNames[0];
        _imageSize = _session.InputMetadata[_inputName].Dimensions[2];
        _statusByClassIndex = ReadClassNames(_session);

        _logger.LogInformation(
            "Model loaded: input {Size}x{Size}, classes {Classes}",
            _imageSize, _imageSize, string.Join(", ", _statusByClassIndex));
    }

    public async Task<FoodStatusResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await GetStatusCoreAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<FoodStatusResult> GetStatusCoreAsync(CancellationToken cancellationToken)
    {
        using var frame = await _snapshotService.CaptureAsync(cancellationToken);

        if (_video.CollectDataset && _runCount++ % _video.DatasetSaveEvery == 0)
            SaveFrame(frame, _video.DatasetPath);

        var probabilities = Classify(frame);
        var best = Array.IndexOf(probabilities, probabilities.Max());
        var status = _statusByClassIndex[best];

        if (status is FoodStatus.Low or FoodStatus.NoBowl)
            SaveFrame(frame, Path.Combine(_video.StatusSnapshotPath, ToLabel(status)));

        _logger.LogInformation(
            "Food status: {Status} ({Confidence:F2}). All: {All}",
            status,
            probabilities[best],
            string.Join(", ", _statusByClassIndex.Select((s, i) => $"{ToLabel(s)}:{probabilities[i]:F2}")));

        return new FoodStatusResult(status, probabilities[best], DateTimeOffset.UtcNow);
    }

    private float[] Classify(SKBitmap frame)
    {
        var input = new DenseTensor<float>(ToChwPixels(frame), [1, 3, _imageSize, _imageSize]);
        using var outputs = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input)]);

        // The exported classifier already applies softmax
        return outputs[0].AsEnumerable<float>().ToArray();
    }

    private float[] ToChwPixels(SKBitmap frame)
    {
        using var square = new SKBitmap(_imageSize, _imageSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(square))
        {
            canvas.Clear(PadColor);

            var scale = (float)_imageSize / Math.Max(frame.Width, frame.Height);
            var width = frame.Width * scale;
            var height = frame.Height * scale;
            var dest = SKRect.Create((_imageSize - width) / 2, (_imageSize - height) / 2, width, height);

            using var image = SKImage.FromBitmap(frame);
            canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        var rgba = square.GetPixelSpan();
        var plane = _imageSize * _imageSize;
        var pixels = new float[3 * plane];
        for (var i = 0; i < plane; i++)
        {
            pixels[i] = rgba[i * 4] / 255f;
            pixels[plane + i] = rgba[i * 4 + 1] / 255f;
            pixels[2 * plane + i] = rgba[i * 4 + 2] / 255f;
        }

        return pixels;
    }

    // Ultralytics stores class names as metadata, e.g. "{0: 'blocked', 1: 'enough', 2: 'low', 3: 'no_bowl'}"
    private static FoodStatus[] ReadClassNames(InferenceSession session)
    {
        if (!session.ModelMetadata.CustomMetadataMap.TryGetValue("names", out var names))
            throw new InvalidOperationException("ONNX model has no 'names' metadata; is it an Ultralytics export?");

        var labels = Regex.Matches(names, @"(\d+):\s*'([^']+)'")
            .OrderBy(m => int.Parse(m.Groups[1].Value))
            .Select(m => m.Groups[2].Value)
            .ToArray();

        var statuses = labels
            .Select(label => Enum.GetValues<FoodStatus>().FirstOrDefault(s => ToLabel(s) == label, (FoodStatus)(-1)))
            .ToArray();

        if (statuses.Contains((FoodStatus)(-1)) || statuses.Distinct().Count() != Enum.GetValues<FoodStatus>().Length)
            throw new InvalidOperationException(
                $"ONNX model classes [{string.Join(", ", labels)}] do not match food statuses " +
                $"[{string.Join(", ", Enum.GetValues<FoodStatus>().Select(ToLabel))}]; is this the food classifier?");

        return statuses;
    }

    private static string ToLabel(FoodStatus status) => JsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString());

    private void SaveFrame(SKBitmap frame, string directory)
    {
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.jpg");
        using var data = frame.Encode(SKEncodedImageFormat.Jpeg, 95);
        using var file = File.Create(filePath);
        data.SaveTo(file);

        _logger.LogInformation("Saved frame: {File}", filePath);
    }

    public void Dispose()
    {
        _session.Dispose();
        _semaphore.Dispose();
    }
}
