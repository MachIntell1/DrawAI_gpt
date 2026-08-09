# MachIntell SolidWorks manufacturing-drawing add-in

This is a from-scratch SolidWorks x64 add-in for the deterministic v2 drawing-plan contract. It extracts native feature metadata and exact B-rep references, asks the backend for a standards snapshot and drawing plan, creates a new drawing, and proves the executed drawing against that immutable plan.

The add-in intentionally fails closed. It does not:

- identify geometry by a numeric dimension value;
- turn a failed dimension or hole callout into a plain note;
- guess thread classes, datums, tolerances, material requirements, or process requirements;
- reuse a drawing that may contain annotations from an earlier run;
- release a drawing when persistent references, associations, projection, scale, layout, title fields, or approval cannot be verified.

## Supported scope

- SolidWorks saved part documents (`.SLDPRT`), x64, .NET Framework 4.8.
- Native Hole Wizard, Fillet and Chamfer evidence plus conservative B-rep cylindrical evidence.
- ISO metric first-angle and ASME third-angle profiles supplied by the backend.
- Orthographic, projected, isometric, and section views.
- Associative overall, location, size, radius, chamfer, hole and thread requirements.

Assemblies, weldments, sheet-metal bend tables, cast/molded process drawings, model-based definition, and non-axis-aligned datum schemes are rejected as unsupported rather than approximated.

## Workflow

1. Save and rebuild the part. Populate engineering-intent custom properties described in `docs/ENGINEERING_INTENT.md`.
2. Configure an HTTPS backend URL and explicit ISO/ASME drawing templates.
3. Choose **MachIntell > Generate Verified Draft**.
4. Resolve every blocker. Inspect the generated drawing.
5. Choose **MachIntell > Validate and Approve Release**. Approval is never automatic.

See `docs/BUILDING.md`, `docs/SOLIDWORKS_API.md`, and `docs/ACCEPTANCE_TESTS.md`.
