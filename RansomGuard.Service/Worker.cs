using RansomGuard.Service.Engine;
using RansomGuard.Service.Communication;
using RansomGuard.Core.Interfaces;
using RansomGuard.Service.Services;
using RansomGuard.Core.Services;
using RansomGuard.Core.IPC;
using RansomGuard.Core.Helpers;

namespace RansomGuard.Service;

public class Worker : BackgroundService
{
    private SentinelEngine? _engine;
    private TelemetryService? _telemetryService;
    private HistoryManager? _historyManager;
    private HoneyPotService? _honeyPot;
    private VssShieldService? _vssShield;
    private ActiveResponseService? _activeResponse;
    private NamedPipeServer? _pipeServer;
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
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
            // Ensure the ProgramData directory exists and is writable by the UI
            string baseDir = PathConfiguration.GetConfigDirectory();
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
            
            try
            {
                // Grant Everyone full control to this specific folder so UI and Service can sync
                var di = new DirectoryInfo(baseDir);
                var ds = di.GetAccessControl();
                ds.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                    System.Security.AccessControl.PropagationFlags.None,
                    System.Security.AccessControl.AccessControlType.Allow));
                di.SetAccessControl(ds);
                _logger.LogInformation("Permissions initialized for {path}", baseDir);
            }
            catch (Exception ex) { _logger.LogWarning("Could not set folder permissions: {msg}", ex.Message); }

            LogToBootFile("Initializing services...");
            // 1. Initialize decoupled services
            var historyStore = new HistoryStore();
            _historyManager = new HistoryManager(historyStore);
            _telemetryService = new TelemetryService();

            // 2. Initialize analyzer/identity logic
            var entropyAnalyzer = new EntropyAnalysisService();
            var authenticodeVerifier = new AuthenticodeVerifier();
            var processClassifier = new ProcessIdentityService(authenticodeVerifier);
            var quarantine = new QuarantineService(historyStore);

            // 3. Initialize core engine with injected services
            _engine = new SentinelEngine(
                _telemetryService, 
                _historyManager, 
                entropyAnalyzer, 
                processClassifier, 
                quarantine);

            _activeResponse = new ActiveResponseService();
            _honeyPot = new HoneyPotService(_engine);
            _vssShield = new VssShieldService(_engine);
            
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
            _pipeServer.Start();

            _engine.IsHoneyPotActive = true;
            _engine.IsVssShieldActive = true;

            LogToBootFile("All services started successfully.");
            _logger.LogInformation("All proactive shields engaged. Telemetry and Security engines are online.");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
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
                _pipeServer?.Stop();
                _honeyPot?.Stop();
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
                _honeyPot?.Dispose();
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
}
