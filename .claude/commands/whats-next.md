---
description: Show what to work on next (lowest open milestone and open PRs)
---

Find what's next for PromptForge, without changing anything. If `gh` is missing or not logged in (`gh auth status`), tell me to install it and run `gh auth login`, then stop.

1. Run `gh api repos/Maneet7805/PromptForge/milestones --jq '.[]|[.number,.title,.open_issues]|@tsv'` and take the lowest open version milestone (`vX.Y.Z`, not `Later`).
2. List its open issues: `gh api "repos/Maneet7805/PromptForge/issues?milestone=<number>&state=open&per_page=100" --jq '.[]|select(.pull_request|not)|[.number,.title,([.assignees[].login]|join(","))]|@tsv'`.
3. List open pull requests: `gh pr list`.
4. Read issue #38 (the plan) and any sub-issue you recommend, and `docs/design/rewrite-intent.md` if it touches the rewriting rules.
5. Reply with a proposed order, marking each issue as: claimed by someone else (don't touch), needs measuring with the quality check, or safe to do now. Lead with anything that blocks the rest. Ask which one to start; don't claim or open a branch yet.
