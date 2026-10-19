#!/usr/bin/env bash
# Build a versioned, self-contained package for one Linux runtime identifier.
#
#   deploy/publish.sh --rid linux-arm64 [--version 1.0.0] [--out dist]
#   deploy/publish.sh --rid linux-arm
#
# The runtime identifier must match the userland of the target OS (64-bit
# userland: linux-arm64; 32-bit userland: linux-arm). It is deliberately
# required: the target architecture is recorded by LUC-08, not guessed here.
# Native AOT is not used. Output: <out>/lucifer-<version>-<rid>.tar.gz and a
# .sha256 file next to it. Needs the .NET 10 SDK and the runtime pack for the
# RID (restored from NuGet, or already in the local NuGet cache).
set -euo pipefail

rid=""
version=""
out="dist"

usage() { sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
  case "$1" in
    --rid) rid="${2:-}"; shift 2 ;;
    --version) version="${2:-}"; shift 2 ;;
    --out) out="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

case "$rid" in
  linux-arm64|linux-arm) ;;
  *) echo "--rid must be linux-arm64 or linux-arm" >&2; exit 2 ;;
esac

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

if [ -z "$version" ]; then
  version="$(git describe --tags --always --dirty 2>/dev/null || echo unversioned)"
fi
# Version becomes part of a file name and a directory name.
if ! [[ "$version" =~ ^[A-Za-z0-9][A-Za-z0-9._+-]*$ ]]; then
  echo "version '$version' must match [A-Za-z0-9][A-Za-z0-9._+-]*" >&2
  exit 2
fi

name="lucifer-$version-$rid"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
stage="$work/$name"
mkdir -p "$stage/bin" "$stage/config" "$stage/systemd" "$stage/udev"

dotnet publish Amp.Nfc/Amp.Nfc.csproj -c Release -r "$rid" --self-contained true \
  -p:DebugType=None -p:DebugSymbols=false -o "$stage/bin"

cp deploy/lucifer.env.example "$stage/config/lucifer.env.example"
cp catalogue.example.json "$stage/config/catalogue.example.json"
cp input-map.example.json "$stage/config/input-map.example.json"
cp deploy/lucifer.service "$stage/systemd/lucifer.service"
cp deploy/lucifer.tmpfiles.conf "$stage/systemd/lucifer.tmpfiles.conf"
cp deploy/99-lucifer-nfc.rules.example "$stage/udev/99-lucifer-nfc.rules.example"
cp deploy/lucifer-maint "$stage/bin/lucifer-maint"
cp deploy/lucifer-install "$stage/bin/lucifer-install"
cp docs/install/INSTALL.md "$stage/INSTALL.md"
chmod 0755 "$stage/bin/lucifer-maint" "$stage/bin/lucifer-install" "$stage/bin/Amp.Nfc"
printf '%s\n' "$version" > "$stage/VERSION"
printf '%s\n' "$rid" > "$stage/RID"

mkdir -p "$out"
out="$(cd "$out" && pwd)"
archive="$out/$name.tar.gz"
# Stable ordering and ownership; mtime from the commit when available.
mtime="$(git log -1 --format=%cI 2>/dev/null || date -u +%Y-%m-%dT%H:%M:%SZ)"
tar --sort=name --owner=0 --group=0 --numeric-owner --mtime="$mtime" \
  -C "$work" -cf - "$name" | gzip -n > "$archive"
(cd "$out" && sha256sum "$name.tar.gz" > "$name.tar.gz.sha256")

echo "archive:  $archive"
echo "checksum: $archive.sha256"
cat "$archive.sha256"
