namespace ObjectDetectionApi.Models;

public class FoodAlertSettings
{
    public int ConsecutiveMissesToAlert { get; set; } = 3;
    public int NoBowlRunsToAlert { get; set; } = 10;
}
