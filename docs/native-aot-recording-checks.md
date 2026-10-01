# NativeAOT recording write verification

Source under test: `74dc9fc`. The complete checks executable, including the
editor's actual MainWindow save methods and core recording/session code, was
compiled to Windows x64 NativeAOT (not executed through dotnet/JIT).

```powershell
dotnet publish tests/KinokoTAS.Checks -c Release -r win-x64 -p:PublishAot=true --self-contained true -p:TrimmerSingleWarn=false -o artifacts/windows-aot-checks-01 -bl:artifacts/build-84/publish.binlog
./artifacts/windows-aot-checks-01/KinokoTAS.Checks.exe artifacts/checks-aot-84
```

Publication completed without warnings/errors; full native regression suite
passed. Verified:

- Engine recording snapshots remain readable and preserve process/mode.
- Recording continues after repeated save snapshots.
- Live editor save writes a complete package and remembers its destination.
- Subsequent save overwrites the same destination without opening a picker.
- Package roundtrip preserves replay, initial saves and bookmarks; hashes reject corruption.
- Source-generated JSON handles recording manifests, bookmarks and legacy drafts.
- Save on exit writes bookmarks, closes the editor, and clean close does not prompt again.

No AOT-specific recording write fault was observed, so no application fix was
needed. Evidence: `artifacts/aot-checks-publish-84.log`,
`artifacts/build-84/publish.binlog`, `artifacts/checks-aot-84.log` and the generated
recording/session artifacts in `artifacts/checks-aot-84` (all retained).

The native test host uses Avalonia Headless and a simulated engine. It exercises
the real save implementation, but does not validate a native OS file picker or
real-game input/recording. The shipped editor remains `windows-editor-38-aot`.
