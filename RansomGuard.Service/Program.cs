using RansomGuard.Service;

const string appName = "Global\\RansomGuard_SentinelService_Mutex";
using var mutex = new System.Threading.Mutex(true, appName, out bool createdNew);

if (!createdNew)
{
    // Another instance is already running
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "RansomGuardSentinel";
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
