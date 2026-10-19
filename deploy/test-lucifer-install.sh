#!/usr/bin/env bash
# Isolated acceptance test of lucifer-install. Every host path lives under a
# temporary LUCIFER_INSTALL_ROOT and every host command (id, getent, useradd,
# usermod, chown, systemctl, systemd-tmpfiles, runuser, dpkg) is a stub that
# logs to a temporary state directory, so the host's accounts, units and
# installation are never touched. Stubs are not physical evidence.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
installer="$here/lucifer-install"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
fails=0
fail() { echo "FAIL: $*" >&2; fails=$((fails + 1)); }
ok() { echo "ok: $*"; }
check() { local d="$1"; shift; if "$@"; then :; else fail "$d"; fi; }

stubs="$tmp/stubs"; mkdir -p "$stubs"
stub() { printf '#!/usr/bin/env bash\n%s\n' "$2" > "$stubs/$1"; chmod +x "$stubs/$1"; }
stub id 'case "$*" in "-u") echo "${STUB_UID:-0}";; "-nG lucifer") cat "$STUB_STATE/groups" 2>/dev/null; echo;; *) exit 1;; esac'
stub getent 'case "$1 $2" in "passwd lucifer") [ -e "$STUB_STATE/user" ];; "group "*) exit 0;; *) exit 2;; esac'
stub useradd 'echo "useradd $*" >> "$STUB_STATE/calls"; : > "$STUB_STATE/user"'
stub usermod 'echo "usermod $*" >> "$STUB_STATE/calls"; echo "$2 $3 $4" >> "$STUB_STATE/groups"'
stub chown 'echo "chown $*" >> "$STUB_STATE/owners"'
stub dpkg '[ -n "${STUB_SWAP_TARGET:-}" ] && cp "$STUB_SWAP_FROM" "$STUB_SWAP_TARGET" && cp "$STUB_SWAP_FROM.sha256" "$STUB_SWAP_TARGET.sha256"
[ "$1" = "--print-architecture" ] && echo "${STUB_ARCH:-arm64}"'
stub systemd-tmpfiles 'echo "tmpfiles $*" >> "$STUB_STATE/calls"'
stub runuser 'shift 3; echo "check-as-service $(basename "$(dirname "$(dirname "$3")")")" >> "$STUB_STATE/calls"; exec "$@"'
stub systemctl '
echo "systemctl $*" >> "$STUB_STATE/calls"
cur="$(basename "$(readlink "$LUCIFER_INSTALL_ROOT/opt/lucifer/current" 2>/dev/null)")"
case "$1" in
  is-active) [ -e "$STUB_STATE/active" ];;
  is-enabled) [ -e "$STUB_STATE/enabled" ];;
  stop) rm -f "$STUB_STATE/active";;
  enable) : > "$STUB_STATE/enabled";;
  disable) rm -f "$STUB_STATE/enabled";;
  reset-failed) :;;
  start) [ -e "$STUB_STATE/hang-start-armed" ] && { rm -f "$STUB_STATE/hang-start-armed"; echo $$ > "$STUB_STATE/hang-pid"; exec sleep 30; }
         [ "$cur" = "${STUB_FAIL_START_FOR:-}" ] && exit 1
         [ "$cur" = "${STUB_NOACTIVE_FOR:-}" ] && exit 0
         : > "$STUB_STATE/active";;
  daemon-reload) [ -e "$STUB_STATE/hang-armed" ] && { rm -f "$STUB_STATE/hang-armed"; echo $$ > "$STUB_STATE/hang-pid"; exec sleep 30; }
                 [ -n "${STUB_FAIL_RELOAD:-}" ] && [ -e "$STUB_STATE/reload-armed" ] && { rm -f "$STUB_STATE/reload-armed"; exit 1; }; exit 0;;
esac'
export PATH="$stubs:$PATH"
export LUCIFER_INSTALL_SETTLE_SECONDS=0
mkdir -p "$tmp/tmpdir"; export TMPDIR="$tmp/tmpdir"

# -- fixtures ---------------------------------------------------------------
# mkpkg VERSION RID [extra-flags]: builds dist/lucifer-VERSION-RID.tar.gz(+.sha256)
dist="$tmp/dist"; mkdir -p "$dist"
mkpkg() {
  local v="$1" r="$2" n="lucifer-$1-$2" w; w="$(mktemp -d -p "$tmp")"
  mkdir -p "$w/$n"/{bin,config,systemd,udev}
  printf '#!/bin/sh\necho amp\n' > "$w/$n/bin/Amp.Nfc"
  cat > "$w/$n/bin/lucifer-maint" <<'M'
#!/usr/bin/env bash
d="$(cd "$(dirname "$0")/.." && pwd)"
echo "maint $* env=$LUCIFER_ENV_FILE rel=$(basename "$d")" >> "$STUB_STATE/calls"
[ -e "$d/FAIL_CHECK" ] && { echo "bad config" >&2; exit 2; }
grep -q "$(printf '\r')" "$LUCIFER_ENV_FILE" 2>/dev/null && { echo "carriage return" >&2; exit 2; }
exit 0
M
  cp "$installer" "$w/$n/bin/lucifer-install"
  printf 'NAVIDROME_URL=https://navidrome.local:4533\nNAVIDROME_USER=lucifer\nNAVIDROME_PASSWORD=change-me\n' > "$w/$n/config/lucifer.env.example"
  echo '{"example":true}' > "$w/$n/config/catalogue.example.json"
  echo '{"keys":[]}' > "$w/$n/config/input-map.example.json"
  printf '[Service]\n# unit from %s\nExecStart=/opt/lucifer/current/bin/Amp.Nfc play\n' "$v" > "$w/$n/systemd/lucifer.service"
  printf '# tmpfiles from %s\nd /run/lucifer 0750 lucifer lucifer -\n' "$v" > "$w/$n/systemd/lucifer.tmpfiles.conf"
  echo "$v" > "$w/$n/VERSION"; echo "$r" > "$w/$n/RID"
  chmod 0755 "$w/$n/bin/"*
  [ -z "${3:-}" ] || eval "$3"
  tar --sort=name --owner=0 --group=0 --numeric-owner -C "$w" -cf - "$n" | gzip -n > "$dist/$n.tar.gz"
  (cd "$dist" && sha256sum "$n.tar.gz" > "$n.tar.gz.sha256")
  rm -rf "$w"
}
pkgpath() { echo "$dist/lucifer-$1-$2.tar.gz"; }
mkpkg 1.0.0 linux-arm64
mkpkg 1.1.0 linux-arm64
mkpkg 1.2.0 linux-arm64
mkpkg 1.0.0 linux-arm
mkpkg badcheck-1 linux-arm64 'touch "$w/$n/FAIL_CHECK"'
mkpkg badstart-1 linux-arm64

