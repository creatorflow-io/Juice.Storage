#!/usr/bin/env bash
# End-to-end test of Juice.Storage on Linux containers.
#
# Starts a Samba server, an FTP server and Juice.Storage.App (built from this repository) with 3 storages:
#   /storage   LocalDisk  /data/storage in the app container
#   /storage1  SMB        \\smb\Storage\App, mounted by Juice.Storage.Local.Linux with the endpoint credentials
#   /storage2  FTP        ftp://ftp/ftp/ftpuser/upload
# then checks uploads/downloads over HTTP, the files on each server, concurrent first mount and graceful shutdown.
#
# Requires: docker compose v2, curl, python 3, GITHUB_PACKAGE_USERNAME/GITHUB_PACKAGE_TOKEN (NuGet feed).
# Options (environment): CONCURRENCY=6 clients, E2E_PORT=18080, KEEP=true to leave the stack running.
set -uo pipefail

cd "$(dirname "$0")"
export MSYS_NO_PATHCONV=1 # Git Bash on Windows: do not rewrite /storage arguments to Windows paths

: "${GITHUB_PACKAGE_USERNAME:?GITHUB_PACKAGE_USERNAME is required to restore packages}"
: "${GITHUB_PACKAGE_TOKEN:?GITHUB_PACKAGE_TOKEN is required to restore packages}"

COMPOSE="docker compose"
CONCURRENCY=${CONCURRENCY:-6}
export E2E_PORT=${E2E_PORT:-18080}
export BASE="http://localhost:${E2E_PORT}"
STORAGES=("/storage=LocalDisk" "/storage1=SMB (Linux CIFS mount)" "/storage2=FTP")
# sample.bin size and 2021-01-18T17:08:50Z, see client.py
EXPECTED_SAMPLE="2109497 1610989730"
EXPECTED_COPY="11 1610989730"

PYTHON=python3
if ! "$PYTHON" -c "import sys" >/dev/null 2>&1; then PYTHON=python; fi

failures=0
pass() { echo "[PASS] $*"; }
fail() { echo "[FAIL] $*"; failures=$((failures + 1)); }
expect() { # expect <name> <expected> <actual>
    if [ "$2" = "$3" ]; then pass "$1"; else fail "$1 -- expected '$2', got '$3'"; fi
}

cleanup() {
    if [ "${KEEP:-false}" != "true" ]; then
        $COMPOSE down --volumes --rmi local >/dev/null 2>&1
    fi
}
trap cleanup EXIT

wait_for_app() {
    local code
    for _ in $(seq 1 60); do
        code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/" || true)
        if [ "$code" = "200" ]; then return 0; fi
        sleep 1
    done
    echo "App did not start:"; $COMPOSE logs app | tail -30
    exit 1
}

verify_backends() { # verify_backends <run id>
    local run=$1
    for target in "app:/data/storage:LocalDisk" "smb:/srv/storage/App:SMB server" "ftp:/ftp/ftpuser/upload:FTP server"; do
        IFS=: read -r service dir label <<<"$target"
        expect "$label has sample.bin (size, modified time)" "$EXPECTED_SAMPLE" \
            "$($COMPOSE exec -T "$service" stat -c '%s %Y' "$dir/e2e-$run/dir/sample.bin" 2>&1 | tr -d '\r')"
        expect "$label has sample(1).bin" "$EXPECTED_COPY" \
            "$($COMPOSE exec -T "$service" stat -c '%s %Y' "$dir/e2e-$run/dir/sample(1).bin" 2>&1 | tr -d '\r')"
    done
}

echo "== Build and start"
$COMPOSE up -d --build || exit 1
wait_for_app

echo; echo "== Sequential run"
SEQ_RUN="seq$(date +%s)"
E2E_RUN=$SEQ_RUN "$PYTHON" client.py "${STORAGES[@]}" || fail "sequential client run"

echo; echo "== Files on the servers"
verify_backends "$SEQ_RUN"

echo; echo "== Leftovers"
expect "path traversal did not write /etc/juice-evil.bin" "absent" \
    "$($COMPOSE exec -T app sh -c 'test -e /etc/juice-evil.bin && echo present || echo absent' | tr -d '\r')"
expect "no backslash-named files on local disk" "" \
    "$($COMPOSE exec -T app find /data/storage -name '*\\*' | tr -d '\r')"
expect "temporary credentials files removed" "0" \
    "$($COMPOSE exec -T app sh -c 'ls /tmp | grep -c juice-cifs' | tr -d '\r')"
expect "SMB password not in logs" "0" "$($COMPOSE logs app | grep -ci 'password=storage')"

echo; echo "== Concurrent run ($CONCURRENCY clients racing for the first mount)"
$COMPOSE up -d --force-recreate app >/dev/null 2>&1 || exit 1
wait_for_app
expect "share not mounted before first request" "0" \
    "$($COMPOSE exec -T app sh -c 'grep -c cifs /proc/mounts' | tr -d '\r')"
out=$(mktemp -d)
pids=()
for i in $(seq 1 "$CONCURRENCY"); do
    E2E_RUN="conc$i-$(date +%s)" "$PYTHON" client.py "${STORAGES[@]}" >"$out/client-$i.txt" 2>&1 &
    pids+=($!)
done
for i in "${!pids[@]}"; do
    if wait "${pids[$i]}"; then
        pass "client $((i + 1)): $(grep 'RUN=' "$out/client-$((i + 1)).txt")"
    else
        fail "client $((i + 1)):"; grep -E 'FAIL|RUN=|Error' "$out/client-$((i + 1)).txt" | head -5
    fi
done
rm -rf "$out"
expect "share mounted once" "1" "$($COMPOSE logs app | grep -c 'Mounted network share')"

echo; echo "== Graceful shutdown"
container=$($COMPOSE ps -q app)
start=$(date +%s)
$COMPOSE stop -t 20 app >/dev/null 2>&1
elapsed=$(($(date +%s) - start))
if [ "$elapsed" -lt 10 ]; then pass "stopped in ${elapsed}s"; else fail "stop took ${elapsed}s (hung until timeout?)"; fi
expect "exit code" "0" "$(docker inspect -f '{{.State.ExitCode}}' "$container")"
expect "share unmounted on shutdown" "1" "$(docker logs "$container" 2>&1 | grep -c 'Unmounted network share')"

echo
if [ "$failures" -eq 0 ]; then echo "E2E PASSED"; else echo "E2E FAILED: $failures failure(s)"; fi
[ "$failures" -eq 0 ]
