# PromptForge

A personal Windows tray app that turns the rough prompt you're typing into a clear one, in the app you're already using. Type naturally, press **Ctrl+Alt+P**, review the rewrite, press **Ctrl+Enter**, and carry on.

- Works in any normal text box: browser AI chats (ChatGPT, Claude, Gemini, Perplexity), desktop apps, editors, email.
- Always shows the rewrite for review first. It never sends or submits anything.
- Free to run: uses Groq's free API tier by default (no card needed).
- Optional voice dictation with the same free key.

It was built after researching Prompto (joinprompto.com) and Anthropic's prompt engineering guidance, but it's a personal tool: no accounts, quotas, billing or server.

---

## Install

1. Get a free Groq API key at <https://console.groq.com/keys>.
2. Double-click **`dist\PromptForge.exe`** and choose **Yes** to install. It copies itself to `%LOCALAPPDATA%\Programs\PromptForge`, adds a Start menu shortcut, and asks whether to start with Windows. No admin rights are needed.
3. Settings opens on the **Model** tab. Paste the key, click **Test connection**, then **Save**.

If Windows SmartScreen appears, choose **More info → Run anyway**. The exe isn't code-signed, because that needs a paid certificate.

**Update:** double-click a newer `PromptForge.exe` and choose **Yes**. The running copy closes cleanly and restarts; settings and keys are kept.

**Uninstall:** run `installer\uninstall.cmd`. It removes the app, the shortcut and start-with-Windows, and asks whether to delete settings, keys and logs.

## Use

| Shortcut (changeable in Settings) | What it does |
| --- | --- |
| **Ctrl+Alt+P** | Optimise everything in the text box you're typing in |
| **Ctrl+Alt+Shift+P** | Optimise only the selected text. Use this in Word, code editors and long documents |
| **Ctrl+Alt+D** | Voice: tap to start and tap again to stop, or hold while talking and release |

In the review window:

| Key | Action |
| --- | --- |
| Ctrl+Enter | Replace the original with the (edited) improved prompt |
| Esc | Keep the original |
| Ctrl+Shift+C | Copy the improved prompt instead |
| Ctrl+R | Re-run, e.g. after changing Mode/Task or adding details |
| Ctrl+1 / 2 / 3 | Improved / Changes (word diff) / Original |

The **"consider adding"** questions list details the optimiser needed but didn't invent. Type answers in the box under them and press Ctrl+R.

**Modes:** *Light* = minimal edits. *Standard* (default) = clarity, plus structure where it helps. *Detailed* = full structure for complex tasks. The tray menu sets the default.

**Tray menu:** left- or right-click the icon. It has the default mode, *Copy last original text* (undo safety net), *Pause shortcuts*, Settings and logs.

## Where it works

PromptForge reads and writes text by sending Ctrl+A / Ctrl+C / Ctrl+V to the focused app and restoring your clipboard afterwards. That works in almost any text box without per-app integrations.

| Where | Status | Notes |
| --- | --- | --- |
| Notepad (Windows 11) | ✅ Tested | Capture, review, replace, reject, field-changed and repeat cases verified |
| Browser AI chats (ChatGPT, Claude, Gemini, Perplexity) | ⏳ Not yet tested | Uses the same mechanism as Notepad; checklist below |
| VS Code / Cursor chat panels | ⏳ Not yet tested | Use **Optimise selection** in the code editor itself |
| Word, Outlook, Teams | ⏳ Not yet tested | In Word use **Optimise selection**: Ctrl+A selects the whole document |
| Terminals (Windows Terminal, cmd, PowerShell, VS Code terminal) | ⚠️ By design: copy only | PromptForge never sends keys to a terminal (Ctrl+C would interrupt, a multi-line paste can run commands). Copy your prompt first, press Ctrl+Alt+P, then paste the result yourself. Applies to Claude Code / Codex CLI too |
| Password fields | 🚫 Refused | Detected via UI Automation |
| Apps running as administrator | ❌ Not possible | Windows blocks input from normal apps into elevated ones; you get a message and can copy instead |

If a replacement can't be confirmed, the improved prompt goes to your clipboard and a notification says so. Your original text is never lost.

## How the optimiser works

One model call per optimisation. Everything around it is plain code:

1. **Mask pasted material.** Fenced code, stack traces, logs and `<document>` blocks are swapped for `[[BLOCK_n]]` markers before sending. They come back byte-for-byte, and the request uses fewer tokens.
2. **One call** to the model with a fixed system prompt (`src/PromptForge.Core/Optimization/SystemPrompt.cs`). The prompt is static so the provider can cache it. The call classifies the task (coding, debugging, research, study, writing, planning, general), decides how much to change, rewrites, and lists missing details as questions, all as JSON.
3. **Check exact details.** URLs, paths, file names, identifiers, quoted text and numbers from your draft must appear in the rewrite. If any were dropped, one repair call fixes it; otherwise the review window warns you.
4. **Fallback.** If the main model is rate-limited or down, the fallback model is tried. On Groq the fallback has its own separate limits.

