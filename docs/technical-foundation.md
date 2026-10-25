# Technical foundation contracts

Tasks 2.5–2.6 define contracts and policy, not production adapters. The interfaces live in [UseConfig.Catalog](../src/UseConfig.Catalog/UseConfig.Catalog.csproj), which has no WPF or storage dependency. Storage and Windows integration implement these ports in later stages; App composes them. The isolated storage prototype remains experimental. The user approved completion marks for 2.5, 2.6 and C2 on 2026-10-25; stage 3 remains unstarted.

## Desktop ports

- `IUrlLauncher.LaunchAsync` accepts one absolute HTTP(S) URI only; invalid schemes return `InvalidInput` without a Windows handoff. `Accepted` means Windows accepted the handoff, not that the page loaded. Bulk launch, confirmation above ten entries and retry selection belong to later catalog/UI orchestration.
- `IClipboardAccess.CopyAsync` returns an opaque ownership identity, never the text or its hash. Each successful copy replaces ownership, including identical text. `ClearIfOwnedAsync` checks both the native clipboard generation and application ownership while excluding concurrent replacement; it returns `OwnershipLost` without clearing replacement content. Clipboard contention returns `ClipboardUnavailable`. Native implementation must marshal to the appropriate Windows thread. Failed copying does not cancel the previous successful copy's cleanup.
- `IDelayScheduler.DelayAsync` allows deterministic time substitution without WPF. `CredentialTiming` fixes reveal at 30 seconds and clipboard expiry at 60 seconds. Selecting another site cancels reveal; focus loss does not. A new successful password copy cancels the previous expiry and starts another. Closing attempts conditional cleanup. Callers must ignore stale timer completions after cancellation by checking the active operation identity. Username copying does not schedule password cleanup. Clipboard history and other applications may retain copies.

## Catalog, files and ZIP ports

`CatalogDocument` contains a relative path and decoded text, including sensitive fields. It is a transport object, not the stage 3 domain model; do not log it. Snapshots and their collections must be detached, immutable copies for the lifetime of the revision. The default document string representation excludes text.

`ICatalogFiles.ReadAsync` scans without changing files. It returns a snapshot containing readable documents and diagnostics; any diagnostic makes the entire snapshot read-only. Consumers must validate before exposing records and exclude invalid/duplicate records according to the [format](markdown-format.md). A root that cannot be read returns a categorized failure. `ICatalogValidator` validates decoded documents, schema, hierarchy, versions and identities; native filesystem hazards and decoding errors are checked by the file adapter and combined with its diagnostics.

`ApplyAsync` accepts relative create-directory, create-document, replace-document, move and delete changes with an expected catalog revision. The adapter checks the complete catalog, source contents/identity, topic metadata and destinations before mutation and during the write window. Timestamp-only checks are insufficient. Unknown, stale or foreign revisions return `Conflict`; a read-only catalog returns `ReadOnly`. Destination collisions never overwrite existing data. Paths escaping the root, links and reparse points are rejected. Changes describe one intended operation; the adapter must reject an invalid resulting catalog before writing and retain recoverable prior state on failure. No multi-file atomicity is claimed. Success returns a newly validated snapshot/revision. Detailed recovery algorithms remain stage 3 work.

`ICatalogArchives.ExportAsync` requires a valid unchanged revision and writes a consistent complete ZIP to a new destination outside the catalog; collisions fail. `ValidateAsync` stages and validates a complete archive, including safe paths and format, without modifying the catalog. Its opaque handle must bind to the exact validated bytes; changed/unknown handles cannot authorize restoration. `RestoreAsync` requires an unchanged valid current catalog and creates a new prior backup outside the catalog before replacement. Backup failure prevents replacement. Confirmation and unsaved-edit handling are caller prerequisites. The adapter retains recoverable old/new state on interruption; stage 8 defines implementation and staging cleanup. These ports do not promise implemented ZIP behavior.

All asynchronous ports accept cancellation. Observed cancellation is represented by `OperationCanceledException` with the supplied token, not an ordinary failure. Check cancellation before side effects; after a commit boundary, finish necessary recovery and report the actual outcome. Never report cancellation as proof that no write occurred. Expected external failures use `OperationResult<T>.Failure` and fixed `ErrorCode` values; unexpected programming defects are not disguised as success. No arbitrary exception message enters the result. UI diagnostics may include relative file locations and fixed schema field names, never values or source excerpts.

## Per-user settings and errors

