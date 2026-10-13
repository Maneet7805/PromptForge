# Security policy

## Supported versions

Only the [latest release](https://github.com/Maneet7805/PromptForge/releases/latest) gets fixes. To update, download the new `PromptForge.exe`, run it and choose **Yes**.

## Reporting a vulnerability

**Please don't open a public issue for security problems.** Report them privately through [GitHub's private vulnerability reporting](https://github.com/Maneet7805/PromptForge/security/advisories/new).

Include what you found, the steps to reproduce it, and the impact you expect. You should get a reply within a week. Once a fix is released, you'll be credited in the release notes unless you'd rather not be.

## What PromptForge does with your data

This is useful context for judging a report:

- **API keys** are encrypted with Windows DPAPI (`CurrentUser` scope) in `%APPDATA%\PromptForge\secrets.dat`. Only your Windows account can decrypt them.
- **Prompt text** is sent only when you press a shortcut, and only to the provider you configured. It's never written to disk or logs.
- **Logs** (`%APPDATA%\PromptForge\logs`, kept 7 days) record events and timings only.
- **Keystrokes:** PromptForge sends <kbd>Ctrl</kbd>+<kbd>A</kbd>/<kbd>C</kbd>/<kbd>V</kbd> to the focused app. It refuses password fields, never sends keys to terminals, and never presses Enter or submits anything.
- **Network:** the only connections are the HTTPS calls to your configured model and voice providers. There's no telemetry, update check or server of our own.

## Unsigned executable

Release builds aren't code-signed, so Windows SmartScreen warns on first run. Every release lists a SHA-256 checksum in `SHA256SUMS.txt`. Verify your download in PowerShell:

```powershell
Get-FileHash .\PromptForge.exe -Algorithm SHA256
```

Releases are built from the tagged source by GitHub Actions. You can also build it yourself; see the README.
