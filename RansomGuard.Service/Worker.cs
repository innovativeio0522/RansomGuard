using RansomGuard.Service.Engine;
using RansomGuard.Service.Communication;
using RansomGuard.Core.Interfaces;
using RansomGuard.Service.Services;
using RansomGuard.Core.Services;

namespace RansomGuard.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly SentinelEngine _engine;
    private readonly TelemetryService _telemetryService;
    private readonly HistoryManager _historyManager;
    private readonly HoneyPotService _honeyPot;
    private readonly VssShieldService _vssShield;
    private readonly ActiveResponseService _activeResponse;
    private readonly NamedPipeServer _pipeServer;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        
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
            
            if (threat.Severity == RansomGuard.Core.Models.ThreatSeverity.Critical)
            {
                HandleCriticalThreat(threat);
            }
        };
    }

    private void HandleCriticalThreat(RansomGuard.Core.Models.Threat threat)
    {
        _logger.LogCritical("!!! EXTREME THREAT DETECTED: {name} !!!", threat.Name);
        _activeResponse.LockdownNetwork();
        _engine.IsPanicModeActive = true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RansomGuard Sentinel Service starting...");

        try
        {
            // Start all services
            await _historyManager.LoadFromStoreAsync().ConfigureAwait(false);
            _telemetryService.Start();
            _honeyPot.Start();
            _vssShield.Start();
            _pipeServer.Start();

            _engine.IsHoneyPotActive = true;
            _engine.IsVssShieldActive = true;

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
            _logger.LogError(ex, "Unexpected error in service execution.");
        }
        finally
        {
            _logger.LogInformation("RansomGuard Sentinel Service stopping...");
            
            // Stop all services
            try
            {
                _telemetryService.Stop();
                _vssShield.Stop();
                _pipeServer.Stop();
                _honeyPot.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping services.");
            }

            // Dispose all resources
            try
            {
                _engine.Dispose();
                _telemetryService.Dispose();
                _historyManager.Dispose();
                _honeyPot.Dispose();
                (_vssShield as IDisposable)?.Dispose();
                (_activeResponse as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing resources.");
            }

            _logger.LogInformation("RansomGuard Sentinel Service stopped.");
        }
    }
}
