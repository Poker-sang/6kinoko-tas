# Roadmap

- [x] Independent editor and validated replay/package reader.
- [x] Horizontal live timeline, playhead, follow mode and bookmarks.
- [x] Cell/range edits, undo/redo and compressed draft projects.
- [x] Single-file .krec including initial saves and bookmarks.
- [x] Pause/step, optional embedded preview and external game window.
- [x] New recording, restart/seek and takeover with retained source.
- [x] Execute edited input, regenerate checkpoints and verify full playback.
- [x] Restore previous overwrite, including frame position, bookmarks and draft.
- [x] Editor shortcuts, operation progress/cancellation, failure isolation.
- [x] Public GitHub repository with master as default.
- [x] Playback speed controls and accelerated seek/resimulation.
- [x] Insert/delete frames and drag painting with grouped undo.
- [ ] Save-state seeking after engine serialization is complete.
- [ ] Native Linux/macOS editor validation.

Recovery history currently belongs to the active editor session. Old runtime
files remain on disk; no automatic cleanup is performed.
