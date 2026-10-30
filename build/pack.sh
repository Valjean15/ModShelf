#!/usr/bin/env bash
# Packs ModShelf's installers and update packages with Velopack (vpk) into artifacts/releases, from a self-contained
# publish of the desktop app for each runtime:
#
#   build/pack.sh [--version 0.9.1] [--notes notes.md] [--output folder] [runtime ...]
#
# Runtimes: win-x64 (Setup.exe, per user, with a Start-menu shortcut), linux-x64 (an AppImage), osx-arm64 and osx-x64
# (an .app in a .pkg). Without one, it packs for this system. vpk packs for another system where it can, such as
# Windows packages on Linux; macOS packages need a Mac. The version comes from the git tag through MinVer, unless
# --version gives one. Each runtime is a channel of its own, named after it: an installed ModShelf looks for updates
# in its own.
#
# Packing again with the previous release's full package in the output folder adds a delta package to the update.
# An AppImage needs mksquashfs (the squashfs-tools package), and a delta zstd. PowerShell: build/pack.ps1 does the same.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/ModShelf.Desktop/ModShelf.Desktop.csproj"

# Velopack installs the Windows app into %LocalAppData%\<pack ID>, and its uninstaller deletes that whole folder.
# ModShelf keeps its library database, settings, logs and secrets in %LocalAppData%\ModShelf, so the pack ID must never
# be ModShelf (ADR 0038).
pack_id=ModShelf.App
output="$root/artifacts/releases"
version=""
notes=""
runtimes=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) version="$2"; shift 2 ;;
    --notes) notes="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"; shift 2 ;;
    --output) mkdir -p "$2"; output="$(cd "$2" && pwd)"; shift 2 ;;
    -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "${BASH_SOURCE[0]}"; exit 0 ;;
    -*) echo "Unknown option: $1" >&2; exit 2 ;;
    *) runtimes+=("$1"); shift ;;
  esac
done

case "$(uname -s)" in
  Linux*) host=linux ;;
  Darwin*) host=osx ;;
  MINGW*|MSYS*|CYGWIN*) host=win ;;
  *) echo "Unknown system: $(uname -s)" >&2; exit 2 ;;
esac

if [[ ${#runtimes[@]} -eq 0 ]]; then
  case "$host" in
    linux) runtimes=(linux-x64) ;;
    osx) [[ "$(uname -m)" == arm64 ]] && runtimes=(osx-arm64) || runtimes=(osx-x64) ;;
    win) runtimes=(win-x64) ;;
  esac
fi

cd "$root"
dotnet tool restore >/dev/null

if [[ -z "$version" ]]; then
  version="$(dotnet msbuild "$project" -nologo -t:MinVer -getProperty:MinVerVersion -p:Configuration=Release | tr -d '[:space:]')"
fi
short_version="${version%%[-+]*}"
echo "Packing ModShelf $version for ${runtimes[*]} into $output"

for runtime in "${runtimes[@]}"; do
  system="${runtime%%-*}"
  case "$system" in
    win|linux|osx) ;;
    *) echo "Unknown runtime: $runtime" >&2; exit 2 ;;
  esac

  if [[ "$system" == osx && "$host" != osx ]]; then
    echo "macOS packages need a Mac: skipping $runtime." >&2
    continue
  fi

  # vpk packs for another system when told which, as its first argument.
  directive=()
  [[ "$system" != "$host" ]] && directive=("[$system]")

  publish="$root/artifacts/publish/$runtime"
  rm -rf "$publish"
  dotnet publish "$project" -nologo -c Release -r "$runtime" --self-contained -o "$publish" -p:MinVerVersionOverride="$version"

  pack=(--packId "$pack_id" --packVersion "$version" --packDir "$publish" --packTitle ModShelf --packAuthors ModShelf
        --runtime "$runtime" --channel "$runtime" --outputDir "$output")
  [[ -n "$notes" ]] && pack+=(--releaseNotes "$notes")

  case "$system" in
    win)
      dotnet vpk "${directive[@]}" pack "${pack[@]}" --mainExe ModShelf.Desktop.exe \
        --icon "$root/src/ModShelf.Desktop/Assets/modshelf.ico" --aumid ModShelf.App --shortcuts StartMenuRoot
      ;;
    linux)
      dotnet vpk "${directive[@]}" pack "${pack[@]}" --mainExe ModShelf.Desktop \
        --icon "$root/src/ModShelf.Desktop/Assets/modshelf.png" --categories Utility
      ;;
    osx)
      plist="$root/artifacts/publish/Info-$runtime.plist"
      sed -e "s/@VERSION@/$version/" -e "s/@SHORT_VERSION@/$short_version/" "$root/build/macos/Info.plist" > "$plist"
      dotnet vpk pack "${pack[@]}" --mainExe ModShelf.Desktop \
        --icon "$root/build/macos/modshelf.icns" --plist "$plist"
      ;;
  esac
done

echo "Done: $output"
