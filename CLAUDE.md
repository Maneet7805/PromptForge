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

## Workflow

Follow `CONTRIBUTING.md` ("Working on an issue"): open issue, `/claim`, branch `<type>/<issue#>-<slug>`, draft PR with `Fixes #<issue>`, Conventional Commits, CHANGELOG line under `Unreleased`. Rename any auto-generated branch (`claude/...`) before pushing. Slash commands: `/whats-next`, `/start-issue <number>`.

Before writing code, read `docs/CODING.md`.
