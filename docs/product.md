# MVP: Local Website and Credentials Catalog for Windows

Status: approved MVP requirements revised for plaintext Markdown storage (D-21). Stage 1 records historical planning completion; stage 2 is complete, including the application skeleton, contracts and technical policy. Markdown storage is not implemented. Tasks 2.2–2.4 provide the format, isolated prototype and build automation. The [technical foundation](technical-foundation.md) specifies ports and local settings/logging policy; it does not implement production adapters. Further implementation requires a separate instruction or `next-task` invocation.

## 1. Purpose and scope

The application helps users return to a topic without having to remember links and credentials. A user opens a folder such as “My VPS”, views their services, opens one or all websites, and copies sign-in details.

The initial release is a standalone Windows desktop application that must not be limited to recent Windows 11 releases. The agreed compatibility target is Windows 10 22H2 and Windows 11 from 21H2; the agreed architecture is x64. The approved stack and decision history are recorded in [decisions.md](decisions.md). Compatibility must be verified with the chosen runtime, native dependencies, and installer. All user data, including saved website passwords, is stored locally as plaintext Markdown. The catalog requires no server, registration, or internet connection.

The application interface is English, including navigation, forms, dialogs, validation messages, notifications, and installer text. User-entered content is preserved as entered and may use any supported language.

Decisions made:

- The user selects a local catalog directory; no master password or application locking is used.
- Websites open in the default browser; users sign in manually.
- An unencrypted ZIP archive provides backup and transfer.
- Autofill, a browser extension, synchronization, a mobile version, and shared access are outside the MVP scope.

## 2. User workflow and interface

**First launch:** the user selects an existing catalog directory or creates a new one. The application remembers the path and opens the catalog on subsequent launches without authentication. Explain that files and backups contain readable credentials. An unavailable path produces a recoverable error without silently replacing existing data.

**Main window:** folders on the left, websites in the selected folder in the center, and details of the selected website on the right. This is a regular application window; always-on-top behavior is not required for the MVP.

**Folders:**

- Create, rename, and delete.
- A single level, with no nested folders. Folder names must be unique after trimming surrounding whitespace and comparing without case sensitivity.
- Deleting a nonempty folder requires confirmation showing the number of websites to be deleted.

**Website entry:**

- Name and primary URL are required.
- Each entry has a multiline Description field, separate from its name and 2FA note. It can explain what the website is for and contain searchable keywords. Description is optional so a link can be saved quickly.
- Username, password, additional URL, and a 2FA note are optional.
- One username/password pair per entry. Multiple accounts can use separate entries for the same website; duplicate website names and URLs are allowed.
- The password is masked, with temporary reveal and separate copy buttons for username and password.
- A separate button opens the additional URL. The application neither retrieves email nor generates 2FA codes.

**Main actions:**

- Add, edit, delete, and move a website to another folder.
- Select a website to view its details.
- Open a website using a button or context menu.
- “Open all” launches the primary URLs in the selected folder; more than 10 websites requires confirmation.
- Quick search filters the entire catalog as the user types, matching name, primary URL, additional URL, description, and username. It ignores case and supports partial matches; Ctrl+F focuses the search field. Passwords are excluded. Results remain selectable without blocking typing.
- Descriptions can be edited and viewed in entry details, persist after restart, and are included in ZIP backup and restore.
- Sort folders and websites by name. An empty search returns to the selected folder; no matches shows an empty state with a Clear search action.
- Run one application instance per Windows user. A repeated launch activates the existing window without opening a second catalog writer.

Only `https://` and `http://` links may be opened. The application passes them to Windows to launch the browser; it does not manage tabs, profiles, or active sessions. Opening a link does not select an account. Open all is disabled for an empty folder. If some launches fail, continue with the rest, report successful handoffs and failures separately, and allow retrying only failed launches. A successful handoff to Windows does not establish that the website loaded or that sign-in succeeded.

## 3. Storage and data integrity

- The user chooses the catalog root directory; its path is remembered per Windows user.
- Hierarchy: catalog root → topic directories at one level → one Markdown (`.md`) file per website entry.
- All entry fields, including usernames and website passwords, are plaintext. UI masking does not encrypt files or backups.
- Manual editing outside the application follows the [Markdown catalog format v1](markdown-format.md), specified in task 2.2: restricted YAML front matter, a Markdown description, stable identifiers, metadata files, filenames, and versioning. Any malformed or unsupported content makes the catalog read-only and blocks mutations and export until corrected and reloaded; original files are preserved. An isolated prototype verifies format round trips; production storage remains future work.
- Reloading the catalog reflects valid external edits in details and search, including renamed, moved, or deleted files. Detect conflicts with in-app edits and never silently overwrite external changes or recreate stale records. Report malformed or unsupported files clearly and preserve their original contents.
- Save changes safely: failed or interrupted writes must preserve a recoverable previously saved state.
- No telemetry in the MVP. Technical logs and error messages must not contain entry contents or secrets.
- Clear a copied password after 60 seconds only if clipboard contents have not been replaced. Repeated copying replaces the previous application expiry timer. On close, attempt the same conditional cleanup. Windows clipboard history and other applications may retain a copy; explain this limitation.
- Reveal passwords for 30 seconds, ending earlier on selecting another website. Ordinary focus loss alone does not end reveal.

