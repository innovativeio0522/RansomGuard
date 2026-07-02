using RansomGuard.Service;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Interfaces;
using RansomGuard.Service.Engine;
using RansomGuard.Service.Services;

try 
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "RGService";
    });

    // Register services
    builder.Services.AddSingleton<ITelemetryService, TelemetryService>();
    builder.Services.AddSingleton<IHistoryStore, HistoryStore>();
    builder.Services.AddSingleton<HistoryManager>();
    builder.Services.AddSingleton<EntropyAnalysisService>();
    builder.Services.AddSingleton<IAuthenticodeVerifier, AuthenticodeVerifier>();
    builder.Services.AddSingleton<ProcessIdentityService>();
    builder.Services.AddSingleton<QuarantineService>();
    builder.Services.AddSingleton<LanCircuitBreaker>();
    builder.Services.AddSingleton<EtwMonitorService>();
    // Add more as needed

    builder.Services.AddHostedService<Worker>();
    builder.Services.AddHostedService<RansomGuard.Service.Engine.WatchdogPersistenceService>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    string logPath = Path.Combine(PathConfiguration.LogPath, "fatal_startup.log");
    File.AppendAllText(logPath, $"{DateTime.Now}: FATAL STARTUP ERROR: {ex.Message}\n{ex.StackTrace}\n");
}
