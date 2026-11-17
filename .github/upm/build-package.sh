#!/usr/bin/env bash
# Builds the Unity Package Manager (UPM) package of FlexGit into <output-dir>:
#   package.json, README.md, CHANGELOG.md, LICENSE.md, Editor/FlexGit.asmdef, Editor/**/*.cs, and a .meta for everything.
# publish.sh commits that folder to the `upm` branch; Unity installs it with
#   https://github.com/Bloknoter/FlexGit.git#upm/vX.Y.Z
#
# Usage: .github/upm/build-package.sh <output-dir> [version]    (version defaults to <Version> in FlexGit.csproj)
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="${1:?usage: build-package.sh <output-dir> [version]}"
version="${2:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/src/FlexGit/FlexGit.csproj" | head -n 1)}"
package_name=com.bloknoter.flexgit

if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "error: '$version' is not a semantic version (X.Y.Z or X.Y.Z-suffix)" >&2
  exit 1
fi

rm -rf "$out"
mkdir -p "$out/Editor"

# Library sources only: no csproj, no build output.
(cd "$root/src/FlexGit" && find . -name '*.cs' -not -path './bin/*' -not -path './obj/*' -print0) |
  while IFS= read -r -d '' file; do
    mkdir -p "$out/Editor/$(dirname "$file")"
    cp "$root/src/FlexGit/$file" "$out/Editor/$file"
  done

cp "$here/FlexGit.asmdef" "$out/Editor/FlexGit.asmdef"
sed "s/\"version\": \"0.0.0\"/\"version\": \"$version\"/" "$here/package.json" > "$out/package.json"
if ! grep -q "\"version\": \"$version\"" "$out/package.json"; then
  echo "error: could not set the version in package.json" >&2
  exit 1
fi
cp "$root/README.md" "$out/README.md"
cp "$root/CHANGELOG.md" "$out/CHANGELOG.md"
cp "$root/LICENSE" "$out/LICENSE.md"

# Unity can't create .meta files inside an installed (read-only) git package, so every file and folder needs one.
# GUIDs are derived from the package path, so they stay the same in every version: user asmdefs reference
# FlexGit.asmdef by GUID, and changing it would break them.
guid_for() { printf '%s' "$package_name/$1" | md5sum | cut -c1-32; }

importer_for() {
  local path="$1"
  if [ -d "$out/$path" ]; then
    printf 'folderAsset: yes\nDefaultImporter:\n'
    return
  fi
  case "$path" in
    package.json) printf 'PackageManifestImporter:\n' ;;
    *.cs) printf 'MonoImporter:\n' ;;
    *.asmdef) printf 'AssemblyDefinitionImporter:\n' ;;
    *.md | *.json | *.txt) printf 'TextScriptImporter:\n' ;;
    *) printf 'DefaultImporter:\n' ;;
  esac
}

count=0
while IFS= read -r -d '' entry; do
  path="${entry#./}"
  {
    printf 'fileFormatVersion: 2\nguid: %s\n' "$(guid_for "$path")"
    importer_for "$path"
    printf '  externalObjects: {}\n'
    case "$path" in
      *.cs) [ -d "$out/$path" ] || printf '  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n' ;;
    esac
    printf '  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
  } > "$out/$path.meta"
  count=$((count + 1))
done < <(cd "$out" && find . -mindepth 1 -not -name '*.meta' -print0 | sort -z)

echo "Built $package_name $version in $out ($count assets)."
