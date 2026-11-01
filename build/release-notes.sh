#!/usr/bin/env bash
# Writes a release's notes, in Markdown, from the commits since the release before it: the subjects of feat: commits
# under "New" and of fix: commits under "Fixed", as Conventional Commits name them (CLAUDE.md). Other commits, such as
# docs:, test: or build:, change nothing a user sees, and aren't listed. The packages carry the notes, and the installed
# app shows them when it offers the update (ADR 0039).
#
#   build/release-notes.sh [commit] > notes.md
#
# The commit is the release's, HEAD unless given. The release before it is the newest tag like v0.9.0 that the commit's
# parent reaches; without one, this is the first release, and the notes say so. Run it in the repository. It needs only
# git and a POSIX shell's tools, so it runs on macOS's own bash too.
set -euo pipefail

release="${1:-HEAD}"
previous="$(git describe --tags --abbrev=0 --match 'v[0-9]*' "$release^" 2>/dev/null || true)"

if [[ -z "$previous" ]]; then
  echo "The first release of ModShelf."
  exit 0
fi

# One subject a line, oldest first, without merges. A breaking change's "!" and a scope such as "(settings)" go;
# the subject starts with a capital letter.
subjects() {
  git log --no-merges --reverse --format=%s "$previous..$release" \
    | sed -n -E "s/^$1(\\([^)]*\\))?!?: +(.+)$/\\2/p" \
    | awk '{ print "- " toupper(substr($0, 1, 1)) substr($0, 2) }'
}

new="$(subjects feat)"
fixed="$(subjects fix)"

if [[ -z "$new" && -z "$fixed" ]]; then
  echo "Changes behind the scenes only: nothing you'd see in ModShelf is new or fixed since ${previous#v}."
  exit 0
fi

if [[ -n "$new" ]]; then
  printf '## New\n\n%s\n' "$new"
fi

if [[ -n "$fixed" ]]; then
  [[ -n "$new" ]] && echo
  printf '## Fixed\n\n%s\n' "$fixed"
fi
