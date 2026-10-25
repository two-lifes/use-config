# Markdown Catalog Format v1

Status: specified in task 2.2 under D-21 and D-22. The isolated task 2.3 prototype verified feasibility; tasks 3.2, 3.4 and 3.5 now implement opening, parsing/serialization and validation with recorded Windows checks. Task 3.6 implements safe writes/recovery; task 3.7 adds verified migration preparation with no production transitions. Only v1 is supported. See [implementation evidence](technical-foundation.md#task-32-completion-and-markdown-dependencies). Requirements: [product.md](product.md), [user stories](user-stories.md), and [decisions.md](decisions.md).

## 1. Catalog layout and identity

```text
catalog/
  _catalog.md
  My VPS/
    _topic.md
    Example Hosting--33333333-3333-4333-8333-333333333333.md
```

- The root contains exactly one `_catalog.md` and zero or more topic directories. An empty catalog contains only `_catalog.md`.
- Each topic contains exactly one `_topic.md` and zero or more website `.md` files. An empty topic still has `_topic.md`. Nested directories and website files directly in the root are invalid.
- Other files, including editor backup files, are structural errors, not silently ignored. Symbolic links, junctions, and other reparse points at the root or within the catalog are invalid; do not traverse them. Operational temporary files must not be treated as catalog entries or exported; the future write implementation must keep them outside the validated catalog tree.
- The topic directory name is its display name. Reject empty names, surrounding whitespace, Windows-invalid path components, and names equal after trimming and ordinal case-insensitive comparison. Do not silently rename externally created topics.
- Topic and website IDs are nonzero UUIDs in the hyphenated `8-4-4-4-12` hexadecimal form. Accept either letter case; write lowercase. IDs must be unique across all topics and entries in the catalog. The root has a version but no ID.
- Identity comes from the internal ID, never a path or display name. A website belongs to its containing topic; no duplicated parent ID is stored in its YAML.
- Application-created website filenames are `<safe-name>--<id>.md`. Replace each Windows-invalid filename character or control character in the name with `_`, trim surrounding whitespace and trailing periods, and use `entry` if the result is empty. Prefix Windows device names with `_`; truncate the prefix to 80 UTF-16 code units without splitting a surrogate pair, then trim trailing spaces/periods again. Preserve the full ID suffix. Path-length/access failures are reported before mutation, not solved by silently changing user data.
- Windows-invalid components include `.` and `..`, `< > : " / \ | ? *`, U+0000–U+001F, trailing spaces/periods, and reserved device names (also with extensions). Apply platform validation as well; the specification does not promise that every syntactically valid path is accessible.
- Manual entry filenames may be any valid Windows filename ending in `.md` (case-insensitive), except reserved `_catalog.md` and `_topic.md`. A filename suffix is informational; it does not override the internal ID. Saving an existing entry retains its filename, including after editing its display name. Creating or moving never overwrites an existing destination; a path collision is reported.

## 2. Text envelope and YAML subset

All three document types start with a YAML front-matter block. The first line is exactly `---`, followed by field lines and a closing line exactly `---`. No leading blank lines or trailing spaces are allowed on delimiter lines. Accept UTF-8 with or without a leading BOM; reject invalid UTF-8. Accept LF and CRLF, normalizing CRLF to LF in values; reject isolated CR. Write UTF-8 without BOM and with LF.

The description starts immediately after the line ending of the closing delimiter. There is no discarded separator blank line. A closing delimiter at end of file means an empty body. Metadata files have no body; entry bodies may contain arbitrary Markdown, including further `---` lines. Body text is stored as text, not interpreted as additional YAML fields or executable content.

This format accepts only the following YAML subset, not arbitrary YAML:

- One flat mapping, with each unindented field written `key: value`. Keys are the exact lowercase names in the schemas below. Field order is arbitrary on read; write the table order. No blank lines or comments within the mapping, nested maps, sequences, multiple documents, aliases, anchors, tags, merge keys, duplicate keys, or unknown keys.
- `format_version` is the unquoted decimal integer `1`. Other decimal versions are recognized as unsupported, not coerced to 1. Quoted versions, booleans, nulls, and floating-point values are invalid.
- All other values are strings: single-line double-quoted or single-quoted scalars, or literal blocks. Unquoted strings and folded blocks (`>`) are not accepted. Empty strings use `""` or `''`.
- In double quotes, support only `\"`, `\\`, `\n`, `\r`, `\t`, and `\uXXXX` escapes; invalid escapes or unpaired Unicode surrogates are errors. A surrogate pair represents one Unicode scalar. In single quotes, `''` represents an apostrophe; backslashes are literal. Literal tabs and control characters are not allowed in quoted source; use escapes. Reject decoded NUL and isolated CR; normalize decoded CRLF to LF as well.
- Literal block headers are `|`, `|-`, or `|+`. Every nonempty content line has at least two leading spaces; remove exactly two. Extra indentation is value content. Blank lines may be empty or contain the two indentation spaces. Tabs cannot provide indentation. The next unindented field or closing delimiter ends the block.
- `|-` removes all terminal LF characters; `|` clips them to one when content has a terminal line break; `|+` retains all terminal LF characters. An empty block decodes to an empty string. Internal blank lines are retained. Use quoted `\n` escapes where precise terminal newlines would be awkward to edit.
- Application serialization uses double-quoted strings with escaped quotes, backslashes, tabs, control characters, and newlines. Unicode printable text is written directly. Literal blocks remain a supported manual input form. Whitespace, quoting style, key order, BOM, and source newline style are not preserved; decoded field values and body text are preserved after the stated newline normalization.

No value is Unicode-normalized, translated, or trimmed on read/write. Leading/trailing spaces in credentials are significant. Required-name validation checks for at least one non-whitespace character without changing the value. Description trailing blank lines are significant and must not be added or removed by serialization. The only exception is the envelope newline before the body, which is structural.

## 3. Schemas

`_catalog.md` contains only `format_version`. `_topic.md` contains `format_version` followed by `id`. Both have an empty body. Every document must declare its own version; all must be version 1.

Website front matter:

| Key | Required | Value |
|---|---|---|
| `format_version` | Yes | Integer `1`. |
| `id` | Yes | Stable, nonzero UUID. |
| `name` | Yes | Non-whitespace string. Duplicate website names are allowed. |
| `url` | Yes | Absolute HTTP(S) URL with a nonempty host. |
| `username` | No | One username; empty by default. |
| `password` | No | One plaintext password; empty by default. No whitespace trimming. |
| `additional_url` | No | Empty or an absolute HTTP(S) URL with a nonempty host. |
| `two_factor_note` | No | Text, including multiple lines; empty by default. |

The Markdown body is the optional multiline description, not a YAML key. Missing optional keys decode to empty strings; canonical output includes all keys. Name, IDs, and URLs must be single-line values. Credentials, the 2FA note, and description may be multiline. URLs must not contain raw whitespace or control characters; encode such characters in the URL. Scheme comparison is case-insensitive. Validation does not fetch the URL or promise successful navigation. Preserve valid URL text without rewriting or canonicalizing it.

## 4. Synthetic examples

All values below are invented demonstration data, not real credentials.

### Minimal catalog

The complete empty catalog consists of `_catalog.md`:

```markdown
---
format_version: 1
---
```

### Complete catalog

Use the layout in section 1 and the same `_catalog.md`. `My VPS/_topic.md`:

```markdown
---
format_version: 1
id: "22222222-2222-4222-8222-222222222222"
---
```

`My VPS/Example Hosting--33333333-3333-4333-8333-333333333333.md`:

```markdown
---
format_version: 1
id: "33333333-3333-4333-8333-333333333333"
name: "Example Hosting"
url: "https://example.com/"
username: "demo-user"
password: "  demo:\"quote\"\\path#123  "
additional_url: "https://example.com/mail"
two_factor_note: |-
  Demo authenticator
  Recovery reminder: пример
---
# Hosting notes
Тестовый сервер — 日本語.

Second paragraph with **Markdown** and searchable keywords.
```

The example password decodes to two spaces, `demo:"quote"\path#123`, then two spaces. The note has one internal LF and no terminal LF. The description starts with `# Hosting notes`; any terminal LF in the actual file belongs to the description.

A minimal website in a topic with valid metadata:

```markdown
---
format_version: 1
id: "44444444-4444-4444-8444-444444444444"
name: "Example"
url: "http://example.org/"
---
```

Its description and all optional fields are empty. Metadata and minimal-entry examples end immediately after the closing delimiter or its single structural newline, not an extra blank body line.

### Invalid examples and expected handling

| Synthetic modification | Expected result |
|---|---|
| Copy the complete website to `Copy.md` without changing its internal ID. | Duplicate ID; neither conflicting entry is selectable as a unique valid record. Catalog becomes read-only. |
| Change any `format_version: 1` to `format_version: 2`. | Unsupported version; do not interpret that document using v1 or rewrite it. Catalog becomes read-only. |
| Delete `_topic.md`, add `notes.txt`, or create `My VPS/Nested/`. | Structural error; preserve every original file and block mutations/export. |
| Add a second `password` key or an unknown `description` key to YAML. | Invalid mapping; do not silently choose a value or discard a key. |
| Replace a URL with `file:///example` or remove `name`. | Invalid entry; preserve source and report the field identifier, not its contents. |
| Replace the description with two paragraphs ending in two LF characters. | Valid; preserve both terminal LF characters on serialization. |

## 5. Manual editing and validation

- To create a catalog manually, create `_catalog.md`; to add a topic, create a valid directory and `_topic.md` with a fresh UUID. To add an entry, create a `.md` file with the required fields and a fresh UUID. A directory without metadata is not automatically initialized during loading.
- Rename a topic directory to change its display name, retaining `_topic.md` and its ID. Edit `name` to rename a website; renaming only its file does not change its display name or identity.
- Move an entry file between existing valid topic directories, retaining its ID. Delete a file to delete its entry; delete a whole topic directory to delete its topic and entries. Do not leave a topic directory without its metadata.
- Copying an entry creates a new record only after assigning a fresh internal UUID and a distinct filename. Copying a topic within the same catalog requires fresh IDs for the topic and every copied entry. Moving or transferring the whole catalog to another computer retains IDs.
- Reload rescans structure, versions, fields, and identity uniqueness. Correct entries are visible only within valid topics and a supported root. An invalid root prevents treating its children as a v1 catalog; report the issue without guessing the format. Duplicate-ID participants are all invalid, not resolved by scan order.
- Any validation or access error puts the entire catalog into read-only mode. Show valid records and actionable errors; block create/edit/save/delete/move, migrations, replacement restore, and ZIP export until errors are corrected externally and a complete reload succeeds. Viewing, search, opening valid links, and copying existing values remain available. Selecting a different catalog remains possible, subject to unsaved-edit prompts. Never export a partial catalog as a successful backup.
- User-facing diagnostics identify an error category, relative file location, and fixed field name when useful, never field values or source excerpts. Technical logs omit user path names as well as entry contents; use operation/error categories. Do not leak parser exception text containing source values into either destination.

## 6. External changes, writes, and backups

Before any mutation, recheck the affected source, topic metadata, and destination against the loaded state. A changed, renamed, moved, or deleted source is a conflict, even if the in-app value appears unchanged. Reread and reload rather than silently overwrite, recreate a deleted record, or use an old path. Keep unsaved editor input when saving is blocked; follow the existing Save / Discard / Cancel requirements. Timestamp checks alone are not a sufficient content-conflict contract. Task 2.3 must validate the detection and replacement approach, including changes during the write window.

Failed or interrupted writes must retain a recoverable previously saved state. This specification defines the required behavior, not a claim that multi-file operations are atomic. Transaction/recovery mechanics belong to the prototype and storage implementation.

A ZIP backup represents a validated, consistent complete catalog: `_catalog.md` at the archive root, topic directories and their metadata, every website file, and empty topics. Do not wrap it in an extra catalog directory or omit format metadata. Detect changes during export; fail rather than label a mixed snapshot consistent. Validate the archive and the complete extracted catalog before replacement, including duplicate paths under Windows case-insensitive rules, unsafe paths, links, and unsupported content. Do not extract outside a staging directory or modify the active catalog during validation.

Restoration follows [product.md](product.md): confirmation, unsaved-edit handling, and a successful backup of the current valid catalog precede replacement. An invalid active catalog cannot be replaced through this flow while its required backup is blocked. A separate manual filesystem copy may preserve it for repair, but is not a validated application export.

Only version 1 is supported initially. No legacy versions or migrations are implied. The [migration preparation foundation](technical-foundation.md#task-37-migration-preparation-repair-2026-10-26) validates version routing and prepares a verified raw-file backup and separate candidate documents for explicitly registered transitions; the production transition registry is empty. A future version requires an explicit compatibility definition, tested migration, compatible safe-write/recovery support, and backup before changes. Never silently downgrade an unsupported file. Original malformed or unsupported files remain unchanged.

## 7. Verification boundary

Task 2.2 checks documentation links, schema completeness, terminology, and consistency with D-21/D-22 and US-01/03/08/09. Examples define expected behavior; they are not executed parser tests.

Task 2.3 must verify Unicode and multiline round trips (including whitespace, escapes, empty values, and terminal newlines), external rename/move/delete conflicts, safe replacement, and ZIP round trips on Windows. Stage 3 adds parsing, validation, and failure tests; stage 8 verifies restoration. No parser library or new dependency is selected by this document.
