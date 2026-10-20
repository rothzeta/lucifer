#!/usr/bin/env bash
# Shell-level check of the `lucifer` command (deploy/lucifer): update, rollback and
# list reach the private installer engine without reading any configuration;
# everything else is forwarded to lucifer-maint or the application with its
# arguments and exit status intact. Runs in a temporary tree with stub engine,
# maint and application; nothing on the host is touched.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
fails=0
fail() { echo "FAIL: $*" >&2; fails=$((fails + 1)); }
check() { local d="$1"; shift; if "$@"; then :; else fail "$d"; fi; }

rel="$tmp/opt/lucifer/releases/lucifer-t-linux-arm64"
mkdir -p "$rel/bin" "$rel/libexec" "$tmp/usr/local/bin"
cp "$here/lucifer" "$rel/bin/lucifer"
for n in libexec/lucifer-install bin/lucifer-maint bin/Amp.Nfc; do
  name="${n##*/}"
  cat > "$rel/$n" <<STUB
#!/usr/bin/env bash
{ printf '$name'; printf ' [%s]' "\$@"; echo; } >> "$tmp/calls"
[ "$name" != Amp.Nfc ] || [ "\${1:-}" != help ] || echo "application help"
exit "\${STUB_RC:-0}"
STUB
done
chmod +x "$rel/bin/"* "$rel/libexec/"*
ln -s releases/lucifer-t-linux-arm64 "$tmp/opt/lucifer/current"
ln -s "$tmp/opt/lucifer/current/bin/lucifer" "$tmp/usr/local/bin/lucifer"
cmd="$tmp/usr/local/bin/lucifer"   # through two symlinks, as installed
export LUCIFER_ENV_FILE="$tmp/no-such-env-file"   # update/rollback/list/help must not need it

last() { tail -n 1 "$tmp/calls"; }
run() { : > "$tmp/calls"; out="$("$cmd" "$@" 2>"$tmp/err")"; rc=$?; err="$(cat "$tmp/err")"; }

bash -n "$here/lucifer" || fail "bash -n"

run update --version v1.2.3 --adopt; check "update reaches the engine with its arguments" [ "$(last)" = "lucifer-install [update] [--version] [v1.2.3] [--adopt]" ]
check "update does not need the configuration" [ $rc = 0 ]
STUB_RC=3 run update; check "engine exit status 3 is forwarded" [ $rc = 3 ]
STUB_RC=1 run update --checksum "a b" "arch ive.tar.gz"; check "exit 1 forwarded" [ $rc = 1 ]
check "arguments with spaces stay single words" [ "$(last)" = "lucifer-install [update] [--checksum] [a b] [arch ive.tar.gz]" ]
run rollback lucifer-1.0.0-linux-arm64; check "rollback reaches the engine" [ "$(last)" = "lucifer-install [rollback] [lucifer-1.0.0-linux-arm64]" ]
run list; check "list reaches the engine" [ "$(last)" = "lucifer-install [list]" ]
check "no maint or application call for admin commands" bash -c '! grep -q "^lucifer-maint\|^Amp.Nfc" "$1"' _ "$tmp/calls"

run play; check "play goes to lucifer-maint" [ "$(last)" = "lucifer-maint [play]" ]
run check; check "check goes to lucifer-maint" [ "$(last)" = "lucifer-maint [check]" ]
STUB_RC=2 run tag write --release 1234; check "tag write forwarded with arguments" [ "$(last)" = "lucifer-maint [tag] [write] [--release] [1234]" ]
check "tag exit status forwarded" [ $rc = 2 ]
run input inspect --count 3; check "input inspect forwarded" [ "$(last)" = "lucifer-maint [input] [inspect] [--count] [3]" ]

run help; check "help exit 0 without configuration" [ $rc = 0 ]
check "help shows application help" grep -q 'application help' <<<"$out"
check "help documents lucifer update" grep -q 'lucifer update' <<<"$out"
check "help documents rollback and list" bash -c 'grep -q "lucifer rollback" <<<"$1" && grep -q "lucifer list" <<<"$1"' _ "$out"
STUB_RC=2 run frobnicate; check "unknown command exit status from the application" [ $rc = 2 ]
check "unknown command reaches the application, not maint" [ "$(last)" = "Amp.Nfc [frobnicate]" ]
check "unknown command hint goes to stderr" grep -q 'lucifer update' <<<"$err"
STUB_RC=2 run; check "no command: application usage status" [ $rc = 2 ]

[ -e "$rel/bin/lucifer-install" ] && fail "a public lucifer-install must not exist in the package layout"
if [ "$fails" = 0 ]; then echo "lucifer-command: ok"; else echo "lucifer-command: $fails check(s) failed" >&2; exit 1; fi
