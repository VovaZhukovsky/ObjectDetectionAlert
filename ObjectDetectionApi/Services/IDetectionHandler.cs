namespace ObjectDetectionApi.Services;

public interface IDetectionHandler
{
    Task HandleAsync(FoodStatusResult result, CancellationToken cancellationToken = default);
}
