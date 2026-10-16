---
description: Claim an issue and create its branch following CONTRIBUTING.md
argument-hint: <issue-number>
---

Start work on issue #$ARGUMENTS following "Working on an issue" in CONTRIBUTING.md.

0. Run `gh auth status`. If `gh` is missing or not logged in, tell me to install it and run `gh auth login`, then stop.
1. Run `gh issue view $ARGUMENTS`. Stop and tell me if it is closed or already assigned to someone else.
2. Pick the branch type (`feat`, `fix`, `docs`, `eval`, `infra`, `chore`) and a slug of at most five lowercase words. Show me the branch name `<type>/$ARGUMENTS-<slug>` and wait for my OK.
3. Find out whether I can push to this repo: `gh api repos/Maneet7805/PromptForge --jq .permissions.push`.
   - `true` (collaborator): after my OK, comment `/claim` and, on a second line, `via Claude, on behalf of @<my login>` (`gh issue comment $ARGUMENTS --body $'/claim\nvia Claude, on behalf of @<login>'`). Check the bot assigned me.
   - `false` (fork contributor): `/claim` is for collaborators only. After my OK, comment that I'd like to take the issue (same "via Claude, on behalf of @<login>" line), then stop until the owner has assigned it to me.
4. Collaborator: `git switch main && git pull`, then `git switch -c <branch>`. Fork contributor: fork first (`gh repo fork --remote`), then branch from the fork's updated `main` with the same branch name.
5. Read `docs/CODING.md`, then make a short plan for the change. Don't write code until I approve the plan.
6. When there's a first commit, push the branch (to my fork if I'm not a collaborator) and open a draft PR against `Maneet7805/PromptForge` with `Fixes #$ARGUMENTS` in the description.
