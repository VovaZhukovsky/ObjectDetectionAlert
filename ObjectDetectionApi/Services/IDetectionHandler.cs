namespace ObjectDetectionApi.Services;

public interface IDetectionHandler
{
    Task HandleAsync(DetectionResult result, CancellationToken cancellationToken = default);
}
