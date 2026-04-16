namespace ObjectDetectionApi.Dto;

public class ObjectExistResponse
{
    public bool ObjectsFound { get; set; }
    public string[] DetectedLabels { get; set; } = [];
    public int TotalDetections { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
