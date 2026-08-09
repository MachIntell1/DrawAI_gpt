# Build and registration

Prerequisites: Windows x64, Visual Studio 2022 or MSBuild, .NET Framework 4.8 Developer Pack, and the interop assemblies from the target SolidWorks installation.

Set `SOLIDWORKS_SDK` to the folder containing:

- `SolidWorks.Interop.sldworks.dll`
- `SolidWorks.Interop.swconst.dll`
- `SolidWorks.Interop.swpublished.dll`

Build `plugin-solidworks.sln` for `Release|x64`. Register the output with the 64-bit .NET Framework `RegAsm.exe /codebase MachIntellDrawAI.dll`. Start SolidWorks once as the same user and enable the add-in.

Configuration is stored in `%PROGRAMDATA%\MachIntell\DrawingAddin\settings.json`. API keys are read from the Windows user environment variable named by `ApiKeyEnvironmentVariable`; they are never stored in the settings file or logs.

The ISO and ASME templates must be separately controlled, revisioned `.drwdot` files. A template/projection mismatch aborts generation.
