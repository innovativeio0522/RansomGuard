using RansomGuard.Service.Engine;
using RansomGuard.Service.Communication;
using RansomGuard.Core.Interfaces;

namespace RansomGuard.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly SentinelEngine _engine;
    private readonly HoneyPotService _honeyPot;
    private readonly VssShieldService _vssShield;
    private readonly ActiveResponseService _activeResponse;
    private readonly NamedPipeServer _pipeServer;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        
        // Initialize core engines
        var entropyAnalyzer = new EntropyAnalysisService();
        var authenticodeVerifier = new AuthenticodeVerifier();
        var processClassifier = new ProcessIdentityService(authenticodeVerifier);
        _engine = new SentinelEngine(entropyAnalyzer, processClassifier);
        _activeResponse = new ActiveResponseService();
        _honeyPot = new HoneyPotService(_engine);
        _vssShield = new VssShieldService(_engine);
        
        _pipeServer = new NamedPipeServer(_engine);

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
        
        // FUTURE: Check if Auto-Shutdown is enabled in config
        // _activeResponse.PerformEmergencyShutdown();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RansomGuard Sentinel Service starting...");

        try
        {
            _honeyPot.Start();
            _vssShield.Start();
            _pipeServer.Start();

            _engine.IsHoneyPotActive = true;
            _engine.IsVssShieldActive = true;

            _logger.LogInformation("All proactive shields engaged.");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when service is stopping
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
                (_engine as IDisposable)?.Dispose();
                (_honeyPot as IDisposable)?.Dispose();
                (_vssShield as IDisposable)?.Dispose();
                (_activeResponse as IDisposable)?.Dispose();
                // Note: _pipeServer doesn't implement IDisposable currently
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing resources.");
            }

            _logger.LogInformation("RansomGuard Sentinel Service stopped.");
        }
    }
}
