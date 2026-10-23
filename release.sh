#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

target="${1:-dist/Rimlore}"
target="${target%/}"

project_dir="$(pwd -P)"
target_abs="$(realpath -m -- "$target")"
if [[ "$project_dir" == "$target_abs" || "$project_dir" == "$target_abs"/* ]]; then
  echo "Refusing to build into $target: it is (or contains) this project folder. If your Mods folder IS the project," >&2
  echo "just run: dotnet build Source/Rimlore -c Release" >&2
  exit 1
fi

parent="$(dirname "$target_abs")"
if [[ $# -gt 0 && ! -d "$parent" ]]; then
  echo "Folder not found: $parent" >&2
  echo "Check the path to your RimWorld Mods folder. Nothing was changed." >&2
  exit 1
fi

if [[ -d "$target" && ! -L "$target" && -n "$(ls -A -- "$target")" ]]; then
  if ! grep -q -E '<packageId>(cavan|cavaphelan)\.rimlore</packageId>' "$target/About/About.xml" 2>/dev/null; then
    echo "Refusing to overwrite $target: it doesn't look like Rimlore (no About/About.xml with Rimlore's ID)." >&2
    [[ -f "$target/Rimlore/About/About.xml" ]] && echo "It contains a nested Rimlore folder: $target/Rimlore. Remove $target and run this again." >&2
    exit 1
  fi
  if [[ -f "$target/About/PublishedFileId.txt" && ! -f About/PublishedFileId.txt ]]; then
    cp -- "$target/About/PublishedFileId.txt" About/
    echo "Saved About/PublishedFileId.txt from the previous upload."
  fi
fi

rm -f 1.6/Assemblies/ColonyChronicle.dll 1.7/Assemblies/ColonyChronicle.dll

dotnet build Source/Rimlore -c Release -nologo -v q

stage_root="$(mktemp -d)"
trap 'rm -rf -- "$stage_root"' EXIT
stage="$stage_root/Rimlore"
mkdir -p "$stage/Source/Rimlore"
for item in About Defs Viewer 1.6 1.7 LoadFolders.xml README.md LICENSE CHANGELOG.md; do
  [[ -e "$item" ]] && cp -r -- "$item" "$stage/"
done
cp -- Source/Rimlore/*.cs Source/Rimlore/Rimlore.csproj "$stage/Source/Rimlore/"

for required in About/About.xml Defs 1.6/Assemblies/Rimlore.dll 1.7/Assemblies/Rimlore.dll; do
  if [[ ! -e "$stage/$required" ]]; then
    echo "The build is missing $required, so $target was left as it was." >&2
    exit 1
  fi
done

rm -rf -- "$target"
mkdir -p "$(dirname "$target")"
cp -r -- "$stage" "$target"

version="$(sed -n 's:.*<modVersion>\(.*\)</modVersion>.*:\1:p' About/About.xml | head -n1)"
echo "Rimlore${version:+ v$version} is ready in: $target"

if [[ $# -eq 0 ]] && command -v zip >/dev/null; then
  (cd dist && rm -f Rimlore.zip && zip -r -q Rimlore.zip Rimlore)
  echo "Release zip: dist/Rimlore.zip"
fi
