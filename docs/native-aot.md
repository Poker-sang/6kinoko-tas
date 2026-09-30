# NativeAOT publication

All application-owned JsonSerializer calls use generated JsonTypeInfo, including
settings, session metadata, recording-package manifests, bookmarks and drafts.
Reflection-based JSON fallback is disabled for all projects. Existing JSON field
names and recording formats remain compatible. JSON DOM access is reflection-free.
Application XAML uses compiled bindings; no explicit dynamic reflection or
Activator calls were found in application sources. This does not claim that
Avalonia or other dependencies contain no reflection internally.

```powershell
dotnet publish src/KinokoTAS.App -c Release -r win-x64 -p:PublishProfile=NativeAot -o artifacts/windows-aot-unique
```

Windows requires the Visual Studio C++ native toolchain. Publish on the target
platform for Linux/macOS; those AOT targets have not been validated.
NativeAOT output is self-contained and does not need .NET installed. Keep native
DLLs beside the executable; PDB files are optional debugging artifacts.

Delivery: `artifacts/windows-editor-37-aot/6kinokoTAS.App.exe`, application source
`afaf604`. Publish log/binlog: `artifacts/aot-publish-80.log`, `artifacts/build-80`.
Native compilation completed without warnings or errors. Startup probe confirmed
a live native window titled 6kinoko TAS, then closed it without loading a game.
Headless regression suite at `artifacts/checks-81` passed in full with JSON
reflection disabled (test source `96544ab`). This is not real-game validation.

Previous CI run 36761894435 failed because the resume click's asynchronous focus
request had not released its command guard when the bookmark test inspected the
button. The test now waits for command completion instead of only the live phase.
CI also publishes NativeAOT and uploads binaries and diagnostics separately.