Future implementation stores settings at `%LOCALAPPDATA%\UseConfig\settings.json` with `settings_version: 1` and `catalog_path` (absolute selected directory, or null). These are local settings, not catalog metadata, and are not exported. Only remember a selection after successfully opening/creating and validating it; never replace a remembered path with a failed choice. Reject catalog roots equal to, containing, or inside the application settings/log directory, keeping application files outside the catalog.

Missing settings mean no remembered catalog and prompt selection. Malformed or unsupported settings are preserved and produce a recoverable fixed diagnostic; do not silently reset or downgrade them. Explicit selection can establish a replacement only after preserving the previous settings outside the catalog. Missing/inaccessible catalog paths remain remembered until another successful choice; report the problem and allow retry or selection without creating a substitute catalog.

Write settings through a sibling temporary file, flush and safely replace while retaining a recoverable previous file; never truncate the only valid settings copy first. A persistence failure leaves the prior setting intact and reports separately that the current session's catalog is open but its path was not remembered. Settings access failures do not erase catalog data. Recovery details and executable settings tests belong to stage 3.

## Technical logging policy

The future local logger uses `%LOCALAPPDATA%\UseConfig\Logs`. Only UTC timestamp, fixed operation code, fixed error category and application/runtime/OS versions are permitted. Operation codes identify actions such as catalog read/write, ZIP export/restore, settings read/write, clipboard copy/cleanup and URL handoff. No entries, URLs, credentials, paths, arbitrary messages, exception text/stack traces or raw objects. No telemetry or remote sink. UI-only relative-path diagnostics must never be forwarded to this sink.

Use one bounded local log with rotation at 1 MiB and at most three retained files including the active file. Rotation touches only application-owned logs. A logging failure must not abort catalog work or recursively log itself; expose a fixed session diagnostic. This is a specification, not a logger implementation.

## Verification boundary

[ContractChecks](../tests/UseConfig.ContractChecks/UseConfig.ContractChecks.csproj) is a dependency-free executable exercising interface substitution, controlled deadlines, cancellation, ownership, revision conflicts and read-only error transport using in-memory doubles. It does not verify native clipboard behavior, real file writes, ZIP restoration or production timer orchestration. Existing file/ZIP evidence remains in [storage-prototype.md](storage-prototype.md).

The user's verification budget applies to the entire task: **at most five minutes total and five verification command invocations**, including retries, builds, formatting, tests and smoke checks. Do not split commands or restart the budget to evade it. Stop at either limit, report unverified checks and leave unsupported completion claims open. See [build.md](build.md).

### Current verification status (2026-10-25)

Two verification command invocations were attempted on Windows. The first was blocked by PowerShell's authorization policy. A second invocation with process-local execution-policy bypass identified SDK 10.0.402 but stopped at solution restore with exit code 1 and no underlying diagnostic in the captured output. Verification stopped under the repository's two-failed-attempt rule, before the time/call budget was exhausted.

That initial attempt did not compile or execute the new contracts/checks. The user subsequently authorized a new attempt with a fresh five-minute/five-command limit; its results supersede the initial verification gap below.

### Authorized retry results (2026-10-25)

The retry used exactly five shell command invocations and finished within five minutes on Windows 10.0.26200.0 x64 / NTFS, SDK 10.0.402, runtime 10.0.13. Restore failed silently inside the restricted sandbox but succeeded with exit code 0 on the Windows host. This isolates the observed failure to the sandbox environment; the exact underlying restriction was not diagnosed. A diagnostic Python wrapper also encountered a Windows text-decoding error, corrected in the subsequent capture. No dependency versions or application code needed changing.

The complete `scripts/verify.ps1` passed on the host: solution, storage probe and contract checks built in Debug/Release with zero warnings/errors; all formatting checks passed; the storage probe passed 96 scenarios and the contract executable passed 11 checks in each configuration. The hidden WPF window was found by its English title and closed normally through WM_CLOSE with exit code 0. An earlier CloseMainWindow attempt could not address the hidden window and is not counted as a successful smoke check. No visual layout inspection is claimed.

Local documentation file links and `git diff --check` passed before this results-only update. The stage review confirms the format specification, isolated file/ZIP evidence, build automation, layer contracts and settings/logging policy needed for C2. The user subsequently approved marking tasks 2.5–2.6 and C2 complete on 2026-10-25; they are now checked in tasks.md. Minimum supported Windows versions, native production adapters and remote CI were not tested. No commit or push was performed during verification; the user subsequently authorized committing and pushing the completed work.
