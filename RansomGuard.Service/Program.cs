using RansomGuard.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "RansomGuardSentinel";
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
