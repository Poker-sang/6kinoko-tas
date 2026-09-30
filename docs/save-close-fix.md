# Save and close lifecycle fix

Windows delivery: artifacts/windows-editor-32/KinokoTAS.App.exe, source f2b4206.
Final Close is posted to the UI dispatcher after the original cancelled Closing
event has completed, avoiding synchronous reentrant close. Saving a stopped game
session no longer tries to send it a pause command; CaptureRecordingAsync already
validates and loads the finalized current recording when the process is stopped.
Successful package save still increments saveRevision and clears pending save
flags. Cancelling a picker or failing save does not approve exit.

Release build, publish and complete synthetic checks passed; retained evidence is
in artifacts/build-72 and artifacts/checks-72. Coverage includes save before exit,
clean synchronous close without another prompt, cancellation, discard, repeated
live snapshot saves and packaged bookmark precedence. These are fake-engine and
headless editor checks, not actual-gameplay validation. The active user editor at
investigation time was editor-30; user files and processes were left untouched.
