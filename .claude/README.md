# Juice.Storage — Developer Reference

> Quick reference for implementing digital asset management using this library.
> Generated: 2026-02-24 | Branch: release/9.0

## Table of Contents

- [Overview](#overview)
- [Projects](#projects)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
- [Interfaces Reference](#interfaces-reference)
- [Upload Flow](#upload-flow)
- [Download Flow](#download-flow)
- [Storage Providers](#storage-providers)
- [DI Registration Cheatsheet](#di-registration-cheatsheet)
- [HTTP API (Middleware)](#http-api-middleware)
- [Events (MediatR)](#events-mediatr)
- [Authorization](#authorization)
- [Background Maintenance](#background-maintenance)
- [File Versioning](#file-versioning)
- [Implementation Checklist](#implementation-checklist)

---

## Overview

Juice.Storage is a **pluggable, multi-protocol file storage library** for ASP.NET Core. It supports:
- **Protocols**: Local disk, SMB/network shares, FTP/FTPS
- **Chunked & resumable uploads** with offset tracking
- **File versioning** (`file(1).txt`, `file(2).txt`, ...)
- **Metadata & correlation tracking** per file
- **Event-driven notifications** via MediatR
- **Quota checking** and **authorization** hooks
- **Background cleanup** of timed-out uploads

---

## Projects

| Project | NuGet / Purpose |
|---|---|
| `Juice.Storage.Abstractions` | Core interfaces: `IStorage`, `IStorageProvider`, `IStorageResolver`, `StorageEndpoint` |
| `Juice.Storage` | Upload/download managers, middleware, events, in-memory impls |
| `Juice.Storage.Local` | `LocalStorageProvider` (LocalDisk/SMB) + `FTPStorageProvider` |
| `Juice.Storage.App` | Sample app — see `src/Juice.Storage.App/Program.cs` |

**Key dependencies:**
- `Polly 8.4.1` — retry policy in LocalStorageProvider
- `FluentFTP 52.1.0` — async FTP in FTPStorageProvider
- `Newtonsoft.Json` — `JObject` metadata on `IFile`
- `MediatR` (via `Juice.MediatR`) — upload events

---

## Quick Start

### Minimal setup (local disk + in-memory repos)

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddStorage();                                      // core resolver
builder.Services.AddLocalStorageProviders();                        // LocalDisk + FTP
builder.Services.AddInMemoryUploadManager<UploadFileInfo>(config);  // upload manager + in-memory repos
builder.Services.AddSingleton<IFileRepository<UploadFileInfo>, MyFileRepository>();
builder.Services.AddDefaultDownloadManager<UploadFileInfo>(config);
builder.Services.AddInMemoryStorageMaintainServices(config, ["/storage"]);

builder.Services.AddAuthorization(opt => {
    opt.AddPolicy(StoragePolicies.CreateFile,   p => p.RequireAssertion(_ => true));
    opt.AddPolicy(StoragePolicies.DownloadFile, p => p.RequireAssertion(_ => true));
});

var app = builder.Build();
app.UseStorage(opt => { opt.Endpoints = ["/storage"]; });
app.Run();
```

### appsettings.json

```json
{
  "Juice": {
    "Storage": {
      "Storages": [
        {
          "WebBasePath": "/storage",
          "Endpoints": [
            { "Protocol": "LocalDisk", "Uri": "D:\\Assets" }
          ]
        }
      ]
    }
  },
  "Upload":   { "SectionSize": 10485760, "DeleteOnAbort": false, "PreserveDateModified": true },
  "Download": { "IsSupportDownloadByPath": false }
}
```

---

## Core Concepts

### Storage Resolution

```
identity ("/storage") → IStorageRepository.GetEndpointsAsync()
  → List<StorageEndpoint>
  → IStorageProviderFactory.CreateProviders()
  → StorageProxy (tries providers by priority, highest first)
  → IStorageProvider.ReadAsync / WriteAsync / ...
```

- **identity** = the URL segment, e.g. `/storage`
- **StorageEndpoint** = `{ Uri, Protocol, Identity(user), Password, BasePath }`
- **StorageProxy** = fallback across multiple providers

### File Lifecycle

```
1. Init   → allocate ID, create empty file, save to upload repo
2. Upload → stream chunks with offset
3. Complete → persist to file repo, preserve dates, publish event
4. Failure / Timeout → cleanup / abort
```

---

## Interfaces Reference

See [`interfaces.md`](./interfaces.md) for full signatures.

### Must implement (for production)

| Interface | Purpose |
|---|---|
| `IFileRepository<T>` | Persist completed file records |
| `IStorageRepository` | Return `StorageEndpoint[]` for an identity |

### Optional to implement

| Interface | Purpose |
|---|---|
| `IFileNameGenerator<T>` | Custom storage path/name generation |
| `IQuotaChecker<T>` | Block uploads over quota |

### Already provided

| Class | Description |
|---|---|
| `DefaultUploadManager<T>` | Full upload orchestration |
| `DefaultDownloadManager<T>` | Stream-based download |
| `InMemoryUploadRepository<T>` | Dev/test upload tracking |
| `InMemoryStorageRepository` | Dev/test endpoint store |
| `LocalStorageProvider` | LocalDisk + SMB provider |
| `FTPStorageProvider` | FTP/FTPS provider |
| `StorageProxy` | Multi-provider fallback |
| `CleanupTimedoutUploadService<T>` | Background cleanup |

---

## Upload Flow

### HTTP sequence

```
POST /{identity}/init
  Body: InitialFileInfo { Name, FileSize, ContentType, OriginalName, LastModified,
                          FileExistsBehavior, CorrelationId, Metadata, UploadId? }
  Response: UploadConfiguration { UploadId, Name, SectionSize, Exists, Offset, PackageSize }

POST /{identity}/upload
  Headers: x-uploadid: <guid>, x-offset: <bytes>
  Body: raw bytes (chunk)
  Response headers: x-offset: <new>, x-completed: true/false

POST /{identity}/complete   (if x-completed is false after last chunk)
  Headers: x-uploadid: <guid>

POST /{identity}/failure    (on client error)
  Headers: x-uploadid: <guid>
```

### Resume an interrupted upload

```
POST /{identity}/init  with { UploadId: <existing-guid>, FileExistsBehavior: Resume }
→ UploadConfiguration.Exists = true, Offset = <bytes already written>
→ resume uploading from that offset
```

---

## Download Flow

```
GET /{identity}/file/{fileId}          → download by ID
GET /{identity}/file?path={filePath}   → download by path (requires DownloadOptions.IsSupportDownloadByPath=true)

Supports HTTP Range header for partial/streaming downloads.
```

### Check existence

```
POST /{identity}/exists
  Body: { "path": "relative/path/to/file.ext" }
  Response: 200 / 404
```

---

## Storage Providers

### Protocol enum

| Value | Description |
|---|---|
| `LocalDisk` (2) | Local file system path |
| `VirtualDirectory` (1) | Virtual/mapped directory |
| `Smb` (0) | Network share `\\server\share` |
| `Ftp` (3) | `ftp://host:port/path` or `ftps://...` |

### StorageEndpoint properties

```csharp
string Uri        // e.g. "D:\\Assets" or "ftp://192.168.1.10/uploads"
Protocol Protocol // LocalDisk | Smb | Ftp | VirtualDirectory
string? Identity  // username (for SMB/FTP)
string? Password  // password
string? BasePath  // local mount point for network URIs
```

### LocalStorageProvider

- Handles `Smb`, `LocalDisk`, `VirtualDirectory`
- Auto-creates directories
- Uses Polly: 3 retries with exponential back-off
- Connects to SMB shares via `NetworkConnection` (Win32 WNetAddConnection2)

### FTPStorageProvider

- Handles `Ftp`
- URI format: `ftp[s]://host[:port][/working-dir]`
- Uses FluentFTP `AsyncFtpClient`
- Supports append for resume

---

## DI Registration Cheatsheet

```csharp
// ---- Required ----
services.AddStorage();                    // IStorageProviderFactory, IStorageResolver
services.AddLocalStorageProviders();      // LocalStorageProvider + FTPStorageProvider

// ---- Upload ----
// Option A: production (bring your own IUploadRepository)
services.AddDefaultUploadManager<TFile>(config, opt => { opt.SectionSize = 5_242_880; });

// Option B: dev/test (in-memory repos)
services.AddInMemoryUploadManager<TFile>(config);

// ---- File repo (always required, must implement yourself) ----
services.AddScoped<IFileRepository<TFile>, MyFileRepository>();

// ---- Download ----
services.AddDefaultDownloadManager<TFile>(config);

// ---- Maintenance ----
services.AddStorageMaintainServices<TFile>(config, identities: ["/storage"]);
// or in-memory:
services.AddInMemoryStorageMaintainServices(config, ["/storage"]);

// ---- Storage repo (required for production resolution) ----
services.AddScoped<IStorageRepository, MyStorageRepository>();
// or in-memory (reads from appsettings Juice:Storage):
services.AddInMemoryStorageRepository(config);
```

---

## HTTP API (Middleware)

```csharp
// Register middleware (after UseRouting / UseAuthorization)
app.UseStorage(opt => {
    opt.Endpoints  = ["/storage", "/assets"];  // multiple identities supported
    opt.RewritePath = true;
});
```

All routes are handled by `StorageMiddleware`:

| Method | Path | Description |
|---|---|---|
| `POST` | `/{id}/init` | Initialize upload |
| `POST` | `/{id}/upload` | Upload chunk |
| `POST` | `/{id}/exists` | Check file exists |
| `POST` | `/{id}/complete` | Mark upload done |
| `POST` | `/{id}/failure` | Report failure |
| `GET`  | `/{id}/file/{fileId}` | Download by ID |

---

## Events (MediatR)

Subscribe by implementing `INotificationHandler<TEvent>`.

| Event | Trigger |
|---|---|
| `FileUploadStartedEvent` | Init completed |
| `FileUploadedEvent` | Upload completed |
| `FileUploadFailedEvent` | Upload failed |
| `FileUploadResumedEvent` | Upload resumed |

All events carry: `Id`, `Name`, `ContentType`, `Length`, `CorrelationId`, `Metadata`, `UserName`.

```csharp
public class MyHandler : INotificationHandler<FileUploadedEvent>
{
    public Task Handle(FileUploadedEvent e, CancellationToken ct)
    {
        // index the asset, send notifications, etc.
        return Task.CompletedTask;
    }
}
```

---

## Authorization

Two policy names (constants in `StoragePolicies`):

```csharp
StoragePolicies.CreateFile   = "Storage_CreateFile"
StoragePolicies.DownloadFile = "Storage_DownloadFile"
```

Operations available via `StorageOperations`:
`Write`, `Read`, `Delete`, `RenameFile`

Register ASP.NET Core authorization policies with these names to control access.

---

## Background Maintenance

```csharp
// Cleans up timed-out (incomplete) uploads
services.AddStorageMaintainServices<TFile>(config, ["/storage"], opt => {
    opt.Interval     = TimeSpan.FromMinutes(5);   // how often to run
    opt.CleanupAfter = TimeSpan.FromDays(1);      // abandon uploads older than this
});
```

---

## File Versioning

When `FileExistsBehavior.AscendedCopyNumber` is set, the library automatically appends a copy number:

```
document.pdf  →  document(1).pdf  →  document(2).pdf  ...
```

Regex for detecting existing copies:
```
(?<n>[^\n]+)\((?<cn>[0-9]+)\)[\s]*\.[\S]+$
```

Other behaviors:
- `RaiseError` — throws if file exists
- `Replace` — overwrites
- `Resume` — resumes interrupted upload

---

## Implementation Checklist

For a production digital asset management service:

- [ ] Implement `IFileRepository<TFile>` (EF Core, Dapper, MongoDB, etc.)
- [ ] Implement `IStorageRepository` (return endpoints from DB/config)
- [ ] Define your `TFile` model implementing `IFile`
- [ ] Register all services (see DI cheatsheet)
- [ ] Configure `appsettings.json` with endpoints
- [ ] Set up ASP.NET Core auth policies
- [ ] Add `INotificationHandler` for upload events (indexing, webhooks)
- [ ] Optionally implement `IFileNameGenerator<TFile>` for custom paths
- [ ] Optionally implement `IQuotaChecker<TFile>` for storage limits
- [ ] Call `app.UseStorage(...)` in middleware pipeline
- [ ] Register background maintenance services

---

## Key Source Files

| File | What it does |
|---|---|
| `src/Juice.Storage.Abstractions/IStorage.cs` | Core read/write interface |
| `src/Juice.Storage.Abstractions/StorageEndpoint.cs` | Endpoint config model |
| `src/Juice.Storage.Abstractions/Services/StorageProxy.cs` | Multi-provider fallback |
| `src/Juice.Storage/IUploadManager.cs` | Upload orchestration interface |
| `src/Juice.Storage/DefaultUploadManager.cs` | Full upload implementation |
| `src/Juice.Storage/DefaultDownloadManager.cs` | Download implementation |
| `src/Juice.Storage/Middleware/StorageMiddleware.cs` | HTTP request handling |
| `src/Juice.Storage/InMemory/` | Dev/test implementations |
| `src/Juice.Storage.Local/LocalStorageProvider.cs` | Local/SMB provider |
| `src/Juice.Storage.Local/FTPStorageProvider.cs` | FTP/FTPS provider |
| `src/Juice.Storage.App/Program.cs` | Full wiring example |
| `test/Juice.Storage.Tests/UploadManagerTest.cs` | Upload flow tests |
