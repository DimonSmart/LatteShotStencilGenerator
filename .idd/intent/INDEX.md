# IDD Intent Index

This index helps humans and Coding Agents find relevant current intent documents.
It is not the source of truth.

Current `IDD-NNNN` documents directly under `.idd/intent/` contain normative
product intent, ADRs, or active spikes.

`GLOSSARY.md`, when present, is an optional unnumbered vocabulary support file
and is not listed in this index.

Git history is the source for deleted or previous document versions.

For shared or cross-cutting product intent, use `Area`, `Notes`, or equivalent
discovery metadata to make the scope obvious to planners. Existing projects do
not need a schema migration; keep their current INDEX shape when it already
communicates this information.

Implementation-only durable constraints belong in the optional
`.idd/engineering/` layer, not in this index.

## Current documents

The `Document` column contains stable `IDD-NNNN` identifiers only. Do not put
filenames, file paths, or Markdown links in this column. Resolve an identifier to
the unique current `.idd/intent/IDD-NNNN.*.md` file when the document must be
opened.

| Document | Role | Area | Notes | Replaces |
| --- | --- | --- | --- | --- |
| IDD-0001 | Spec | Product overview | Browser-only SVG-to-STL workflow and MVP scope | — |
| IDD-0002 | Spec | Reference card | Reference / Donut preset and dimensional layout | — |
| IDD-0003 | Spec | SVG artwork | Safe SVG subset, interpretation, placement, and limits | — |
| IDD-0004 | Spec | Caption | Embossed caption and bundled-font behavior | — |
| IDD-0005 | Spec | Stencil topology | Islands, automatic bridges, and manufacturing warnings | — |
| IDD-0006 | Spec | Preview and export | Shared preview geometry, mesh validation, and binary STL | — |
| IDD-0007 | Spec | Operability | Responsiveness, deterministic behavior, and deployment quality | — |