fresh() { # new isolated host
  R="$tmp/host.$RANDOM$RANDOM"; S="$R.state"; mkdir -p "$R" "$S"
  export LUCIFER_INSTALL_ROOT="$R" STUB_STATE="$S"
  unset STUB_NOACTIVE_FOR STUB_FAIL_START_FOR STUB_FAIL_RELOAD STUB_UID STUB_ARCH STUB_SWAP_TARGET STUB_SWAP_FROM
  : > "$S/calls"; : > "$S/owners"
}
run() { out="$("$installer" "$@" 2>&1)"; rc=$?; [ -z "${DBG:-}" ] || echo "[$rc] $out" >&2; }
calls() { cat "$STUB_STATE/calls"; }
tree_hash() { (cd "$R" && find . -mindepth 1 -printf '%p %l %m\n' -type f -exec sha256sum {} + | sort | sha256sum); }
OPT() { echo "$R/opt/lucifer"; }
cur() { basename "$(readlink "$(OPT)/current")"; }
setcfg() { printf 'NAVIDROME_PASSWORD=SECRETPW\nLUCIFER_X=1\n' > "$R/etc/lucifer/lucifer.env"; }
no_host_effects() { # $1 label
  [ ! -e "$R/opt" ] && [ ! -e "$R/etc" ] && [ ! -e "$R/var" ] && [ ! -s "$STUB_STATE/calls" ] \
    || fail "$1: host effects before rejection"
}

# -- syntax, help, arguments ---------------------------------------------------
bash -n "$installer" && ok "bash -n" || fail "bash -n"
fresh
run --help; check "help exit" [ $rc = 0 ]; check "help text" grep -q 'lucifer-install update' <<<"$out"
run; check "no args exit 2" [ $rc = 2 ]
run frob; check "unknown command exit 2" [ $rc = 2 ]
run install; check "install without archive exit 2" [ $rc = 2 ]
run install --bogus x; check "unknown option exit 2" [ $rc = 2 ]
run install a b; check "two archives exit 2" [ $rc = 2 ]
run install "$tmp/none.tar.gz"; check "missing archive exit 2" [ $rc = 2 ]
run install --target-rid linux-x86 "$(pkgpath 1.0.0 linux-arm64)"; check "bad target rid exit 2" [ $rc = 2 ]
run install --start; check "install --start without archive exit 2" [ $rc = 2 ]
run update --start "$(pkgpath 1.0.0 linux-arm64)"; check "update --start exit 2" [ $rc = 2 ]
no_host_effects "arguments"; ok "help and invalid arguments"

# -- rejection before any host effect ---------------------------------------------
fresh
p="$(pkgpath 1.0.0 linux-arm64)"
mkdir -p "$tmp/c"; cp "$p" "$tmp/c/$(basename "$p")"; c="$tmp/c/$(basename "$p")"; echo "$(printf 0%.0s {1..64})  $(basename "$p")" > "$c.sha256"
run install "$c"; check "checksum mismatch rejected" [ $rc = 1 ]; no_host_effects "checksum mismatch"
check "mismatch message" grep -q 'checksum mismatch' <<<"$out"
rm "$c.sha256"; run install "$c"; check "missing checksum rejected" [ $rc = 1 ]; no_host_effects "missing checksum"
sed "s/lucifer-1.0.0-linux-arm64/other/" "$p.sha256" > "$tmp/wrongname.sha256"; run install --checksum "$tmp/wrongname.sha256" "$c"
check "checksum for other file rejected" [ $rc = 1 ]; no_host_effects "checksum name"
run install "$(pkgpath 1.0.0 linux-arm)"; check "wrong RID rejected" [ $rc = 1 ]; no_host_effects "wrong rid"
check "wrong RID message" grep -q 'userland' <<<"$out"
STUB_ARCH=armhf run install "$p"; check "armhf host rejects arm64" [ $rc = 1 ]; no_host_effects "armhf"
STUB_ARCH=riscv run install "$p"; check "unknown arch rejected" [ $rc = 1 ]; no_host_effects "unknown arch"
STUB_ARCH=armhf run install "$(pkgpath 1.0.0 linux-arm)"; check "armhf host accepts arm (reaches root check)" [ $rc != 2 ]

