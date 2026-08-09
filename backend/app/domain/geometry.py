from __future__ import annotations

from math import sqrt
from typing import Literal

from pydantic import BaseModel, Field, model_validator


class Point3(BaseModel):
    x: float
    y: float
    z: float

    def coordinate(self, axis: str) -> float:
        return {"X": self.x, "Y": self.y, "Z": self.z}[axis.upper()]


class Vector3(BaseModel):
    x: float
    y: float
    z: float

    @property
    def magnitude(self) -> float:
        return sqrt(self.x * self.x + self.y * self.y + self.z * self.z)

    def normalized(self) -> "Vector3":
        magnitude = self.magnitude
        if magnitude < 1e-12:
            raise ValueError("direction vector cannot be zero")
        return Vector3(x=self.x / magnitude, y=self.y / magnitude, z=self.z / magnitude)

    @property
    def dominant_axis(self) -> Literal["X", "Y", "Z"]:
        values = {"X": abs(self.x), "Y": abs(self.y), "Z": abs(self.z)}
        return max(values, key=values.get)  # type: ignore[return-value]


class EntityRef(BaseModel):
    """Opaque SolidWorks persistent reference encoded as base64."""

    token: str = Field(min_length=4)
    entity_type: str
    model_configuration: str = "Default"

    @property
    def identity(self) -> str:
        return f"{self.model_configuration}:{self.entity_type}:{self.token}"


class Bounds3(BaseModel):
    minimum: Point3
    maximum: Point3
    extreme_refs: dict[str, EntityRef] = Field(default_factory=dict)

    @model_validator(mode="after")
    def validate_order(self) -> "Bounds3":
        for axis in ("x", "y", "z"):
            if getattr(self.maximum, axis) <= getattr(self.minimum, axis):
                raise ValueError(f"maximum.{axis} must exceed minimum.{axis}")
        return self

    def size(self, axis: str) -> float:
        name = axis.lower()
        return getattr(self.maximum, name) - getattr(self.minimum, name)

    @property
    def dimensions(self) -> dict[str, float]:
        return {axis: self.size(axis) for axis in ("X", "Y", "Z")}


class Rect2(BaseModel):
    left: float
    bottom: float
    right: float
    top: float

    @model_validator(mode="after")
    def validate_order(self) -> "Rect2":
        if self.right <= self.left or self.top <= self.bottom:
            raise ValueError("rectangle must have positive area")
        return self

    def expanded(self, amount: float) -> "Rect2":
        return Rect2(
            left=self.left - amount,
            bottom=self.bottom - amount,
            right=self.right + amount,
            top=self.top + amount,
        )

    def intersects(self, other: "Rect2", clearance: float = 0.0) -> bool:
        a = self.expanded(clearance)
        return not (
            a.right <= other.left
            or other.right <= a.left
            or a.top <= other.bottom
            or other.top <= a.bottom
        )

