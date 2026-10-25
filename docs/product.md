# MVP: Local Website and Credentials Catalog for Windows

Status: approved MVP requirements; stage 1 is complete. Application code has not been created. The current instruction closes planning through documentation only; stage 2 starts on a separate instruction or `next-task` invocation.

## 1. Purpose and scope

The application helps users return to a topic without having to remember links and credentials. A user opens a folder such as “My VPS”, views their services, opens one or all websites, and copies sign-in details.

The initial release is a standalone Windows desktop application that must not be limited to recent Windows 11 releases. The agreed compatibility target is Windows 10 22H2 and Windows 11 from 21H2; the agreed architecture is x64. The approved stack and decision history are recorded in [decisions.md](decisions.md). Compatibility must be verified with the chosen runtime, native dependencies, and installer. All user data is stored locally in encrypted form. The catalog requires no server, registration, or internet connection.

The application interface is English, including navigation, forms, dialogs, validation messages, notifications, and installer text. User-entered content is preserved as entered and may use any supported language.

Decisions made:

- A master password unlocks the vault.
- Websites open in the default browser; users sign in manually.
- An encrypted file provides backup and transfer.
- Autofill, a browser extension, synchronization, a mobile version, and shared access are outside the MVP scope.

## 2. User workflow and interface

**First launch:** the user creates and confirms a master password of 4–128 characters (Unicode code points), without mandatory character-class rules. Spaces are allowed; do not trim, truncate, or alter the password. The application explains that a forgotten password cannot be recovered and creates an empty vault.

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
- Descriptions can be edited and viewed in entry details, persist after restart, and are included in encrypted backup and restore.
- Sort folders and websites by name. An empty search returns to the selected folder; no matches shows an empty state with a Clear search action.
- Run one application instance per Windows user. A repeated launch activates the existing window without unlocking it.

Only `https://` and `http://` links may be opened. The application passes them to Windows to launch the browser; it does not manage tabs, profiles, or active sessions. Opening a link does not select an account. Open all is disabled for an empty folder. If some launches fail, continue with the rest, report successful handoffs and failures separately, and allow retrying only failed launches. A successful handoff to Windows does not establish that the website loaded or that sign-in succeeded.

## 3. Storage and data protection

- One local vault per Windows user, stored in the application data directory.
- All content is encrypted: folder names, URLs, descriptions, usernames, passwords, and notes.
- The master password is never stored. Use an established encryption library, not custom cryptographic algorithms.
- Lock on in-app inactivity, Windows lock, or sleep; also provide a manual “Lock” command. The agreed inactivity interval is 15 minutes.
- Clear a copied password from the clipboard only if its contents have not been replaced. The agreed interval is 60 seconds for clipboard expiry and 30 seconds for temporary password reveal. Mask a revealed password earlier when selecting a different website or locking the vault; ordinary loss of application focus alone does not end the reveal. Repeated copying replaces the previous application expiry timer. On lock or close, attempt to clear the application-owned copied password only if its contents remain unchanged. Windows clipboard history and other applications may retain a copy; explain this limitation clearly.
- Save changes transactionally. A write failure must not destroy previously saved data.
- No telemetry in the MVP. Technical logs must not contain entry content or secrets.

**Unsaved edits:** preserve unfinished website entry input as an encrypted draft separate from the saved entry before automatic locking. Offer to resume it only after successful unlocking; never silently commit it to the entry. Saving the entry or explicitly discarding the draft removes the draft. If saving the draft fails, lock without delay and report the failure after unlocking. Manual locking with unsaved input offers Save and lock / Discard and lock / Cancel; a failed explicit save leaves the editor open with an error, unless automatic locking takes priority. Drafts receive the same encryption and access protections as other vault content.

**Backup:** export creates a consistent encrypted copy of the vault. Restoration requires the master password in effect when the copy was exported.

**Restore:** validate the password, file integrity, and format version before asking the user to confirm replacement of the current vault. Save an encrypted copy of the current vault before replacing it. A failed pre-replacement backup prevents replacement. Merging vaults is outside the MVP scope. Support the current format and explicitly supported older formats through tested migrations; reject newer or unsupported formats without changing the active vault. Interrupted restore or migration must retain a recoverable valid old or new state, not a partial vault.

Support changing the master password, requiring the current password. Older backups continue to use their original password. Password changes must also preserve a recoverable valid state on failure or interruption. Remove temporary recovery copies after successful completion; do not retain them indefinitely after changing the password.

## 4. Technical foundation and distribution

**Approved stack:** C# / .NET 10 LTS, WPF, and SQLite with SQLCipher. This supports a native Windows application without a required browser component. Stage 2 selects the exact managed provider and native SQLCipher package and verifies licenses, Windows compatibility, encryption, backup, and rekeying before implementation proceeds on that foundation.

Separate the UI, catalog operations, protected storage, and Windows integration. URL launching, clipboard operations, and backup must be testable independently of the UI.

The main entities are folders and website entries with stable identifiers, plus encrypted drafts stored separately from committed entries. The storage format is versioned; schema changes use migrations with a backup. There is no public network API in the MVP.

Use the working name **Use Config**. Final application name, publisher identity, and signing certificate must be resolved before stage 11; they do not block completion of stage 1. Distribute a signed x64 MSI installer for per-user installation, containing the self-contained application, .NET runtime, and native dependencies. Manual updates through a new installer must preserve data. Uninstalling must retain the vault by default. Test interrupted installation/update separately from interrupted first-launch migration: retain a usable application and a valid vault, with no silent downgrade of the data format.

Tens of thousands of users do not require server infrastructure for this product. A production foundation means reliable storage, safe updates, format compatibility, and verifiable builds. Before public release, separately review data protection implementation and dependency licenses.

## 5. Verification and completion criteria

The MVP is complete when the following have been verified:

- Vault creation, subsequent unlock, rejection of an incorrect password, and password changes.
- All folder and website operations persist across restarts.
- Search and opening one website or an entire folder work with the Windows default browser.
- Locking, password masking, and clipboard clearing follow the specified rules. Encrypted drafts resume after unlocking, never silently change saved entries, and are removed on save or explicit discard; draft-write failures do not delay locking.
- A backup restores on another computer with the correct password.
- Incorrect passwords, damaged backups, and unsupported versions leave current data unchanged.
- Interrupted writes and insufficient disk space do not corrupt the last saved state.
- Installation, updates, and catalog workflows work on clean installations of the oldest agreed Windows versions and a current Windows release; the catalog works offline.
- Files and logs contain no plaintext user data.
- Quick search is verified for each searchable field, including descriptions and additional URLs, and updates after edits, moves, and restoration.
- A catalog of 10,000 entries with representative descriptions remains responsive: search p95 is at most 300 ms on a test machine with 4 CPU cores, 8 GB RAM, and an SSD, measured using the method below.
- All application-owned interface and installer text is English; user-entered content remains unchanged.

**Search measurement:** use a Release x64 build on Windows and record OS, CPU, runtime, build, and dataset details. After 5 warm-up queries, run a fixed set of at least 30 queries 3 times, covering names, both URLs, descriptions, usernames, no matches, and broad matches. Measure from the final input change to results availability and visible list update, including debounce. Require p95 ≤ 300 ms and report median and maximum. Measure vault unlock and first-search timings separately.

Implementation order: protected storage → folders and entries → browser integration and search → backups → installer and release verification. Application code has not yet been created; implementation starts with a new project. See [tasks.md](tasks.md) for the detailed sequence.
