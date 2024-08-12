using System.Security.Claims;

namespace Juice.Storage
{
    public interface IQuotaChecker<T>
         where T : class, IFile
    {
        Task<bool> IsQuotaLimitExceededAsync(ClaimsPrincipal? user, T file, long? resumingPosition = default);
    }
}