# malformed and unsafe archives: valid checksum, bad content
badpkg() { # name python-body operating on tarfile object t and os
  local f="$dist/$1"; python3 -I - "$f" "$2" <<'PY'
import sys, tarfile, io
path, kind = sys.argv[1], sys.argv[2]
top = "lucifer-9.9.9-linux-arm64"
t = tarfile.open(path, "w:gz")
def add(name, data=b"x", mode=0o755, typ=tarfile.REGTYPE, link=""):
    i = tarfile.TarInfo(name); i.mode = mode; i.type = typ; i.linkname = link
    i.size = len(data) if typ == tarfile.REGTYPE else 0
    t.addfile(i, io.BytesIO(data) if typ == tarfile.REGTYPE else None)
good = {"VERSION": b"9.9.9\n", "RID": b"linux-arm64\n", "bin/Amp.Nfc": b"x", "bin/lucifer-maint": b"x",
        "bin/lucifer-install": b"x", "config/lucifer.env.example": b"x", "config/catalogue.example.json": b"x",
        "systemd/lucifer.service": b"x", "systemd/lucifer.tmpfiles.conf": b"x"}
if kind != "nomember":
    for k, v in good.items(): add(f"{top}/{k}", v)
if kind == "traversal": add(f"{top}/../../evil", b"x")
if kind == "absolute": add("/etc/evil", b"x")
if kind == "outside": add("other/file", b"x")
if kind == "symlink": add(f"{top}/bin/link", typ=tarfile.SYMTYPE, link="/etc/passwd")
if kind == "hardlink": add(f"{top}/bin/hl", typ=tarfile.LNKTYPE, link=f"{top}/VERSION")
if kind == "device": add(f"{top}/dev0", typ=tarfile.CHRTYPE)
if kind == "space": add(f"{top}/bin/a b", b"x")
if kind == "badversion":
    pass
if kind == "nomember": add(f"{top}/VERSION", b"9.9.9\n")
t.close()
PY
  (cd "$dist" && sha256sum "$1" > "$1.sha256")
}
for k in traversal absolute outside symlink hardlink device space nomember; do
  badpkg "lucifer-9.9.9-linux-arm64.tar.gz" "$k"
  fresh; run install "$dist/lucifer-9.9.9-linux-arm64.tar.gz"
  check "unsafe archive ($k) rejected" [ $rc = 1 ]; no_host_effects "unsafe archive $k"
  [ -e /etc/evil ] && fail "extraction escaped ($k)"
done
printf 'not a tar' | gzip > "$dist/lucifer-9.9.8-linux-arm64.tar.gz"
(cd "$dist" && sha256sum lucifer-9.9.8-linux-arm64.tar.gz > lucifer-9.9.8-linux-arm64.tar.gz.sha256)
fresh; run install "$dist/lucifer-9.9.8-linux-arm64.tar.gz"; check "malformed archive rejected" [ $rc = 1 ]; no_host_effects "malformed"
# VERSION file disagreeing with the name
mkpkg 2.0.0 linux-arm64 'echo 3.0.0 > "$w/$n/VERSION"'
fresh; run install "$(pkgpath 2.0.0 linux-arm64)"; check "VERSION mismatch rejected" [ $rc = 1 ]; no_host_effects "version mismatch"
fresh; STUB_UID=1000 run install "$p"; check "non-root refused" [ $rc = 1 ]; no_host_effects "non-root"
ok "rejections happen before host effects"

# -- first install ------------------------------------------------------------------
fresh; run install "$p"
check "first install exit 0" [ $rc = 0 ]
check "current points at release" [ "$(cur)" = lucifer-1.0.0-linux-arm64 ]
check "release has entry points" [ -x "$(OPT)/current/bin/lucifer-install" ] 
check "unit installed from release" cmp -s "$R/etc/systemd/system/lucifer.service" "$(OPT)/current/systemd/lucifer.service"
check "tmpfiles installed" cmp -s "$R/etc/tmpfiles.d/lucifer.conf" "$(OPT)/current/systemd/lucifer.tmpfiles.conf"
check "env created 0640" [ "$(stat -c %a "$R/etc/lucifer/lucifer.env")" = 640 ]
check "catalogue created 0640" [ "$(stat -c %a "$R/var/lib/lucifer/catalogue.json")" = 640 ]
check "etc dir 0750" [ "$(stat -c %a "$R/etc/lucifer")" = 750 ]
check "env group lucifer, root owner" grep -q 'chown root:lucifer .*lucifer.env' "$STUB_STATE/owners"
check "release owned by root" grep -q 'chown -R root:root' "$STUB_STATE/owners"
check "account created" grep -q '^useradd --system' "$STUB_STATE/calls"
check "groups added" grep -q 'usermod -aG dialout' "$STUB_STATE/calls"
check "link in sbin" [ -L "$R/usr/local/sbin/lucifer-install" ]
check "service not started" bash -c '! grep -E "systemctl (start|enable)" "$1"' _ "$STUB_STATE/calls"
check "configuration instructions" grep -q 'NEXT: edit' <<<"$out"
check "no stage leftovers" bash -c '[ -z "$(ls -A "$1"/opt/lucifer/releases | grep "^\\.")" ]' _ "$R"
ok "first install"

# repeat install
before="$(tree_hash)"; : > "$STUB_STATE/calls"
run install "$p"; check "repeat install refused (3)" [ $rc = 3 ]
check "repeat install changes nothing" [ "$before" = "$(tree_hash)" ]
check "repeat install no systemctl" bash -c '! grep -q systemctl "$1"' _ "$STUB_STATE/calls"
# idempotent account: second host effect with existing user and groups
fresh; : > "$STUB_STATE/user"; echo "-aG dialout lucifer" > "$STUB_STATE/groups"; echo "-aG audio lucifer" >> "$STUB_STATE/groups"; echo "-aG input lucifer" >> "$STUB_STATE/groups"
stub id 'case "$*" in "-u") echo "${STUB_UID:-0}";; "-nG lucifer") echo "lucifer dialout audio input";; *) exit 1;; esac'
run install "$p"; check "install with existing account" [ $rc = 0 ]
check "no duplicate account/group changes" bash -c '! grep -Eq "^(useradd|usermod)" "$1"' _ "$STUB_STATE/calls"
stub id 'case "$*" in "-u") echo "${STUB_UID:-0}";; "-nG lucifer") cat "$STUB_STATE/groups" 2>/dev/null; echo;; *) exit 1;; esac'
ok "repeat install and idempotent account"

