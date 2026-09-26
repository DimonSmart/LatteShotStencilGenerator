# Engineering Rules Index

This index is a compact discovery projection for current Engineering Rules. Full
rule documents are normative.

The Rule column contains stable ENG-NNNN identifiers only. Resolve each ID to
exactly one current .idd/engineering/ENG-NNNN.rule-*.md file.

Next ID: ENG-0005

| Rule | Applicability | Applies when | Summary |
| --- | --- | --- | --- |
| ENG-0001 | Always | Every implementation task | Use a browser-native TypeScript/JavaScript static app; no C#/.NET/Blazor runtime. |
| ENG-0002 | Conditional | Adding or changing STL, 3MF, SVG, font, or serialization integration | Hide format/font libraries behind project-owned adapters. |
| ENG-0003 | Conditional | Implementing planar or solid geometry, bridges, or booleans | Use manifold-3d/CrossSection as the geometry kernel; do not implement local CSG. |
| ENG-0004 | Always | Every implementation task | Default stack: Vite + Three.js + manifold-3d + opentype.js, worker-based CSG, replaceable standards-compliant 3MF export adapter. |
