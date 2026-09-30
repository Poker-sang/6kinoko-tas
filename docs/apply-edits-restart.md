# Applying edited timeline inputs

Windows editor: `artifacts/windows-editor-30/KinokoTAS.App.exe`, source `9f40f3e`.
The current game executable remains `modern-x64-tas-pipeline-02`; this batch does
not modify the engine or replay wire format. Source and delivery are on master.

The restart command previously only relaunched the original recording and did
not apply pending inputs. With pending edits it is now enabled even while a
playback session is running and labelled Apply and Restart. It invokes the same
resimulation and full replay verification as Apply Edits. Without pending edits,
the existing process restart behavior is retained. Applying can also use a
stopped playback session's source instead of trying to pause a dead process.

After resimulation and full verification, the new session seeks back to the
selected frame before replacing the old session. Failures or cancellation at
this additional seek dispose the candidate and retain the original recording
and draft. Applied inputs are adopted as the new project source, so the yellow
pending-edit corner markers disappear. The resulting recording remains unsaved
to the user's .krec until Save is requested; unsaved-document confirmation still
applies. The status message distinguishes applying from saving. Recovery still
retains the original source and its input draft.

Release compilation, publication and full synthetic checks passed. Logs and
fixtures are retained in `artifacts/build-70` and `artifacts/checks-70`. Checks
invoke both actual editor command events, verify the changed recorded input,
confirm yellow markers/InvalidFrom are cleared, confirm selected-frame preview
after applying, and confirm the changed document still requires saving. Existing
live pause/resume, save, recovery, verification failure/cancellation, structural
editing and exit-dialog checks remain passing. These use the headless editor
and fake engine; this is not a new real-game editing validation.

At investigation time the active user editor process was windows-editor-27.
Launch windows-editor-30 to receive these changes and the intervening pause,
resume and empty-tooltip fixes. Existing user processes were not stopped and
recording/session files were not modified by this batch.
