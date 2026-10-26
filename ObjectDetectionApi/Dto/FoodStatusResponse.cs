using ObjectDetectionApi.Services;

namespace ObjectDetectionApi.Dto;

public class FoodStatusResponse
{
    public FoodStatus Status { get; set; }
    public float Confidence { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
