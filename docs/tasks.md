# MVP Implementation Sequence

Status: stage 1 is complete; tasks 1.1–1.8 and C1 are checked. Application code has not been created. Stage 2 has not started and requires a separate instruction or `next-task` invocation.

Sources: [product.md](product.md), [US-01–US-10](user-stories.md), [decision statuses D/P](decisions.md), and [design.md](design.md). Stage 1 choices are confirmed in D-15–D-20; checked planning items do not imply implemented features or verified Windows compatibility.

## Maintaining this plan

- Invoke `next-task` in chat to execute one numbered item using the [command workflow](next-task.md). Completion marks, commit, and push are proposed after execution and verification; creating the workflow does not start development.

- Follow the sequence: decisions → foundation → protected storage → catalog → browser and search → backups → release. Verify UI and protection as features become available.
- Mark `[x]` only after completion and verification. Close checkpoints based on the stated outcome, not merely the existence of files.
- Record the check, date, and environment beside completed checkpoints; record the reason and next step for blockers. Leave unverified scenarios open.
- Resolve questions before implementing dependent behavior; independent work may continue. Update the decision log and related requirements when decisions change.
- This plan covers the MVP. Servers, synchronization, autofill, browser extensions, mobile clients, and shared access are excluded.
- Application-owned UI and installer text must be English (D-12); preserve user-entered content without translation.

## 1. Clarify the foundation and unresolved behavior

Dependency: none. Source: approved P-01–P-08 decisions and D-15–D-20 in the decision log.

