using System.Diagnostics;

namespace RansomGuard.Service.Engine
{
    public interface IProcessIdentityClassifier
    {
        (bool IsTrusted, string Status) DetermineIdentity(Process p);
    }
}
