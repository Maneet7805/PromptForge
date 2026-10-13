# Changelog

All notable changes to PromptForge are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-10-13

First public release. It contains everything from the 0.x builds plus the fixes below.

### Added
- Open-source release under the MIT License, with contributing guide, security policy, issue templates and CI.
- Release builds are produced by GitHub Actions from version tags, with a SHA-256 checksum.

### Fixed
- Settings: **Cancel** and **Esc** now close the window. Before, only the title-bar close button did.

### Highlights
- Global shortcuts optimise the focused text box (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>P</kbd>) or the current selection (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>P</kbd>), with a review window (improved / word diff / original) before anything is replaced.
- Replacement is verified by reading the field back. Windows 11 Notepad's delayed paste is handled with delayed clipboard rendering, and the clipboard is restored afterwards.
- The optimiser classifies the task, asks questions instead of inventing details, keeps exact details (with a repair pass), and holds back pasted code and traces so they come back byte-for-byte.
- Groq (free tier) by default, with an automatic fallback model. Gemini, Ollama and any OpenAI-compatible API are also supported.
- Voice dictation via Whisper, tap-to-toggle or hold-to-talk.
- The app installs and updates itself: no admin rights, a Start menu shortcut, and optional start with Windows.
- Built-in quality check over a 49-prompt test set.

## [0.3.1] - 2026-10-13

### Fixed
- Pasted material that the model placed mid-sentence is restored on its own lines.

## [0.3.0] - 2026-10-13

### Changed
- Vague drafts now get follow-up questions instead of coming back unchanged; no invented numbers; no questions the draft already answers.

## [0.2.0] - 2026-10-13

### Added
- Verified paste via delayed clipboard rendering, self-install/update, quality check from the tray, Settings brought to the front on relaunch.

## [0.1.0] - 2026-10-12

### Added
- Initial personal build: core optimiser, tray app, review window, settings, voice and eval runner.

[1.0.0]: https://github.com/Maneet7805/PromptForge/releases/tag/v1.0.0
[0.3.1]: https://github.com/Maneet7805/PromptForge/commits/main
[0.3.0]: https://github.com/Maneet7805/PromptForge/commits/main
[0.2.0]: https://github.com/Maneet7805/PromptForge/commits/main
[0.1.0]: https://github.com/Maneet7805/PromptForge/commits/main
