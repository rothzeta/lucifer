#!/usr/bin/env bash
# Shell-level check of lucifer-maint: plain values are taken literally and
# values systemd would read differently are refused. Uses a stub
# Amp.Nfc that prints what it receives. Run from anywhere; touches only a
# temporary directory.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
cp "$here/lucifer-maint" "$tmp/lucifer-maint"
cat > "$tmp/Amp.Nfc" <<'STUB'
#!/bin/sh
printf 'args=%s\n' "$*"
printf 'PW=[%s]\n' "$NAVIDROME_PASSWORD"
printf 'IPC=[%s]\n' "$LUCIFER_IPC_DIR"
printf 'EXT=[%s]\n' "${EXTRA-unset}"
STUB
chmod +x "$tmp/Amp.Nfc" "$tmp/lucifer-maint"

# shellcheck disable=SC2016  # the dollar signs are the point
printf '%s\n' \
  '# comment' \
  '' \
  '   # indented comment' \
  '   ' \
  'NAVIDROME_PASSWORD=pa$$ w0rd $HOME `id` q n=x a"b"c' \
  'LUCIFER_IPC_DIR=/run/lucifer' > "$tmp/env"

out="$(LUCIFER_ENV_FILE="$tmp/env" "$tmp/lucifer-maint" tag read)"
fail() { echo "FAIL: $1" >&2; echo "$out" >&2; exit 1; }
# shellcheck disable=SC2016
[ "$(grep '^PW=' <<<"$out")" = 'PW=[pa$$ w0rd $HOME `id` q n=x a"b"c]' ] || fail "password not literal"
grep -qx 'IPC=\[/run/lucifer\]' <<<"$out" || fail "IPC dir not passed"
grep -qx 'args=tag read' <<<"$out" || fail "arguments not passed"
grep -qx 'EXT=\[unset\]' <<<"$out" || fail "unexpected variable"

printf 'garbage line\n' > "$tmp/bad"
if LUCIFER_ENV_FILE="$tmp/bad" "$tmp/lucifer-maint" check 2>/dev/null; then
  echo "FAIL: malformed line accepted" >&2; exit 1
fi
# Lines systemd would parse differently are refused (exit 2) without echoing the value.
refused() {
  printf '%b\n' "$1" > "$tmp/diff"
  rc=0; err="$(LUCIFER_ENV_FILE="$tmp/diff" "$tmp/lucifer-maint" check 2>&1 >/dev/null)" || rc=$?
  [ "$rc" = 2 ] || { echo "FAIL: not refused (exit $rc): $2" >&2; exit 1; }
  case "$err" in *SECRET*) echo "FAIL: value echoed: $2" >&2; exit 1 ;; esac
}
refused 'A=SECRET\\b' backslash
refused 'A=SECRET   ' trailing-space
refused 'A=   SECRET' leading-value-space
refused 'A="SECRET"' double-quotes
refused "A='SECRET'" single-quotes
refused 'A="SECRET' open-quote
refused '  A=SECRET' indented-line
refused 'A=SECRET\rx' carriage-return-inside
refused 'A=SECRET\001x' control-character
refused 'A=SECRET\033x' escape
refused 'A=SECRET\377x' invalid-utf8
printf 'A=SE\tCRET\n' > "$tmp/tab"
LUCIFER_ENV_FILE="$tmp/tab" "$tmp/lucifer-maint" check >/dev/null || { echo "FAIL: tab inside a value refused" >&2; exit 1; }
printf 'last=noeol' > "$tmp/noeol"
LUCIFER_ENV_FILE="$tmp/noeol" "$tmp/lucifer-maint" check >/dev/null || { echo "FAIL: missing final newline" >&2; exit 1; }
echo "lucifer-maint: ok"