# existing configuration and manual leftovers are preserved
fresh; mkdir -p "$R/etc/lucifer" "$R/var/lib/lucifer"; setcfg; chmod 0644 "$R/etc/lucifer/lucifer.env"
echo '{"mine":1}' > "$R/var/lib/lucifer/catalogue.json"; echo '{"keys":[{"code":1}]}' > "$R/var/lib/lucifer/input-map.json"
cfg0="$(sha256sum "$R/etc/lucifer/lucifer.env" "$R/var/lib/lucifer/catalogue.json" "$R/var/lib/lucifer/input-map.json")"
run install "$p"; check "install over existing config" [ $rc = 0 ]
check "config bytes preserved" [ "$cfg0" = "$(sha256sum "$R/etc/lucifer/lucifer.env" "$R/var/lib/lucifer/catalogue.json" "$R/var/lib/lucifer/input-map.json")" ]
check "loose env mode tightened" [ "$(stat -c %a "$R/etc/lucifer/lucifer.env")" = 640 ]
check "real config: not started without --start" bash -c '! grep -E "systemctl (start|enable)" "$1"' _ "$STUB_STATE/calls"
check "secret not printed" bash -c '! grep -q SECRETPW <<<"$1"' _ "$out"
fresh; run install --start "$p"; check "--start refused with placeholder" [ $rc = 3 ]
check "--start refusal leaves no current" [ ! -e "$(OPT)/current" ]
fresh; mkdir -p "$R/etc/lucifer"; setcfg
run install --start "$p"; check "--start with real config" [ $rc = 0 ]
check "enabled and started" bash -c 'grep -q "systemctl enable" "$1" && grep -q "systemctl start" "$1"' _ "$STUB_STATE/calls"
# manual unit differing: refused, then adopted with backup
fresh; mkdir -p "$R/etc/systemd/system"; echo 'hand made' > "$R/etc/systemd/system/lucifer.service"
run install "$p"; check "manual unit refused (3)" [ $rc = 3 ]
check "manual unit untouched" [ "$(cat "$R/etc/systemd/system/lucifer.service")" = 'hand made' ]
check "refusal leaves no current" [ ! -e "$(OPT)/current" ]
run install --adopt "$p"; check "manual unit adopted" [ $rc = 0 ]
check "adopt kept backup" bash -c 'grep -q "hand made" "$1"/opt/lucifer/backups/*' _ "$R"
# manual installation directory refused
fresh; mkdir -p "$R/opt/lucifer/current/bin"; run install "$p"; check "manual current dir refused" [ $rc = 3 ]
ok "existing configuration and manual installs"

# -- update -------------------------------------------------------------------------
new_install() { fresh; run install "$p" >/dev/null; setcfg; : > "$STUB_STATE/active"; : > "$STUB_STATE/enabled"; : > "$STUB_STATE/calls"; : > "$STUB_STATE/owners"; }
new_install
cfg0="$(sha256sum "$R/etc/lucifer/lucifer.env" "$R/var/lib/lucifer/catalogue.json")"
run update "$(pkgpath 1.1.0 linux-arm64)"
check "update exit 0" [ $rc = 0 ]
check "current is 1.1.0" [ "$(cur)" = lucifer-1.1.0-linux-arm64 ]
check "previous is 1.0.0" [ "$(basename "$(readlink "$(OPT)/previous")")" = lucifer-1.0.0-linux-arm64 ]
check "old release kept" [ -d "$(OPT)/releases/lucifer-1.0.0-linux-arm64" ]
check "unit refreshed" grep -q 'unit from 1.1.0' "$R/etc/systemd/system/lucifer.service"
check "tmpfiles refreshed" grep -q 'tmpfiles from 1.1.0' "$R/etc/tmpfiles.d/lucifer.conf"
check "config preserved" [ "$cfg0" = "$(sha256sum "$R/etc/lucifer/lucifer.env" "$R/var/lib/lucifer/catalogue.json")" ]
check "service active again" [ -e "$STUB_STATE/active" ]
check "stage check, stop, reload, start in that order" bash -c '
  first="$(grep -n "^maint check" "$1" | head -1 | cut -d: -f1)"; stop="$(grep -n "^systemctl stop" "$1" | head -1 | cut -d: -f1)"
  reload="$(grep -n "^systemctl daemon-reload" "$1" | head -1 | cut -d: -f1)"; start="$(grep -n "^systemctl start" "$1" | head -1 | cut -d: -f1)"
  [ "$first" -lt "$stop" ] && [ "$stop" -lt "$reload" ] && [ "$reload" -lt "$start" ]' _ "$STUB_STATE/calls"
check "stage check ran the new release against the live env file" grep -q "^maint check env=$R/etc/lucifer/lucifer.env rel=lucifer-1.1.0-linux-arm64" "$STUB_STATE/calls"
run update "$(pkgpath 1.1.0 linux-arm64)"; check "same version refused (3)" [ $rc = 3 ]
check "no-secret output" bash -c '! grep -q SECRETPW <<<"$1"' _ "$out"
ok "update success"

# inactive service stays inactive
new_install; rm -f "$STUB_STATE/active"
run update "$(pkgpath 1.1.0 linux-arm64)"; check "inactive update exit 0" [ $rc = 0 ]
check "inactive update switched" [ "$(cur)" = lucifer-1.1.0-linux-arm64 ]
check "inactive update: no stop/start" bash -c '! grep -E "systemctl (stop|start)" "$1"' _ "$STUB_STATE/calls"
check "still inactive" [ ! -e "$STUB_STATE/active" ]
ok "inactive service handling"

# stage check failure: playback untouched
new_install; before="$(tree_hash)"
run update "$(pkgpath badcheck-1 linux-arm64)"; check "bad staged release rejected" [ $rc = 1 ]
check "bad stage: not stopped" bash -c '! grep -E "systemctl (stop|daemon-reload|start)" "$1"' _ "$STUB_STATE/calls"
check "bad stage: tree unchanged" [ "$before" = "$(tree_hash)" ]
check "bad stage: still active" [ -e "$STUB_STATE/active" ]
ok "failed pre-stop check"

