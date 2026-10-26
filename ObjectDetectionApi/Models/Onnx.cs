namespace ObjectDetectionApi.Models;

public class Onnx
{
    // food classifier exported by Ultralytics; class names and input size are read from the model
    public required string ModelPath { get; set; }
}
