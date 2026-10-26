using ObjectDetectionApi.Models;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ObjectDetectionApi.Services;

public class FoodAlertHandler : IDetectionHandler
{
    private readonly TelegramBotClient _client;
    private readonly string _chatId;
    private readonly int _missesToAlert;
    private readonly int _noBowlRunsToAlert;
    private readonly ILogger<FoodAlertHandler> _logger;
    private int _consecutiveMisses;
    private bool _alertSent;
    private int _consecutiveNoBowl;
    private bool _noBowlAlertSent;

    public FoodAlertHandler(TelegramBot settings, FoodAlertSettings alertSettings, ILogger<FoodAlertHandler> logger)
    {
        _client = new TelegramBotClient(settings.Token);
        _chatId = settings.ChatId;
        _missesToAlert = alertSettings.ConsecutiveMissesToAlert;
        _noBowlRunsToAlert = alertSettings.NoBowlRunsToAlert;
        _logger = logger;
    }

    public async Task HandleAsync(FoodStatusResult result, CancellationToken cancellationToken = default)
    {
        switch (result.Status)
        {
            case FoodStatus.Enough:
                _consecutiveMisses = 0;
                _alertSent = false;
                ResetNoBowl();
                break;

            case FoodStatus.Low:
                ResetNoBowl();
                await HandleLowAsync(result, cancellationToken);
                break;

            case FoodStatus.NoBowl:
                await HandleNoBowlAsync(result, cancellationToken);
                break;

            // the cat or a hand covers the bowl — we can't tell anything, keep the counters as they are
            case FoodStatus.Blocked:
                break;
        }
    }

    private async Task HandleLowAsync(FoodStatusResult result, CancellationToken cancellationToken)
    {
        // alert only after several low runs in a row, so one wrong prediction doesn't send a false alarm
        _consecutiveMisses++;
        _logger.LogInformation("FoodAlertHandler: food is low ({Misses}/{Required})", _consecutiveMisses, _missesToAlert);

        if (_alertSent || _consecutiveMisses < _missesToAlert)
            return;

        _logger.LogWarning("FoodAlertHandler: food is low — sending Telegram alert");
        await SendAlertAsync("🍽️ Cat food is running low! Time to refill the bowl.", result.SnapshotPath, cancellationToken);
        _alertSent = true;
    }

    private async Task HandleNoBowlAsync(FoodStatusResult result, CancellationToken cancellationToken)
    {
        _consecutiveNoBowl++;
        _logger.LogInformation("FoodAlertHandler: bowl not in view ({Runs}/{Required})", _consecutiveNoBowl, _noBowlRunsToAlert);

        if (_noBowlAlertSent || _consecutiveNoBowl < _noBowlRunsToAlert)
            return;

        _logger.LogWarning("FoodAlertHandler: bowl is missing — sending Telegram alert");
        await SendAlertAsync("🥣 The bowl is not in view of the camera.", result.SnapshotPath, cancellationToken);
        _noBowlAlertSent = true;
    }

    // the frame that triggered the alert goes along as a photo, so the bowl can be checked at a glance
    private async Task SendAlertAsync(string text, string? snapshotPath, CancellationToken cancellationToken)
    {
        if (snapshotPath is null || !File.Exists(snapshotPath))
        {
            await _client.SendMessage(_chatId, text, cancellationToken: cancellationToken);
            return;
        }

        await using var photo = File.OpenRead(snapshotPath);
        await _client.SendPhoto(_chatId, InputFile.FromStream(photo, Path.GetFileName(snapshotPath)), caption: text, cancellationToken: cancellationToken);
    }

    private void ResetNoBowl()
    {
        _consecutiveNoBowl = 0;
        _noBowlAlertSent = false;
    }
}
