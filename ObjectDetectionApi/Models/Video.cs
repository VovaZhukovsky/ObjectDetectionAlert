namespace ObjectDetectionApi.Models;

public class Video
{
    public required string Input { get; set; }
    // clean frames (no boxes) for training a new model
    public bool CollectDataset { get; set; } = false;
    public string DatasetPath { get; set; } = "dataset_frames";
    public int DatasetSaveEvery { get; set; } = 10;
    // every frame classified as low or no_bowl, in a subfolder per status
    public string StatusSnapshotPath { get; set; } = "status_snapshots";
}
