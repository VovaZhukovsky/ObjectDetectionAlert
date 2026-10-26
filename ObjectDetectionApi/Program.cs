using System.Text.Json;
using System.Text.Json.Serialization;
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
    .AddSingleton(s => s.GetRequiredService<IOptions<WorkerSettings>>().Value)
    .Configure<FoodAlertSettings>(builder.Configuration.GetSection("FoodAlert"))
    .AddSingleton(s => s.GetRequiredService<IOptions<FoodAlertSettings>>().Value);

builder.Services.AddSingleton<ISnapshotService, FfmpegSnapshotService>();
builder.Services.AddSingleton<IFoodStatusService, FoodStatusService>();
builder.Services.AddSingleton<IDetectionHandler, FoodAlertHandler>();
builder.Services.AddHostedService<DetectionWorker>();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger UI at /swagger, only for local runs
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