# activation failure restores release, unit and support files
new_install; before="$(tree_hash)"; unit0="$(sha256sum "$R/etc/systemd/system/lucifer.service" "$R/etc/tmpfiles.d/lucifer.conf")"
STUB_FAIL_START_FOR=lucifer-badstart-1-linux-arm64 run update "$(pkgpath badstart-1 linux-arm64)"
check "failed activation exit 1" [ $rc = 1 ]
check "previous release current again" [ "$(cur)" = lucifer-1.0.0-linux-arm64 ]
check "unit and tmpfiles restored exactly" [ "$unit0" = "$(sha256sum "$R/etc/systemd/system/lucifer.service" "$R/etc/tmpfiles.d/lucifer.conf")" ]
check "previous service state restored (active)" [ -e "$STUB_STATE/active" ]
check "failed release removed" [ ! -e "$(OPT)/releases/lucifer-badstart-1-linux-arm64" ]
check "no previous link invented" [ ! -e "$(OPT)/previous" ]
check "tree identical after rollback of failed update" [ "$before" = "$(tree_hash)" ]
check "daemon-reload after restore" bash -c '[ "$(grep -c "daemon-reload" "$1")" -ge 2 ]' _ "$STUB_STATE/calls"
# failure in daemon-reload midway
new_install; before="$(tree_hash)"; : > "$STUB_STATE/reload-armed"
STUB_FAIL_RELOAD=1 run update "$(pkgpath 1.1.0 linux-arm64)"
check "reload failure exit 1" [ $rc = 1 ]; check "reload failure: tree restored" [ "$before" = "$(tree_hash)" ]
check "reload failure: service active" [ -e "$STUB_STATE/active" ]
# first-install failure leaves nothing activated
fresh; STUB_FAIL_RELOAD=1; : > "$STUB_STATE/reload-armed"; export STUB_FAIL_RELOAD
run install "$p"; unset STUB_FAIL_RELOAD
check "install failure exit 1" [ $rc = 1 ]
check "install failure: no current/unit/tmpfiles" bash -c '[ ! -e "$1/opt/lucifer/current" ] && [ ! -e "$1/etc/systemd/system/lucifer.service" ] && [ ! -e "$1/etc/tmpfiles.d/lucifer.conf" ]' _ "$R"
ok "activation failure restores prior state"

# update refuses hand-edited unit, adopts with backup
new_install; echo 'edited' >> "$R/etc/systemd/system/lucifer.service"
run update "$(pkgpath 1.1.0 linux-arm64)"; check "edited unit refused (3)" [ $rc = 3 ]
check "edited unit: current unchanged" [ "$(cur)" = lucifer-1.0.0-linux-arm64 ]
check "edited unit: not stopped" bash -c '! grep -q "systemctl stop" "$1"' _ "$STUB_STATE/calls"
run update --adopt "$(pkgpath 1.1.0 linux-arm64)"; check "edited unit adopted" [ $rc = 0 ]
check "backup holds the edit" bash -c 'grep -q edited "$1"/opt/lucifer/backups/lucifer.service.*' _ "$R"
# update without installation
fresh; run update "$p"; check "update without installation refused (3)" [ $rc = 3 ]
check "update w/o install: no host dirs" [ ! -e "$R/etc" ]
ok "update guards"

# -- rollback and list -----------------------------------------------------------------
new_install; run update "$(pkgpath 1.1.0 linux-arm64)" >/dev/null
cfg0="$(sha256sum "$R/etc/lucifer/lucifer.env" "$R/var/lib/lucifer/catalogue.json")"
run list; check "list exit 0" [ $rc = 0 ]; check "list marks current" grep -q '^\* lucifer-1.1.0-linux-arm64' <<<"$out"
check "list shows old" grep -q '^  lucifer-1.0.0-linux-arm64' <<<"$out"
run rollback; check "rollback exit 0" [ $rc = 0 ]
check "rolled back to 1.0.0" [ "$(cur)" = lucifer-1.0.0-linux-arm64 ]
check "rollback restores that release's unit" grep -q 'unit from 1.0.0' "$R/etc/systemd/system/lucifer.service"
check "rollback keeps config" [ "$cfg0" = "$(sha256sum "$R/etc/lucifer/lucifer.env" "$R/var/lib/lucifer/catalogue.json")" ]
check "rollback keeps newer release" [ -d "$(OPT)/releases/lucifer-1.1.0-linux-arm64" ]
check "rollback leaves service active" [ -e "$STUB_STATE/active" ]
run rollback; check "rollback again returns to 1.1.0" [ "$(cur)" = lucifer-1.1.0-linux-arm64 ]
run rollback lucifer-1.0.0-linux-arm64; check "named rollback" [ "$(cur)" = lucifer-1.0.0-linux-arm64 ]
run rollback lucifer-1.0.0-linux-arm64; check "rollback to current refused (3)" [ $rc = 3 ]
run rollback lucifer-9-linux-arm64; check "unknown release refused (3)" [ $rc = 3 ]
run rollback ../etc; check "bad release name refused (2)" [ $rc = 2 ]
# rollback failure restores
before="$(tree_hash)"; STUB_FAIL_START_FOR=lucifer-1.1.0-linux-arm64 run rollback lucifer-1.1.0-linux-arm64
check "failed rollback exit 1" [ $rc = 1 ]; check "failed rollback restores" [ "$before" = "$(tree_hash)" ]
check "failed rollback service active" [ -e "$STUB_STATE/active" ]
fresh; run rollback; check "rollback without installation refused (3)" [ $rc = 3 ]
new_install; run rollback; check "rollback without previous refused (3)" [ $rc = 3 ]
ok "rollback and list"


# ============ fixes v2 regressions (F1-F4) ============
tmpdir_clean() { [ -z "$(ls -A "$TMPDIR")" ] || fail "$1: private snapshot left in TMPDIR"; }