Stage review (2026-10-25): all remaining choices were explicitly accepted by the user and reconciled across the documents. See the [completion record](decisions.md#stage-1-completion). Final release identity and signing prerequisites have an agreed deadline before stage 11.

- [x] 1.1. Obtain a separate explicit instruction to implement before starting stage 2.
- [x] 1.2. Confirm P-01: x64, C# / .NET 10 LTS, WPF, and SQLite with SQLCipher for Windows 10 22H2 and Windows 11 21H2+; verify actual compatibility in stage 2.
- [x] 1.3. Confirm P-02–P-03: three panes, flat folders, sorting, and one credential pair per entry.
- [x] 1.4. Define the 4–128-character master password range, 30-second reveal, 15-minute inactivity lock, 60-second clipboard expiry, early masking on site selection/lock, and encrypted drafts with manual-lock choices (P-04).
- [x] 1.5. Define repeated launches, duplicate names, empty queries, no results, partial launch failures, and confirmation above 10 sites (P-05).
- [x] 1.6. Define validated replacement with a prior encrypted backup, supported-format migrations, and recovery from interrupted restore/password changes (P-06).
- [x] 1.7. Agree on the search measurement method (P-08), signed per-user x64 MSI and manual update policy (P-07), Use Config as the working name, and deferral of final name/publisher/certificate until before stage 11.
- [x] 1.8. Record answers in `decisions.md` and align `product.md`, `user-stories.md`, and this plan.

- [x] **Checkpoint C1:** implementation is authorized and the technical foundation is defined; remaining release prerequisites have an explicit deadline before stage 11.

Verification (2026-10-25, cloud workspace): stage 1 decisions were checked against the user's answers and approved closure plan. Documentation links/anchors, English text, task IDs/order, and requirement consistency were reviewed. Encrypted drafts replace the rejected discard-on-lock proposal. Tasks 2–12 and their checkpoints remain open. This is documentation completion; code lint/build/runtime checks are not applicable, and Windows compatibility is not yet verified.

## 2. Prepare the technical foundation

Dependency: C1 (complete). P-01 is confirmed; start this stage only when instructed.

- [ ] 2.1. Create the solution and separate the WPF UI, catalog operations, protected storage, and Windows integration.
- [ ] 2.2. Select a compatible .NET provider and native SQLCipher x64 build; review licenses and redistribution rights.
- [ ] 2.3. Use a small technical prototype to verify actual database encryption, incorrect-key rejection, backup operations, and rekeying support.
- [ ] 2.4. Pin dependency versions and document build/test commands on Windows; arrange automated execution of these checks.
- [ ] 2.5. Define interfaces for URL launching, clipboard access, time/inactivity, and file operations so they can be tested independently of the UI.
- [ ] 2.6. Define the Windows user data directory, technical log location, and error handling without entry contents or secrets. Do not add telemetry.

- [ ] **Checkpoint C2:** a minimal application builds and runs on Windows; the chosen SQLCipher integration works, and dependencies and layers are defined.

## 3. Implement protected storage

Dependency: C2 and master password rules. Coverage: US-01 and part of US-10.

- [ ] 3.1. Define folders and entries with stable IDs, folder relationships, all product fields including a separate description, separate encrypted drafts, and a schema version.
- [ ] 3.2. Implement one local vault per Windows user and master password creation/confirmation with 4–128 Unicode code points, no mandatory character classes, and no trimming or truncation.
- [ ] 3.3. Explain before creation that a forgotten master password cannot be recovered.
- [ ] 3.4. Implement unlocking with an established cryptographic library; never store the master password or implement custom cryptographic algorithms.
- [ ] 3.5. Verify encryption of every field and the absence of plaintext in database auxiliary files, temporary files, and logs.
- [ ] 3.6. Apply changes transactionally; preserve the previous committed state when writing fails.
- [ ] 3.7. Prepare versioning and migrations with an encrypted backup before schema changes; do not modify unsupported formats.
- [ ] 3.8. Test master password lengths 3/4/128/129, Unicode and spaces, creation, reopening, mismatched confirmation, incorrect passwords, damaged files, denied write access, insufficient disk space, and interrupted saves.

- [ ] **Checkpoint C3:** US-01 is verified; saved state survives restarts and tested write failures, and no plaintext user data has been found on disk.

## 4. Build the UI foundation

Dependency: C3 and decisions about layout and repeated launches. Rules: `design.md`.

- [ ] 4.1. Create shared WPF resources for the palette, typography, dimensions, spacing, and control styles in the concise design system.
- [ ] 4.2. Build vault creation/unlock screens with local loading indicators and clear errors, using English text.
- [ ] 4.3. Build the main window: folders, website list, and details, with a multiline Description field, Ctrl+F quick search, a resume-draft offer after unlocking, and manual-lock choices.
- [ ] 4.4. Prepare shared inputs, buttons, lists, confirmations, and hover/focus/disabled/loading/empty/error states.
- [ ] 4.5. Implement agreed repeated-launch behavior without allowing inconsistent writes to the same vault.
- [ ] 4.6. Verify keyboard navigation, focus order, long labels, and resizing. Show an honest empty state before entries exist.

- [ ] **Checkpoint C4:** users can create/unlock the vault and enter the catalog shell; core components and states are consistent.

## 5. Implement locking and secret lifecycles

Dependency: C4 and P-04. Coverage: US-07 and the foundation of US-06.

- [ ] 5.1. Implement manual locking and in-application inactivity tracking; use the confirmed 15-minute interval.
- [ ] 5.2. Handle Windows locking and sleep; require the master password on return.
- [ ] 5.3. On lock, close vault access, remove data from the UI, and release accessible resources containing secrets without promising guaranteed erasure of all managed memory.
- [ ] 5.4. Preserve unsaved entry input in a separate encrypted draft before automatic locking; offer resume only after unlocking, without changing the saved entry. Remove drafts on save or explicit discard. A failed draft write must not delay locking; report it after unlocking. Manual lock offers Save and lock / Discard and lock / Cancel.
- [ ] 5.5. Test unlocking again, incorrect passwords, sleep/resume, inactivity timer resets, encrypted draft recovery and removal, manual-lock choices, failed draft writes, and preservation of saved entries.

- [ ] **Checkpoint C5:** every lock trigger is verified on Windows; protected data is unavailable until successful unlocking.

## 6. Implement folders, entries, and credentials

Dependency: C5, duplicate-name rules, and password reveal duration. Coverage: US-02, US-03, US-06.

- [ ] 6.1. Add flat-folder creation, renaming, deletion, selection, and sorting by name; reject duplicate folder names after trimming and ignoring case.
- [ ] 6.2. Show the number of affected websites before deleting a nonempty folder; canceling changes nothing, and folder/entry deletion is consistent.
- [ ] 6.3. Implement adding, editing, deleting, and moving entries between folders; allow duplicate website names and URLs and integrate encrypted draft save/discard cleanup.
- [ ] 6.4. Validate required names and primary URLs; allow only `http://` and `https://`, including for a supplied additional URL.
- [ ] 6.5. Support an optional multiline description, username, password, additional URL, and 2FA note; represent multiple accounts as separate entries.
- [ ] 6.6. Show selected website details with a masked password; end temporary reveal after 30 seconds or earlier on selecting another website or locking. Ordinary focus loss alone must not end the reveal.
- [ ] 6.7. Add separate username/password copy actions, brief confirmation without exposing secrets, and handling for a busy/unavailable clipboard.
- [ ] 6.8. Clear copied passwords after the confirmed 60-second interval only if clipboard contents have not been replaced; test repeated copying and timer interaction with locking.
- [ ] 6.9. Explain Windows clipboard history and third-party application limitations. A 2FA note must not imply email retrieval or code generation.
- [ ] 6.10. Test the full operation cycle across restarts, canceled edits/deletions, validation and save errors, empty optional fields, description persistence after edits/moves/restarts/backup/restore, and clipboard replacement by another application.

- [ ] **Checkpoint C6:** US-02, US-03, and US-06 are verified; catalog data persists, secrets are not inadvertently exposed, and stale timers do not delete new clipboard content.

## 7. Add search and website launching

Dependency: C6, empty-search decisions, and P-05. Coverage: US-04, US-05.

- [ ] 7.1. Implement quick search across names, primary URLs, additional URLs, descriptions, and usernames in all folders; exclude passwords. Update results while typing, support case-insensitive substring matches, and provide Ctrl+F focus without blocking input.
- [ ] 7.2. Sort websites by name and allow navigation from results to details; an empty query returns to the selected folder, and no matches provides a Clear search action.
- [ ] 7.3. Open primary URLs through buttons and context menus in the Windows default browser; provide a separate button for additional URLs.
- [ ] 7.4. Revalidate URL schemes before launching; do not manage browser profiles, accounts, or sign-in forms.
- [ ] 7.5. Implement “Open all” for primary URLs in the selected folder; require confirmation above 10 websites and disable Open all for an empty folder.
- [ ] 7.6. Continue remaining launches after individual failures; report handoff success/failure counts and retry only failures. Do not equate a browser handoff with successful page loading or sign-in.
- [ ] 7.7. Test searches across folders, description-only and additional-URL-only matches, Unicode and empty descriptions, refreshed results after edits/moves/restoration, password exclusion, permitted/forbidden schemes, additional URLs, 0/1/10/11 websites, and canceled bulk launches.

- [ ] **Checkpoint C7:** functional criteria for US-04 and US-05 are verified on Windows; stage 10 measures search performance.

## 8. Implement backups and master password changes

Dependency: C7, P-06, and compatibility rules. Coverage: US-08, US-09.

- [ ] 8.1. Create consistent encrypted exports; test recently written data and destination-file errors.
- [ ] 8.2. Validate the password, integrity, and format version before restoration without changing the current catalog.
- [ ] 8.3. After validation, request replacement confirmation; save an encrypted copy of the current vault first. If that backup fails, do not replace the vault.
- [ ] 8.4. Implement replacement without merging, preserving a recoverable state if interrupted.
- [ ] 8.5. Test transfer to another computer, incorrect passwords, damaged/unsupported backups, cancellation, insufficient space, and interrupted restoration.
- [ ] 8.6. Add master password changes with current-password verification and new-password confirmation; preserve data on failure or interruption.
- [ ] 8.7. Explain and verify that the current catalog accepts only the new password after a change, while older exports retain their original password.

- [ ] **Checkpoint C8:** US-08 and US-09 are verified, including cross-computer transfer and failures; restore and password changes preserve the last saved state.

## 9. Review data integrity and protection

Dependency: C8. Coverage: cross-cutting requirements of US-01 and US-06–US-10.

- [ ] 9.1. Repeat write-failure checks for catalog operations, migrations, password changes, and restoration: insufficient space, access loss, and abnormal termination.
- [ ] 9.2. Verify supported format compatibility and refusal to modify unsupported formats; test recovery from an encrypted backup after a failed migration.
- [ ] 9.3. Verify locking during editing, password reveal/copy, and file operations; inspect encrypted draft persistence/access/cleanup and failed-write reporting without delaying locks.
- [ ] 9.4. Inspect generated files, database auxiliary files, logs, and error messages for plaintext test data; verify that telemetry is absent.
- [ ] 9.5. Conduct a separate pre-release review of data protection: library usage, key management, files, clipboard, locking, and restoration. Record findings and fix release-blocking issues.

- [ ] **Checkpoint C9:** protection and failure-recovery results are recorded; no unresolved issues permit data loss or exposure in tested scenarios. This is not a guarantee of absolute security.

## 10. Verify UI, performance, and offline operation

Dependency: C9 and the P-08 measurement method. Coverage: US-04, US-10, and `design.md`.

- [ ] 10.1. Check every screen on Windows at 1280×800, 1440×900, and 1920×1080, with display scaling and long names/URLs. Verify English UI text and unchanged multilingual user content.
- [ ] 10.2. Complete core workflows using only the keyboard; check dialog focus, contrast, states, and the system animation-disable setting.
- [ ] 10.3. Measure search against the p95 ≤ 300 ms target with 10,000 synthetic entries including representative descriptions, queries matching descriptions/additional URLs, and end-to-end results updates while typing on a machine with 4 CPU cores, 8 GB RAM, and an SSD. Record the build, machine specifications, queries, and results using the agreed method.
- [ ] 10.4. Verify responsiveness of lists, entry selection, saving, and lengthy file operations; resolve UI blocking if needed and repeat affected measurements.
- [ ] 10.5. Without internet access, test creation/unlocking, catalog operations, search, copying, export, and restoration; assess website connectivity separately.
- [ ] 10.6. Map results to US-01–US-10; fix defects and repeat checks for changed scenarios.

- [ ] **Checkpoint C10:** functional scenarios, visual checks, offline operation, and target performance are supported by recorded Windows results.

## 11. Prepare installation and updates

Dependency: C10 and confirmed P-07. Before starting stage 11, resolve the final application name, actual publisher identity, and signing certificate; Use Config remains the working name until then. Coverage: distribution requirements in US-10.

- [ ] 11.1. Prepare a signed English-language per-user x64 MSI for the agreed Windows targets, bundling the self-contained application, .NET runtime, and native SQLCipher dependencies; verify operation without a separately installed SDK/runtime.
- [ ] 11.2. Configure release artifact signing with the publisher certificate and signature verification; record a missing certificate as a signed-release blocker.
- [ ] 11.3. Implement manual updates through a new installer, preserving the catalog and using verified migrations; uninstalling retains the vault by default.
- [ ] 11.4. On clean installations of the oldest agreed Windows versions and a current release, test installation, launch, updates with existing data, interrupted updates, uninstalling, and reinstalling.
- [ ] 11.5. Document a reproducible build process, dependency versions, release artifacts, and redistributed component licenses.
- [ ] 11.6. Prepare concise English installation, backup, restoration, and update instructions, including master password and clipboard limitations.

- [ ] **Checkpoint C11:** the signed installer is verified on clean Windows; updates and uninstalling meet requirements, and the release can be rebuilt.

## 12. Accept the MVP and prepare release

Dependency: C11.

- [ ] 12.1. Collect C1–C11 and US-01–US-10 results, environment versions, and known limitations for the final build. Check every completion criterion in `product.md`.
- [ ] 12.2. Resolve release-blocking defects and repeat affected checks. Explicitly leave unverified items open.
- [ ] 12.3. Update documentation so implemented behavior, decisions, instructions, and this checklist agree.
- [ ] 12.4. Present the ready build, verification results, and limitations to the user for acceptance.
- [ ] 12.5. Publish/release only when instructed; otherwise record artifact readiness without claiming publication.

- [ ] **Checkpoint C12:** the MVP is accepted based on a verified build; release preparation and actual publication are reported separately.
