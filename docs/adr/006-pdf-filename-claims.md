# ADR 006 • PDF filename claims

Accepted 2026-11-18

Other apps can write into `PDFs/` while Plico holds its library lease. The managed Unix move can replace a file that arrives between its destination check and its rename. [Managed Unix move](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Unix.cs#L193-L211)

Plico claims PDF paths with the same filesystem operation for library publication and single-PDF export. Windows uses the managed no-replace move. Linux uses `renameat2` with `RENAME_NOREPLACE`. macOS uses `renamex_np` with `RENAME_EXCL`. On Unix, the source and destination must be on the same filesystem. If that filesystem lacks exclusive rename, Plico can publish a private staged PDF by hard-linking it and cleaning up staging afterward. A live library move requires exclusive rename so the journal can recover it from the disappearance of its source path.

Imports and moves honor paths reserved by catalog records and try the next numbered filename when the destination is occupied. A move records each attempted path in the journal, clears a rejected intent, and commits the successful path with its catalog update. Restores claim the original path and report a conflict if another file arrives first.
