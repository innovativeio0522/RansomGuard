using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RansomGuard.Core.Services;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Constants;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Identifies processes using files and determines their trust level.
    /// Handles Restart Manager calls and process inference from context.
    /// </summary>
    public class ProcessAttributionService
    {
        private readonly IProcessIdentityClassifier _processClassifier;
        private readonly SemaphoreSlim _processIdSemaphore = new(1, 1);

        public ProcessAttributionService(IProcessIdentityClassifier processClassifier)
        {
            _processClassifier = processClassifier ?? throw new ArgumentNullException(nameof(processClassifier));
        }

        /// <summary>
        /// Identifies the process responsible for a file operation.
        /// Returns process name, process ID, and whether it's trusted.
        /// </summary>
        public async Task<ProcessAttributionResult> IdentifyProcessAsync(
            string path, 
            string action, 
            int providedProcessId, 
            string providedProcessName,
            bool needsIdentification)
        {
            var result = new ProcessAttributionResult
            {
                ProcessName = providedProcessName,
                ProcessId = providedProcessId
            };

            // If we already have a process ID from ETW, check its identity
            if (providedProcessId != 0)
            {
                try
                {
                    using var p = Process.GetProcessById(providedProcessId);
                    result.IsTrusted = _processClassifier.DetermineIdentity(p).IsTrusted;
                    result.ProcessName = p.ProcessName;
                    return result;
                }
                catch
                {
                    // Process may have exited
                }
            }

            // If identification is not needed (file not suspicious), return early
            if (!needsIdentification)
            {
                return result;
            }

            // Perform expensive process identification
            try
            {
                await _processIdSemaphore.WaitAsync().ConfigureAwait(false);

                try
                {
                    var processes = new List<Process>();

                    // Use Restart Manager with timeout to find owner
                    try
                    {
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        var processTask = _processClassifier.GetProcessesUsingFileAsync(path);

                        if (await Task.WhenAny(processTask, Task.Delay(-1, cts.Token)) == processTask)
                        {
                            processes = await processTask;
                        }
                        else
                        {
                            FileLogger.LogWarning(AppIdentifiers.ProcessAttributionLogFile, $"[ProcessAttribution] Timeout for: {path}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        FileLogger.LogWarning(AppIdentifiers.ProcessAttributionLogFile, $"[ProcessAttribution] Cancelled for: {path}");
                    }

                    if (processes != null && processes.Count > 0)
                    {
                        var primary = processes[0];
                        result.ProcessName = primary.ProcessName;
                        try { result.ProcessId = primary.Id; } catch { /* process may have exited */ }
                        result.IsTrusted = _processClassifier.DetermineIdentity(primary).IsTrusted;
                    }
                    else
                    {
                        // Use heuristics based on file location and type
                        result.ProcessName = InferProcessFromContext(path, action);
                        result.IsTrusted = false; // Inferred processes are not trusted by default
                    }
                }
                finally
                {
                    _processIdSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.ProcessAttributionLogFile, $"[ProcessAttribution] Error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Infers the likely process responsible for a file operation based on context clues.
        /// </summary>
        private string InferProcessFromContext(string path, string action)
        {
            try
            {
                string pathLower = path.ToLowerInvariant();
                string fileName = Path.GetFileName(pathLower);
                string extension = Path.GetExtension(pathLower);

                // Browser downloads
                if (pathLower.Contains(@"\downloads\"))
                {
                    if (fileName.Contains("reddit") || fileName.Contains("www."))
                        return "Browser (Download)";
                    return "explorer";
                }

                // Screenshots
                if (pathLower.Contains(@"\pictures\screenshots\") || fileName.Contains("screenshot"))
                    return "SnippingTool";

                // Desktop files
                if (pathLower.Contains(@"\desktop\"))
                {
                    if (extension == ".txt") return "notepad";
                    if (extension == ".bmp" || extension == ".png") return "mspaint";
                    return "explorer";
                }

                // Documents folder
                if (pathLower.Contains(@"\documents\"))
                {
                    if (extension == ".txt") return "notepad";
                    if (extension == ".docx" || extension == ".doc") return "WINWORD";
                    if (extension == ".xlsx" || extension == ".xls") return "EXCEL";
                    if (extension == ".pdf") return "AcroRd32";
                    return "explorer";
                }

                // Temp files
                if (pathLower.Contains(@"\temp\") || pathLower.Contains(@"\tmp\"))
                    return "System Process";

                // AppData operations
                if (pathLower.Contains(@"\appdata\"))
                    return "Application";

                // Default fallback
                return "explorer";
            }
            catch
            {
                return "Unknown";
            }
        }

        public void Dispose()
        {
            _processIdSemaphore?.Dispose();
        }
    }

    /// <summary>
    /// Results of process attribution for a file operation.
    /// </summary>
    public class ProcessAttributionResult
    {
        public string ProcessName { get; set; } = "Unknown";
        public int ProcessId { get; set; }
        public bool IsTrusted { get; set; }
    }
}
