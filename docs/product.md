# MVP: Local Website and Credentials Catalog for Windows

Status: a plan for discussion and refinement. This document does not authorize application implementation. Coding starts only after an explicit instruction from the user.

## 1. Purpose and scope

The application helps users return to a topic without having to remember links and credentials. A user opens a folder such as “My VPS”, views their services, opens one or all websites, and copies sign-in details.

The initial release is a standalone application for Windows 11 x64. All user data is stored locally in encrypted form. The catalog requires no server, registration, or internet connection.

The application interface is English, including navigation, forms, dialogs, validation messages, notifications, and installer text. User-entered content is preserved as entered and may use any supported language.

Decisions made:

- A master password unlocks the vault.
- Websites open in the default browser; users sign in manually.
- An encrypted file provides backup and transfer.
- Autofill, a browser extension, synchronization, a mobile version, and shared access are outside the MVP scope.

## 2. User workflow and interface

**First launch:** the user creates and confirms a master password. The application explains that a forgotten password cannot be recovered and creates an empty vault.

**Main window:** folders on the left, websites in the selected folder in the center, and details of the selected website on the right. This is a regular application window; always-on-top behavior is not required for the MVP.

**Folders:**

- Create, rename, and delete.
- A single level, with no nested folders.
- Deleting a nonempty folder requires confirmation showing the number of websites to be deleted.

**Website entry:**

- Name and primary URL are required.
- Username, password, additional URL, and a 2FA note are optional.
- One username/password pair per entry. Multiple accounts can use separate entries for the same website.
- The password is masked, with temporary reveal and separate copy buttons for username and password.
- A separate button opens the additional URL. The application neither retrieves email nor generates 2FA codes.

**Main actions:**

- Add, edit, delete, and move a website to another folder.
- Select a website to view its details.
- Open a website using a button or context menu.
- “Open all” launches the primary URLs in the selected folder; more than 10 websites requires confirmation.
- Search the entire catalog by name, URL, and username. Passwords are excluded.
- Sort folders and websites by name.

Only `https://` and `http://` links may be opened. The application passes them to Windows to launch the browser; it does not manage tabs, profiles, or active sessions. Opening a link does not select an account.

## 3. Storage and data protection

- One local vault per Windows user, stored in the application data directory.
- All content is encrypted: folder names, URLs, usernames, passwords, and notes.
- The master password is never stored. Use an established encryption library, not custom cryptographic algorithms.
- Automatically lock after 5 minutes of inactivity within the application, when Windows locks, and when the computer enters sleep. Provide a manual “Lock” command.
- Clear a copied password from the clipboard after 30 seconds, only if the user has not replaced its contents. Windows clipboard history and other applications may retain a copy; explain this limitation clearly.
- Save changes transactionally. A write failure must not destroy previously saved data.
- No telemetry in the MVP. Technical logs must not contain entry content or secrets.

**Backup:** export creates a consistent encrypted copy of the vault. Restoration requires the master password in effect when the copy was exported.

**Restore:** validate the password, file integrity, and format version before asking the user to confirm replacement of the current vault. Save an encrypted copy of the current vault before replacing it. Merging vaults is outside the MVP scope.

Support changing the master password, requiring the current password. Older backups continue to use their original password.

## 4. Technical foundation and distribution

**Proposed stack:** C# / .NET 10 LTS, WPF, and SQLite with SQLCipher. This supports a native Windows application without a required browser component. The confirmation status of technical choices is recorded in [decisions.md](decisions.md).

Separate the UI, catalog operations, protected storage, and Windows integration. URL launching, clipboard operations, and backup must be testable independently of the UI.

The main entities are folders and website entries with stable identifiers. The storage format is versioned; schema changes use migrations with a backup. There is no public network API in the MVP.

Distribute a signed installer containing the .NET runtime. Manual updates through a new installer must preserve data. Uninstalling must retain the vault by default.

Tens of thousands of users do not require server infrastructure for this product. A production foundation means reliable storage, safe updates, format compatibility, and verifiable builds. Before public release, separately review data protection implementation and dependency licenses.

## 5. Verification and completion criteria

The MVP is complete when the following have been verified:

- Vault creation, subsequent unlock, rejection of an incorrect password, and password changes.
- All folder and website operations persist across restarts.
- Search and opening one website or an entire folder work with the Windows default browser.
- Locking, password masking, and clipboard clearing follow the specified rules.
- A backup restores on another computer with the correct password.
- Incorrect passwords, damaged backups, and unsupported versions leave current data unchanged.
- Interrupted writes and insufficient disk space do not corrupt the last saved state.
- Installation and updates work on a clean Windows 11 system; the catalog works offline.
- Files and logs contain no plaintext user data.
- A catalog of 10,000 entries remains responsive: search completes within 300 ms on a test machine with 4 CPU cores, 8 GB RAM, and an SSD.
- All application-owned interface and installer text is English; user-entered content remains unchanged.

Implementation order: protected storage → folders and entries → browser integration and search → backups → installer and release verification. Application code has not yet been created; implementation starts with a new project. See [tasks.md](tasks.md) for the detailed sequence.
