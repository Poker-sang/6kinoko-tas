# Unsaved recording confirmation

Windows editor: `artifacts/windows-editor-27/KinokoTAS.App.exe`, source `8f5b92c`.
Release build, publish and full synthetic checks passed; retained logs and fixtures
are in `artifacts/build-67` and `artifacts/checks-67`. Earlier batch 66 is retained.

Closing, starting a new recording and opening another document pause the current
game and offer Save, Don't Save and Cancel when there are unsaved changes.
New recordings, live prefix growth, takeovers, verified input edits, recovery
and bookmark changes count as changes. Saving clears the current change state;
recording more frames makes the document unsaved again. Opening a saved file
starts clean. Internal session/recovery files do not count as a user save.

Save uses the remembered .krec destination or asks for the first destination.
Unapplied input changes use the .ktas draft picker, preserving pending intentions.
Cancelling a picker or failing a save leaves the document and game session open.
The game is stopped only after the exit decision and successful saving, when
requested. Repeated close requests while a dialog is active do not bypass it.
Don't Save leaves the existing saved package untouched; internal recovery files
remain retained under the existing retention policy.

Synthetic checks exercise a live saved prefix becoming clean, bookmark changes,
new-recording cancellation, exit cancellation, save before exit and discard
without changing saved package bytes. Actual gameplay is user-validated.

For faster seeking, select this updated game executable and reopen the session:
`C:/WorkSpace/6kinoko-modern/runtime-builds/modern-x64-tas-fast-seek-04/kinoko_modern_gpu.exe`
(source `80271671`). Intermediate screen-only GPU output is deferred; draw callbacks,
offscreen dependencies and exact target/cancel rendering remain. Replay publication
is batched during seeking and flushed at pause/target/save boundaries. No image
history cache or full runtime save-state was added. Real-game speedup is unmeasured.
