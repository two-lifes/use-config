---
name: next-task
description: Execute and verify one next unfinished project task when explicitly invoked as $next-task or next-task. Do not activate for discussions or edits of the command.
---

# next-task

Run this repository skill when the user explicitly invokes `$next-task` or sends `next-task` as a command. Creating, editing, or discussing the command does not invoke it. Follow the root [AGENTS.md](../../../AGENTS.md). Resolve repository paths from the repository root; Markdown links below are relative to this skill.

One invocation handles one numbered `X.Y` item from [tasks.md](../../../docs/tasks.md), followed by verification and a proposal to record completion, commit, and push. Creating or editing this workflow does not execute it.

## 1. Locate the next item

1. Read `AGENTS.md`, `docs/tasks.md`, the relevant requirements, and [decisions.md](../../../docs/decisions.md). Inspect Git status and preserve existing user changes.
2. Use `[x]` completion marks in `docs/tasks.md` as the progress record. Select the first unchecked numbered `X.Y` item in document order. Checkpoint rows are not numbered tasks. Do not select the numerically largest completed item and skip earlier gaps.
3. Report the last completed item before the selected item, or that none has been completed, and the selected task ID and title.
4. Inspect existing work for the selected item. If its implementation is already present but its completion mark is pending, verify it and propose recording completion instead of implementing it again or skipping ahead. Do not infer completion from a commit message alone.
5. Check dependencies and prerequisite decisions. If a prerequisite checkpoint is open, establish whether its evidence exists; if so, propose closing it before advancing. If evidence or a material decision is missing, explain the blocker and request the missing information. Do not silently choose an unapproved product decision or bypass the item.
6. If all numbered items are checked, report remaining open checkpoints or that the plan is complete. Do not invent another task.

## 2. Execute exactly one item

- An explicit `next-task` invocation authorizes execution of the selected item, including coding when it is an implementation item. It does not authorize the entire stage, publication, a commit, or a push.
- For item 1.1, the invocation itself supplies the requested implementation instruction; report that result and propose its completion mark. Do not also implement 1.2.
- For items requiring a user's decision, use an existing explicit answer if available; otherwise ask for the missing decision. A command invocation does not confirm every proposal in the plan.
- Complete all work necessary for the selected item within its scope. Fix defects introduced by the change. Avoid unrelated refactoring or changing other tasks.
- Keep application-owned text and project documentation in English. Follow [design.md](../../../docs/design.md) for UI changes.
- Determine whether this is the stage's last numbered item from the actual document structure, not from a fixed task count. For example, stage 8 currently ends at 8.7.

## 3. Verify the result

The user removed the total-duration and command-count verification limits on 2026-10-26 (D-25). Run the checks required for the selected item and its checkpoint. Report omitted checks and leave unsupported completion claims open. Historical budget records remain unchanged.

Use the repository's actual configured commands and available environment. Do not invent successful results or install tools solely for documentation checks.

| Item position | Required verification |
|---|---|
| Not the last numbered item of its stage | Fast checks relevant to the change: formatting/lint/static checks, compilation or type checking where applicable, and focused existing tests. Include functional checks needed to establish that this item works. |
| Last numbered item of its stage | Stage completion review: code quality and diff review, configured formatting/static analysis, build, relevant unit and integration suites, and functional/smoke checks covering the stage's requirements and checkpoint. Include regressions in affected earlier behavior. |
| Documentation or decision-only work | Check links, terminology, task numbering, requirements consistency, and recorded decisions. At the end of the stage, review all stage outcomes and its checkpoint. Code tools are not applicable when no application code exists. |

- The last-item rule expands verification even if the final item's own edits are small. It does not require running unrelated tests or future-stage checks.
- Investigate and fix failures caused by the task, then rerun affected checks. Report unrelated failures separately; never describe failed required checks as passing.
- Windows UI and installer checks require Windows. If required checks cannot run, report them as not run with the reason and leave the affected item/checkpoint unverified. A successful Linux build is not a substitute.
- A stage checkpoint is eligible for completion only when all its numbered items are completed and its stated outcome is verified. Do not close it solely because the last item was attempted.

## 4. Offer documentation updates, commit, and push

After work and verification, provide a concise result containing:

1. Selected task ID, outcome, and changed files.
2. Checks that passed, failed, or could not run, and any blockers.
3. The exact proposed documentation changes: normally `[ ]` → `[x]` for the completed item, plus its checkpoint if eligible. Include a short date/environment/check summary for a completed checkpoint, and any necessary updates to requirements or decisions.
4. A proposed commit message and the intended branch/remote for push, if configured.
5. An offer to apply these documentation updates, commit, and push.

Do not apply completion marks, create commits, or push merely because `next-task` was invoked. The user requested that these actions be offered after execution. Documentation that is itself the selected task may be edited while executing it; its completion mark still follows this finalization step.

If the user has already explicitly authorized these finalization actions for the current work, perform them without asking again. Otherwise wait for their answer after presenting the concrete result. This confirmation boundary comes from the user's requested workflow, not a blanket restriction on reversible documentation edits.

On approval, apply only justified completion marks and agreed documentation changes, inspect the final diff, and commit only the intended files or hunks. Preserve unrelated work; do not use blanket staging. Verify the branch, remote, and outgoing commits before an ordinary push; never force-push as part of this command. If the destination is absent or ambiguous, obtain it before pushing. Report the actual commit and push result, including failures. A request to finalize does not also start the next task.

If implementation or required verification is incomplete, keep completion marks open and report the blocker. Do not offer to record successful completion. Any proposed partial-work commit must be clearly identified as incomplete and separately authorized.
