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
