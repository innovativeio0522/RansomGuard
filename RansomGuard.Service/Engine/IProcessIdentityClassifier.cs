using System.Diagnostics;

namespace RansomGuard.Service.Engine
{
    public interface IProcessIdentityClassifier
    {
        (bool IsTrusted, string Status) DetermineIdentity(Process p);
        System.Collections.Generic.List<Process> GetProcessesUsingFile(string path);
        System.Threading.Tasks.Task<System.Collections.Generic.List<Process>> GetProcessesUsingFileAsync(string path);
    }
}
