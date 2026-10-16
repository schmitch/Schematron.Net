#!/usr/bin/env bash
# Downloads the upstream test data listed in eng/testdata.lock into .testdata (or $SCHEMATRON_TESTDATA):
# rule sets with their compiled XSLT, conformance suites and sample documents, each at a pinned commit or checksum.
#
#   eng/fetch-testdata.sh            fetch what is missing or outdated
#   eng/fetch-testdata.sh qt3 cen    fetch only these entries
#
# A directory is reused while its .revision file matches the lock file. Requires git, curl and unzip.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="${SCHEMATRON_TESTDATA:-$here/../.testdata}"
mkdir -p "$root"
root="$(cd "$root" && pwd)"

sha256() {
    if command -v sha256sum > /dev/null; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

wanted() {
    [ ${#selected[@]} -eq 0 ] && return 0
    for w in "${selected[@]}"; do [ "$w" = "$1" ] && return 0; done
    return 1
}

fetch_git() {
    local target=$1 url=$2 commit=$3
    shift 3
    git init --quiet "$target"
    git -C "$target" remote add origin "$url"
    local filter=()
    if [ $# -gt 0 ]; then
        git -C "$target" sparse-checkout set --no-cone "${@/#//}"
        filter=(--filter=blob:none)
    fi
    git -C "$target" fetch --quiet --depth 1 ${filter[@]+"${filter[@]}"} origin "$commit"
    git -C "$target" -c advice.detachedHead=false checkout --quiet FETCH_HEAD
    rm -rf "$target/.git"
}

fetch_zip() {
    local target=$1 url=$2 checksum=$3
    local archive="$root/.download.zip"
    curl -fsSL --retry 5 --retry-delay 5 -o "$archive" "$url"
    if [ "$(sha256 "$archive")" != "$checksum" ]; then
        echo "checksum mismatch for $url" >&2
        rm -f "$archive"
        exit 1
    fi
    mkdir -p "$target"
    unzip -q "$archive" -d "$target"
    rm -f "$archive"
}

selected=("$@")
while read -r name kind source revision rest; do
    case "$name" in "" | \#*) continue ;; esac
    wanted "$name" || continue
    target="$root/$name"
    stamp="$kind $source $revision $rest"
    if [ -f "$target/.revision" ] && [ "$(cat "$target/.revision")" = "$stamp" ]; then
        echo "$name: up to date"
        continue
    fi
    echo "$name: fetching $source @ $revision"
    rm -rf "$target" "$target.tmp"
    case "$kind" in
        # shellcheck disable=SC2086
        git) fetch_git "$target.tmp" "$source" "$revision" $rest ;;
        zip) fetch_zip "$target.tmp" "$source" "$revision" ;;
        *) echo "unknown kind $kind for $name" >&2; exit 1 ;;
    esac
    echo "$stamp" > "$target.tmp/.revision"
    mv "$target.tmp" "$target"
done < "$here/testdata.lock"
