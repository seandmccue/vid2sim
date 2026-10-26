import copy
import json

from backend.pipeline.common import SCHEMA_PATH, REPO_ROOT
from backend.pipeline.validate import validate_spec


def _p1():
    return json.loads((REPO_ROOT / "data/procedures/p1/spec.json").read_text())


def test_schema_is_valid_draft7():
    import jsonschema
    jsonschema.Draft7Validator.check_schema(json.loads(SCHEMA_PATH.read_text()))


def test_handwritten_spec_validates():
    assert validate_spec(_p1()) == []


def test_unknown_action_rejected():
    s = _p1()
    s["steps"][0]["action"] = "unscrew"
    errs = validate_spec(s)
    assert any("/steps/0/action" in str(e) for e in errs)


def test_dangling_part_id_rejected():
    s = _p1()
    s["steps"][0]["part_id"] = "ghost"
    assert any("unknown part" in str(e) for e in validate_spec(s))


def test_box_outside_frame_rejected():
    s = _p1()
    s["parts"][0]["box"] = [0, 0, 5000, 10]
    assert any("exceeds source frame" in str(e) for e in validate_spec(s))


def test_extra_field_rejected_schema_is_frozen():
    s = _p1()
    s["parts"][0]["colour"] = "red"
    assert validate_spec(s)
