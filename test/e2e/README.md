# Juice.Storage end-to-end tests (Linux containers)

Runs `Juice.Storage.App` built from this repository in a Linux container with three storages and exercises
the storage HTTP API against real servers:

| Storage | Protocol | Backend |
|---|---|---|
| `/storage` | LocalDisk | `/data/storage` in the app container |
| `/storage1` | SMB | Samba container, mounted by `Juice.Storage.Local.Linux` with the endpoint credentials (`--cap-add SYS_ADMIN --cap-add DAC_READ_SEARCH`, no `--privileged`) |
| `/storage2` | FTP | `delfer/alpine-ftp-server` container |

## Run

```bash
export GITHUB_PACKAGE_USERNAME=... GITHUB_PACKAGE_TOKEN=...   # NuGet feed used by the app build
bash test/e2e/run.sh
```

Works from Linux, macOS and Git Bash on Windows (Docker Desktop). Needs docker compose v2, curl and Python 3.

Options: `CONCURRENCY=6` (parallel clients), `E2E_PORT=18080`, `KEEP=true` (leave the stack running for debugging;
remove it with `docker compose -f test/e2e/docker-compose.yml down --rmi local`).

## What is checked

- Per storage (`client.py`): chunked upload (3 × 1 MiB sections), `\` separated client names stored as `/` folders,
  modified date preserved, exists, download by path / by file id / `Range`, copy number on the same name,
  `RaiseError` on the same name, `../` path traversal rejected.
- On the servers: file sizes and modified time on the local disk, Samba share and FTP server.
- Leftovers: no traversal file, no backslash-named files, temporary CIFS credentials files removed, password not logged.
- Concurrency: several clients against a freshly started app, the share must be mounted exactly once.
- Shutdown: graceful stop within seconds, exit code 0, share unmounted.

`client.py` can also target any running app: `BASE=http://host:port python client.py "/storage=LocalDisk"`.
