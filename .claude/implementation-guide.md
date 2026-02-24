# Juice.Storage — Digital Asset Management Implementation Guide

> Step-by-step guide for building a production DAM on top of Juice.Storage.

---

## 1. Define Your File Model

Implement `IFile` with your domain-specific fields:

```csharp
public class AssetFile : IFile
{
    public Guid     Id                    { get; init; } = Guid.NewGuid();
    public string   Name                  { get; set; } = default!;  // storage path
    public string?  OriginalName          { get; init; }
    public long     PackageSize           { get; set; }
    public string?  ContentType           { get; set; }
    public string?  CorrelationId         { get; set; }  // e.g. project/folder ID
    public JObject? Metadata              { get; set; }  // tags, alt-text, dimensions…
    public DateTimeOffset? LastModified   { get; set; }
    public bool     DateModifiedPreserved { get; set; }
    public DateTimeOffset CreatedDate     { get; init; } = DateTimeOffset.Now;

    // --- Your extra fields ---
    public string? OwnerId    { get; set; }
    public string? FolderPath { get; set; }
    public bool    IsPublic   { get; set; }
}
```

---

## 2. Implement IFileRepository&lt;AssetFile&gt;

```csharp
public class AssetFileRepository : IFileRepository<AssetFile>
{
    private readonly AppDbContext _db;
    public AssetFileRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(AssetFile item, string storageIdentity, CancellationToken token)
    {
        item.StorageIdentity = storageIdentity;  // store identity if you need it later
        _db.Assets.Add(item);
        await _db.SaveChangesAsync(token);
    }

    public Task<AssetFile?> GetAsync(string storageIdentity, Guid id, CancellationToken token)
        => _db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.StorageIdentity == storageIdentity, token);
}
```

---

## 3. Implement IStorageRepository

Return storage endpoints for each identity from your configuration or database:

```csharp
public class DbStorageRepository : IStorageRepository
{
    private readonly AppDbContext _db;
    public DbStorageRepository(AppDbContext db) => _db = db;

    public async Task<bool> ExistsAsync(string identity)
        => await _db.StorageBuckets.AnyAsync(b => b.Identity == identity);

    public async Task<IEnumerable<StorageEndpoint>> GetEndpointsAsync(string identity)
    {
        var bucket = await _db.StorageBuckets
            .Include(b => b.Endpoints)
            .FirstOrDefaultAsync(b => b.Identity == identity);
        return bucket?.Endpoints.Select(e => new StorageEndpoint(
            e.Uri, e.Protocol, e.Identity, e.Password, e.BasePath)) ?? [];
    }
}
```

Or for simple config-based setups use the built-in:
```csharp
services.AddInMemoryStorageRepository(configuration);
// reads from appsettings: Juice:Storage:Storages[*].Endpoints
```

---

## 4. Optional: Custom File Name Generator

Control where files land on storage based on metadata:

```csharp
public class YearMonthFileNameGenerator : IFileNameGenerator<AssetFile>
{
    public Task<string> GenerateAsync(AssetFile file, CancellationToken token)
    {
        var date = file.LastModified ?? DateTimeOffset.UtcNow;
        var ext  = Path.GetExtension(file.OriginalName ?? file.Name);
        var name = $"{date:yyyy/MM}/{file.Id}{ext}";
        return Task.FromResult(name);
    }
}

// Register:
services.AddScoped<IFileNameGenerator<AssetFile>, YearMonthFileNameGenerator>();
```

---

## 5. Optional: Quota Checker

```csharp
public class UserQuotaChecker : IQuotaChecker<AssetFile>
{
    private readonly IQuotaService _quotaService;
    public UserQuotaChecker(IQuotaService quotaService) => _quotaService = quotaService;

    public async Task<bool> IsQuotaLimitExceededAsync(
        ClaimsPrincipal? user, AssetFile file, long? resumingPosition = default)
    {
        var userId = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return true;  // block anonymous

        var used  = await _quotaService.GetUsedBytesAsync(userId);
        var limit = await _quotaService.GetLimitBytesAsync(userId);
        var needed = file.PackageSize - (resumingPosition ?? 0);
        return (used + needed) > limit;
    }
}

// Register:
services.AddScoped<IQuotaChecker<AssetFile>, UserQuotaChecker>();
```

---

## 6. React to Upload Events

```csharp
// Auto-registered by MediatR scan
public class AssetIndexHandler : INotificationHandler<FileUploadedEvent>
{
    private readonly ISearchIndex _index;
    public AssetIndexHandler(ISearchIndex index) => _index = index;

    public async Task Handle(FileUploadedEvent e, CancellationToken ct)
    {
        await _index.IndexAsync(new AssetDocument {
            Id          = e.Id,
            Name        = e.Name,
            ContentType = e.ContentType,
            UploadedBy  = e.UserName,
            Tags        = e.Metadata?["tags"]?.ToObject<string[]>(),
        }, ct);
    }
}
```

