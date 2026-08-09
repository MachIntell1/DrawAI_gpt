from __future__ import annotations

from app.domain.models import ModelManifest, ReferenceScheme


def select_reference_scheme(model: ModelManifest) -> ReferenceScheme:
    approved = [datum for datum in model.engineering_intent.datums if datum.approved]
    if approved:
        by_role = {datum.role: datum for datum in approved}
        labels = {datum.role: datum.label for datum in approved}
        return ReferenceScheme(
            reference_type="APPROVED_DATUM",
            x_origin_ref=by_role.get("X_ORIGIN").feature_ref if by_role.get("X_ORIGIN") else None,
            y_origin_ref=by_role.get("Y_ORIGIN").feature_ref if by_role.get("Y_ORIGIN") else None,
            z_origin_ref=by_role.get("Z_ORIGIN").feature_ref if by_role.get("Z_ORIGIN") else None,
            datum_labels=labels,
            provisional=False,
        )

    refs = {key.upper(): value for key, value in model.bounds.extreme_refs.items()}
    return ReferenceScheme(
        reference_type="PROVISIONAL_GEOMETRIC",
        x_origin_ref=refs.get("X_MIN"),
        y_origin_ref=refs.get("Y_MIN"),
        z_origin_ref=refs.get("Z_MIN"),
        provisional=True,
    )

