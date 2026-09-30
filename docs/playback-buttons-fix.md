# Live pause and resume command fix

Windows editor: `artifacts/windows-editor-29/KinokoTAS.App.exe`.
The game executable remains `modern-x64-tas-pipeline-02`; no game changes are
required for this fix. The recording format and session protocol are unchanged.

Each 33 ms UI refresh previously reset command state to unknown, hiding pause
and disabling play, then immediately reapplied the running session state. This
interrupted pointer press/release handling even though commands worked through
bookmark creation. Both running pause and paused resume could lose clicks.

Refresh now applies the last valid session state once, retaining it during a
transient missing status mailbox. Session replacement/exit resets this cached
state. Completing an editor operation or pause acknowledgement refreshes the
buttons immediately, including bookmark-triggered pauses.

Release build and full synthetic checks passed in `artifacts/build-68` and
`artifacts/checks-68`. The regression physically presses the live pause and
resume controls, refreshes five times before release, verifies visibility and
enablement stay stable, and checks engine pause/resume acknowledgements. It
also resumes after bookmark creation without changing recording mode. Previous
save, exit confirmation, editing and replay checks continue passing. Fixtures
and all earlier artifacts are retained. These are headless editor/fake-engine
checks, not a new actual-gameplay validation.

The final delivery also removes the empty timeline tooltip by treating blank
bookmark text as no tooltip and closing any previously visible popup. Named
bookmark hints remain. Build/publish and complete checks for this final version
are retained in `artifacts/build-69` and `artifacts/checks-69`; build-68,
checks-68 and the intermediate windows-editor-28 package are retained.
