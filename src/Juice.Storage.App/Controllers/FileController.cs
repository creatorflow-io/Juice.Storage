using Juice.Storage.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Juice.Storage.App.Controllers
{
    public class FileController : Controller
    {
        [HttpGet]
        public IActionResult IndexAsync(Guid id,
            [FromServices] IStorageResolver storageResolver
            )
        {
            if(storageResolver.Storage == null)
            {
                return NotFound("Storage not be resolved");
            }
            return Ok(storageResolver.Identity);
        }
    }
}
