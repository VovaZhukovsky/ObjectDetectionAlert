namespace ObjectDetectionApi.Dto;

public class ObjectCountResponse
{
    public int TotalDetections { get; set; }
    public Dictionary<string, int> DetectionsByLabel { get; set; } = [];
    public DateTimeOffset ProcessedAt { get; set; }
}
