using System.Collections.Generic;
using System.Threading.Tasks;

namespace RansomGuard.Service.Engine
{
    public interface IQuarantineService
    {
        Task QuarantineFile(string filePath);
        Task RestoreQuarantinedFile(string quarantinePath);
        Task DeleteQuarantinedFile(string quarantinePath);
        Task ClearOldFiles();
        IEnumerable<string> GetQuarantinedFiles();
        double GetStorageUsageMb();
    }
}
