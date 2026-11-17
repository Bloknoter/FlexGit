# Changelog

All notable changes to this library are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the library uses [Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-11-17

Complete rewrite on top of the git command line, replacing the LibGit2Sharp-based v1. The API is not compatible with v1.

### Added
- Git LFS support through git's own filters and hooks. New `Lfs` API: install check, track/untrack, locks, pull/fetch.
- Credentials through git's credential helpers (Git Credential Manager), SSH remotes, and hooks.
- Repository: open/init/clone with progress; status (porcelain v2); commit with author override, amend and allow-empty; reset.
- Branches: list/create/delete/rename, switch (never discards local changes unless asked), detach, upstream management, remote delete.
- Fetch, pull (merge, rebase, fast-forward only, autostash), and push. Push sets the upstream on the first push and reports per-ref results.
- Merge with abort/continue, rebase with continue/skip/abort, conflict helpers (ours/theirs, conflict-marker check), stash, tags (push/delete remote), history (log, commit, file contents with LFS smudge), config.
- `RunRawAsync` to run any git command.
- Results with status enums for expected outcomes; `GitCommandException` with a `GitErrorKind` for failures; cancellation kills the git process tree.
- Releases attach `FlexGit.dll` (netstandard2.1), `FlexGit.xml` and `FlexGit.pdb`.
- If using in a Unity project: Editor-only package `com.bloknoter.flexgit`, installed by git URL `https://github.com/Bloknoter/FlexGit.git#upm/vX.Y.Z`.

### Removed
- LibGit2Sharp and its native libraries.
