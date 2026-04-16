using ObjectDetectionApi.Models;
using Telegram.Bot;

namespace ObjectDetectionApi.Services;

public class FoodAlertHandler : IDetectionHandler
{
    private readonly TelegramBotClient _client;
    private readonly string _chatId;
    private readonly ILogger<FoodAlertHandler> _logger;
    private bool? _previousFoodFound;

    public FoodAlertHandler(TelegramBot settings, ILogger<FoodAlertHandler> logger)
    {
        _client = new TelegramBotClient(settings.Token);
        _chatId = settings.ChatId;
        _logger = logger;
    }

    public async Task HandleAsync(DetectionResult result, CancellationToken cancellationToken = default)
    {
        var foodFound = result.ObjectsFound;

        // alert only on transition from found → not found
        var shouldAlert = !foodFound && (_previousFoodFound == true);

        _previousFoodFound = foodFound;

        if (!shouldAlert)
            return;

        _logger.LogWarning("FoodAlertHandler: cat food just disappeared — sending Telegram alert");
        await _client.SendMessage(_chatId, "Кошачий корм не обнаружен! Нужно пополнить миску.", cancellationToken: cancellationToken);
    }
}
