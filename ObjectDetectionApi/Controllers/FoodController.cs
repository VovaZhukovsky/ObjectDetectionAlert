using Microsoft.AspNetCore.Mvc;
using ObjectDetectionApi.Dto;
using ObjectDetectionApi.Services;

namespace ObjectDetectionApi.Controllers;

[ApiController]
[Route("food")]
public class FoodController : ControllerBase
{
    private readonly IFoodStatusService _foodStatusService;

    public FoodController(IFoodStatusService foodStatusService)
    {
        _foodStatusService = foodStatusService;
    }

    [HttpGet("status")]
    public async Task<ActionResult<FoodStatusResponse>> Status(CancellationToken cancellationToken)
    {
        var result = await _foodStatusService.GetStatusAsync(cancellationToken);

        return Ok(new FoodStatusResponse
        {
            Status = result.Status,
            Confidence = result.Confidence,
            ProcessedAt = result.ProcessedAt
        });
    }
}
