namespace ObjectDetectionApi.Models;

public class Video
{
    public required string Input { get; set; }
    // clean frames (no boxes) for training a new model
    public bool CollectDataset { get; set; } = false;
    public string DatasetPath { get; set; } = "dataset_frames";
    public int DatasetSaveEvery { get; set; } = 10;
}
