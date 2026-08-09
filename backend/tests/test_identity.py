from app.services.classifier import classify_all
from app.services.families import group_families
from app.services.identity import measurand_key, specification_key
from tests.fixtures import drawing2_request


def test_equal_values_on_different_features_are_not_duplicates():
    first = measurand_key(
        configuration="Default",
        feature_ids=["H1"],
        characteristic="CENTER_X",
        geometry_ref_ids=["edge-a"],
        reference_ref_ids=["datum-b"],
        measurement_axis="X",
        controlled_extent="INSTANCE_1",
    )
    second = measurand_key(
        configuration="Default",
        feature_ids=["H2"],
        characteristic="CENTER_X",
        geometry_ref_ids=["edge-b"],
        reference_ref_ids=["datum-b"],
        measurement_axis="X",
        controlled_extent="INSTANCE_1",
    )
    assert first != second
    assert specification_key({"nominal": 30}) == specification_key({"nominal": 30})


def test_family_grouping_deduplicates_same_persistent_instance_reference():
    manifest = drawing2_request().model_data
    duplicate = manifest.features[2].model_copy(deep=True)
    duplicate.feature_id = "duplicated-pattern-owner"
    manifest.features.append(duplicate)

    classified = classify_all(manifest.features, manifest.bounds.dimensions)
    families = group_families(classified)
    diameter_11 = next(family for family in families if family.specification.diameter == 11.0)

    assert diameter_11.instance_count == 4
    assert len(diameter_11.entity_refs) == 4
    assert len(diameter_11.centers) == 4
