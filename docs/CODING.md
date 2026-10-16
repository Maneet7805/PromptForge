# Coding guide

Conventions for this codebase. Short on purpose: if the code and this file disagree, fix whichever is wrong in the same change.

## Where code goes

- Anything that doesn't need Windows goes in `PromptForge.Core`, so it can be unit-tested on any machine. `PromptForge.App` holds only UI, Win32 and OS integration, and calls into Core.
- Core types the App or tests need are `public`; things only the tests need are `internal` (the test project sees them through `InternalsVisibleTo`).
- New provider behaviour goes in `Llm/ProviderPreset.cs` (a preset), not in `if (provider == ...)` checks elsewhere.

## Style

- Follow `.editorconfig`: UTF-8, LF, 4-space indent (2 for json/yml/csproj/xml), file-scoped namespaces, final newline.
- Nullable is on. Don't silence it with `!` unless the line before proves the value exists.
- Prefer `sealed` classes and records for data. Prefer clear names over comments; comment why, not what.
- Add new enum values at the end when something maps onto them by index (`TaskType` is mapped from a dropdown).

## Errors

- Provider failures become `LlmException` with an `LlmErrorKind` (`Auth`, `RateLimit`, `TooLarge`, `Timeout`, `Network`, `Cancelled`, ...). Callers decide on fallback from `WorthFallingBack`; the user sees `UserMessage`. Don't catch `HttpRequestException` or parse provider status codes outside the HTTP clients (`OpenAiCompatibleClient`, `UpdateChecker`).
- A run that errored is not a failed run. The eval report leaves errored runs out of pass rates; keep it that way.
- Don't swallow exceptions silently. Either handle them with a user-visible outcome, or log the type (never the content) and move on.

## Privacy and safety

- `Log` records events, timings and error types. Never log prompt text, transcripts, keys or anything derived from them, including in exception messages you build yourself.
- API keys are stored with DPAPI (`DpapiSecretStore`). Never write a key to settings JSON, logs, test data or a commit. Tests use fake keys.
- No auto-submit (no Enter after pasting) and no keystrokes to terminals.

## Tests

- xUnit, in `tests/PromptForge.Core.Tests`. Test names are sentences with underscores, e.g. `Complete_finish_reason_length_is_a_truncation_error`.
- HTTP is faked with an `HttpMessageHandler` subclass; don't call real providers from tests.
- Every bug fix gets a test that fails without the fix. Check by temporarily reverting the fix.
- Run `dotnet test tests/PromptForge.Core.Tests` before committing. UI and Win32 changes can't be unit-tested here: say in the PR which apps you tried (Notepad, a browser chat and Word cover most cases).

## Changing the rewriting rules

1. The rules live in `Optimization/SystemPrompt.cs` and are one static string so providers can cache the prefix. Anything that varies per request goes in the user message (`UserMessageBuilder`), not in the system prompt.
2. Read `docs/design/rewrite-intent.md` first: it says what a rewrite may add (only what serves a stated goal and changes the result) and what it must never do.
3. Every reported bad rewrite becomes a case in `eval/cases.json` before the rules change.
4. Measure before and after with the quality check (`eval/README.md`): same cases, 3 runs each, judge on a different provider from the optimiser. Keep a change that makes cases better without making any worse; look at every "worse" case before merging.
5. Free-tier limits mean two full judged runs don't fit in one day on Groq. Run the two sides of a comparison on different days. A judged run takes about an hour and uses the owner's free-tier allowance: ask before starting one.
6. Paste the before and after pass counts in the PR.

## Commits, PRs, releases

- Conventional Commits: `feat`, `fix`, `docs`, `eval`, `infra`, `chore`, `test`, with an optional scope: `fix(voice): ignore faint mic noise`.
- One change per PR. Add a line under `Unreleased` in `CHANGELOG.md` for anything a user would notice.
- Releases: see "Releasing (maintainers)" in `CONTRIBUTING.md`. Only the owner creates tags.
