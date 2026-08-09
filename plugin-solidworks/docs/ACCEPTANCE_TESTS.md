# Acceptance protocol

Automated contract tests are necessary but not sufficient for a manufacturing drawing. Before production use, run these cases in every supported SolidWorks major version and template revision:

1. Through, blind, counterbored, countersunk, tapped, patterned, and Advanced Hole features.
2. Equal-value distinct dimensions and symmetric features; every measurand must survive without value-based deletion.
3. Open cylindrical edge notches versus closed internal holes, and fillets versus bosses.
4. ISO first-angle and ASME third-angle templates; deliberately swap them and confirm a hard failure.
5. Broken/stale persistent references, suppressed features, configurations, imported bodies, and topology ambiguity.
6. A part whose views nearly fill each reserved layout cell; verify real view/annotation envelopes and clearances.
7. Save, reopen, rebuild, change configuration, and regenerate. Associations must remain valid or fail closed.
8. Confirm that draft generation never sets release-ready, and explicit validation/approval is required.

The golden set must be independently reviewed by a qualified drawing checker against the applicable company standard, drawing type requirements, and current purchased standards. Record SolidWorks version, template revision, backend profile digest, model hash, plan digest, and checker approval.
