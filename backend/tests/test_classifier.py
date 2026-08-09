from app.domain.enums import FeatureKind
from app.services.classifier import classify_all
from tests.fixtures import drawing2_request


def test_open_cylinders_are_notches_and_tangent_radii_are_fillets():
    request = drawing2_request()
    classified = classify_all(request.model_data.features, request.model_data.bounds.dimensions)
    by_id = {feature.feature_id: feature for feature in classified}
    assert by_id["notches"].kind == FeatureKind.EDGE_NOTCH
    assert by_id["edge-fillets"].kind == FeatureKind.FILLET
    assert by_id["notches"].kind != FeatureKind.PLAIN_HOLE
    assert by_id["edge-fillets"].kind != FeatureKind.BOSS


def test_native_hole_wizard_beats_generic_brep_shape():
    request = drawing2_request()
    classified = classify_all(request.model_data.features, request.model_data.bounds.dimensions)
    by_id = {feature.feature_id: feature for feature in classified}
    assert by_id["m6-holes"].kind == FeatureKind.TAPPED_HOLE
    assert by_id["plain-11"].kind == FeatureKind.PLAIN_HOLE