# F1: rollback protects hand-edited unit and tmpfiles
new_install; run update "$(pkgpath 1.1.0 linux-arm64)" >/dev/null
echo '# operator edit' >> "$R/etc/systemd/system/lucifer.service"
: > "$STUB_STATE/calls"; before="$(tree_hash)"
run rollback; check "F1 rollback over edited unit refused (3)" [ $rc = 3 ]
check "F1 refusal names --adopt" grep -q -- '--adopt' <<<"$out"
check "F1 refusal leaves everything unchanged" [ "$before" = "$(tree_hash)" ]
check "F1 refusal does not stop the service" bash -c '! grep -q "systemctl stop" "$1"' _ "$STUB_STATE/calls"
run rollback --adopt; check "F1 rollback --adopt exit 0" [ $rc = 0 ]
check "F1 adopted: rolled back" [ "$(cur)" = lucifer-1.0.0-linux-arm64 ]
check "F1 adopted: original kept in backups" bash -c 'grep -q "operator edit" "$1"/opt/lucifer/backups/lucifer.service.*' _ "$R"
echo '# tmpfiles edit' >> "$R/etc/tmpfiles.d/lucifer.conf"
run rollback; check "F1 edited tmpfiles refused (3)" [ $rc = 3 ]
check "F1 help documents rollback --adopt" bash -c '"$1" --help | grep -q "rollback \[--adopt\]"' _ "$installer"
ok "F1 rollback guard"

# F2: SIGTERM mid-activation restores and allows an immediate retry
new_install; before="$(tree_hash)"
: > "$STUB_STATE/hang-armed"
"$installer" update "$(pkgpath 1.1.0 linux-arm64)" >"$tmp/sig.out" 2>&1 & ipid=$!
for _ in $(seq 1 100); do [ -e "$STUB_STATE/hang-pid" ] && break; sleep 0.1; done
check "F2 stub reached daemon-reload" [ -e "$STUB_STATE/hang-pid" ]
kill -TERM "$ipid"; kill "$(cat "$STUB_STATE/hang-pid")" 2>/dev/null
wait "$ipid"; rc=$?
check "F2 interrupted update exit 1" [ $rc = 1 ]
check "F2 tree identical to before the update" [ "$before" = "$(tree_hash)" ]
check "F2 service active" [ -e "$STUB_STATE/active" ]
check "F2 no leftover release or marker" bash -c '[ ! -e "$1/opt/lucifer/releases/lucifer-1.1.0-linux-arm64" ] && [ -z "$(ls -A "$1"/opt/lucifer/releases | grep -v "^lucifer-1.0.0")" ]' _ "$R"
tmpdir_clean "F2 signal"
run update "$(pkgpath 1.1.0 linux-arm64)"; check "F2 retry succeeds" [ $rc = 0 ]; check "F2 retry activated" [ "$(cur)" = lucifer-1.1.0-linux-arm64 ]
# uncatchable kill: leftovers carry a pending marker and are cleared by the next run
new_install; rel="$(OPT)/releases"
mkdir -p "$rel/lucifer-1.1.0-linux-arm64/bin" "$rel/.stage.dead"; : > "$rel/.pending-lucifer-1.1.0-linux-arm64"
run update "$(pkgpath 1.1.0 linux-arm64)"; check "F2 retry after simulated SIGKILL" [ $rc = 0 ]
check "F2 leftover replaced by the real release" [ -x "$rel/lucifer-1.1.0-linux-arm64/bin/lucifer-install" ]
check "F2 stale staging removed" [ ! -e "$rel/.stage.dead" ]
check "F2 warned about the leftover" grep -q 'interrupted run' <<<"$out"
# a directory without a marker is never deleted
new_install; mkdir -p "$(OPT)/releases/lucifer-1.1.0-linux-arm64/keep"
run update "$(pkgpath 1.1.0 linux-arm64)"; check "F2 unmarked existing release refused (3)" [ $rc = 3 ]
check "F2 unmarked release untouched" [ -d "$(OPT)/releases/lucifer-1.1.0-linux-arm64/keep" ]
check "F2 refusal explains removal" grep -q 'remove' <<<"$out"
ok "F2 interruption and retry"

# F3: install --start validates the configuration first
start_case() { # label content
  fresh; mkdir -p "$R/etc/lucifer"; printf '%b' "$2" > "$R/etc/lucifer/lucifer.env"
  run install --start "$p"; rc0=$rc
}
for c in 'crlf placeholder|NAVIDROME_PASSWORD=change-me\r\n' 'blank password|NAVIDROME_PASSWORD=\n' 'whitespace password|NAVIDROME_PASSWORD=   \n' \
         'missing password|NAVIDROME_USER=x\n' 'padded placeholder|NAVIDROME_PASSWORD= change-me \n'; do
  start_case "${c%%|*}" "${c#*|}"
  check "F3 --start refused (${c%%|*})" [ $rc0 = 3 ]
  check "F3 no host effects (${c%%|*})" bash -c '[ ! -e "$1/opt" ] && [ ! -s "$2/calls" ]' _ "$R" "$STUB_STATE"
done
start_case crlf-real 'NAVIDROME_PASSWORD=SECRETPW\r\n'
check "F3 CRLF real password: failed check exit 1" [ $rc0 = 1 ]
check "F3 not enabled or started" bash -c '! grep -E "systemctl (enable|start)" "$1"' _ "$STUB_STATE/calls"
check "F3 not activated" bash -c '[ ! -e "$1/opt/lucifer/current" ] && [ ! -e "$1/etc/systemd/system/lucifer.service" ]' _ "$R"
check "F3 secret not printed" bash -c '! grep -q SECRETPW <<<"$1"' _ "$out"
check "F3 env bytes untouched" [ "$(cat "$R/etc/lucifer/lucifer.env")" = "$(printf 'NAVIDROME_PASSWORD=SECRETPW\r')" ]
fresh; mkdir -p "$R/etc/lucifer"; setcfg
run install --start "$(pkgpath badcheck-1 linux-arm64)"; check "F3 failing check blocks --start (1)" [ $rc = 1 ]
check "F3 failing check: no enable/start" bash -c '! grep -E "systemctl (enable|start)" "$1"' _ "$STUB_STATE/calls"
check "F3 failing check: release removed" [ ! -e "$(OPT)/releases/lucifer-badcheck-1-linux-arm64" ]
check "F3 failing check: service stays inactive" [ ! -e "$STUB_STATE/active" ] && [ ! -e "$STUB_STATE/enabled" ]
fresh; mkdir -p "$R/etc/lucifer"; setcfg; run install --start "$p"
check "F3 valid --start still works" [ $rc = 0 ]
check "F3 check precedes enable and start" bash -c '
  c="$(grep -n "^maint check" "$1" | head -1 | cut -d: -f1)"; e="$(grep -n "systemctl enable" "$1" | head -1 | cut -d: -f1)"
  s="$(grep -n "systemctl start" "$1" | head -1 | cut -d: -f1)"; [ "$c" -lt "$e" ] && [ "$e" -lt "$s" ]' _ "$STUB_STATE/calls"
