namespace ObjectDetectionApi.Models;

public class Video
{
    public required string Input { get; set; }
    public bool NeedOutput { get; set; } = false;
    public string OutputPath { get; set; } = "video_output";
    public int MaxFiles { get; set; } = 30;
}
