---
description: Claim an issue and create its branch following CONTRIBUTING.md
argument-hint: <issue-number>
---

Start work on issue #$ARGUMENTS following "Working on an issue" in CONTRIBUTING.md.

1. Run `gh issue view $ARGUMENTS`. Stop and tell me if it is closed or already assigned to someone else.
2. Pick the branch type (`feat`, `fix`, `docs`, `eval`, `infra`, `chore`) and a slug of at most five lowercase words. Show me the branch name `<type>/$ARGUMENTS-<slug>` and wait for my OK.
3. After my OK: comment `/claim` on the issue (`gh issue comment $ARGUMENTS --body "/claim"`), then check the bot assigned me.
4. `git switch main && git pull`, then `git switch -c <branch>`.
5. Read `docs/CODING.md`, then make a short plan for the change. Don't write code until I approve the plan.
6. When there's a first commit, push and open a draft PR with `Fixes #$ARGUMENTS` in the description.
