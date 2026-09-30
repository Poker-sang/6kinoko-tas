# Saving and game focus

Save asks for a destination only for a new recording. Subsequent saves reuse
that destination; opening a .krec makes its path the destination. Save As in
the file menu always asks for a path and makes the successful result the new
destination. Ctrl+S saves; Ctrl+Shift+S saves as. Cancelling or failing a save
does not change the destination. Unapplied input drafts still require .ktas.

Saving pauses at a frame boundary without closing, restarting or replacing the
game session. Playback saves the complete source; live recording uses the
engine's snapshot-v1 command to export its completed prefix with a valid footer.
The writer and process remain alive, so recording can continue. Initial saves
and bookmarks are packaged atomically into the selected .krec. Work files in
the active session directory cannot be overwritten by the package.

Engines lacking snapshot-v1 report that live saving requires an updated engine;
they do not silently stop the game. The export is a recording copy, not a runtime
save-state. Saving leaves the game paused.

For external game focus, the editor grants Windows foreground permission to
the game process, then requests focus-v1. The main SDL thread raises its own
window. Embedded sessions focus the preview as before. Older engines retain
the direct activation fallback. Windows permission grant is the narrow native
boundary; window activation in the engine uses SDL on every platform.

## Delivery

Editor: `artifacts/windows-editor-24/KinokoTAS.App.exe`, source `5d62e21`.
Game: `C:/WorkSpace/6kinoko-modern/runtime-builds/modern-x64-tas-live-save-01/kinoko_modern_gpu.exe`,
source `7263d4df`. Select this new game executable before creating a new session.
The current session remains tied to its original executable.

Playback command update: `artifacts/windows-editor-26/KinokoTAS.App.exe`
(source `1af599c`). Only playback is shown while paused; only pause is shown
while playing/recording. Playback is disabled without a running paused session,
at playback EOF, or during conflicting operations. F9 retains its toggle behavior.
Release build and full synthetic checks are retained in `artifacts/build-65`
and `artifacts/checks-65`, including paused/live command visibility checks.
The game executable remains the live-save-01 version above.

Editor Release build and full synthetic checks passed in `artifacts/build-63`
and `artifacts/checks-63`. Checks cover repeated saves without a picker, complete
live packages, unchanged process/mode, and continued recording after export.
Game's replay, runtime replay and TAS edit contracts passed; DAT hashes verified.
The real runtime/Squirrel test exports a paused live recording, then resumes
the original writer. No actual gameplay or physical foreground activation was
agent-tested. All prior output is retained.
