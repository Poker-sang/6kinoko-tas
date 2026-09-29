# Embedded session delivery — 2026-09-29

Editor source: `39cacca`, local independent repository; remote still unconfigured.
Game source: `94aefb79`, pushed to `codex/tas-bridge` in 6kinoko-modern.
Modern is based on completed deterministic script-math fixes at 65b2eb01.
No Mod changes merged; Mod work remains in its separate checkout/PR.

Published editor: `artifacts/windows-editor-02/KinokoTAS.App.exe` (.NET 10 required).
Game: `C:/WorkSpace/6kinoko-modern/runtime-builds/modern-windows-tas-bridge-01/6kinoko-modern-windows-x64-94aefb79/kinoko_modern_gpu.exe`.
See [operation and limitations](embedded-session.md).

Validation:
- Editor Release build: zero warnings/errors; logs in artifacts/build-07.
- Core file/edit checks and headless Avalonia click/undo checks passed.
- Separate fake engine process checks: handshake, forward/backward seek,
  acknowledged takeover, one-frame execution, branch finalization and initial-save isolation.
- Headless editor received the fake engine preview and rendered it in its image panel.
  Screenshot artifacts/checks-07/editor.png inspected; it is synthetic, not gameplay.
- Game replay contracts: two-frame verified prefix + takeover release edge + new
  valid frame saved and replayed with matching checksums through the real runtime.
- Synthetic hidden-window SDL GPU test passed actual RGBA readback/frame-tag check.
- Game CI run 36590053303 succeeded across seven jobs (Windows tests/native builds,
  Linux/macOS compilation; no Linux/macOS gameplay).
- Original three DAT copied and SHA256 verified beside delivered game EXE.

No actual game gameplay was run by the agent this batch. Module/transport/GPU
checks do not establish end-to-end gameplay validation. All old artifacts retained.
