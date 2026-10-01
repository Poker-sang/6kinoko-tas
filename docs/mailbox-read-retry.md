# Windows mailbox read conflicts

Engine publication currently removes the previous state mailbox and renames a
same-directory temporary file. Even with ReadWrite/Delete sharing, Windows may
report access denied while an old file is delete-pending. State reads previously
handled missing files but allowed these transient errors to abort commands.

Shared reads now retry Windows access-denied, sharing-violation and lock-violation
codes up to 15 times at 2 ms intervals. Persistent failures are still surfaced;
other I/O errors are not suppressed. No recording format or permissions changed.

Source: `12045b4`. Full headless regression suite passed, including releasing an
exclusive state-file lock during a read and retaining errors for a persistent
lock: `artifacts/checks-82`, logs `artifacts/checks-82.log`.
NativeAOT publication succeeded without warnings/errors:
`artifacts/windows-editor-38-aot/6kinokoTAS.App.exe`,
`artifacts/aot-publish-83.log`, `artifacts/build-83/publish.binlog`.
These are simulated-engine tests, not real-game user validation.
