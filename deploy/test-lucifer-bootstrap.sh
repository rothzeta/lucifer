#!/usr/bin/env bash
# Isolated acceptance test of install.sh (GitHub Releases bootstrap) and of
# `lucifer-install` installing and updating without an archive. A local HTTP
# server on 127.0.0.1 stands in for api.github.com and github.com; the host is a
# temporary LUCIFER_INSTALL_ROOT with stubbed host commands (as in
# test-lucifer-install.sh). It proves the download/validation/transaction wiring
# only: nothing here contacts GitHub, and stubs are not physical evidence.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$here/.." && pwd)"
installer="$here/lucifer-install"
bootstrap="$repo_root/install.sh"
tmp="$(mktemp -d)"
server_pids=()
cleanup() { [ "${#server_pids[@]}" = 0 ] || kill "${server_pids[@]}" 2>/dev/null; rm -rf "$tmp"; }
trap cleanup EXIT
fails=0
fail() { echo "FAIL: $*" >&2; fails=$((fails + 1)); }
ok() { echo "ok: $*"; }
check() { local d="$1"; shift; if "$@"; then :; else fail "$d"; fi; }

# -- host stubs ----------------------------------------------------------------
stubs="$tmp/stubs"; mkdir -p "$stubs"
stub() { printf '#!/usr/bin/env bash\n%s\n' "$2" > "$stubs/$1"; chmod +x "$stubs/$1"; }
stub id 'case "$*" in "-u") echo "${STUB_UID:-0}";; "-nG lucifer") cat "$STUB_STATE/groups" 2>/dev/null; echo;; *) exit 1;; esac'
stub getent 'case "$1 $2" in "passwd lucifer") [ -e "$STUB_STATE/user" ];; "group "*) exit 0;; *) exit 2;; esac'
stub useradd 'echo "useradd $*" >> "$STUB_STATE/calls"; : > "$STUB_STATE/user"'
stub usermod 'echo "usermod $*" >> "$STUB_STATE/calls"; echo "$2 $3 $4" >> "$STUB_STATE/groups"'
stub chown 'echo "chown $*" >> "$STUB_STATE/owners"'
stub dpkg '[ "$1" = "--print-architecture" ] && echo "${STUB_ARCH:-arm64}"'
stub uname 'echo aarch64'   # a 64-bit kernel must not influence the userland RID
stub systemd-tmpfiles 'echo "tmpfiles $*" >> "$STUB_STATE/calls"'
stub runuser 'shift 3; echo "check-as-service" >> "$STUB_STATE/calls"; exec "$@"'
stub systemctl '
echo "systemctl $*" >> "$STUB_STATE/calls"
case "$1" in
  is-active) [ -e "$STUB_STATE/active" ];;
  is-enabled) [ -e "$STUB_STATE/enabled" ];;
  stop) rm -f "$STUB_STATE/active";;
  enable) : > "$STUB_STATE/enabled";;
  disable) rm -f "$STUB_STATE/enabled";;
  start) : > "$STUB_STATE/active";;
esac'
export PATH="$stubs:$PATH"
export LUCIFER_INSTALL_SETTLE_SECONDS=0
mkdir -p "$tmp/tmpdir"; export TMPDIR="$tmp/tmpdir"

