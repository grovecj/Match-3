---
description: Commit and push changes, open a PR, request a Copilot review, and iterate on its feedback until it stops raising new issues
argument-hint: [optional notes for the PR description, or an issue number to close]
---

# Raise PR

Ship the current branch's work as a reviewed pull request: commit, push, open the PR,
request a Copilot code review, then evaluate and act on every round of Copilot feedback
until Copilot stops raising new issues.

Arguments (optional, may be empty): $ARGUMENTS — extra context for the PR description
(e.g. "Closes #12") or things to call out.

Work out `<owner>/<repo>` with `gh repo view --json nameWithOwner -q .nameWithOwner`
rather than hardcoding it. Copilot's bot login for reviews is
`copilot-pull-request-reviewer[bot]`.

## 1. Commit and push

- Run `git status` and `git diff` to see what's changed. Never run this command on `main`
  or another shared branch that isn't meant to hold this work — if the current branch is
  `main`, stop and ask the user what branch name to use before doing anything else.
- Stage the relevant files by name (not a blind `git add -A`) and check what got staged
  before committing — watch for anything that looks like a secret, credential, or a
  Unity-generated file that shouldn't be tracked (`Library/`, `Temp/`, `Logs/`,
  `UserSettings/`, `*.csproj`, `*.sln*` — see CLAUDE.md).
- If there's nothing to commit (working tree already clean and the branch is already
  ahead of its merge-base with `main`), skip straight to step 2 — don't create an empty
  commit.
- Write a commit message that explains *why*, following this repo's existing commit
  style (`git log --oneline -10` for reference).
- Push with `git push -u origin <branch>`.

## 2. Open the PR

- Check whether a PR already exists for this branch: `gh pr view --json number,url
  2>/dev/null`. If one does, reuse it instead of creating a duplicate — skip to step 3.
- Otherwise create one: `gh pr create --base main --title "..." --body "..." --reviewer
  @copilot`. Base the title/body on the commit(s) in this branch (`git log
  main..HEAD`), fold in any `$ARGUMENTS` context (e.g. a "Closes #N" line), and follow
  the format of recent PRs in this repo if one is visible via `gh pr list --state all
  --limit 5`.
- If `--reviewer @copilot` is rejected by this `gh` version, fall back to `gh pr edit
  <number> --add-reviewer @copilot` right after creating the PR.
- Report the PR URL to the user once it's open.

## 3. Wait for Copilot's first review

Copilot typically takes anywhere from under a minute to several minutes. Poll rather
than assume a fixed wait:

- Every ~30s, check `gh api repos/<owner>/<repo>/pulls/<number>/reviews` for an entry
  with `"user":{"login":"copilot-pull-request-reviewer[bot]"}`.
- Stop polling once that review appears, or after ~10 minutes (20 polls) — if it times
  out, tell the user Copilot hasn't reviewed yet and stop rather than looping forever.
- Note the review's `id`/`submitted_at` — you'll need it in step 5 to detect a genuinely
  *new* review cycle, as opposed to re-finding the one you already processed.

## 4. Read Copilot's findings

For the most recent Copilot review, get its open threads via GraphQL (REST alone won't
tell you resolution state):

```
gh api graphql -f query='
query {
  repository(owner: "<owner>", name: "<repo>") {
    pullRequest(number: <number>) {
      reviewThreads(first: 50) {
        nodes {
          id
          isResolved
          comments(first: 1) {
            nodes { databaseId body author { login } path line }
          }
        }
      }
    }
  }
}'
```

Filter to threads where `isResolved` is false and the first comment's `author.login` is
`copilot-pull-request-reviewer[bot]` (or `Copilot`). If there are none, Copilot had no
findings this round — treat that the same as "no new feedback" (see step 7) and skip to
wrapping up.

## 5. Evaluate and fix each finding

For every open Copilot thread, read the flagged code (don't take the comment's word for
it) and judge it on its merits — Copilot is sometimes right and sometimes wrong or
overly cautious for a learning project (see CLAUDE.md: favor clear, simple code).

- **Valid, worth fixing:** make the smallest correct change that addresses it, matching
  the surrounding code's style. Don't bundle in unrelated cleanup.
- **Not valid, or a deliberate tradeoff for this project:** don't change the code — but
  still explain the reasoning in the reply (step 6), same as a valid finding gets fix
  details.

After going through every open thread, if you made any code changes: stage them, commit
(reference what was addressed), and `git push`. This push is what triggers Copilot's
next review cycle.

## 6. Reply to and resolve every thread

For each thread you evaluated in step 5, regardless of verdict:

- Reply in-thread with what you decided and why:
  `gh api repos/<owner>/<repo>/pulls/<number>/comments/<databaseId>/replies -f
  body="..."`. If a fix was applied, name the commit and summarize the change; if not,
  explain why the current code is fine as-is.
- Resolve the thread: `gh api graphql -f query='mutation {
  resolveReviewThread(input: {threadId: "<thread id from step 4>"}) { thread {
  isResolved } } }'`.

Resolve every thread you replied to, whether or not you changed code — the loop in step
7 only cares about *new* threads on *new* reviews, so leaving old ones open would cause
you to re-process them.

## 7. Iterate until Copilot stops finding things

If step 5 made no code changes (nothing valid to fix, or no open threads at all in step
4), the loop is done — go to step 8.

Otherwise, since you pushed a fix commit, Copilot will likely start a new review cycle:

- Poll `gh api repos/<owner>/<repo>/pulls/<number>/reviews` again (same cadence as step
  3), watching for a *new* review from `copilot-pull-request-reviewer[bot]` — one whose
  `id` differs from every review you've already processed, submitted after your latest
  push.
- Give it the same ~10 minute budget as step 3. If nothing new shows up in that window,
  treat it as convergence (Copilot is done) and go to step 8.
- If a new review does show up, go back to step 4 with it.
- Cap this at 5 review cycles total. If Copilot is still raising new findings after 5
  rounds, stop, summarize the situation for the user, and let them decide whether to
  continue manually — don't loop indefinitely.

## 8. Wrap up

Tell the user, concisely:

- The PR URL.
- How many Copilot review cycles ran.
- What got fixed vs. what was pushed back on and why.
- Current state (e.g. "clean, no open threads" or "stopped after 5 cycles, N threads
  still open").