**Unsaved edits:** before leaving an edited entry, closing the application, or replacing the catalog, offer Save / Discard / Cancel. Save proceeds only on success; a failure preserves the editor input and reports an error. Discard leaves the saved entry unchanged; Cancel aborts the pending action. Persistent drafts are outside the MVP.

**Backup:** export creates a consistent unencrypted ZIP archive of the catalog, including all entry fields and format information. The archive can be restored on another computer without a password.

**Restore:** validate archive integrity, catalog structure, entries, and format version before asking to replace the current catalog. Reject archive paths that could write outside the restoration directory. Save a backup of the current catalog before replacement; a failed backup prevents replacement. Restore replaces rather than merges. Support the current format and explicitly supported older formats through tested migrations; reject newer or unsupported formats without changing the active catalog. Interrupted restore or migration must retain a recoverable valid old or new state.

## 4. Technical foundation and distribution

**Approved stack:** C# / .NET 10 LTS and WPF, with plaintext Markdown files for storage and ZIP archives for backup. Stage 2 specifies the file format, verifies file round trips and native Windows compatibility, and reviews any required dependency licenses. No database engine is required.

Separate the UI, catalog operations, file storage, and Windows integration. URL launching, clipboard operations, and backup must be testable independently of the UI.

The main entities are folders and website entries with stable identifiers. The storage format is versioned; format changes use migrations with a backup. There is no public network API in the MVP.

Use the working name **Use Config**. Final application name, publisher identity, and signing certificate must be resolved before stage 11; they do not block completion of stage 1. Distribute a signed x64 MSI installer for per-user installation, containing the self-contained application, .NET runtime, and native dependencies. Manual updates through a new installer must preserve data. Uninstalling must retain the catalog by default. Test interrupted installation/update separately from interrupted first-launch migration: retain a usable application and a valid catalog, with no silent downgrade of the data format.

Tens of thousands of users do not require server infrastructure for this product. A production foundation means reliable storage, safe updates, format compatibility, and verifiable builds. Before public release, separately review data integrity, credential display, and file handling and dependency licenses.

## 5. Verification and completion criteria

Each task is subject to the user-mandated shared limit of five minutes and five verification command invocations, including retries and all build/test/smoke checks. Stop at either limit and report missing evidence; do not weaken acceptance criteria or claim omitted checks passed. See [build.md](build.md#mandatory-verification-budget).

The MVP is complete when the following have been verified:

- Directory selection/creation, remembered-path reopening, and recovery from unavailable paths.
- All folder and website operations persist across restarts.
- Search and opening one website or an entire folder work with the Windows default browser.
- Password masking, reveal, clipboard cleanup, and Save / Discard / Cancel follow the specified rules.
- A ZIP backup restores on another computer without authentication.
- Malformed files, damaged or unsafe archives, and unsupported versions leave current data unchanged.
- Interrupted writes and insufficient disk space do not corrupt the last saved state.
- Installation, updates, and catalog workflows work on clean installations of the oldest agreed Windows versions and a current Windows release; the catalog works offline.
- Manual file edits are supported; external changes are not silently overwritten, and malformed source files are preserved. Logs and error messages contain no entry contents.
- Quick search is verified for each searchable field, including descriptions and additional URLs, and updates after edits, moves, and restoration.
- A catalog of 10,000 entries with representative descriptions remains responsive: search p95 is at most 300 ms on a test machine with 4 CPU cores, 8 GB RAM, and an SSD, measured using the method below.
- All application-owned interface and installer text is English; user-entered content remains unchanged.

**Search measurement:** use a Release x64 build on Windows and record OS, CPU, runtime, build, and dataset details. After 5 warm-up queries, run a fixed set of at least 30 queries 3 times, covering names, both URLs, descriptions, usernames, no matches, and broad matches. Measure from the final input change to results availability and visible list update, including debounce. Require p95 ≤ 300 ms and report median and maximum. Measure catalog loading and first-search timings separately.

Implementation order: Markdown format and file storage → folders and entries → browser integration and search → ZIP backups → installer and release verification. The four-project skeleton is complete; catalog behavior remains unimplemented. See [tasks.md](tasks.md) for the detailed sequence.
