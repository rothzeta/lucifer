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
stub dpkg '[ "$1" = "--print-architecture" ] && echo "${STUB_ARCH:-arm64}"'
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
  start) [ "$cur" = "${STUB_FAIL_START_FOR:-}" ] && exit 1; : > "$STUB_STATE/active";;
  daemon-reload) [ -n "${STUB_FAIL_RELOAD:-}" ] && [ -e "$STUB_STATE/reload-armed" ] && { rm -f "$STUB_STATE/reload-armed"; exit 1; }; exit 0;;
esac'
export PATH="$stubs:$PATH"
export LUCIFER_INSTALL_SETTLE_SECONDS=0

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
  unset STUB_FAIL_START_FOR STUB_FAIL_RELOAD STUB_UID STUB_ARCH
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

if [ "$fails" = 0 ]; then echo "lucifer-install: ok"; else echo "lucifer-install: $fails check(s) failed" >&2; exit 1; fi
