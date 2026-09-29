# First editor delivery — 2026-09-29

Repository: `C:\WorkSpace\6kinoko-tas`, independent local Git repository.
No remote configured or pushed. No changes made to modern/rebuild during this task.
Source for this executable: `311d98b` (see artifact source-commit.txt for full SHA).

Executable: `artifacts/windows-editor-01/KinokoTAS.App.exe`.
Keep its adjacent DLLs/native libraries. This framework-dependent build requires
.NET 10, already installed on this computer. `launch.cmd` runs the development build.

Delivered: strict KINORPL1 import; visible-row input timeline; cell/range editing;
undo/redo; frame cursor navigation; compressed portable .ktas save/load; exact
original-source export; unsaved-edit prompt; original RNG/checkpoint inspection.

Game control remains unimplemented: cursor movement does not advance simulation.
Edited input is intentionally saved as project intent, not a fake verified .krec.
The game-side integration and deterministic replay investigation remain separate.

Validation on Windows:

- Release solution build: 0 warnings / 0 errors (build-04).
- Checks passed: wire fields/offsets, bad checksums/schema/truncation/trailing
  data, range edits, undo/redo, project roundtrip, exact source export, atomic-save
  failure preservation, and real 4005-frame recording read-only validation.
- Avalonia headless UI checks passed: open recording, click an input cell, undo.
- Screenshot `artifacts/checks-04/editor.png` was visually inspected: toolbar,
  timeline, navigation, range editing and validation state are visible. The
  timeline intentionally scrolls horizontally to reach all 19 actions.
- Windows framework-dependent publish succeeded. No game executed and no claim
  of Linux/macOS editor runtime validation.

Logs: `artifacts/build-04/{build,checks,publish}.log`.
Fixtures and screenshot: `artifacts/checks-04/`.
Earlier unsuccessful build attempts remain retained. No proprietary DAT or
user recording is committed to Git.
