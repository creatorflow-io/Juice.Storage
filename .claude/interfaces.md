# Juice.Storage — Interface Signatures Reference

> Full method signatures for all public interfaces. Branch: release/9.0

---

## Juice.Storage.Abstractions

### IStorage
```csharp
// src/Juice.Storage.Abstractions/IStorage.cs
public interface IStorage
{
    Protocol[] Protocols { get; }
    Task<Stream> ReadAsync(string filePath, CancellationToken token);
    Task WriteAsync(string filePath, Stream stream, long offset, TransferOptions options, CancellationToken token);
    Task<string> CreateAsync(string filePath, CreateFileOptions options, CancellationToken token);
    Task<bool> ExistsAsync(string filePath, CancellationToken token);
    Task<long> FileSizeAsync(string filePath, CancellationToken token);
    Task DeleteAsync(string filePath, CancellationToken token);
    Task PreserveModifiedTimeAsync(string filePath, DateTimeOffset? modifiedTime, CancellationToken token);
}
```

### IStorageProvider
```csharp
// src/Juice.Storage.Abstractions/IStorageProvider.cs
public interface IStorageProvider : IStorage
{
    StorageEndpoint StorageEndpoint { get; }
    NetworkCredential Credential { get; }
    int Priority { get; }
    IStorageProvider WithCredential(NetworkCredential credential);
    IStorageProvider Configure(StorageEndpoint endpoint, int? priority);
}
```

### IStorageRepository
```csharp
// src/Juice.Storage.Abstractions/IStorageRepository.cs
public interface IStorageRepository
{
    Task<bool> ExistsAsync(string identity);
    Task<IEnumerable<StorageEndpoint>> GetEndpointsAsync(string identity);
}
```

### IStorageProviderFactory
```csharp
// src/Juice.Storage.Abstractions/IStorageProviderFactory.cs
public interface IStorageProviderFactory
{
    IStorageProvider[] CreateProviders(IEnumerable<StorageEndpoint> endpoints);
}
```

### IStorageResolver
```csharp
// src/Juice.Storage.Abstractions/IStorageResolver.cs
public interface IStorageResolver
{
    bool IsResolved { get; }
    string Identity { get; }
    IStorage? Storage { get; }
    IEnumerable<StorageEndpoint> Endpoints { get; }
    Task<bool> TryResolveAsync(string identity);
}
```

### IStorageResolveStrategy
```csharp
// src/Juice.Storage.Abstractions/IStorageResolveStrategy.cs
public interface IStorageResolveStrategy : IStorageResolver
{
    int Priority { get; }
}
```

---

## Juice.Storage — DTOs & Options

### StorageEndpoint
```csharp
public class StorageEndpoint
{
    public string? BasePath { get; }   // local mount point for network URIs
    public string Uri { get; }         // storage path or ftp://host/path
    public string? Identity { get; }   // username
    public string? Password { get; }   // password
    public Protocol Protocol { get; }  // enum: Smb=0, VirtualDirectory=1, LocalDisk=2, Ftp=3
}
```

### CreateFileOptions
```csharp
public class CreateFileOptions
{
    public FileExistsBehavior FileExistsBehavior { get; set; }
    // FileExistsBehavior: RaiseError=0, Replace=1, AscendedCopyNumber=2, Resume=3
}
```

### TransferOptions
```csharp
public class TransferOptions
{
    public int? BufferSize { get; set; }
}
```

### InitialFileInfo
```csharp
public class InitialFileInfo
{
    public Guid? UploadId { get; init; }             // set to resume existing upload
    public string Name { get; init; }                // relative storage path
    public long FileSize { get; init; }
    public string? ContentType { get; init; }
    public string? CorrelationId { get; init; }
    public JObject? Metadata { get; init; }
    public string? OriginalName { get; init; }       // user-visible filename
    public DateTimeOffset? LastModified { get; init; }
    public FileExistsBehavior FileExistsBehavior { get; init; }
}
```

### UploadConfiguration
```csharp
public class UploadConfiguration
{
    public Guid UploadId { get; init; }
    public string Name { get; init; }          // actual path on storage (may differ if versioned)
    public long SectionSize { get; private set; }  // recommended chunk size
    public bool Exists { get; init; }          // true when resuming
    public long PackageSize { get; init; }     // total file size
    public long Offset { get; init; }          // bytes already written (for resume)
}
```

### UploadOptions
```csharp
public class UploadOptions
{
    public long SectionSize { get; set; } = 10_485_760;  // 10 MB
    public bool DeleteOnAbort { get; set; }
    public bool PreserveDateModified { get; set; }
}
```

### DownloadOptions
```csharp
public class DownloadOptions
{
    public bool IsSupportDownloadByPath { get; set; }
}
```

### StorageMaintainOptions
```csharp
public class StorageMaintainOptions
{
    public TimeSpan Interval { get; set; }     = TimeSpan.FromMinutes(5);
    public TimeSpan CleanupAfter { get; set; } = TimeSpan.FromDays(1);
}
```

---

## Juice.Storage — Core Service Interfaces