The rewriting rules apply Anthropic's guidance:

- Be clear and direct, and state the deliverable.
- Keep the user's reasons.
- Say what to do rather than what to avoid.
- Add structure only when there are several requirements.
- Put pasted material before the request.
- No shouting, no "world-class expert" filler, nothing invented.
- A simple question stays simple; a good prompt comes back unchanged.

## Measured quality (13 Oct 2026, gpt-oss-120b on Groq's free tier)

| Run | Passed all checks | Median / 95th-percentile latency | Notes |
| --- | --- | --- | --- |
| v0.2 optimiser | 48/53 (91%) | 830 ms / 1.5 s | Vague one-liners came back unchanged with no questions; invented "three sources" |
| v0.3 optimiser | 50/53 (94%) | 914 ms / 1.7 s | Vague drafts now get questions; no invented counts. Remaining: "explain databases" gets no questions, one "best practices" phrase, one "e.g. dₖ=2" example value |

The checks look for dropped details, leftover markers, fill-in placeholders, filler phrases, invented numbers, over-expansion, rewriting already-good prompts, and missing questions on vague drafts. Repeated runs of the same draft gave the same task type and verdict each time, with output length varying by roughly ±20%. An LLM judge is available but was unreliable in testing: about half its "invented details" flags were wrong.

## Free-tier limits (Groq, checked 13 Oct 2026)

| Model | Requests/min | Requests/day | Tokens/min | Tokens/day |
| --- | --- | --- | --- | --- |
| openai/gpt-oss-120b (main) | 30 | 1,000 | 8,000 | 200,000 |
| openai/gpt-oss-20b (fallback) | 30 | 1,000 | 8,000 | 200,000 |
| whisper-large-v3-turbo (voice) | 20 | 2,000 | — | 2 h of audio/hour, 8 h/day |

A typical optimisation uses about 1,200 input tokens, most of them the cached system prompt, which doesn't count toward the limits, plus 150–300 output tokens. Very long drafts can hit the 8K tokens/minute limit. The app then says so: use Light mode, *Optimise selection*, or wait a minute.

Other providers (Settings → Model): **Gemini** (free tier; Google may use free-tier prompts to improve its products), **Ollama** (local and private, but slower and RAM-hungry), or any **custom** OpenAI-compatible API.

## Privacy

- Nothing leaves your PC until you press a shortcut. Then only the text you chose goes to your model provider, and dictated audio goes to your voice provider.
- API keys are encrypted with Windows DPAPI for your Windows account (`%APPDATA%\PromptForge\secrets.dat`).
- No prompt history is stored. Logs (`%APPDATA%\PromptForge\logs`, kept 7 days) contain events and timings only: never prompt text, transcripts or keys.
- Text PromptForge puts on the clipboard is marked to stay out of Windows clipboard history and cloud sync. Text that apps copy when PromptForge reads a field can still appear in clipboard history.
- When *Tell the optimiser what kind of app I'm typing in* is on, a coarse label such as "Claude (browser)" is sent, never the window title.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| Shortcut does nothing | Another app may own it: Settings → Shortcuts shows conflicts on save. Or shortcuts are paused in the tray menu |
| "Couldn't read text here" | The field is empty, or the app runs as administrator. Select the text and use *Optimise selection*, or copy it manually |
| "Rate limit reached" | Free tier: wait a few seconds; the fallback model is used automatically |
| Result copied instead of replaced | Focus changed, the field changed, or the app rejected the paste. Press Ctrl+V |
| Voice: "Couldn't start the microphone" | Windows Settings → Privacy & security → Microphone → allow desktop apps |
| Anything else | Tray → Open logs folder, and check the latest `promptforge-*.log` |

## Build from source

Requires the .NET 10 SDK. Building also works on Linux or macOS via `EnableWindowsTargeting`; running needs Windows.

```
dotnet test tests/PromptForge.Core.Tests
dotnet publish src/PromptForge.App -c Release -o dist/app
```

The output is a single self-contained `PromptForge.exe` (~72 MB; no .NET install needed on the target PC).

**Quality check:** Settings → Data → *Run quality check on a test set…* and choose `eval\cases.json`. It runs 49 prompts across simple, vague, coding, debugging, research, writing, study, planning, strict-constraint, already-good, detail-heavy, long, injection and non-English cases. Each output gets code checks (dropped details, placeholders, filler, over-expansion), plus an optional second-model judge for invented details and answering-instead-of-rewriting. The report lands in `eval\results\<timestamp>\report.md`.

## Project layout

```
src/PromptForge.Core     optimiser pipeline, provider client, settings, eval runner (cross-platform, unit-tested)
src/PromptForge.App      Windows tray app: hotkeys, text capture/replace, review window, settings, voice, self-install
tests/                   unit tests for the core
eval/cases.json          optimiser test set
installer/uninstall.cmd  uninstaller
```
