## Agent skills

### Issue tracker

Issues and specs live in GitHub Issues; use the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the default canonical labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, and `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repo with root `CONTEXT.md` and `docs/adr/`. See `docs/agents/domain.md`.

## Project source of truth

Before planning, implementing, reviewing, or creating tickets, read:

- `docs/PLAN.md`
- `docs/TECHNICAL.md`

These two files are the authoritative project baseline.

- `docs/PLAN.md` defines WHAT is in scope.
- `docs/TECHNICAL.md` defines HOW it must be implemented.
- Do not redesign settled architecture without explicit human approval.
- Do not add deferred or out-of-scope features.
- Do not silently override decisions in these documents.
- If the two documents conflict, stop and report the conflict before implementing.
- Work on the current GitHub ticket only. Do not start the next ticket early.
