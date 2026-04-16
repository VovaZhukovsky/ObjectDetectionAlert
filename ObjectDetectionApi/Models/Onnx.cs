namespace ObjectDetectionApi.Models;

public class Onnx
{
    public required string ModelPath { get; set; }
    public string[] ObjectsForSearch { get; set; } = [];
    public double Confidence { get; set; } = 0.5;
    public double Iou { get; set; } = 0.45;
}
