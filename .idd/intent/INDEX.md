# IDD Intent Index

This index helps humans and Coding Agents find relevant current intent documents.
It is not the source of truth.

Current IDD-NNNN documents directly under .idd/intent/ contain normative product
intent, ADRs, or active spikes.

GLOSSARY.md, when present, is an optional unnumbered vocabulary support file and
is not listed in this index.

Git history is the source for deleted or previous document versions.

For shared or cross-cutting product intent, use Area, Notes, or equivalent
discovery metadata to make the scope obvious to planners. Existing projects do
not need a schema migration; keep their current INDEX shape when it already
communicates this information.

Implementation-only durable constraints belong in the optional
.idd/engineering/ layer, not in this index.

## Current documents

The Document column contains stable IDD-NNNN identifiers only. Resolve an
identifier to the unique current .idd/intent/IDD-NNNN.*.md file when the
document must be opened.

| Document | Role | Area | Notes | Replaces |
| --- | --- | --- | --- | --- |
| IDD-0001 | Spec | Product overview | Browser-only uploaded-template + SVG workflow with STL/3MF output | — |
| IDD-0002 | Spec | Template and coordinates | Uploaded template contract plus reference-card dimensions and orientation | — |
| IDD-0003 | Spec | SVG artwork | Safe SVG import, working rectangle, placement, orientation, and through-cut behavior | — |
| IDD-0004 | Spec | Caption and color | Embossed caption, bundled fonts, placement, and 3MF color regions | — |
| IDD-0005 | Spec | Stencil topology | Physical island detection, automatic bridges, and manufacturability warnings | — |
| IDD-0006 | Spec | 3D preview and export | Interactive 3D result preview plus STL and color/material 3MF export | — |
| IDD-0007 | Spec | Operability | Responsive worker-based geometry workflow, deterministic results, and static deployment | — |
