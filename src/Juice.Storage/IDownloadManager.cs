using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Juice.Storage
{
    public interface IDownloadManager
    {
        Task<IOperationResult<(Stream Stream, string FileName)>> GetStreamAsync(Guid id, CancellationToken token);
    }
}
