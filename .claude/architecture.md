# Juice.Storage — Architecture & Flow Diagrams

---

## Storage Resolution Chain

```
HTTP Request  /storage/init
      │
      ▼
StorageMiddleware
      │  extracts identity = "/storage"
      ▼
IStorageResolver.TryResolveAsync("/storage")
      │
      ▼
StorageResolver  ──── iterates IStorageResolveStrategy[] by priority ────
      │
      ▼
RepoStorageResolver (Priority = 0)
      │  calls IStorageRepository.GetEndpointsAsync("/storage")
      │  returns List<StorageEndpoint>
      ▼
IStorageProviderFactory.CreateProviders(endpoints)
      │  DefaultStorageProviderFactory:
      │  - gets all IStorageProvider from DI
      │  - filters by endpoint.Protocol in provider.Protocols
      │  - calls provider.Configure(endpoint, priority)
      ▼
StorageProxy
      │  holds ordered IStorageProvider[]
      │  tries each provider (highest priority first)
      │  returns first successful result
      ▼
LocalStorageProvider / FTPStorageProvider
```

---

## Upload State Machine

```
                    ┌─────────┐
              POST /init       │  PENDING  │
                    └────┬────┘
                         │ InitAsync()
                         │ - create IFile record
                         │ - IFileNameGenerator (optional)
                         │ - IQuotaChecker (optional)
                         │ - IStorage.CreateAsync()
                         │ - IUploadRepository.AddAsync()
                         │ - publish FileUploadStartedEvent
                         ▼
                    ┌──────────┐
              POST /upload      │ IN FLIGHT │◄──────────┐
                    └────┬─────┘           │  more chunks
                         │ UploadAsync()   │
                         │ - IStorage.WriteAsync(offset)
                         │ - check if file size reached
                         └───── not done ──┘
                         │ done
                         ▼
                    ┌───────────┐
              POST /complete    │ COMPLETING│
                    └─────┬─────┘
                          │ CompleteAsync()
                          │ - PreserveModifiedTimeAsync
                          │ - IFileRepository.AddAsync()
                          │ - publish FileUploadedEvent
                          │ - IUploadRepository.CompleteAsync()
                          ▼
                    ┌───────────┐
                    │ COMPLETED │
                    └───────────┘

  POST /failure ──► FailureAsync()
                    - IStorage.DeleteAsync (if DeleteOnAbort)
                    - IUploadRepository.AbortAsync()
                    - publish FileUploadFailedEvent

  Background   ──► TimedoutAsync() (via CleanupTimedoutUploadService)
                    - same as failure
```

---

## Download Flow

```
GET /{identity}/file/{fileId}
      │
      ▼
StorageMiddleware
      │ resolves IStorage for identity
      ▼
DefaultDownloadManager<T>.GetStreamAsync(Guid id)
      │
      ├─► IFileRepository.GetAsync(identity, id)
      │         returns AssetFile { Name = "2026/02/abc.jpg" }
      │
      ├─► Authorization check (StoragePolicies.DownloadFile)
      │
      └─► IStorage.ReadAsync(file.Name)
                returns Stream

HTTP Response
  Content-Type: image/jpeg
  Content-Disposition: attachment; filename="vacation.jpg"
  [Range support if requested]
```

---

## Multi-Provider Fallback

```
StorageProxy providers (ordered by priority desc):
  [0] FTPStorageProvider    Priority=10  (ftp://primary-server/assets)
  [1] LocalStorageProvider  Priority=5   (\\backup-share\assets)

ReadAsync("2026/02/abc.jpg"):
  → try FTPStorageProvider.ReadAsync()  ──► success → return stream
                                        ──► fail    → try next
  → try LocalStorageProvider.ReadAsync() ──► success → return stream
                                          ──► fail    → throw
```

---

## In-Memory vs Production

```
Development / Testing                   Production
─────────────────────────────           ──────────────────────────────
AddInMemoryUploadManager<T>()           AddDefaultUploadManager<T>()
  ├─ InMemoryStorageRepository            ├─ [implement IStorageRepository]
  ├─ InMemoryUploadRepository<T>          ├─ [implement IUploadRepository<T>]
  └─ InMemoryStorageProvider              └─ LocalStorageProvider / FTPStorageProvider

AddInMemoryStorageMaintainServices()    AddStorageMaintainServices<T>()
```

---

## Package / Project Dependency Graph

```
Juice.Storage.App (sample)
    ├── Juice.Storage
    │       ├── Juice.Storage.Abstractions
    │       │       └── Microsoft.Extensions.*
    │       ├── Newtonsoft.Json
    │       └── Juice.MediatR → MediatR
    └── Juice.Storage.Local
            ├── Juice.Storage.Abstractions
            ├── Polly
            └── FluentFTP
```

---

## HTTP Headers Used by Middleware

| Header | Direction | Description |
|---|---|---|
| `x-uploadid` | Request | Upload session GUID |
| `x-offset` | Request | Byte offset of this chunk |
| `x-offset` | Response | New offset after write |
| `x-completed` | Response | `"true"` when upload finished |
| `Range` | Request | Byte range for partial download |
| `Content-Range` | Response | Range returned |
| `Content-Disposition` | Response | `attachment; filename="..."` |

---

## Key Design Decisions

| Decision | Choice | Why |
|---|---|---|
| File identity | GUID per upload | Avoids name collisions, enables resume |
| Chunk protocol | Offset-based | Simple, stateless server |
| Provider selection | Protocol-filtered + priority | Pluggable, fallback support |
| Events | MediatR INotification | Decoupled, async-friendly |
| Metadata | JObject | Schema-less, flexible |
| Auth | ASP.NET Core policies | Standard, composable |
| Cleanup | IHostedService | Non-blocking, configurable |
