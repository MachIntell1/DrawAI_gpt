# SolidWorks API decisions

The extractor uses `IModelDocExtension.GetPersistReference3` and the executor resolves each token using `GetObjectByPersistReference3`. Feature IDs are hashes of persistent feature references. Model edges/faces are mapped into each drawing view with `IView.GetCorrespondingEntity` before annotations are created.

Views use `CreateDrawViewFromModelView3`; projected views use `CreateUnfoldedViewAt3` and must remain aligned; sections use an actual cutting line and `CreateSectionViewAt5`. Hole Wizard callouts use `AddHoleCallout2` on a selected corresponding circular edge and are accepted only if SolidWorks reports a hole callout. A failed native callout is a blocker; it never becomes a note.

Exact body bounds come from body vertices. `GetPartBox` is recorded as approximate and therefore blocked by the backend.

Useful official references:

- https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelDocExtension~GetPersistReference3.html
- https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDocExtension~GetObjectByPersistReference3.html
- https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IView~GetCorrespondingEntity.html
- https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~CreateDrawViewFromModelView3.html
- https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~CreateUnfoldedViewAt3.html
- https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~AddHoleCallout2.html

COM API calls with signatures that changed between supported SolidWorks releases are isolated behind `ComCall`. Missing capabilities stop the run and produce a blocker.
