using System;
using System.Linq;
using System.Threading.Tasks;
using Juice.Storage.InMemory;
using Xunit;

namespace Juice.Storage.Tests
{
    public class InMemoryUploadRepositoryTest
    {
        [Fact(DisplayName = "Concurrent add/complete does not lose uploads")]
        public async Task Concurrent_uploads_should_not_be_lost_Async()
        {
            var repository = new InMemoryUploadRepository<UploadFileInfo>();
            var kept = Enumerable.Range(0, 2000).Select(_ => new UploadFileInfo { Id = Guid.NewGuid() }).ToArray();
            var completed = Enumerable.Range(0, 2000).Select(_ => new UploadFileInfo { Id = Guid.NewGuid() }).ToArray();

            await Task.WhenAll(
                Parallel.ForEachAsync(kept, async (file, token) => await repository.AddAsync("/storage", file)),
                Parallel.ForEachAsync(completed, async (file, token) =>
                {
                    await repository.AddAsync("/storage", file);
                    await repository.CompleteAsync("/storage", file.Id, token);
                }));

            foreach (var file in kept)
            {
                Assert.True(await repository.ExistsAsync("/storage", file.Id, default));
                Assert.Same(file, await repository.GetAsync("/storage", file.Id, default));
            }
            foreach (var file in completed)
            {
                Assert.False(await repository.ExistsAsync("/storage", file.Id, default));
            }
            Assert.Empty(await repository.FindAllForCleanupAsync("/other", DateTimeOffset.Now, default));
        }
    }
}
