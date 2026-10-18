# PromptForge

@AGENTS.md

Windows tray app. The user selects text in any app, presses a hotkey, and PromptForge rewrites the draft as a clearer prompt for an AI assistant, shown in a review window before anything is pasted. It also has voice dictation and a built-in quality check (eval). Public repo: nothing private goes in commits, issues or PRs.

## Setup (new machine or collaborator)

Install the .NET 10 SDK, and the GitHub CLI (`gh auth login`) for the slash commands. The App only builds on Windows, but Core and its tests run on any OS. Private notes about your own machine go in `CLAUDE.local.md` (gitignored). Not a collaborator? Fork the repo; the same files apply.

## Commands

```
dotnet test tests/PromptForge.Core.Tests          # unit tests; run before every commit
dotnet build                                      # whole solution (Windows only: the app is WinForms)
dotnet publish src/PromptForge.App -c Release -o publish
publish\PromptForge.exe --portable                # try a build without touching the installed copy; quit the installed one first
```

`PromptForge.Core` is `net10.0` and builds and tests anywhere. `PromptForge.App` is `net10.0-windows`. CI ("Build and test", windows-latest) is required before merge.

## Layout

- `src/PromptForge.Core/`: rewriting rules (`Optimization/SystemPrompt.cs`), pipeline, provider clients (`Llm/`), settings, eval runner, update check. No Windows APIs.
- `src/PromptForge.App/`: tray, hotkeys, text capture and paste (`TextAccess/`), review and settings windows (`UI/`), voice, Win32.
- `tests/PromptForge.Core.Tests/`: xUnit. Core logic is tested here; the App is not.
- `eval/`: quality test set (`cases.json`) and how to run it (`eval/README.md`).
- `docs/design/`: design docs. `rewrite-intent.md` sets what the rewriter should and shouldn't add.

## Hard rules

- Never log prompt text, transcripts or API keys. Logs hold events, timings and error types only.
- Never auto-submit anything, and never send keystrokes to terminals.
- Never push to `main`, create tags, or change repo settings or rulesets. Only the owner merges and releases.
- A change to the system prompt or optimiser behaviour is not done until the quality check has measured it before and after (`eval/README.md`). Don't tune the rules against `heldOut` cases.
- Build only things that hold in every situation and belong in the product. No one-off fix for a single reported draft: prefer general rules over phrase lists.
- Keep the user's detail exactly: don't invent facts, numbers or requirements in rewrites.

## Working rules

- Non-trivial change: say what you'll change and why in two or three short bullets, then wait for a go-ahead. Spans more than 3 files, or touches settings or a data format: write a plan first.
- Never rewrite working code that wasn't part of the ask. If you spot a problem outside the ask, tell me; don't fix it. Suggest an issue.
- Before pushing to an existing branch, check its PR is still open (`gh pr view <branch> --json state`). A merged PR's branch is deleted, so a push there goes nowhere: start a fresh branch.
- Never claim "done" or "fixed" without running the tests. Say what you ran and what you did not.

## How to report back

When a task ends, write the report below, not an essay. The reader skims it, so plain words and short lines matter more than detail.

Rules for every report:
- Plain, simple English. Bullets only. At most 5 bullets per section, at most 15 words per bullet, no nested bullets.
- No jargon without a few words explaining it. No pasted logs: quote at most one line of an error.
- Never retell the steps you took. Only say the result.
- No apologies, praise or warm-up lines.

Sections, in this order. Skip a section only where noted:
1. **Problem** (skip if none). One line, first. Use it when something failed, is half done, or is risky. Never open with "Done" if it isn't.
2. **Done**. What changed, as the user would see it. Give the PR or commit number instead of a file list. No PR yet: list the file names, one line each.
3. **Checked**. Two lines: `Tested:` what you ran and the result. `Not tested:` what you could not or did not check. Write "Not tested: nothing" only if it's true.
4. **You need to do**. Anything only the user can do: merge, run something on their machine, supply a key or a decision. Put what blocks the work first. If there is nothing, write "Nothing." Never skip this section.
5. **Confirm next**. Up to 3 numbered choices for the next task, your recommended one first, each with a few words on why. Say what it costs if it uses free-tier limits, an hour of runtime or another person's time. Don't start the next task before the user picks.
6. **Heads up** (skip if none). Risks, things you noticed but didn't touch, open questions. Start each with `[Certain]`, `[Likely]` or `[Guessing]`.

Special cases:
- A tiny task (one small edit, a rename): one line starting "Done:", plus anything under "You need to do". No other sections.
- A question, not a task: answer in a few short bullets. This format does not apply.
- Blocked and can't finish: Problem, what you tried in one line, then "You need to do" with the exact thing that unblocks you.
- You changed something the user didn't ask for: say so under Done.
- The work is a long list (many files, many issues): summarise by group, never list everything.
- More than one question for the user: number them and put them all in "Confirm next", not spread across sections.

Collaborators who prefer a different style can override this in their own `CLAUDE.local.md`.

## Workflow

Follow `CONTRIBUTING.md` ("Working on an issue"): open issue, `/claim`, branch `<type>/<issue#>-<slug>`, draft PR with `Fixes #<issue>`, Conventional Commits, CHANGELOG line under `Unreleased`. Rename any auto-generated branch (`claude/...`) before pushing. Slash commands: `/whats-next`, `/start-issue <number>`.

Before writing code, read `docs/CODING.md`.
