"""Schema and cross-reference validation. A spec that fails never reaches the engine."""
from __future__ import annotations

from dataclasses import dataclass

import jsonschema

from .common import ACTION_VOCAB, load_schema


@dataclass
class SpecError:
    path: str
    message: str

    def __str__(self) -> str:
        return f"{self.path}: {self.message}"


def validate_spec(spec: dict) -> list[SpecError]:
    """Returns [] when valid. Each error carries a JSON-pointer-ish path so the dashboard
    can show it on the step that caused it."""
    errors: list[SpecError] = []
    v = jsonschema.Draft7Validator(load_schema())
    for e in sorted(v.iter_errors(spec), key=lambda e: list(e.absolute_path)):
        errors.append(SpecError("/" + "/".join(str(p) for p in e.absolute_path), e.message))
    if errors:
        return errors

    part_ids = {p["id"] for p in spec["parts"]}
    if len(part_ids) != len(spec["parts"]):
        errors.append(SpecError("/parts", "duplicate part id"))
    orders = [s["order"] for s in spec["steps"]]
    if orders != sorted(orders) or len(set(orders)) != len(orders):
        errors.append(SpecError("/steps", "step order must be strictly increasing"))
    for i, s in enumerate(spec["steps"]):
        if s["part_id"] not in part_ids:
            errors.append(SpecError(f"/steps/{i}/part_id", f"unknown part '{s['part_id']}'"))
        if s["end_frame"] < s["start_frame"]:
            errors.append(SpecError(f"/steps/{i}/end_frame", "end_frame before start_frame"))
        if s["action"] not in ACTION_VOCAB:
            errors.append(SpecError(f"/steps/{i}/action", f"'{s['action']}' not in engine vocabulary"))
    for i, p in enumerate(spec["parts"]):
        x1, y1, x2, y2 = p["box"]
        if x2 < x1 or y2 < y1:
            errors.append(SpecError(f"/parts/{i}/box", "x2/y2 must be >= x1/y1 (inclusive corners)"))
        if x2 >= spec["procedure"]["width"] or y2 >= spec["procedure"]["height"]:
            errors.append(SpecError(f"/parts/{i}/box", "box exceeds source frame"))
    return errors


def assert_valid(spec: dict) -> None:
    errs = validate_spec(spec)
    if errs:
        raise ValueError("spec failed validation:\n  " + "\n  ".join(str(e) for e in errs))
