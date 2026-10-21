# Quality check

`cases.json` is the test set. Each case is a draft plus what to check; every case runs 3 times by default, because the model runs at temperature 0.3 and a single run can pass or fail by chance.

## Run it

From the tray: **Developer → Run quality check** (or **Settings → Data → Run quality check**), pick `eval\cases.json`, and say yes to the judge. The report and `results.json` go to `eval\results\<date-time>\`.

From PowerShell, in the repo folder, with PromptForge running or not:

```
& "$env:LOCALAPPDATA\Programs\PromptForge\PromptForge.exe" --eval eval\cases.json eval\results\main --judge
```

| Option | What it does |
| --- | --- |
| `--judge` | Grade each rewrite with the judge model (Settings → Data). Doubles the requests. |
| `--repeat N` | Runs per case (default 3). Cases with their own higher `repeat` keep it. |
| `--system-prompt <file>` | Use these rewriting rules instead of the built-in ones. |
| `--pause-seconds N` | Wait between requests (default 8), to stay inside free-tier rate limits. |
| `--include-held-out` | Also run the held-out cases. Only for the final measurement of a change. |

The command returns when it's done; `progress.txt` in the output folder shows where it is. A full run with the judge is roughly 230 rewrites plus 230 judge calls, and takes about an hour.

**Free-tier limits.** Groq's free tier runs out partway through the second full run of a day (2026-10-15: from run 153 of the second run). So run the two sides of a comparison on different days, or use `--repeat 2`. Gemini (the judge) allows roughly 500 requests a day.

When a provider says to wait, the runner waits (up to 20 minutes, 3 times per call) and retries the same run. When the wait is longer, the day's allowance is gone: the run stops, keeps what finished, and `progress.txt` and the report say which cases didn't finish. Runs that still errored are left out of pass rates and comparisons, never counted as failures.

## Set up the judge

The judge should be a different provider from the main model, or the same model family grades its own work. The default judge is Gemini: get a free key (Settings → Model → Google Gemini → Get a key), save it, and leave Settings → Data → Judge on `gemini`. The report's header says whether the judge was independent.

## Compare two versions of the rules

This is how a change to the system prompt gets accepted or rejected.

1. Run the baseline with the old rules, for example v1.0.0's:
   ```
   & "$env:LOCALAPPDATA\Programs\PromptForge\PromptForge.exe" --eval eval\cases.json eval\results\v1.0.0 --judge --system-prompt eval\prompts\v1.0.0.txt
   ```
2. Run the version you're testing. For the built-in rules, leave out `--system-prompt`. For a candidate, save its rules to a file and pass that.
3. Compare:
   ```
   & "$env:LOCALAPPDATA\Programs\PromptForge\PromptForge.exe" --eval-compare eval\results\v1.0.0\results.json eval\results\main\results.json eval\results\compare.md
   ```

`compare.md` lists each case whose pass rate changed. Errored runs are left out; a case with no clean runs on one side, or fewer than two because of errors, is **not comparable** and needs re-running. A case is **better** or **worse** only when its pass rate moved by at least half (with 3 runs: 0–1 passes to 2–3, or the reverse); smaller moves, and any change in a case that ran only once, are **noise**. Keep a rule that makes cases better without making any worse, and look at any "worse" case before merging.

Only the system prompt changes between the two runs; the rest of the pipeline (masking, repair pass, checks) is the current build's.

Both sides must be graded by the same judge. When the judge's instructions change, re-judge the older run (below) before comparing, or the comparison measures the judge, not the rules.

## Re-judge a saved run

`--rejudge` grades the rewrites saved in a run's `results.json` again, with this build's checks and judge. It doesn't call the optimiser, so it costs only judge calls (about one per run) and no Groq allowance.

```
& "$env:LOCALAPPDATA\Programs\PromptForge\PromptForge.exe" --rejudge eval\results\before\results.json eval\cases.json eval\results\before-rejudged | Out-Null
```

It writes `report.md` and `results.json` to the new folder, which must differ from the run's own folder. Runs whose case is no longer in `cases.json`, or whose draft has changed since, are left out and listed in the report. Runs saved before the draft was recorded in `results.json` are graded against today's drafts.

## Check the judge against your own grades

Grade some rewrites yourself, blind (without looking at the judge's verdict), in a CSV kept outside the repo:

```
id,run,added,left_out,changed_kind
dns-basic,1,no,no,no
drive-organise,2,no,yes,
```

Each answer is `yes` or `no`; leave it blank if you didn't grade that question. `added`: the rewrite added something you didn't mean (the judge's "added requests" or "invented details"). `left_out`: it dropped or under-delivered something you meant ("dropped a stated requirement" or "under-delivered"). `changed_kind`: it changed the kind of message ("changed the kind of request", "written as the AI's reply" or "answered instead of rewriting"). Then:

```
& "$env:LOCALAPPDATA\Programs\PromptForge\PromptForge.exe" --rejudge eval\results\before\results.json eval\cases.json eval\results\calibration-1 --grades D:\grades.csv | Out-Null
```

`calibration.md` shows, for each question, how often the judge agreed with you and which runs it got wrong. Change the judge's instructions (`JudgeInstructions` in `src/PromptForge.Core/Eval/EvalRunner.cs`) and re-judge until it mostly agrees; only then trust its numbers.

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
| `expect` | How deep the ideal rewrite goes: `Keep` (asks for nothing new: fix the wording only), `Clarify`, `SpellOut` (turn a stated goal into the steps or checks it requires), `Propose` (suggest a structure for an open-ended request) or `Write` (the draft is a brief for a prompt, #43). See `docs/design/rewrite-intent.md`. A `Keep` case fails if it's rewritten or grows past 1.6x. |
| `mustCover` | What the ideal rewrite must cover, in plain words. The judge reads it, with `expect`, and flags the rewrite as under-delivered if an item is missing (#34). Not checked by code. |
| `heldOut` | Left out of runs unless you pass `--include-held-out`. These cases are for the final measurement of a rules change, so the rules can't be tuned to them. Don't look at their results while changing rules. |
| `source` | Where the case comes from: an issue, the user's own draft, or a pattern found in #32. |

`cases-write.json` holds briefs for write mode (#43): drafts that ask for a prompt to be written. Run them once write mode exists.
