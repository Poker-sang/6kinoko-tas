# Timeline edits and recording saves

Application source: `aeaafbe`. Delivery:
`artifacts/windows-editor-40-aot/6kinokoTAS.App.exe`.

The timeline's main toolbar owns apply/undo/redo. The range expander is renamed
batch editing and only contains range-input and structural editing tools.

- F5 / Apply to recording resimulates and verifies edits, returning to the selected
  frame. It does not save the user's file; the current recording remains unsaved.
- Ctrl+S always saves .krec. Pending timeline edits are applied and verified first.
  Close/new/open save confirmations use this same workflow instead of .ktas export.
- Advanced .ktas export retains original input plus pending edit intentions. It
  does not clear the .krec unsaved state or change the current save destination.
- The current .krec filename is followed by `*` when unsaved. Timeline and footer
  show pending/applied-unsaved/saved state and the current file path.
- Playback/seek with pending edits explains how to apply them instead of silently
  showing the original input. Game restart is a separate operation.

0.75x is added between 0.5x and 1x, with default 1x retained. The editor requires
the engine's pacing-075-v1 capability before sending 75-percent speed, protecting
old engines from unsupported-command failure. Paired engine source `4bebdac`:
`C:\WorkSpace\6kinoko-modern\runtime-builds\modern-x64-tas-speed075-01\kinoko_modern_gpu.exe`.

Full Windows NativeAOT headless suite passed in `artifacts/checks-aot-89`:
toolbar ownership, workflow labels, unsaved title star, 0.75x selector, automatic
apply-save roundtrip, old-file preservation on verification failure, retained
draft/session/star, successful retry, and pending-edit close-save to .krec.
Original fixture bytes remain unchanged. Rendering was inspected from
`artifacts/checks-aot-88/timeline-edit-workflow.png`.
Publish logs/binlogs are retained in `artifacts/aot-publish-90.log` and
`artifacts/build-90`; no warnings/errors. Real gameplay remains user-validated.
