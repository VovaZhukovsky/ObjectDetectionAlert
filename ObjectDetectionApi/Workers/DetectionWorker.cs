using ObjectDetectionApi.Models;
using ObjectDetectionApi.Services;

namespace ObjectDetectionApi.Workers;

public class DetectionWorker : BackgroundService
{
    private readonly IObjectDetectionService _detectionService;
    private readonly IEnumerable<IDetectionHandler> _handlers;
    private readonly WorkerSettings _settings;
    private readonly ILogger<DetectionWorker> _logger;

    public DetectionWorker(
        IObjectDetectionService detectionService,
        IEnumerable<IDetectionHandler> handlers,
        WorkerSettings settings,
        ILogger<DetectionWorker> logger)
    {
        _detectionService = detectionService;
        _handlers = handlers;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DetectionWorker started. Interval: {Interval}s", _settings.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("DetectionWorker: starting scheduled detection run");
                var result = await _detectionService.RunDetectionAsync(stoppingToken);

                _logger.LogInformation(
                    "DetectionWorker: run complete. ObjectsFound={Found}, TotalDetections={Count}",
                    result.ObjectsFound,
                    result.TotalDetections);

                foreach (var handler in _handlers)
                    await handler.HandleAsync(result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DetectionWorker: unhandled error during detection run");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.IntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("DetectionWorker stopped.");
    }
}
