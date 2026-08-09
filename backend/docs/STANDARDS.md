# Standards profile notes

Profiles encode presentation and validation choices, not the copyrighted text
of a standard.  Deployments must maintain licensed copies and a company
standards owner must approve each profile/policy ID.

`ISO_METRIC_2025` references ISO 128-3, ISO 129-1, ISO 5455, ISO 5456-2,
ISO 5457, ISO 7200, ISO 8015, ISO 1101, ISO 5458, ISO 5459, ISO 14405-1,
ISO 21920-1, and ISO 22081 at the editions recorded in
`app/standards/profiles.py`.

`ASME_Y14_2018` references the controlled ASME Y14 drawing baseline recorded in
the same file, including Y14.5-2018 and Y14.100-2017.

Important policy constraints:

- ISO does not intrinsically force first-angle, and ASME does not intrinsically
  force third-angle.  Projection is an explicit immutable selection; the
  profile supplies only the company default.
- The backend never emits `ISO 2768-mK`.  ISO 2768-2 is withdrawn.  A selected
  ISO 2768-1 linear/angular class may be used only through a versioned approved
  company policy; general geometrical specification is handled under the
  company interpretation of ISO 22081.
- Datums, GD&T values, fits, thread classes, surface texture, heat treatment,
  coating, and edge requirements are never inferred from shape.

