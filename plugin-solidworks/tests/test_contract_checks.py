from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "MachIntellDrawAI"


def sources() -> dict[str, str]:
    return {str(path.relative_to(ROOT)): path.read_text(encoding="utf-8") for path in SOURCE.rglob("*.cs")}


class ContractChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.files = sources()
        cls.all_source = "\n".join(cls.files.values())

    def test_project_is_net48_x64_and_xml_is_valid(self) -> None:
        project = ROOT / "MachIntellDrawAI" / "MachIntellDrawAI.csproj"
        ET.parse(project)
        text = project.read_text(encoding="utf-8")
        self.assertIn("<TargetFramework>net48</TargetFramework>", text)
        self.assertIn("<PlatformTarget>x64</PlatformTarget>", text)

    def test_v2_endpoints_are_used(self) -> None:
        self.assertIn('"api/v2/plugin/plan"', self.all_source)
        self.assertIn('"api/v2/plugin/validate-execution"', self.all_source)
        self.assertNotIn('"api/v1/', self.all_source)

    def test_persistent_reference_round_trip_is_required(self) -> None:
        self.assertIn("GetPersistReference3", self.all_source)
        self.assertIn("GetObjectByPersistReference3", self.all_source)
        self.assertIn("GetCorrespondingEntity", self.all_source)

    def test_associative_native_drawing_apis_are_present(self) -> None:
        for api in (
            "CreateDrawViewFromModelView3",
            "CreateUnfoldedViewAt3",
            "CreateSectionViewAt5",
            "AddHoleCallout2",
            "AddDimension2",
            "InsertCenterMark2",
        ):
            self.assertIn(api, self.all_source)

    def test_only_draft_watermark_uses_insert_note(self) -> None:
        insert_note_calls = []
        for name, text in self.files.items():
            for match in re.finditer(r"\.InsertNote\s*\(", text):
                insert_note_calls.append((name, text.count("\n", 0, match.start()) + 1))
        self.assertEqual(len(insert_note_calls), 1)
        self.assertEqual(insert_note_calls[0][0], "MachIntellDrawAI/SolidWorks/AssociativeRequirementExecutor.cs")
        self.assertIn("no plain-note fallback is permitted", self.all_source)

    def test_no_value_based_geometry_search_or_deletion(self) -> None:
        forbidden = (
            "SelectEdgeAtDistance",
            "FindEntityByValue",
            "DeleteDuplicateByValue",
            "correction loop",
            "LLM",
        )
        implementation = "\n".join(
            text for name, text in self.files.items()
            if not name.endswith("AssemblyInfo.cs")
        )
        for phrase in forbidden:
            self.assertNotIn(phrase, implementation)

    def test_release_requires_explicit_confirmation(self) -> None:
        self.assertIn("MessageBoxDefaultButton.Button2", self.all_source)
        self.assertIn("HumanApprovalConfirmed = humanApprovalConfirmed", self.all_source)
        self.assertIn('WriteGate(session.Drawing.Model, gate, "RELEASE_READY")', self.all_source)

    def test_sketches_and_reference_geometry_are_explicitly_hidden(self) -> None:
        self.assertIn("BlankSketch", self.all_source)
        self.assertIn("BlankRefGeom", self.all_source)
        self.assertIn("CadArtifactsHidden", self.all_source)


if __name__ == "__main__":
    unittest.main()