---

## 7. Wire Up Everything

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
var cfg     = builder.Configuration;
var svc     = builder.Services;

// Core storage resolution
svc.AddStorage();
svc.AddLocalStorageProviders();           // add cloud providers here too

// Repositories
svc.AddScoped<IStorageRepository,         DbStorageRepository>();
svc.AddScoped<IFileRepository<AssetFile>, AssetFileRepository>();

// Upload / download
svc.AddDefaultUploadManager<AssetFile>(cfg, opt => {
    opt.SectionSize          = 5_242_880;  // 5 MB chunks
    opt.DeleteOnAbort        = true;
    opt.PreserveDateModified = true;
});
svc.AddDefaultDownloadManager<AssetFile>(cfg);

// Optional extensions
svc.AddScoped<IFileNameGenerator<AssetFile>, YearMonthFileNameGenerator>();
svc.AddScoped<IQuotaChecker<AssetFile>,      UserQuotaChecker>();

// MediatR (for events)
svc.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

// Background cleanup
svc.AddStorageMaintainServices<AssetFile>(cfg, identities: ["/assets"], opt => {
    opt.CleanupAfter = TimeSpan.FromHours(12);
});

// Auth
svc.AddAuthorization(opt => {
    opt.AddPolicy(StoragePolicies.CreateFile,
        p => p.RequireAuthenticatedUser());
    opt.AddPolicy(StoragePolicies.DownloadFile,
        p => p.RequireAssertion(_ => true));  // public downloads
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseStorage(opt => { opt.Endpoints = ["/assets"]; });
app.Run();
```

---

## 8. Client Upload (JavaScript example)

```javascript
// Step 1: init
const init = await fetch('/assets/init', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    name:          'photos/vacation.jpg',
    fileSize:      file.size,
    contentType:   file.type,
    originalName:  file.name,
    lastModified:  new Date(file.lastModified).toISOString(),
    fileExistsBehavior: 2,  // AscendedCopyNumber
    metadata: { tags: ['vacation', '2026'] }
  })
});
const { uploadId, name, sectionSize, offset } = await init.json();

// Step 2: upload in chunks
let pos = offset;
while (pos < file.size) {
  const chunk = file.slice(pos, pos + sectionSize);
  const res = await fetch('/assets/upload', {
    method:  'POST',
    headers: { 'x-uploadid': uploadId, 'x-offset': pos },
    body:    chunk
  });
  pos = parseInt(res.headers.get('x-offset'));
  if (res.headers.get('x-completed') === 'true') break;
}
```

---

## 9. Configuration Reference

```json
{
  "Juice": {
    "Storage": {
      "Storages": [
        {
          "WebBasePath": "/assets",
          "Endpoints": [
            {
              "Protocol": "LocalDisk",
              "Uri": "/data/assets"
            },
            {
              "Protocol": "Ftp",
              "Uri": "ftp://backup-server/assets",
              "Identity": "ftpuser",
              "Password": "secret"
            }
          ]
        }
      ]
    }
  },
  "Upload": {
    "SectionSize": 5242880,
    "DeleteOnAbort": true,
    "PreserveDateModified": true
  },
  "Download": {
    "IsSupportDownloadByPath": false
  }
}
```

---

## Patterns & Gotchas

### Multiple endpoints = automatic failover
If you configure both a primary and backup endpoint for the same identity, `StorageProxy` will try them in priority order. Set higher `priority` values for preferred providers.

### Resume requires same UploadId
Pass the original `UploadId` back in `InitialFileInfo.UploadId` with `FileExistsBehavior.Resume` to resume from the stored offset.

### `Name` vs `OriginalName`
- `OriginalName` = what the user uploaded (`vacation photo.jpg`)
- `Name` = actual storage path (`2026/02/550e8400-e29b-41d4-a716-446655440000.jpg`)
- Use `IFileNameGenerator` to control `Name`

### IFile.Metadata is a JObject
Store arbitrary key/value pairs:
```csharp
file.Metadata = JObject.FromObject(new {
    tags       = new[] { "photo", "vacation" },
    width      = 3840,
    height     = 2160,
    altText    = "Beach sunset"
});
```

### StorageProxy priority is descending
Higher number = tried first. Default priority for providers is set in `Configure(endpoint, priority?)`.

### MediatR events are fire-and-forget within request
They run synchronously in the same request context. For heavy post-processing, dispatch to a background queue inside the handler.
