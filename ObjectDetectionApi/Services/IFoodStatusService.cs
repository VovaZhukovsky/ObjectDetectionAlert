namespace ObjectDetectionApi.Services;

// Values match the classifier's class names: blocked, enough, low, no_bowl
public enum FoodStatus
{
    Blocked,
    Enough,
    Low,
    NoBowl
}

public record FoodStatusResult(
    FoodStatus Status,
    float Confidence,
    DateTimeOffset ProcessedAt,
    // saved frame for low and no_bowl, otherwise null
    string? SnapshotPath
);

public interface IFoodStatusService
{
    Task<FoodStatusResult> GetStatusAsync(CancellationToken cancellationToken = default);
}