### IFile
```csharp
public interface IFile
{
    Guid Id { get; init; }
    string Name { get; set; }                    // actual filename on storage
    string? OriginalName { get; init; }          // original upload filename
    long PackageSize { get; set; }               // total file size in bytes
    string? ContentType { get; set; }
    string? CorrelationId { get; set; }
    JObject? Metadata { get; set; }              // arbitrary custom metadata
    DateTimeOffset? LastModified { get; set; }
    bool DateModifiedPreserved { get; set; }
    DateTimeOffset CreatedDate { get; init; }
}
```

### IFileRepository&lt;T&gt;
```csharp
// Must implement for production
public interface IFileRepository<T> where T : IFile
{
    Task AddAsync(T item, string storageIdentity, CancellationToken token);
    Task<T?> GetAsync(string storageIdentity, Guid id, CancellationToken token);
}
```

### IUploadRepository&lt;T&gt;
```csharp
public interface IUploadRepository<T> where T : IFile
{
    Task<bool> ExistsAsync(string storageIdentity, Guid uploadId, CancellationToken token);
    Task<T>    GetAsync(string storageIdentity, Guid uploadId, CancellationToken token);
    Task       RemoveAsync(string storageIdentity, Guid uploadId, CancellationToken token);
    Task       AddAsync(string storageIdentity, T item);
    Task       AbortAsync(string storageIdentity, Guid uploadId, bool fileDeleted);
    Task       CompleteAsync(string storageIdentity, Guid uploadId, CancellationToken token);
    Task<IEnumerable<T>> FindAllForCleanupAsync(string storageIdentity, DateTimeOffset beforeDate, CancellationToken token);
}
```

### IUploadManager
```csharp
public interface IUploadManager
{
    IStorage Storage { get; }
    Task<bool> ExistsAsync(string filePath, CancellationToken token);
    Task<UploadConfiguration> InitAsync(InitialFileInfo fileInfo, CancellationToken token);
    Task<(bool Completed, bool DateModifiedPreserved, long Size)> UploadAsync(
        Guid uploadId, Stream stream, long offset, CancellationToken token);
    Task<bool> CompleteAsync(Guid uploadId, CancellationToken token);
    Task FailureAsync(Guid uploadId, CancellationToken token);
    Task TimedoutAsync(Guid uploadId, CancellationToken token);
}
```

### IDownloadManager
```csharp
public interface IDownloadManager
{
    Task<IOperationResult<(Stream Stream, string FileName)>> GetStreamAsync(Guid id, CancellationToken token);
    Task<IOperationResult<Stream>> GetStreamAsync(string filePath, CancellationToken token);
}
```

### IFileNameGenerator&lt;T&gt;
```csharp
// Optional: generate custom storage paths
public interface IFileNameGenerator<T> where T : IFile
{
    Task<string> GenerateAsync(T file, CancellationToken token);
}
```

### IQuotaChecker&lt;T&gt;
```csharp
// Optional: enforce storage quota
public interface IQuotaChecker<T> where T : IFile
{
    Task<bool> IsQuotaLimitExceededAsync(
        ClaimsPrincipal? user, T file, long? resumingPosition = default);
}
```

---

## StorageMiddlewareOptions

```csharp
public class StorageMiddlewareOptions
{
    public string[] Endpoints { get; set; }  // e.g. ["/storage", "/assets"]
    public bool RewritePath { get; set; }
}
```

---

## Authorization Constants

```csharp
// StoragePolicies
public const string CreateFile   = "Storage_CreateFile";
public const string DownloadFile = "Storage_DownloadFile";

// StorageOperations (AuthorizationRequirement)
StorageOperations.Write
StorageOperations.Read
StorageOperations.Delete
StorageOperations.RenameFile
```

---

## Events (MediatR INotification)

```csharp
// All events in src/Juice.Storage/Events/
public class FileUploadStartedEvent : INotification
{
    public Guid    Id            { get; }
    public string  Name          { get; }
    public string? ContentType   { get; }
    public long    Length        { get; }
    public string? CorrelationId { get; }
    public JObject? Metadata     { get; }
    public string?  UserName     { get; }
}

public class FileUploadedEvent      : INotification { /* same properties */ }
public class FileUploadResumedEvent : INotification { /* same properties */ }

public class FileUploadFailedEvent : INotification
{
    public string  Name          { get; }
    public string? CorrelationId { get; }
    public string? UserName      { get; set; }
}
```

---

## Default IFile Implementation

```csharp
// src/Juice.Storage/InMemory/UploadFileInfo.cs  (usable as your TFile)
public class UploadFileInfo : IFile
{
    public Guid     Id                   { get; init; }
    public string   Name                 { get; set; }
    public long     PackageSize          { get; set; }
    public DateTimeOffset? LastModified  { get; set; }
    public string?  ContentType          { get; set; }
    public string?  OriginalName         { get; init; }
    public string?  CorrelationId        { get; set; }
    public JObject? Metadata             { get; set; }
    public DateTimeOffset CreatedDate    { get; init; } = DateTimeOffset.Now;
    public bool     DateModifiedPreserved { get; set; }
}
```
