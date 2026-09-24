# IDD Engineering

Engineering Rules are current durable implementation guardrails owned by this project.

Read `INDEX.md` first. Use the index for discovery, then read every Always rule
and only semantically relevant Conditional rules.

## Boundary

Use `.idd/intent/` for product behavior, external contracts, and
product-significant constraints. Use `.idd/engineering/` for implementation-only
constraints that a different conforming implementation could otherwise avoid.

Engineering Rules are not product intent, Factory state, verification commands,
task plans, migration progress, or generated descriptions of current code. Git
stores history; there is no Engineering archive.

## Discovery and applicability

- `Always` applies to every implementation change.
- `Conditional` applies only when its human-readable `Applies when` condition
  is semantically relevant.

## Rule format

Rule IDs use `ENG-NNNN` and canonical filenames match
`^ENG-\d{4}\.rule-[a-z0-9][a-z0-9-]*\.md$`. The first heading equals the filename
stem. Operational verification commands belong in `.idd/verification.yaml`.
