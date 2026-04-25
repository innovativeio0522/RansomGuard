using RansomGuard.Service;
using RansomGuard.Core.Helpers;

try 
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "WinMaintenance";
    });
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
