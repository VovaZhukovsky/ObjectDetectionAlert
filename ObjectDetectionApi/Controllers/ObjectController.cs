using Microsoft.AspNetCore.Mvc;
using ObjectDetectionApi.Dto;
using ObjectDetectionApi.Services;

namespace ObjectDetectionApi.Controllers;

[ApiController]
[Route("object")]
public class ObjectController : ControllerBase
{
    private readonly IObjectDetectionService _detectionService;

    public ObjectController(IObjectDetectionService detectionService)
    {
        _detectionService = detectionService;
    }

    [HttpGet("exist")]
    public async Task<ActionResult<ObjectExistResponse>> Exist(CancellationToken cancellationToken)
    {
        var result = await _detectionService.RunDetectionAsync(cancellationToken);

        return Ok(new ObjectExistResponse
        {
            ObjectsFound = result.ObjectsFound,
            DetectedLabels = result.DetectedLabels.ToArray(),
            TotalDetections = result.TotalDetections,
            ProcessedAt = result.ProcessedAt
        });
    }

    [HttpGet("count")]
    public async Task<ActionResult<ObjectCountResponse>> Count(CancellationToken cancellationToken)
    {
        var result = await _detectionService.RunDetectionAsync(cancellationToken);

        return Ok(new ObjectCountResponse
        {
            TotalDetections = result.TotalDetections,
            DetectionsByLabel = result.DetectionsByLabel.ToDictionary(),
            ProcessedAt = result.ProcessedAt
        });
    }
}
