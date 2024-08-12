using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Juice.Storage.Tests.Services
{
    internal class QuotaCheckerService<T> : IQuotaChecker<T>
        where T : class, IFile
    {
        public Task<bool> IsQuotaLimitExceededAsync(ClaimsPrincipal? user, T file, long? resumingPosition = null)
        {
            return Task.FromResult(true);
        }
    }
}
