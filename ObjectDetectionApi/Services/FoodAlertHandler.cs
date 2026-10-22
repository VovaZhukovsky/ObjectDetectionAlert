using ObjectDetectionApi.Models;
using Telegram.Bot;

namespace ObjectDetectionApi.Services;

public class FoodAlertHandler : IDetectionHandler
{
    private readonly TelegramBotClient _client;
    private readonly string _chatId;
    private readonly int _missesToAlert;
    private readonly ILogger<FoodAlertHandler> _logger;
    private int _consecutiveMisses;
    private bool _alertSent;

    public FoodAlertHandler(TelegramBot settings, FoodAlertSettings alertSettings, ILogger<FoodAlertHandler> logger)
    {
        _client = new TelegramBotClient(settings.Token);
        _chatId = settings.ChatId;
        _missesToAlert = alertSettings.ConsecutiveMissesToAlert;
        _logger = logger;
    }

    public async Task HandleAsync(DetectionResult result, CancellationToken cancellationToken = default)
    {
        if (result.ObjectsFound)
        {
            _consecutiveMisses = 0;
            _alertSent = false;
            return;
        }

        // a single miss may be a cat blocking the bowl — alert only after several misses in a row
        _consecutiveMisses++;
        _logger.LogInformation("FoodAlertHandler: food not found ({Misses}/{Required})", _consecutiveMisses, _missesToAlert);

        if (_alertSent || _consecutiveMisses < _missesToAlert)
            return;

        _logger.LogWarning("FoodAlertHandler: cat food disappeared — sending Telegram alert");
        await _client.SendMessage(_chatId, "🍽️ Cat food is empty! Time to refill the bowl.", cancellationToken: cancellationToken);
        _alertSent = true;
    }
}
