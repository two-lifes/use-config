# Windows Markdown Storage Feasibility Probe

Status: task 2.3 experiments implemented and verified on the Windows environment below. The initial replacement algorithm was rejected; a guarded, recoverable in-place write now passes the required concurrency scenarios. **Task 2.3 is complete with user-authorized finalization; C2 was open at that time and is now closed by the [stage 2 review](technical-foundation.md#authorized-retry-results-2026-10-25).** No production storage, UI integration, migration, or release compatibility is claimed.

Requirements: [Markdown format v1](markdown-format.md), [product requirements](product.md), and [task sequence](tasks.md). Source: [UseConfig.StorageProbe](../prototypes/UseConfig.StorageProbe/UseConfig.StorageProbe.csproj). The probe is deliberately outside the application solution and has no references to its four projects.

## Recorded environment and results

Executed on 2026-10-25 on Windows build 10.0.26200.0, x64, NTFS, .NET SDK 10.0.402 and runtime 10.0.13.

| Check | Result |
|---|---|
| Restore YamlDotNet 18.1.0 from NuGet | Passed outside the restricted sandbox. |
| Prototype Debug and Release builds | Passed, zero warnings and errors in the final builds. |
| Existing application solution Debug and Release builds | Passed, zero warnings and errors. |
| Prototype and application `dotnet format --verify-no-changes --no-restore` | Passed; no formatter edits were applied. |
| Debug scenario run | 96 passed, 0 failed; executable exit code **0**. |
| Release scenario run | 96 passed, 0 failed; executable exit code **0**. |
| Documentation links, whitespace, and completion marks | Checked; 2.3 completion was subsequently authorized by the user; C2 remains unchecked. |

Exit code 0 means all assertions passed; 1 means at least one test failed or execution is unsupported. Two negative controls still reproduce the rejected algorithm's race and verify its unsafe byte changes. Separate positive tests require the guarded protocol to prevent the same external operations; reproducing the old race cannot substitute for passing them. No original byte assertions were removed from those negative controls.

Historical result before the guarded writer: 70 passed scenario assertions and two reproduced safety blockers, exit code 2. The current harness treats the old algorithm only as a negative control and checks the replacement protocol independently.

Initial package restore could not resolve the package through the configured sources; explicitly selecting NuGet then failed DNS resolution inside the sandbox. Restore succeeded outside it. The sandbox also denied an MSBuild named pipe used by the formatter and hard-link creation; final builds and checks ran outside it. These environment failures are distinct from the rejected replacement algorithm below. The unchanged application solution's successful builds/format check remain the earlier recorded result; this follow-up changes only the isolated prototype and this report.

## What was exercised

### Format and catalog

The codec enforces the v1 envelope and lexical subset before handing the flat mapping to YamlDotNet. Fixed two-space literal-block indentation is converted to equivalent quoted YAML before library parsing because general YAML infers indentation. Canonical output uses quoted strings in schema order. The Markdown body is separate and retains its terminal newlines.

Checks include independently authored expected values, not only parser/serializer agreement: all fields, missing optional fields, Unicode and emoji, surrogate-pair escapes, escaped quotes/backslashes/control characters, credential whitespace, literal-block chomping, extra indentation, BOM/CRLF normalization, and delimiters inside descriptions. Serialized bytes are checked for LF and absence of BOM.

Negative scenarios cover unknown/repeated fields, anchors/aliases/tags, sequences/maps, folded blocks, comments, unsupported escapes, invalid UTF-8, isolated CR, NUL, unpaired surrogates, unsupported versions, malformed UUIDs, invalid names/URLs, and metadata bodies. Catalog scenarios cover duplicate IDs, missing metadata, extra files, nested topics, root-level entries, and an empty catalog. Rejected catalog reads are checked against the original bytes; invalid catalogs cannot export successfully.

The probe rejects an invalid catalog rather than constructing the production UI's partial read-only view. It also validates existing filenames rather than implementing UI filename generation. Those application behaviors remain in stage 3.

### Rejected replacement algorithm (retained negative controls)

The candidate algorithm validates the catalog, writes and flushes a prepared file outside the catalog on the same volume, rechecks the catalog bytes, then calls `File.Replace` with a backup outside the catalog. A successful replacement preserves the old bytes in that backup. A post-replacement comparison detects whether the backup differs from the previously loaded bytes.

Preexisting external changes are rejected without modifying the external state: changed content with its timestamp restored, renamed/moved/removed entries, and changed topic metadata. Incompatible sharing and a read-only destination produce write failures while preserving original content. The read-only-file case is a Windows write-refusal check, not a full ACL or disk-full test.

Two deterministic child-process experiments demonstrate the unresolved check/replace window:

1. The parent finishes its final validation and signals a child through redirected standard input/output.
2. The child either writes new external bytes directly or replaces the entry as an editor would, then acknowledges completion.
3. The parent executes `File.Replace`. The active file now contains the parent's stale edit; the backup contains the external edit.
4. The post-check reports `conflict-after-replace`, but detection is too late to prevent replacement. This was the original blocker; both experiments remain as explicitly named `unsafe-negative-control` tests.

There are no sleeps used to schedule these races. Child readiness, intervention completion, and crash-test pause points have bounded protocol waits. Separate crash experiments terminate the child immediately before and after replacement: the former retains the original active file, and the latter retains the new active file plus the original backup. These tests do not simulate power loss or interruption within the OS replacement operation.

**Conclusion:** checking bytes immediately before `File.Replace`, even with a backup and a post-check, does not meet the no-overwrite requirement in v1. A recoverable copy is not prevention of an external overwrite. `ReplaceCandidate` is retained only for negative controls and must not be reused as production write code.

### Verified guarded writer

`LockedFileWriter.Save` uses the same file handle throughout validation, modification, verification, and error rollback:

1. Pin every directory component from the volume root through the topic and recovery directory, using Windows directory handles without `FILE_SHARE_DELETE`. Reject reparse points using handle metadata.
2. Open the existing target with `FileAccess.ReadWrite` and `FileShare.None`; keep it open until the operation ends. Open `_catalog.md` and `_topic.md` for reading without sharing write/delete access. Existing incompatible handles or writable mappings cause failure before modification.
3. Read the target through that retained handle. Compare loaded bytes plus native volume/file ID and last-write information of the affected files. Identical-byte replacement with a different native file ID is also a conflict. Recheck catalog validity without reopening the locked target.
4. Create `previous.md` and `intent.json` outside the catalog on the same volume with `CreateNew`, then `Flush(true)` both before modifying the target. Retain their handles against write/delete. The manifest identifies the relative entry path and hashes of the old and proposed bytes. It contains no credentials.
5. Truncate and write through the still-exclusive target handle, flush, verify the bytes, and durably create a completion marker. On a normal exception after modification starts, restore the previous bytes and flush through the same exclusive handle. If storage also prevents rollback, the flushed backup remains the recovery source; no error is silently ignored.
6. After process termination, validate backup bytes against the manifest and parse them before recovery. `RecoverToNewFile` uses `CreateNew` to materialize a separate old version; it never automatically writes the live path, which may have received a subsequent external edit.

This changes the write mechanism without relaxing v1: it prevents overwriting external changes while preserving a recoverable saved version after interruption. It is a **recoverable in-place save, not an atomic file replacement**. A crash may leave the active file incomplete; the validated original remains outside the catalog and is recoverable separately. Production recovery UI and lifecycle are later work.

The concurrency guarantee follows Windows sharing rules: [CreateFile sharing](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew) lasts until handle closure and covers write/delete access and writable mappings. Directory handles use [backup semantics](https://learn.microsoft.com/en-us/windows/win32/fileio/obtaining-a-handle-to-a-directory). Sharing applies to the underlying [file stream](https://learn.microsoft.com/en-us/windows/win32/fileio/file-streams), including access through a hard link. No new NuGet dependency is needed for the small Windows interop layer.

Positive tests attempt direct write, editor replacement, rename/delete, topic/root rename, and metadata changes in a synchronized child process while the lock is held. An unexpected successful operation returns `UNEXPECTED-WRITE` and exit code 1. After unlocking, direct write, editor replacement, and directory rename must succeed; a subsequent stale application save must reject the external version unchanged. Directory rename on this Windows build returns native error 5 rather than 32 while pinned; only directory tests accept that distinction, and they require a successful retry after release.

Additional checks cover existing writers, a writable memory mapping after its original file stream has closed, hard-link writes, identical-byte replacement, occupied backup/manifest/completion paths, and an injected mid-write I/O error with rollback. Crash tests terminate the writer after durable preparation, after a flushed partial write, and after complete target writing. Each recovers the original bytes to a new file; a new external edit after the crash survives recovery. Corrupted recovery copies and occupied recovery destinations are rejected.

Directory pins do not freeze unrelated sibling entries or make a whole-catalog snapshot atomic. The verified guarantee protects the entry being saved, its path components, and its root/topic metadata. Arbitrary privileged raw-disk modifications and physical power loss are outside these process/file-API experiments. None of these boundaries permits silently overwriting an externally saved target file.

### ZIP

Export validates and captures the files, writes to a `.partial` archive outside the catalog, and compares the source again before publishing the final ZIP. The test confirms that an observed external change prevents a successful final archive. The ZIP round trip preserves exact source bytes, Unicode, all fields, metadata, and an empty topic represented by its `_topic.md`.

Extraction only targets a new synthetic staging directory. It rejects unsafe paths before writing entries and validates the extracted catalog before returning success. Tests exercise parent traversal, absolute paths, drive-qualified paths, backslash paths, truncated archives, and an unsupported extracted format. Invalid extracted content may remain in staging for diagnosis, but never replaces the source catalog. This is not the stage 8 replacement/restore workflow.

These snapshot comparisons do not prove an atomic snapshot under arbitrary concurrent writers, including change-and-revert (ABA) sequences. The corruption test truncates an archive; it is not comprehensive ZIP payload/CRC validation. Full concurrency, archive-integrity, extraction-resource-limit, and restoration verification remain future work. No complete backup consistency guarantee is inferred from these examples.

## Reproduction

Run in PowerShell from the repository root. The absolute SDK path avoids depending on PATH configuration. Package download needs access to NuGet. Build/format checks may require the host environment if a sandbox prohibits MSBuild named pipes.

```powershell
$probeDotnet = 'C:\Program Files\dotnet\dotnet.exe'
$probeProject = 'prototypes/UseConfig.StorageProbe/UseConfig.StorageProbe.csproj'
& $probeDotnet restore $probeProject --source https://api.nuget.org/v3/index.json
& $probeDotnet build $probeProject --no-restore -c Debug
& $probeDotnet build $probeProject --no-restore -c Release
& $probeDotnet format $probeProject --verify-no-changes --no-restore
& $probeDotnet build UseConfig.slnx --no-restore -c Debug
& $probeDotnet build UseConfig.slnx --no-restore -c Release
& $probeDotnet format UseConfig.slnx --verify-no-changes --no-restore
& ./prototypes/UseConfig.StorageProbe/bin/Release/net10.0/UseConfig.StorageProbe.exe
$LASTEXITCODE
```

Each invocation creates its own GUID-named fixtures beneath the executable's `probe-runs/` directory under ignored `bin/`. Fixtures are retained, not recursively deleted. No real catalog path is accepted by the main runner. Worker mode only accepts paths beneath that same probe directory. All credentials are synthetic. Console output contains case names, fixed error categories, environment versions, and counts; it does not print entry contents, paths, or parser exception messages. Diagnostic files and archives are not tracked or shipped with the application.

The existing solution commands above use the already-restored application skeleton. A clean checkout must restore that solution first. This document is a reproduction record for this experiment, not the project-wide build automation assigned to task 2.4.

## Dependencies and license review

The restored `net10.0` assets contain **YamlDotNet 18.1.0 only**, with no transitive NuGet packages. The package's `net10.0` dependency group is empty; its recorded upstream revision is `748334a8fa7c227740018b284b71ad95cc6b7fc7`. ZIP, hashing, processes, and file operations use the existing .NET runtime. No additional test framework or native package was introduced.

YamlDotNet is justified by the need to decode YAML strings without introducing a second general YAML parser. Its broader syntax is restricted by the prototype before parsing. [Package metadata](https://www.nuget.org/packages/YamlDotNet/18.1.0) declares MIT; the [versioned license](https://github.com/aaubry/YamlDotNet/blob/v18.1.0/LICENSE.txt) permits use and redistribution provided its copyright and permission notice accompany copies or substantial portions. No production dependency decision is implied. If prototype binaries and their YamlDotNet assembly are distributed, include the following notice:

```text
Copyright (c) 2008, 2009, 2010, 2011, 2012, 2013, 2014 Antoine Aubry and contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to
do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Remaining boundaries

No Windows 10 22H2 / Windows 11 21H2 compatibility matrix, UI exercise, actual disk exhaustion, power-loss simulation, production-scale performance, or production recovery protocol was tested. No installed tooling or global configuration was changed. Existing unrelated working-tree edits are outside this task. Finalization does not start task 2.4 or close C2.