ok "F3 --start validation"

# F4: malformed packages fail before the host changes; verified bytes are installed bytes
python3 -I - "$dist/lucifer-9.9.7-linux-arm64.tar.gz" <<'PY'
import sys, tarfile, io
top = "lucifer-9.9.7-linux-arm64"
t = tarfile.open(sys.argv[1], "w:gz")
def add(n, d=b"x"):
    i = tarfile.TarInfo(n); i.mode = 0o755; i.size = len(d); t.addfile(i, io.BytesIO(d))
for k, v in {"VERSION": b"9.9.7\n", "RID": b"linux-arm64\n", "bin/Amp.Nfc": b"x", "bin/lucifer-maint": b"x", "bin/lucifer-install": b"x",
             "config/lucifer.env.example": b"x", "config/catalogue.example.json": b"x",
             "systemd/lucifer.service": b"x", "systemd/lucifer.tmpfiles.conf": b"x"}.items(): add(f"{top}/{k}", v)
add(f"{top}/bin/" + "a" * 300)
t.close()
PY
(cd "$dist" && sha256sum lucifer-9.9.7-linux-arm64.tar.gz > lucifer-9.9.7-linux-arm64.tar.gz.sha256)
fresh; run install "$dist/lucifer-9.9.7-linux-arm64.tar.gz"
check "F4 over-long component rejected (1)" [ $rc = 1 ]
no_host_effects "over-long component"; tmpdir_clean "F4 overlong"
# truncated stream: listing may succeed partially; must still fail with no effects
head -c 2000 "$p" > "$dist/lucifer-9.9.6-linux-arm64.tar.gz"; (cd "$dist" && sha256sum lucifer-9.9.6-linux-arm64.tar.gz > lucifer-9.9.6-linux-arm64.tar.gz.sha256)
fresh; run install "$dist/lucifer-9.9.6-linux-arm64.tar.gz"; check "F4 truncated archive rejected" [ $rc = 1 ]; no_host_effects "truncated"; tmpdir_clean "F4 truncated"
# archive and checksum swapped after verification (writable source): installed bytes are the verified ones
cp "$p" "$tmp/orig.tar.gz"; cp "$p.sha256" "$tmp/orig.tar.gz.sha256"
mkpkg 1.0.0 linux-arm64 'echo evil > "$w/$n/EVIL"'
cp "$p" "$tmp/evil.tar.gz"; cp "$p.sha256" "$tmp/evil.tar.gz.sha256"
cp "$tmp/orig.tar.gz" "$p"; cp "$tmp/orig.tar.gz.sha256" "$p.sha256"
fresh; export STUB_SWAP_TARGET="$p" STUB_SWAP_FROM="$tmp/evil.tar.gz"
run install "$p"; unset STUB_SWAP_TARGET STUB_SWAP_FROM
cp "$tmp/orig.tar.gz" "$p"; cp "$tmp/orig.tar.gz.sha256" "$p.sha256"
check "F4 swap-time install exit 0" [ $rc = 0 ]
check "F4 swapped-in content never installed" [ ! -e "$(OPT)/current/EVIL" ]
check "F4 verified content installed" [ -x "$(OPT)/current/bin/lucifer-install" ]
tmpdir_clean "F4 success"
ok "F4 package handling"


# ============ review fixes v3 (B1, O2, O3, N1, N2) ============
killhang() { # pid-of-installer: kill -9 installer and its hanging stub
  kill -KILL "$1" 2>/dev/null; kill -KILL "$(cat "$STUB_STATE/hang-pid")" 2>/dev/null; wait "$1" 2>/dev/null; return 0
}
waithang() { for _ in $(seq 1 100); do [ -e "$STUB_STATE/hang-pid" ] && return 0; sleep 0.1; done; return 1; }

