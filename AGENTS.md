# Agent instructions

Before changing anything in this repository, read [CONTRIBUTING.md](CONTRIBUTING.md) and follow it.

Short version for AI assistants:

1. Work only from an open GitHub issue. Check it is not already assigned to someone else.
2. Claim it before creating a branch, on behalf of the person you are working for ("via Claude, on behalf of @name"). Collaborators comment `/claim`. Fork contributors cannot use `/claim`: they comment that they want the issue, then wait for the owner to assign it, and work in their fork.
3. Branch name: `<type>/<issue-number>-<short-slug>` with type one of `feat`, `fix`, `docs`, `eval`, `infra`, `chore`. Rename auto-generated branch names before pushing.
4. Open a draft PR early; the description includes `Fixes #<issue>`.
5. Never push to `main`, create tags, or change repository settings. Only the owner merges and releases.
