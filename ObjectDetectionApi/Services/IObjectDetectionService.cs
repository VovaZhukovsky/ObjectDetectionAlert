namespace ObjectDetectionApi.Services;

public record DetectionResult(
    bool ObjectsFound,
    IReadOnlyList<string> DetectedLabels,
    int TotalDetections,
    IReadOnlyDictionary<string, int> DetectionsByLabel,
    DateTimeOffset ProcessedAt
);

public interface IObjectDetectionService
{
    Task<DetectionResult> RunDetectionAsync(CancellationToken cancellationToken = default);
}
