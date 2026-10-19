#!/usr/bin/env bash
# Lucifer installer bootstrap, for running straight from GitHub:
#
#   curl -fsSL https://raw.githubusercontent.com/rothzeta/lucifer/master/install.sh | sudo bash
#   curl -fsSL https://raw.githubusercontent.com/rothzeta/lucifer/master/install.sh | sudo bash -s -- --version v1.2.3
#   curl -fsSL .../install.sh | sudo bash -s -- --help
#
# It downloads the release's lucifer-install (the transactional installer that is
# also packaged as bin/lucifer-install), checks it against the release's
# lucifer-install.sha256, and runs `lucifer-install install [ARGS]`. That installer
# downloads the prebuilt archive for this host's userland RID (latest stable
# release, or --version TAG), validates it and installs it exactly as documented
# in docs/install/INSTALL.md. Nothing is built here, and the service is not
# started unless --start is given and the configuration is real.
#
# Arguments are passed to `lucifer-install install` (--version TAG, --start,
# --adopt, --target-rid RID). Environment: LUCIFER_REPO (default rothzeta/lucifer);
# sudo drops it, so put it after sudo: `curl -fsSL URL | sudo env LUCIFER_REPO=owner/name bash`
# (and again for later `sudo env LUCIFER_REPO=owner/name lucifer-install update`).
# The checksum detects corruption only; it does not authenticate the publisher.
# The script is self-contained and never reads its own file, so it works on stdin.
# Everything runs inside main(), which is invoked by the very last line together
# with the argument count and an end marker. bash executes a piped script as it
# arrives, so a download cut anywhere defines nothing, or hits a syntax error, or
# calls main without the complete marker, which refuses to do anything.
set -euo pipefail

END_MARK=__LUCIFER_BOOTSTRAP_COMPLETE__

REPO="${LUCIFER_REPO:-rothzeta/lucifer}"
GH_DL="${LUCIFER_GITHUB_DOWNLOAD:-https://github.com}"

usage() {
  cat <<'USAGE'
usage: curl -fsSL <raw install.sh URL> | sudo bash -s -- [--version TAG] [--start] [--adopt] [--target-rid RID]

Installs the latest stable Lucifer release (or release TAG) from GitHub Releases
for this host's userland (linux-arm64 or linux-arm). Run as root, e.g. through
sudo. Updates later: sudo lucifer-install update [--version TAG]
USAGE
}
die() { echo "install.sh: $*" >&2; exit "${2:-1}"; }

main() {
  local n="${1:-}" version="" line sum rc=0 base code f
  [ "$#" -ge 2 ] && [ "${!#}" = "$END_MARK" ] && [[ "$n" =~ ^[0-9]+$ ]] && [ "$#" -eq $((n + 2)) ] \
    || { echo "install.sh: the script was cut off or is incomplete; nothing was done. Run the whole command again." >&2; exit 1; }
  shift; set -- "${@:1:$#-1}"
  local args=()
  while [ $# -gt 0 ]; do
    case "$1" in
      -h|--help) usage; exit 0 ;;
      --version) [ $# -ge 2 ] || die "--version needs a value" 2; version="$2"; args+=("$1" "$2"); shift 2 ;;
      --start|--adopt) args+=("$1"); shift ;;
      --target-rid) [ $# -ge 2 ] || die "--target-rid needs a value" 2; args+=("$1" "$2"); shift 2 ;;
      *) usage >&2; die "unknown argument: $1" 2 ;;
    esac
  done
  if [ -n "$version" ] && ! [[ "$version" =~ ^[A-Za-z0-9][A-Za-z0-9._+-]*$ ]]; then
    die "invalid --version (a release tag such as v1.2.3): $version" 2
  fi
  [[ "$REPO" =~ ^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$ ]] || die "invalid LUCIFER_REPO: $REPO" 2
  case "$GH_DL" in
    https://*) ;;
    http://*) [ "${LUCIFER_INSTALL_TEST_INSECURE:-}" = 1 ] || die "refusing a non-HTTPS download URL" 2 ;;
    *) die "refusing a non-HTTPS download URL" 2 ;;
  esac

  [ "$(id -u)" = 0 ] || die "must run as root: pipe into 'sudo bash', e.g. curl -fsSL <URL> | sudo bash" 1
  command -v curl >/dev/null || die "curl is required"
  command -v sha256sum >/dev/null || die "sha256sum is required"

  # Redirects obey the same rule as the first URL: HTTPS only, unless the test hook is set.
  if [ "${LUCIFER_INSTALL_TEST_INSECURE:-}" = 1 ]; then proto='=https,http'; else proto='=https'; fi
  work="$(mktemp -d "${TMPDIR:-/var/tmp}/lucifer-bootstrap.XXXXXX")"
  trap 'rm -rf "${work:?}"' EXIT
  chmod 0700 "$work"

  if [ -n "$version" ]; then base="$GH_DL/$REPO/releases/download/$version"; else base="$GH_DL/$REPO/releases/latest/download"; fi

  get() { # name
    local code
    code="$(curl -sSL --proto "$proto" --proto-redir "$proto" --connect-timeout 20 --max-time 300 \
            -o "$work/$1" -w '%{http_code}' "$base/$1" 2>"$work/curl.err")" \
      || die "download failed ($base/$1): $(head -c 200 "$work/curl.err" | tr -d '\r\n')"
    case "$code" in
      200) ;;
      404) die "not found (HTTP 404): $base/$1 -- is a release published${version:+ with tag $version} in $REPO?" ;;
      403|429) die "GitHub refused the request (HTTP $code: rate limit or access denied); wait and retry" ;;
      *) die "unexpected HTTP status $code: $base/$1" ;;
    esac
  }
  get lucifer-install
  get lucifer-install.sha256

  line="$(head -n 1 "$work/lucifer-install.sha256")"
  if ! [[ "$line" =~ ^([0-9a-fA-F]{64})\ [\ \*]lucifer-install$ ]]; then die "malformed lucifer-install.sha256"; fi
  sum="$(sha256sum "$work/lucifer-install")"; sum="${sum%% *}"
  [ "$sum" = "${BASH_REMATCH[1],,}" ] || die "checksum mismatch for lucifer-install (corrupted or altered download)"
  head -n 1 "$work/lucifer-install" | grep -qx '#!/usr/bin/env bash' || die "lucifer-install is not the expected script"
  chmod 0700 "$work/lucifer-install"

  # Not exec: keep the trap so the private directory is removed afterwards. The
  # installer validates the archive completely before it changes the host.
  rc=0
  "$work/lucifer-install" install ${args[@]+"${args[@]}"} </dev/null || rc=$?
  exit "$rc"
}

main "$#" "$@" "$END_MARK"
