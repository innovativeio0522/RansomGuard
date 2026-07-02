using RansomGuard.Service.Engine;
using RansomGuard.Service.Communication;
using RansomGuard.Core.Interfaces;
using RansomGuard.Service.Services;
using RansomGuard.Core.Services;
using RansomGuard.Core.IPC;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Constants;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RansomGuard.Service;

public class Worker : BackgroundService
{
    private SentinelEngine? _engine;
    private TelemetryService? _telemetryService;
    private HistoryManager? _historyManager;
    private HoneyPotService? _honeyPot;
    private VssShieldService? _vssShield;
    private EtwMonitorService? _etwMonitor;
    private ActiveResponseService? _activeResponse;
    private LanCircuitBreaker? _lanCircuitBreaker;
    private NamedPipeServer? _pipeServer;
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;

    public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    private void HandleCriticalThreat(RansomGuard.Core.Models.Threat threat)
    {
        _logger.LogCritical("!!! EXTREME THREAT DETECTED: {name} !!!", threat.Name);
        _activeResponse?.LockdownNetwork();
        if (_engine != null) _engine.IsPanicModeActive = true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogToBootFile("Worker ExecuteAsync started.");
        _logger.LogInformation("RansomGuard Sentinel Service starting...");

        try
        {
            LogToBootFile("Initializing storage and permissions...");
            // Ensure the ProgramData directory exists and is writable by authenticated UI users.
            string baseDir = PathConfiguration.GetConfigDirectory();
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
            
            try
            {
                ApplyProgramDataAcl(baseDir);
                _logger.LogInformation("Permissions initialized for {path}", baseDir);
            }
            catch (Exception ex) { _logger.LogWarning("Could not set folder permissions: {msg}", ex.Message); }

            LogToBootFile("Initializing services...");
            // Resolve services from DI
            _historyManager = _serviceProvider.GetRequiredService<HistoryManager>();
            _telemetryService = (TelemetryService)_serviceProvider.GetRequiredService<ITelemetryService>();
            var entropyAnalyzer = _serviceProvider.GetRequiredService<EntropyAnalysisService>();
            var processClassifier = _serviceProvider.GetRequiredService<ProcessIdentityService>();
            var quarantine = _serviceProvider.GetRequiredService<QuarantineService>();
            _lanCircuitBreaker = _serviceProvider.GetRequiredService<LanCircuitBreaker>();
            _etwMonitor = _serviceProvider.GetRequiredService<EtwMonitorService>();

            // 3c. Initialize core engine with resolved services
            _engine = new SentinelEngine(
                _telemetryService, 
                _historyManager, 
                entropyAnalyzer, 
                processClassifier, 
                quarantine,
                _lanCircuitBreaker,
                _etwMonitor);

            // Wire LAN Circuit Breaker events
            _lanCircuitBreaker.CircuitBreakReceived += (threatInfo) =>
            {
                _logger.LogCritical("[LAN] CIRCUIT BREAK received from peer: {info}", threatInfo);
                FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[LAN] Executing local critical response due to remote circuit break: {threatInfo}");
                
                // Trigger critical response by reporting a critical threat
                _engine.ReportThreat(
                    "NETWORK_ALERT", 
                    "LAN Circuit Break Received", 
                    $"Critical threat detected on network peer: {threatInfo}",
                    "LAN Circuit Breaker",
                    0,
                    RansomGuard.Core.Models.ThreatSeverity.Critical,
                    "Network Alert");
            };

            _lanCircuitBreaker.PeerListChanged += (update) =>
            {
                _pipeServer?.Broadcast(MessageType.LanPeerUpdate, update, true);
            };

            _activeResponse = new ActiveResponseService();
            _honeyPot = new HoneyPotService(_engine);
            _vssShield = new VssShieldService(_engine, _etwMonitor);
            
            // 4. Initialize communication layer
            _pipeServer = new NamedPipeServer(_engine, _telemetryService);

            // Wire up automated response
            _engine.ThreatDetected += (threat) => 
            {
                _logger.LogWarning("THREAT DETECTED: {name} at {path}", threat.Name, threat.Path);
                if (threat.Severity == RansomGuard.Core.Models.ThreatSeverity.Critical) HandleCriticalThreat(threat);
            };

            LogToBootFile("Starting all services...");
            // Start all services
            await _historyManager.LoadFromStoreAsync().ConfigureAwait(false);
            _telemetryService.Start();
            _honeyPot.Start();
            _vssShield.Start();
            _lanCircuitBreaker.Start();
            _pipeServer.Start();

            _engine.IsHoneyPotActive = true;
            _engine.IsVssShieldActive = true;

            LogToBootFile("All services started successfully.");
            _logger.LogInformation("All proactive shields engaged. Telemetry and Security engines are online.");

            int perfLogTick = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken).ConfigureAwait(false);

                // Log performance snapshot every 60 seconds
                if (++perfLogTick >= 60)
                {
                    perfLogTick = 0;
                    var snap = RansomGuard.Core.Services.PerformanceMonitor.Instance.GetSnapshot();
                    _logger.LogInformation("[PERF] {snapshot}", snap.ToString());
                    FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[PERF] {snap}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Service stop requested.");
        }
        catch (Exception ex)
        {
            LogToBootFile($"FATAL EXCEPTION in ExecuteAsync: {ex.Message}\n{ex.StackTrace}");
            _logger.LogCritical(ex, "FATAL ERROR in service execution.");
        }
        finally
        {
            _logger.LogInformation("RansomGuard Sentinel Service stopping...");
            
            // Stop all services
            try
            {
                _telemetryService?.Stop();
                _vssShield?.Stop();
                _lanCircuitBreaker?.Stop();
                _pipeServer?.Stop();
                _honeyPot?.Stop();
                _etwMonitor?.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping services.");
            }

            // Dispose all resources
            try
            {
                _engine?.Dispose();
                _telemetryService?.Dispose();
                _historyManager?.Dispose();
                _lanCircuitBreaker?.Dispose();
                _honeyPot?.Dispose();
                _etwMonitor?.Dispose();
                (_vssShield as IDisposable)?.Dispose();
                (_activeResponse as IDisposable)?.Dispose();
                (_pipeServer as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing resources.");
            }

            _logger.LogInformation("RansomGuard Sentinel Service stopped.");
        }
    }

    private void LogToBootFile(string message)
    {
        try
        {
            string logPath = Path.Combine(PathConfiguration.LogPath, "boot.log");
            string dir = Path.GetDirectoryName(logPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(logPath, $"{DateTime.Now}: {message}\n");
        }
        catch { }
    }

    internal static void ApplyProgramDataAcl(string baseDir)
    {
        var di = new DirectoryInfo(baseDir);
        var ds = di.GetAccessControl();

        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        var localSystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        ds.PurgeAccessRules(everyoneSid);

        const InheritanceFlags inheritance =
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        ds.SetAccessRule(new FileSystemAccessRule(
            localSystemSid,
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        ds.SetAccessRule(new FileSystemAccessRule(
            administratorsSid,
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        ds.SetAccessRule(new FileSystemAccessRule(
            authenticatedUsersSid,
            FileSystemRights.Modify | FileSystemRights.Synchronize,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        di.SetAccessControl(ds);
    }
}
