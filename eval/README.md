# Quality check

`cases.json` is the test set. Each case is a draft plus what to check; every case runs 3 times by default, because the model runs at temperature 0.3 and a single run can pass or fail by chance.

## Run it

From the tray: **Developer → Run quality check** (or **Settings → Data → Run quality check**), pick `eval\cases.json`, and say yes to the judge. The report and `results.json` go to `eval\results\<date-time>\`.

From PowerShell, in the repo folder, with PromptForge running or not:

```
& "$env:LOCALAPPDATA\Programs\PromptForge\PromptForge.exe" --eval eval\cases.json eval\results\main --judge | Out-Null
```

| Option | What it does |
| --- | --- |
| `--judge` | Grade each rewrite with the judge model (Settings → Data). Doubles the requests. |
| `--repeat N` | Runs per case (default 3). Cases with their own higher `repeat` keep it. |
| `--system-prompt <file>` | Use these rewriting rules instead of the built-in ones. |
| `--pause-seconds N` | Wait between requests (default 8), to stay inside free-tier rate limits. |

`| Out-Null` makes PowerShell wait until the run finishes (PromptForge is a windowed app, so otherwise PowerShell returns at once and two runs would overlap). `progress.txt` in the output folder shows where it is. A full run with the judge is roughly 230 rewrites plus 230 judge calls, and takes about an hour. Gemini's free tier allows roughly 500 requests a day, so two judged runs (a comparison) fit in one day but not three.

## Set up the judge

The judge should be a different provider from the main model, or the same model family grades its own work. The default judge is Gemini: get a free key (Settings → Model → Google Gemini → Get a key), save it, and leave Settings → Data → Judge on `gemini`. The report's header says whether the judge was independent.

## Compare two versions of the rules

This is how a change to the system prompt gets accepted or rejected.

1. Run the baseline with the old rules, for example v1.0.0's:
   ```
   & "$env:LOCALAPPDATA\Programs\PromptForge\PromptForge.exe" --eval eval\cases.json eval\results\v1.0.0 --judge --system-prompt eval\prompts\v1.0.0.txt | Out-Null
   ```
2. Run the version you're testing. For the built-in rules, leave out `--system-prompt`. For a candidate, save its rules to a file and pass that.
3. Compare:
   ```
   & "$env:LOCALAPPDATA\Programs\PromptForge\PromptForge.exe" --eval-compare eval\results\v1.0.0\results.json eval\results\main\results.json eval\results\compare.md | Out-Null
   ```

`compare.md` lists each case whose pass rate changed. A case is **better** or **worse** only when its pass rate moved by at least half (with 3 runs: 0–1 passes to 2–3, or the reverse); smaller moves, and any change in a case that ran only once, are **noise**. Keep a rule that makes cases better without making any worse, and look at any "worse" case before merging.

Only the system prompt changes between the two runs; the rest of the pipeline (masking, repair pass, checks) is the current build's.

To test a branch before it's released, download its exe from the pull request's CI run (**Checks → Build and test → Artifacts**) and run that exe instead of the installed one.

## Add a case

Every bad rewrite reported on GitHub becomes a case before the rules change. Use the draft as the user typed it, trimmed to what shows the problem. Fields:

| Field | Meaning |
| --- | --- |
| `id`, `category`, `draft` | Required. |
| `mode` | `Light`, `Standard` (default) or `Detailed`. |
| `writtenIn` | The app hint, as the app sends it, e.g. `the Claude desktop app`. |
| `mustContain` | Exact text the rewrite must keep. |
| `maxLengthRatio` | Longest allowed rewrite, as a multiple of the draft's length. |
| `expectMinimalChange` | The draft is already good. |
| `expectQuestions` | The draft is vague, so the review window should ask questions. |
| `repeat` | Minimum runs for this case. |
