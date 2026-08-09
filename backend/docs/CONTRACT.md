# Drawing contract v2

## Invariants

1. All model-space and sheet-space geometry in the transport contract is in
   millimetres.  `display_value` and `unit` describe the selected display unit.
2. `EntityRef.token` is the base64 value returned from SolidWorks persistent
   reference bytes.  The backend treats it as opaque.
3. A controlling requirement is executable only when every `geometry_ref` and
   `reference_ref` resolves in the exact model configuration.
4. `measurand_key` identifies the physical characteristic.  `specification_key`
   identifies its nominal/tolerance/modifier requirement.  Numerical equality
   is never used as identity.
5. Plain notes are never substitutes for failed dimensions or hole callouts.
6. The standards snapshot is immutable between plan and execution.
7. A screenshot or AI quality score cannot clear a deterministic blocker.

## Feature evidence order

1. Native SolidWorks feature and Hole Wizard metadata.
2. Model PMI/dimensions marked for drawing.
3. B-rep topology with loop, sweep, internality, boundary-opening, and tangency
   evidence.
4. Geometric inference, which is always review-blocking unless approved.

## Topology rules

- A partial cylinder or cylindrical face opening to an outer boundary is an
  edge notch/open slot candidate, not a hole.
- A cylindrical blend tangent to two adjacent faces is a fillet, not a shaft or
  hole.
- A complete, closed internal cylinder is a hole candidate.
- A complete, closed external cylinder is a boss/shaft candidate.
- Native data and topology disagreement is a release blocker.

## Release states

- `DRAFT`: at least one geometry, association, standards, layout, metadata, or
  engineering-intent blocker exists.
- `REVIEW_REQUIRED`: deterministic product definition is complete and only
  recorded human approval remains.
- `RELEASE_READY`: plan and execution evidence pass and human approval is
  recorded.  The plugin must independently set the SolidWorks document release
  property before removing the watermark.

