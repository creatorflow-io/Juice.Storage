"""End-to-end HTTP client for the Juice.Storage middleware.

For each storage: init -> chunked upload -> complete -> exists -> download (by path, by id, range)
-> copy number -> RaiseError -> path traversal.

Usage: python client.py "/storage=LocalDisk" "/storage1=SMB" ...
Environment: BASE (default http://localhost:18080), E2E_RUN (run id, default random).
"""
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = os.environ.get("BASE", "http://localhost:18080")
RUN = os.environ.get("E2E_RUN") or uuid.uuid4().hex[:8]
SAMPLE_SIZE = 2 * 1024 * 1024 + 12345  # 3 chunks with the 1 MiB SectionSize of app.env
LAST_MODIFIED = "2021-01-18T17:08:50+00:00"
results = []


def request(method, path, data=None, headers=None):
    req = urllib.request.Request(BASE + path, data=data, method=method, headers=headers or {})
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            return resp.status, dict(resp.headers), resp.read()
    except urllib.error.HTTPError as e:
        try:
            return e.code, dict(e.headers), e.read()
        except Exception as inner:
            return e.code, dict(e.headers), f"<broken response: {inner}>".encode()
    except Exception as e:
        return -1, {}, f"<broken response: {e}>".encode()


def form(method, path, fields):
    body = urllib.parse.urlencode(fields).encode()
    return request(method, path, body, {"Content-Type": "application/x-www-form-urlencoded"})


def multipart(path, filename, content, headers):
    boundary = "----juice" + uuid.uuid4().hex
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{filename}\"\r\n"
            f"Content-Type: application/octet-stream\r\n\r\n").encode() + content + f"\r\n--{boundary}--\r\n".encode()
    headers = dict(headers, **{"Content-Type": f"multipart/form-data; boundary={boundary}"})
    return request("POST", path, body, headers)


def check(name, ok, detail=""):
    results.append((name, ok, detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" -- {detail}" if detail else ""), flush=True)


def upload(storage, file_path, data, behavior="AscendedCopyNumber"):
    """Upload like a browser client: the last chunk completes the upload, /complete is only needed otherwise.
    Returns (status, info or error message)."""
    status, _, body = form("POST", f"{storage}/init", {
        "filePath": file_path, "originalFilePath": file_path, "fileSize": str(len(data)),
        "fileExistsBehavior": behavior, "lastModifiedDate": LAST_MODIFIED,
        "contentType": "application/octet-stream"})
    if status != 201:
        return status, f"init: {body.decode(errors='replace')}"
    config = json.loads(body)
    upload_id, name, section = config["UploadId"], config["Name"], config["SectionSize"]
    offset, completed, chunks, preserved = 0, "False", 0, None
    while offset < len(data):
        chunk = data[offset:offset + section]
        status, headers, body = multipart(f"{storage}/upload", "chunk.bin", chunk,
                                          {"x-uploadid": upload_id, "x-offset": str(offset)})
        if status != 200:
            return status, f"upload chunk at {offset}: {body.decode(errors='replace')}"
        offset = int(headers.get("x-offset"))
        completed = headers.get("x-completed")
        preserved = headers.get("x-date-modified-preserved")
        chunks += 1
    status = 200
    if completed != "True":
        status, headers, body = form("POST", f"{storage}/complete", {"uploadId": upload_id})
        if status != 204:
            return status, f"complete: {body.decode(errors='replace')}"
        preserved = headers.get("x-date-modified-preserved")
    return status, {"name": name, "chunks": chunks, "completed": completed, "preserved": preserved, "id": upload_id}


def run(storage, label):
    print(f"\n=== {label} ({storage})", flush=True)
    data = os.urandom(SAMPLE_SIZE)
    file_path = f"e2e-{RUN}\\dir\\sample.bin"  # Windows-style client path
    expected = f"e2e-{RUN}/dir/sample.bin"

    status, info = upload(storage, file_path, data)
    ok = isinstance(info, dict)
    check("chunked upload (init/upload/complete)", ok, f"status={status} {info}")
    if not ok:
        return
    check("backslash name normalized", info["name"] == expected, info["name"])
    check("3 chunks, completed on last", info["chunks"] == 3 and info["completed"] == "True",
          f"chunks={info['chunks']} completed={info['completed']}")
    check("modified date preserved", info["preserved"] == "True", f"x-date-modified-preserved={info['preserved']}")

    status, _, body = form("POST", f"{storage}/exists", {"filePath": expected})
    check("exists", status == 200 and body == b"true", f"{status} {body!r}")

    status, _, body = request("GET", f"{storage}/file/{expected}")
    check("download by path matches upload", status == 200 and body == data, f"status={status} bytes={len(body)}")

    status, _, body = request("GET", f"{storage}/file/{info['id']}")
    check("download by file id matches upload", status == 200 and body == data, f"status={status} bytes={len(body)}")

    status, _, body = request("GET", f"{storage}/file/{expected}", headers={"Range": "bytes=1000-1999"})
    check("range download", status == 206 and body == data[1000:2000], f"status={status} bytes={len(body)}")

    status, info2 = upload(storage, file_path, b"second copy")
    name2 = info2["name"] if isinstance(info2, dict) else info2
    check("same name -> copy number", name2 == f"e2e-{RUN}/dir/sample(1).bin", f"status={status} name={name2}")

    status, info3 = upload(storage, file_path, b"x", behavior="RaiseError")
    check("same name + RaiseError rejected", not isinstance(info3, dict), f"status={status} {str(info3)[:80]}")

    status, info4 = upload(storage, "../../etc/juice-evil.bin", b"evil")
    check("path traversal rejected", not isinstance(info4, dict), f"status={status} {str(info4)[:100]}")


if __name__ == "__main__":
    for arg in sys.argv[1:]:
        storage, label = arg.split("=", 1)
        run(storage, label)
    failed = [r for r in results if not r[1]]
    print(f"\nRUN={RUN} total={len(results)} failed={len(failed)}")
    sys.exit(1 if failed or not results else 0)
