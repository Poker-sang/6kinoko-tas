# Unlimited seeking

New engines advertise `seek-fast-v1`. The editor uses `seek <completed-count>`
for positioning, edit resimulation and fresh replay verification. Playback
speed stays unchanged. Engines advertising only `pacing-v1` retain the 4x
fallback; older engines use ordinary target commands.

The engine removes frame pacing, temporarily mutes output gain and uses SDL
immediate presentation (or mailbox when supported) for external windows.
Embedded preview skips intermediate image downloads and exports the target
frame after its GPU work completes. Cancelling pauses at a real simulation
boundary and exports that frame before acknowledging pause. Input/checkpoint
verification and drawing callbacks still execute in order; no runtime state
or frame-image cache is used. Backward seeking still restarts and simulates
from the initial saves. Actual speed depends on CPU, GPU, rendering and I/O.

The previous 64 MiB preview cache is removed. Ordinary embedded display keeps
only its current bitmap and the engine's current framebuffer, not frame history.
