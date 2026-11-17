#!/usr/bin/env bash
# Builds the UPM package for <version>, commits it on top of the `upm` branch, tags the commit upm/v<version>,
# and pushes both to origin in one atomic push. Does nothing if the tag already exists (safe to re-run).
# Called by the Release workflow; run from the repository root with an up-to-date fetch of origin.
#
# Usage: .github/upm/publish.sh <version>
set -euo pipefail

version="${1:?usage: publish.sh <version>}"
tag="upm/v$version"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
git_dir="$(git rev-parse --absolute-git-dir)"
work="${RUNNER_TEMP:-$(mktemp -d)}/flexgit-upm"

if git rev-parse -q --verify "refs/tags/$tag" > /dev/null; then
  echo "Tag $tag already exists: nothing to publish."
  exit 0
fi

bash "$here/build-package.sh" "$work/package" "$version"

# Commit the package folder without touching the current checkout: separate index, separate work tree.
export GIT_INDEX_FILE="$work/index"
rm -f "$GIT_INDEX_FILE"
(cd "$work/package" && git --git-dir="$git_dir" --work-tree=. add --all)
tree="$(git write-tree)"
unset GIT_INDEX_FILE

export GIT_AUTHOR_NAME="github-actions[bot]"
export GIT_AUTHOR_EMAIL="41898282+github-actions[bot]@users.noreply.github.com"
export GIT_COMMITTER_NAME="$GIT_AUTHOR_NAME"
export GIT_COMMITTER_EMAIL="$GIT_AUTHOR_EMAIL"

parent="$(git rev-parse -q --verify refs/remotes/origin/upm || true)"
commit="$(git commit-tree "$tree" ${parent:+-p "$parent"} \
  -m "FlexGit $version" \
  -m "Generated from ${GITHUB_SHA:-$(git rev-parse HEAD)} by .github/upm/publish.sh. Do not edit this branch by hand.")"

git tag "$tag" "$commit"
git push --atomic origin "$commit:refs/heads/upm" "refs/tags/$tag"
echo "Published $tag ($commit)."
