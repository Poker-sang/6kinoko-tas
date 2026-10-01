# Game-window F8 recording requests

The game keeps F9/F10 handling local and publishes playback state as before.
F8 queues `shortcut-N.txt` in the bridge directory, containing
`KTASKEY1 N toggle-recording`. The editor consumes requests once, in numeric order,
then invokes its existing recording toggle workflow. It defers consumption during
dialogs, seeks and other command transactions. Requests already read into the
session queue survive internal process restart when sealing live recording.

The game detects the key's rising edge, so holding F8 does not repeat toggles.
This SDL/file transport adds no global keyboard hook or platform-specific code.
Old games remain usable but cannot send F8 requests. Both updated programs are
required for game-window F8.

Editor source `11b38f5`, NativeAOT delivery:
`artifacts/windows-editor-39-aot/6kinokoTAS.App.exe`.
Native regression executable: `artifacts/windows-aot-checks-02`.
Full suite `artifacts/checks-aot-85` passed, including game request takeover,
duplicate prevention, switching back with recovery and unchanged original bytes.
Logs: `artifacts/checks-aot-85.log`, `artifacts/aot-publish-86.log`.
Tests use the actual editor workflow with a simulated engine, not real gameplay.

Paired game source `a0c8db8`:
`C:\WorkSpace\6kinoko-modern\runtime-builds\modern-x64-tas-shortcuts-01\kinoko_modern_gpu.exe`.
Change the game executable through the editor's file-bar overflow menu, and reopen
the session; an already-running game process continues to use its old binary.