# -- fake GitHub -------------------------------------------------------------------
web="$tmp/web"; mkdir -p "$web"
cat > "$tmp/server.py" <<'PY'
import http.server, os, socketserver, ssl, sys
web, portfile = sys.argv[1], sys.argv[2]
cert = sys.argv[3] if len(sys.argv) > 3 else None
class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def do_GET(self):
        p = os.path.join(web, self.path.split("?")[0].lstrip("/"))
        with open(portfile + ".log", "a") as f: f.write(self.path + "\n")
        if os.path.exists(p + ".redirect"):
            self.send_response(302); self.send_header("Location", open(p + ".redirect").read().strip())
            self.send_header("Content-Length", "0"); self.end_headers(); return
        if os.path.exists(p + ".status"):
            self.send_response(int(open(p + ".status").read())); self.send_header("Content-Length", "0"); self.end_headers(); return
        if not os.path.isfile(p):
            self.send_response(404); self.send_header("Content-Length", "0"); self.end_headers(); return
        data = open(p, "rb").read()
        self.send_response(200); self.send_header("Content-Length", str(len(data))); self.end_headers()
        self.wfile.write(data[: len(data) // 2] if os.path.exists(p + ".truncate") else data)
        self.wfile.flush()
        self.close_connection = True
s = socketserver.TCPServer(("127.0.0.1", 0), H)
if cert:
    c = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER); c.load_cert_chain(cert)
    s.socket = c.wrap_socket(s.socket, server_side=True)
open(portfile, "w").write(str(s.server_address[1]))
s.serve_forever()
PY
start_server() { # name webroot [cert]: writes $tmp/<name>.port
  python3 -I "$tmp/server.py" "$2" "$tmp/$1.port" ${3:+"$3"} & server_pids+=($!)
  local _; for _ in $(seq 100); do [ -s "$tmp/$1.port" ] && return 0; sleep 0.1; done
  echo "server $1 did not start" >&2; exit 1
}
start_server plain "$web"
port="$(cat "$tmp/plain.port")"
export LUCIFER_REPO=test/lucifer LUCIFER_GITHUB_API="http://127.0.0.1:$port/api" LUCIFER_GITHUB_DOWNLOAD="http://127.0.0.1:$port" LUCIFER_INSTALL_TEST_INSECURE=1
apiroot="$web/api/repos/test/lucifer/releases"; dlroot="$web/test/lucifer/releases"

mkpkg() { # TAG RID [shell snippet run inside the package dir $w/$n]
  local v="$1" r="$2" n="lucifer-$1-$2" w out
  w="$(mktemp -d -p "$tmp")"; out="$dlroot/download/$v"; mkdir -p "$out"
  mkdir -p "$w/$n"/{bin,config,systemd,udev}
  printf '#!/bin/sh\necho amp\n' > "$w/$n/bin/Amp.Nfc"
  cat > "$w/$n/bin/lucifer-maint" <<'M'
#!/usr/bin/env bash
d="$(cd "$(dirname "$0")/.." && pwd)"
echo "maint $* rel=$(basename "$d")" >> "$STUB_STATE/calls"
[ -e "$d/FAIL_CHECK" ] && { echo "bad config" >&2; exit 2; }
exit 0
M
  cp "$installer" "$w/$n/bin/lucifer-install"
  printf 'NAVIDROME_PASSWORD=change-me\n' > "$w/$n/config/lucifer.env.example"
  echo '{}' > "$w/$n/config/catalogue.example.json"
  printf '[Service]\n# unit %s\n' "$v" > "$w/$n/systemd/lucifer.service"
  printf '# tmpfiles %s\n' "$v" > "$w/$n/systemd/lucifer.tmpfiles.conf"
  echo "$v" > "$w/$n/VERSION"; echo "$r" > "$w/$n/RID"
  chmod 0755 "$w/$n/bin/"*
  [ -z "${3:-}" ] || eval "$3"
  tar --sort=name --owner=0 --group=0 --numeric-owner -C "$w" -cf - "$n" | gzip -n > "$out/$n.tar.gz"
  (cd "$out" && sha256sum "$n.tar.gz" > "$n.tar.gz.sha256")
  rm -rf "$w"
}
# mkrelease TAG [--latest] RID...: assets + API metadata (+ stable "latest" download path)
mkrelease() {
  local tag="$1" latest=0 rids=() r f
  shift; [ "${1:-}" = --latest ] && { latest=1; shift; }
  rids=("$@")
  mkdir -p "$apiroot/tags" "$dlroot/download/$tag"
  for r in "${rids[@]}"; do mkpkg "$tag" "$r"; done
  cp "$installer" "$dlroot/download/$tag/lucifer-install"
  (cd "$dlroot/download/$tag" && sha256sum lucifer-install > lucifer-install.sha256)
  {
    printf '{\n  "url": "x",\n  "tag_name": "%s",\n  "name": "%s",\n  "assets": [\n' "$tag" "$tag"
    for f in "$dlroot/download/$tag"/*; do printf '    {\n      "name": "%s",\n      "size": 1\n    },\n' "$(basename "$f")"; done
    printf '    {"name": "end"}\n  ]\n}\n'
  } > "$apiroot/tags/$tag"
  if [ "$latest" = 1 ]; then
    cp "$apiroot/tags/$tag" "$apiroot/latest"
    rm -rf "$dlroot/latest"; mkdir -p "$dlroot/latest/download"
    cp "$dlroot/download/$tag/lucifer-install" "$dlroot/download/$tag/lucifer-install.sha256" "$dlroot/latest/download/"
  fi
}
reset_web() { rm -rf "$web/api" "$web/test"; }

fresh() {
  R="$tmp/host.$RANDOM$RANDOM"; S="$R.state"; mkdir -p "$R" "$S"
  export LUCIFER_INSTALL_ROOT="$R" STUB_STATE="$S"
  unset STUB_UID STUB_ARCH
  : > "$S/calls"; : > "$S/owners"
}
OPT() { echo "$R/opt/lucifer"; }
cur() { basename "$(readlink "$(OPT)/current")"; }
no_host_effects() {
  [ ! -e "$R/opt" ] && [ ! -e "$R/etc" ] && [ ! -e "$R/var" ] && [ ! -s "$STUB_STATE/calls" ] || fail "$1: host effects before the failure"
}
# The documented usage: the script text arrives on stdin.
piped() { out="$(bash -s -- "$@" < "$bootstrap" 2>&1)"; rc=$?; [ -z "${DBG:-}" ] || echo "[$rc] $out" >&2; }
run() { out="$("$@" 2>&1)"; rc=$?; [ -z "${DBG:-}" ] || echo "[$rc] $out" >&2; }
tree_hash() { (cd "$R" && find . -type f -not -path './opt/lucifer/.lock' -exec sha256sum {} + | sort | sha256sum; find . -type l -printf '%p -> %l\n' | sort) | sha256sum; }

# ============ 1. help and arguments through stdin ============================
bash -n "$bootstrap" && ok "bash -n install.sh" || fail "bash -n install.sh"
fresh; reset_web
piped --help; check "stdin --help exit 0" [ $rc = 0 ]; check "stdin help text" grep -q 'curl -fsSL' <<<"$out"
piped --bogus; check "stdin invalid argument exit 2" [ $rc = 2 ]; check "invalid arg names it" grep -q -- '--bogus' <<<"$out"
piped --version; check "stdin --version without value exit 2" [ $rc = 2 ]
piped --version 'v1;rm -rf'; check "stdin unsafe version exit 2" [ $rc = 2 ]
piped --version '../x'; check "traversal-looking version exit 2" [ $rc = 2 ]
LUCIFER_INSTALL_TEST_INSECURE= piped; check "plain http endpoint refused without the test hook" [ $rc = 2 ]
STUB_UID=1000 piped; check "non-root refused with guidance (1)" [ $rc = 1 ] && check "guidance mentions sudo bash" grep -q 'sudo bash' <<<"$out"
no_host_effects "arguments"
run "$installer" install --version 'bad tag'; check "backend invalid --version exit 2" [ $rc = 2 ]
run "$installer" install --version v1 "$tmp/x.tar.gz"; check "backend --version with ARCHIVE exit 2" [ $rc = 2 ]
run "$installer" update --checksum "$tmp/x"; check "backend --checksum without ARCHIVE exit 2" [ $rc = 2 ]
no_host_effects "backend arguments"
ok "help and arguments work through stdin"

# ============ 2. latest stable, explicit tag, userland RID ===================
reset_web; mkrelease v1.0.0 linux-arm64 linux-arm; mkrelease v1.1.0 --latest linux-arm64 linux-arm
fresh; piped
check "stdin install of latest exit 0" [ $rc = 0 ]
check "latest stable selected" [ "$(cur)" = lucifer-v1.1.0-linux-arm64 ]
check "unit installed" grep -q 'unit v1.1.0' "$R/etc/systemd/system/lucifer.service"
check "placeholder config created" [ -f "$R/etc/lucifer/lucifer.env" ]
check "service neither enabled nor started" bash -c '! grep -E "systemctl (enable|start)" "$1"' _ "$STUB_STATE/calls"
check "next steps printed" grep -q 'NEXT: edit' <<<"$out"
check "no private temp left" bash -c '[ -z "$(ls -A "$1")" ]' _ "$TMPDIR"
fresh; piped --version v1.0.0
check "explicit tag exit 0" [ $rc = 0 ]; check "explicit tag selected" [ "$(cur)" = lucifer-v1.0.0-linux-arm64 ]
fresh; STUB_ARCH=armhf piped
check "armhf userland (aarch64 kernel) exit 0" [ $rc = 0 ]; check "armhf selects linux-arm" [ "$(cur)" = lucifer-v1.1.0-linux-arm ]
fresh; STUB_ARCH=arm64 piped --target-rid linux-arm
check "explicit --target-rid honoured" [ "$(cur)" = lucifer-v1.1.0-linux-arm ]
fresh; STUB_ARCH=i386 piped; check "unsupported userland fails (1)" [ $rc = 1 ]; no_host_effects "unsupported userland"
ok "latest, tag and RID selection"

# ============ 3. failures before any host change ==============================
expect_fail() { # label; expects rc 1 and no effects, remaining args = bootstrap args
  local label="$1"; shift
  fresh; piped "$@"
  check "$label: exit 1" [ $rc = 1 ]; no_host_effects "$label"
  check "$label: no temp left" bash -c '[ -z "$(ls -A "$1")" ]' _ "$TMPDIR"
}
reset_web; expect_fail "no release published"
check "no-release message" grep -q 'HTTP 404' <<<"$out"
reset_web; mkrelease v1.1.0 --latest linux-arm64
expect_fail "tag that does not exist" --version v9.9.9
: > "$apiroot/latest.status"; echo 429 > "$apiroot/latest.status"
# bootstrap itself does not use the API; the backend does, after fetching it
expect_fail "rate limited API (429)"; check "rate limit explained" grep -q 'rate limit' <<<"$out"
echo 403 > "$apiroot/latest.status"; expect_fail "API 403"; check "403 explained" grep -q 'rate limit or access denied' <<<"$out"
rm -f "$apiroot/latest.status"
echo 500 > "$apiroot/latest.status"; expect_fail "API 500"; rm -f "$apiroot/latest.status"
reset_web; mkrelease v1.1.0 --latest linux-arm64
rm "$dlroot/latest/download/lucifer-install"; expect_fail "installer asset missing"
reset_web; mkrelease v1.1.0 --latest linux-arm64
echo deadbeef > "$dlroot/latest/download/lucifer-install.sha256"; expect_fail "installer checksum malformed"
reset_web; mkrelease v1.1.0 --latest linux-arm64
echo "$(printf '0%.0s' $(seq 64))  lucifer-install" > "$dlroot/latest/download/lucifer-install.sha256"; expect_fail "installer checksum mismatch"
check "installer mismatch message" grep -q 'checksum mismatch for lucifer-install' <<<"$out"
reset_web; mkrelease v1.1.0 --latest linux-arm   # armhf asset only, arm64 host
expect_fail "asset for this userland missing" ; check "missing asset named" grep -q 'lucifer-v1.1.0-linux-arm64.tar.gz' <<<"$out"
reset_web; mkrelease v1.1.0 --latest linux-arm64
: > "$dlroot/download/v1.1.0/lucifer-v1.1.0-linux-arm64.tar.gz.truncate"; expect_fail "truncated archive download"
reset_web; mkrelease v1.1.0 --latest linux-arm64
echo 429 > "$dlroot/download/v1.1.0/lucifer-v1.1.0-linux-arm64.tar.gz.status"; expect_fail "archive download refused (429)"
reset_web; mkrelease v1.1.0 --latest linux-arm64
f="$dlroot/download/v1.1.0/lucifer-v1.1.0-linux-arm64.tar.gz"; printf 'tampered' >> "$f"; expect_fail "archive checksum mismatch"
check "archive checksum message" grep -q 'checksum mismatch' <<<"$out"
reset_web; mkrelease v1.1.0 --latest linux-arm64
f="$dlroot/download/v1.1.0/lucifer-v1.1.0-linux-arm64.tar.gz.sha256"; echo "bad" > "$f"; expect_fail "archive checksum file malformed"
# RID inside the package disagrees with the asset name
reset_web; mkrelease v1.1.0 --latest linux-arm64
mkpkg v1.1.0 linux-arm64 'echo linux-arm > "$w/$n/RID"'; expect_fail "RID mismatch inside package"
# unsafe member (path traversal) with a matching checksum
reset_web; mkrelease v1.1.0 --latest linux-arm64
python3 -I - "$dlroot/download/v1.1.0" <<'PY'
import tarfile, io, sys, hashlib, os
d = sys.argv[1]; n = "lucifer-v1.1.0-linux-arm64"
with tarfile.open(os.path.join(d, n + ".tar.gz"), "w:gz") as t:
    i = tarfile.TarInfo(n + "/../../evil"); i.size = 1; t.addfile(i, io.BytesIO(b"x"))
h = hashlib.sha256(open(os.path.join(d, n + ".tar.gz"), "rb").read()).hexdigest()
open(os.path.join(d, n + ".tar.gz.sha256"), "w").write(f"{h}  {n}.tar.gz\n")
PY
expect_fail "unsafe archive member"
ok "failures leave the host untouched"

# ============ 4. installed update without an archive ==========================
reset_web; mkrelease v1.0.0 linux-arm64; mkrelease v1.1.0 --latest linux-arm64
fresh; piped --version v1.0.0; check "setup install v1.0.0" [ $rc = 0 ]
printf 'NAVIDROME_PASSWORD=SECRETPW\nLUCIFER_X=1\n' > "$R/etc/lucifer/lucifer.env"; echo '{"mine":1}' > "$R/var/lib/lucifer/catalogue.json"
envsum="$(sha256sum "$R/etc/lucifer/lucifer.env")"; catsum="$(sha256sum "$R/var/lib/lucifer/catalogue.json")"
inst="$(OPT)/current/bin/lucifer-install"
# inactive service stays inactive
run "$inst" update; check "no-archive update (inactive) exit 0" [ $rc = 0 ]
check "updated to latest" [ "$(cur)" = lucifer-v1.1.0-linux-arm64 ]
check "inactive service not started" [ ! -e "$STUB_STATE/active" ]
check "config preserved" [ "$envsum" = "$(sha256sum "$R/etc/lucifer/lucifer.env")" ]
check "catalogue preserved" [ "$catsum" = "$(sha256sum "$R/var/lib/lucifer/catalogue.json")" ]
check "previous release kept" [ -d "$(OPT)/releases/lucifer-v1.0.0-linux-arm64" ]
check "secret never printed" bash -c '! grep -q SECRETPW <<<"$1"' _ "$out"
run "$inst" update; check "repeat update is a no-op (0)" [ $rc = 0 ]; check "up-to-date message" grep -q 'already up to date' <<<"$out"
# explicit rollback
run "$inst" rollback; check "explicit rollback exit 0" [ $rc = 0 ]; check "rolled back to v1.0.0" [ "$(cur)" = lucifer-v1.0.0-linux-arm64 ]
check "rollback keeps newer release" [ -d "$(OPT)/releases/lucifer-v1.1.0-linux-arm64" ]
# active service is restarted; explicit --version
rm -rf "$(OPT)/releases/lucifer-v1.1.0-linux-arm64"; : > "$STUB_STATE/active"
run "$inst" update --version v1.1.0; check "active update exit 0" [ $rc = 0 ]
check "active service running after update" [ -e "$STUB_STATE/active" ]
check "update checked config as service" grep -q 'check-as-service' "$STUB_STATE/calls"
# failing release: rejected by the config check, host unchanged
mkrelease v1.2.0 --latest linux-arm64; mkpkg v1.2.0 linux-arm64 'touch "$w/$n/FAIL_CHECK"'
before="$(tree_hash)"
run "$inst" update; check "rejecting release exit 1" [ $rc = 1 ]
check "failed update: still v1.1.0" [ "$(cur)" = lucifer-v1.1.0-linux-arm64 ]
check "failed update: host equal to before" [ "$before" = "$(tree_hash)" ]
check "failed update: service active" [ -e "$STUB_STATE/active" ]
# failure during activation (service does not come up) restores the previous release
mkrelease v1.3.0 --latest linux-arm64
stub systemctl '
echo "systemctl $*" >> "$STUB_STATE/calls"
cur="$(basename "$(readlink "$LUCIFER_INSTALL_ROOT/opt/lucifer/current" 2>/dev/null)")"
case "$1" in
  is-active) [ -e "$STUB_STATE/active" ];;
  is-enabled) [ -e "$STUB_STATE/enabled" ];;
  stop) rm -f "$STUB_STATE/active";;
  enable) : > "$STUB_STATE/enabled";;
  disable) rm -f "$STUB_STATE/enabled";;
  start) [ "$cur" = lucifer-v1.3.0-linux-arm64 ] && exit 1; : > "$STUB_STATE/active";;
esac'
run "$inst" update; check "start failure exit 1" [ $rc = 1 ]
check "start failure: previous release restored" [ "$(cur)" = lucifer-v1.1.0-linux-arm64 ]
check "start failure: service active again" [ -e "$STUB_STATE/active" ]
check "start failure: no leftover release" [ ! -e "$(OPT)/releases/lucifer-v1.3.0-linux-arm64" ]
check "config still preserved" [ "$envsum" = "$(sha256sum "$R/etc/lucifer/lucifer.env")" ]
# offline archive mode is preserved
mkpkg v1.4.0 linux-arm64; a="$dlroot/download/v1.4.0/lucifer-v1.4.0-linux-arm64.tar.gz"
reset_web_net() { :; }
LUCIFER_GITHUB_API=http://127.0.0.1:9 run "$inst" update "$a"; check "offline archive update exit 0 (no network)" [ $rc = 0 ]
check "offline archive activated" [ "$(cur)" = lucifer-v1.4.0-linux-arm64 ]
ok "no-archive update, rollback, failure restoration, offline mode"

# ============ 5. release workflow and naming contract ==========================
wf="$repo_root/.github/workflows/release.yml"
check "workflow triggers on v* tags" grep -q "tags: \['v\*'\]" "$wf"
check "workflow publishes with contents: write" grep -q 'contents: write' "$wf"
check "workflow builds both RIDs with the tag as version" bash -c 'grep -q -- "--rid linux-arm64 --version \"\$TAG\"" "$1" && grep -q -- "--rid linux-arm --version \"\$TAG\"" "$1"' _ "$wf"
check "workflow attaches archives, checksums and installer" bash -c 'grep -q "dist/\*.tar.gz dist/\*.tar.gz.sha256 dist/lucifer-install dist/lucifer-install.sha256" "$1"' _ "$wf"
check "workflow runs this suite" grep -q 'deploy/test-lucifer-bootstrap.sh' "$wf"
check "workflow does not build from a floating ref" grep -q 'refs/tags/' "$wf"
# Names: publish.sh output == installer download expectation == bootstrap asset
check "publish.sh archive name template" grep -q 'name="lucifer-$version-$rid"' "$here/publish.sh"
check "installer expects lucifer-<tag>-<rid>.tar.gz" grep -q 'name="lucifer-$tag-$rid.tar.gz"' "$installer"
check "bootstrap fetches lucifer-install(+.sha256)" bash -c 'grep -q "^ *get lucifer-install$" "$1" && grep -q "^ *get lucifer-install.sha256$" "$1"' _ "$bootstrap"
check "workflow installer asset is the packaged script" grep -q 'cp deploy/lucifer-install dist/lucifer-install' "$wf"
check "publish.sh packages the same script" grep -q 'cp deploy/lucifer-install "$stage/bin/lucifer-install"' "$here/publish.sh"
ok "workflow contract (static; the workflow itself has not been run)"


# ============ 6. a truncated pipe never runs with options dropped (B1) =========
reset_web; mkrelease v1.0.0 linux-arm64; mkrelease v1.1.0 --latest linux-arm64
size="$(stat -c %s "$bootstrap")"
plainlog="$tmp/plain.port.log"; : > "$plainlog"
cuts=()
for ((c = 0; c < size - 1; c += 5)); do cuts+=("$c"); done
for ((c = size - 160; c < size - 1; c++)); do cuts+=("$c"); done            # around the final invocation
while IFS= read -r c; do cuts+=("$c"); done < <(head -c "$size" "$bootstrap" | awk '{n += length($0) + 1; print n}')   # every line boundary
last_start="$(( size - $(tail -n 1 "$bootstrap" | wc -c) ))"
bad=0; badrc=0; n=0
for c in "${cuts[@]}"; do
  [ "$c" -ge 0 ] && [ "$c" -lt $((size - 1)) ] || continue
  n=$((n + 1)); fresh
  out="$(head -c "$c" "$bootstrap" | bash -s -- --version v1.0.0 --target-rid linux-arm64 2>&1)"; rc=$?
  { [ ! -e "$R/opt" ] && [ ! -e "$R/etc" ] && [ ! -e "$R/var" ] && [ ! -s "$STUB_STATE/calls" ] && [ -z "$(ls -A "$TMPDIR")" ]; } || { bad=$((bad + 1)); echo "  cut at $c changed the host" >&2; }
  if [ "$c" -gt "$last_start" ] && [ "$rc" = 0 ]; then badrc=$((badrc + 1)); echo "  cut inside the final invocation (at $c) exited 0" >&2; fi
done
check "no truncation of the piped script ($n cut points) touched the host" [ "$bad" = 0 ]
check "no cut inside the final invocation reports success" [ "$badrc" = 0 ]
check "no truncated script contacted the network" [ ! -s "$plainlog" ]
fresh; out="$(head -c "$((size - 1))" "$bootstrap" | bash -s -- --version v1.0.0 2>&1)"; rc=$?
check "complete script (only the final newline missing) with options installs the requested tag" bash -c '[ "$1" = 0 ] && [ "$(basename "$(readlink "$2/opt/lucifer/current")")" = lucifer-v1.0.0-linux-arm64 ]' _ "$rc" "$R"
fresh; piped --version v1.0.0; check "complete stdin run keeps the requested version" [ "$(cur)" = lucifer-v1.0.0-linux-arm64 ]
piped --help; check "complete stdin --help still works" [ $rc = 0 ]
ok "truncated pipes ($n cut points)"

# ============ 7. HTTPS-only across redirects (B2) ===============================
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$tmp/tls.pem" -out "$tmp/tls.crt" -days 2 -subj /CN=localhost \
  -addext subjectAltName=DNS:localhost,IP:127.0.0.1 >/dev/null 2>&1 && cat "$tmp/tls.crt" >> "$tmp/tls.pem" || fail "could not create the test certificate"
export CURL_CA_BUNDLE="$tmp/tls.crt"
start_server tlsok "$web" "$tmp/tls.pem"          # real content over TLS
mkdir -p "$tmp/tlsweb"; start_server tlsredir "$tmp/tlsweb" "$tmp/tls.pem"   # only redirects
okport="$(cat "$tmp/tlsok.port")"; redirport="$(cat "$tmp/tlsredir.port")"
redirect_all() { # target base URL: every release file redirects there
  rm -rf "$tmp/tlsweb"; mkdir -p "$tmp/tlsweb"
  local f rel
  while IFS= read -r f; do
    rel="${f#"$web"/}"; mkdir -p "$tmp/tlsweb/$(dirname "$rel")"; echo "$1/$rel" > "$tmp/tlsweb/$rel.redirect"
  done < <(find "$web" -type f ! -name '*.status' ! -name '*.truncate')
}
reset_web; mkrelease v1.1.0 --latest linux-arm64
tlsok_api="https://localhost:$okport/api"; tlsok_dl="https://localhost:$okport"; redir_dl="https://localhost:$redirport"
prod() { LUCIFER_INSTALL_TEST_INSECURE= "$@"; }
no_plain_requests() { [ ! -s "$plainlog" ]; }

# valid HTTPS (direct, and HTTPS -> HTTPS redirects) still installs
fresh; LUCIFER_INSTALL_TEST_INSECURE= LUCIFER_GITHUB_API="$tlsok_api" LUCIFER_GITHUB_DOWNLOAD="$tlsok_dl" piped
check "production HTTPS install exit 0" [ $rc = 0 ]; check "production HTTPS install activated" [ "$(cur)" = lucifer-v1.1.0-linux-arm64 ]
redirect_all "$tlsok_dl"
fresh; LUCIFER_INSTALL_TEST_INSECURE= LUCIFER_GITHUB_API="$tlsok_api" LUCIFER_GITHUB_DOWNLOAD="$redir_dl" piped
check "HTTPS->HTTPS redirects followed (exit 0)" [ $rc = 0 ]; check "HTTPS->HTTPS redirect install activated" [ "$(cur)" = lucifer-v1.1.0-linux-arm64 ]

# redirect to plain http is refused by both entry points before any host change
: > "$plainlog"
redirect_all "http://127.0.0.1:$port"
fresh; LUCIFER_INSTALL_TEST_INSECURE= LUCIFER_GITHUB_API="$tlsok_api" LUCIFER_GITHUB_DOWNLOAD="$redir_dl" piped
check "bootstrap: TLS->HTTP redirect refused (exit 1)" [ $rc = 1 ]; no_host_effects "bootstrap downgrade"
check "bootstrap: the http target was never requested" no_plain_requests
check "bootstrap: no temp left" bash -c '[ -z "$(ls -A "$1")" ]' _ "$TMPDIR"
# backend: API metadata redirected to http
fresh; LUCIFER_INSTALL_TEST_INSECURE= LUCIFER_GITHUB_API="https://localhost:$redirport/api" LUCIFER_GITHUB_DOWNLOAD="$tlsok_dl" run "$installer" install
check "backend: API TLS->HTTP redirect refused (exit 1)" [ $rc = 1 ]; no_host_effects "backend api downgrade"
check "backend: API http target never requested" no_plain_requests
# backend: archive download redirected to http (metadata over valid HTTPS)
rm -rf "$tmp/tlsweb"; mkdir -p "$tmp/tlsweb/test/lucifer/releases/download/v1.1.0"
echo "http://127.0.0.1:$port/test/lucifer/releases/download/v1.1.0/lucifer-v1.1.0-linux-arm64.tar.gz" > "$tmp/tlsweb/test/lucifer/releases/download/v1.1.0/lucifer-v1.1.0-linux-arm64.tar.gz.redirect"
fresh; LUCIFER_INSTALL_TEST_INSECURE= LUCIFER_GITHUB_API="$tlsok_api" LUCIFER_GITHUB_DOWNLOAD="$redir_dl" run "$installer" install
check "backend: archive TLS->HTTP redirect refused (exit 1)" [ $rc = 1 ]; no_host_effects "backend archive downgrade"
check "backend: archive http target never requested" no_plain_requests
# the explicit test hook still permits http end to end (existing suites rely on it)
fresh; piped; check "test hook still allows the plain-http fixture" [ $rc = 0 ]
ok "HTTPS-only across redirects"

[ "$fails" = 0 ] && { echo "lucifer-bootstrap: ok"; exit 0; }
echo "lucifer-bootstrap: $fails check(s) failed" >&2; exit 1
