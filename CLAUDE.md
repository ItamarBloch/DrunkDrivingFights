# Working Rules for Claude (DrunkDrivingFights)

## MANDATORY: Plan before coding — for EVERY task (bugs AND features)

Always follow this order. Do NOT skip to implementation.

1. **Explain why** — root cause of the bug, or how the feature should work and why.
2. **Lay out options** — the possible ways of action to solve it, with trade-offs.
3. **Agree on a plan** — reach a shared understanding with the user.
4. **Only after the user confirms** the cause + chosen approach → implement and start changing files.

Reading and searching files to *form* the diagnosis is allowed and encouraged.
Writing or editing code before step 4 is NOT allowed.

This overrides the "side-branch autonomy" shortcut: even on a safe side branch with no
security risk, still align on cause + plan first. Autonomy means not asking permission for
safe operations — it does NOT mean skipping the diagnosis-and-plan discussion.

Even when a fix "looks like a one-liner": present cause → options → plan, then wait for
explicit approval before touching code.

## UI work

Don't walk the user through Unity Editor UI layout step-by-step. Write the scripts; let the
user do the in-Editor wiring/placement.
