using Microsoft.Extensions.Options;
using ObjectDetectionApi.Models;
using ObjectDetectionApi.Services;
using ObjectDetectionApi.Workers;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/log-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services
    .Configure<Onnx>(builder.Configuration.GetSection("Onnx"))
    .AddSingleton(s => s.GetRequiredService<IOptions<Onnx>>().Value)
    .Configure<TelegramBot>(builder.Configuration.GetSection("TelegramBot"))
    .AddSingleton(s => s.GetRequiredService<IOptions<TelegramBot>>().Value)
    .Configure<Video>(builder.Configuration.GetSection("Video"))
    .AddSingleton(s => s.GetRequiredService<IOptions<Video>>().Value)
    .Configure<WorkerSettings>(builder.Configuration.GetSection("Worker"))
    .AddSingleton(s => s.GetRequiredService<IOptions<WorkerSettings>>().Value);

builder.Services.AddSingleton<IObjectDetectionService, ObjectDetectionService>();
builder.Services.AddSingleton<IDetectionHandler, FoodAlertHandler>();
builder.Services.AddHostedService<DetectionWorker>();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();
