# Engineering-intent contract

CAD topology does not determine design intent. The add-in reads controlled custom properties; absent mandatory intent keeps the drawing in draft/review status.

| Property | Meaning |
|---|---|
| `MI_PART_NUMBER` | Controlled part number |
| `MI_DRAWING_NUMBER` | Controlled drawing number |
| `MI_REVISION` | Revision |
| `MI_DESCRIPTION` | Description |
| `MI_MATERIAL_SPEC` | Material specification, not merely appearance |
| `MI_HEAT_TREATMENT` | Approved heat treatment |
| `MI_COATING` | Approved finish/coating |
| `MI_EDGE_REQUIREMENT` | Approved edge requirement |
| `MI_GENERAL_TOLERANCE_POLICY_ID` | Company-controlled policy ID |
| `MI_INTERNAL_THREAD_CLASS` | Default internal thread class, if policy permits |
| `MI_EXTERNAL_THREAD_CLASS` | Default external thread class, if policy permits |
| `MI_DATUMS_JSON` | Approved datum references and roles; see backend contract |
| `MI_GDT_JSON` | Approved geometric tolerance requirements |
| `MI_SURFACE_TEXTURE_JSON` | Approved surface texture requirements |
| `MI_APPROVED_BY` | Approver identity |
| `MI_APPROVED_AT` | ISO-8601 timestamp |
| `MI_APPROVAL_ID` | Change-control approval ID |

Model material is extracted independently and is not treated as an approved material specification.

`MI_GENERAL_TOLERANCE_POLICY_ID` must exactly match `Preferences.CompanyPolicyId` in the controlled add-in settings. The identifier points to a separately approved company policy; the software does not encode or infer a general tolerance class from the part shape.
