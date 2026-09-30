# Windows foreground activation

Independent two-process WinForms probe: `C:\WorkSpace\6kinoko-focus-probe`.
The probe links the editor's actual `GameWindowOrder.cs`, assigns the target's owner,
and checks `GetForegroundWindow`, rather than trusting an IPC acknowledgement.

- `artifacts/probe-01/results.txt`: direct SetForegroundWindow failed in all three trials.
- `artifacts/probe-02/results.txt`: temporary AttachThreadInput fallback succeeded in all three trials.

Input queues are detached in finally; minimized targets are restored. No synthetic
keyboard input, permanent topmost flag, or continuous focus stealing is used.
Bridge focus requests remain available on other platforms. Windows always checks
native activation after the bridge acknowledgement. Playback resume also transfers
focus, including resuming an existing live recording.

These are isolated Windows activation tests, not real-game user validation.