# B1: failed install --start restores enable and active state
b1_case() { # label initial-enabled(0|1) failure-env
  fresh; mkdir -p "$R/etc/lucifer"; setcfg
  [ "$2" = 1 ] && : > "$STUB_STATE/enabled"
  env "$3" "$installer" install --start "$(pkgpath badstart-1 linux-arm64)" >"$tmp/b1.out" 2>&1; rc=$?
  out="$(cat "$tmp/b1.out")"
  check "B1 $1: exit 1" [ $rc = 1 ]
  check "B1 $1: not active" [ ! -e "$STUB_STATE/active" ]
  if [ "$2" = 1 ]; then check "B1 $1: stays enabled" [ -e "$STUB_STATE/enabled" ]
  else check "B1 $1: not enabled" [ ! -e "$STUB_STATE/enabled" ]; fi
  check "B1 $1: stopped what it started" grep -q '^systemctl stop' "$STUB_STATE/calls"
  if [ "$2" = 1 ]; then check "B1 $1: no disable of a prior enable" bash -c '! grep -q "systemctl disable" "$1"' _ "$STUB_STATE/calls"
  else check "B1 $1: disable issued" grep -q '^systemctl disable' "$STUB_STATE/calls"; fi
  check "B1 $1: undo happens before the final daemon-reload" bash -c '
    d="$(grep -n "^systemctl disable\|^systemctl stop" "$1" | tail -1 | cut -d: -f1)"; r="$(grep -n "^systemctl daemon-reload" "$1" | tail -1 | cut -d: -f1)"; [ "$d" -lt "$r" ]' _ "$STUB_STATE/calls"
  check "B1 $1: unit, current and release gone" bash -c '[ ! -e "$1/etc/systemd/system/lucifer.service" ] && [ ! -e "$1/opt/lucifer/current" ] && [ ! -e "$1/opt/lucifer/releases/lucifer-badstart-1-linux-arm64" ]' _ "$R"
  check "B1 $1: secret not printed" bash -c '! grep -q SECRETPW <<<"$1"' _ "$out"
  # following default install with the placeholder configuration
  printf 'NAVIDROME_PASSWORD=change-me\n' > "$R/etc/lucifer/lucifer.env"
  : > "$STUB_STATE/calls"
  run install "$p"; check "B1 $1: later plain install ok" [ $rc = 0 ]
  check "B1 $1: later install does not start" [ ! -e "$STUB_STATE/active" ]
  if [ "$2" = 1 ]; then check "B1 $1: prior enable still in place" [ -e "$STUB_STATE/enabled" ]
  else check "B1 $1: later install leaves it disabled" [ ! -e "$STUB_STATE/enabled" ]
       check "B1 $1: later install did not enable" bash -c '! grep -q "systemctl enable" "$1"' _ "$STUB_STATE/calls"; fi
}
b1_case "start fails, was disabled" 0 STUB_FAIL_START_FOR=lucifer-badstart-1-linux-arm64
b1_case "start fails, was enabled" 1 STUB_FAIL_START_FOR=lucifer-badstart-1-linux-arm64
b1_case "settle fails, was disabled" 0 STUB_NOACTIVE_FOR=lucifer-badstart-1-linux-arm64
b1_case "settle fails, was enabled" 1 STUB_NOACTIVE_FOR=lucifer-badstart-1-linux-arm64
ok "B1 service state after failed install --start"

# O2: a kill after the switch must not make the release look like a leftover
mkpkg 1.3.0 linux-arm64
new_install; run update "$(pkgpath 1.1.0 linux-arm64)" >/dev/null
rm -f "$STUB_STATE/hang-pid"; : > "$STUB_STATE/hang-start-armed"
"$installer" update "$(pkgpath 1.2.0 linux-arm64)" >/dev/null 2>&1 & ipid=$!
waithang; killhang "$ipid"
check "O2 killed after the switch: current is 1.2.0" [ "$(cur)" = lucifer-1.2.0-linux-arm64 ]
check "O2 no marker survives the switch" bash -c '[ -z "$(ls -A "$1"/opt/lucifer/releases | grep "^.pending")" ]' _ "$R"
run rollback lucifer-1.0.0-linux-arm64; check "O2 manual rollback 1" [ $rc = 0 ]
run rollback lucifer-1.1.0-linux-arm64; check "O2 manual rollback 2" [ $rc = 0 ]
run update "$(pkgpath 1.3.0 linux-arm64)"; check "O2 later update ok" [ $rc = 0 ]
for v in 1.0.0 1.1.0 1.2.0; do check "O2 release $v preserved" [ -x "$(OPT)/releases/lucifer-$v-linux-arm64/bin/lucifer-install" ]; done
ok "O2 releases preserved after crash and manual rollback"

# O3: practical recovery after a kill before the switch
new_install; rm -f "$STUB_STATE/hang-pid"; : > "$STUB_STATE/hang-armed"
"$installer" update "$(pkgpath 1.1.0 linux-arm64)" >/dev/null 2>&1 & ipid=$!
waithang; killhang "$ipid"
check "O3 killed: old release still current" [ "$(cur)" = lucifer-1.0.0-linux-arm64 ]
check "O3 killed: service stopped, new unit in place" bash -c '[ ! -e "$1/active" ] && grep -q "unit from 1.1.0" "$2"' _ "$STUB_STATE" "$R/etc/systemd/system/lucifer.service"
run update "$(pkgpath 1.2.0 linux-arm64)"; check "O3 other version refused with hint (3)" [ $rc = 3 ]
check "O3 refusal points at the recovery docs" grep -q 'After a crash' <<<"$out"
run update "$(pkgpath 1.1.0 linux-arm64)"; check "O3 same-version retry completes" [ $rc = 0 ]
check "O3 interrupted run's saved files kept" bash -c 'grep -rq "unit from 1.0.0" "$1"/opt/lucifer/backups/interrupted-*/unit' _ "$R"
check "O3 retry did not restart a stopped service (documented)" [ ! -e "$STUB_STATE/active" ]
ok "O3 recovery after a kill"

# N1: update, rollback and list on an empty host leave nothing behind
fresh
run update "$p"; check "N1 update refused (3)" [ $rc = 3 ]
run rollback; check "N1 rollback refused (3)" [ $rc = 3 ]
check "N1 host root still empty" bash -c '[ -z "$(find "$1" -mindepth 1 -print -quit)" ]' _ "$R"
check "N1 no host commands ran" [ ! -s "$STUB_STATE/calls" ]

# N2: restored tmpfiles snippet is re-applied after a failed update
new_install
STUB_FAIL_START_FOR=lucifer-badstart-1-linux-arm64 run update "$(pkgpath badstart-1 linux-arm64)"
check "N2 failed update exit 1" [ $rc = 1 ]
check "N2 tmpfiles applied for new, then re-applied for restored copy" bash -c '[ "$(grep -c "^tmpfiles --create" "$1")" = 2 ]' _ "$STUB_STATE/calls"
check "N2 last tmpfiles run follows the restore" bash -c '
  t="$(grep -n "^tmpfiles --create" "$1" | tail -1 | cut -d: -f1)"; r="$(grep -n "^systemctl daemon-reload" "$1" | tail -2 | head -1 | cut -d: -f1)"; [ "$t" -gt "$r" ]' _ "$STUB_STATE/calls"
ok "N1/N2"

if [ "$fails" = 0 ]; then echo "lucifer-install: ok"; else echo "lucifer-install: $fails check(s) failed" >&2; exit 1; fi
