using ObjectDetectionApi.Models;
using Telegram.Bot;

namespace ObjectDetectionApi.Services;

public class TelegramNotificationService : ITelegramNotificationService
{
    private readonly TelegramBotClient _client;
    private readonly string _chatId;
    private readonly ILogger<TelegramNotificationService> _logger;

    public TelegramNotificationService(TelegramBot settings, ILogger<TelegramNotificationService> logger)
    {
        _client = new TelegramBotClient(settings.Token);
        _chatId = settings.ChatId;
        _logger = logger;
    }

    public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending Telegram alert: {Message}", message);
        await _client.SendMessage(_chatId, message, cancellationToken: cancellationToken);
    }
}
