# Frame preview cache

Embedded preview keeps observed images in a session-local 64 MiB LRU cache.
Seeking a cached frame displays its image while the engine simulates to the
requested position. Status distinguishes cached images from engine progress.
Playback and takeover remain serialized behind the pending seek operation.
Cancellation discards the temporary preview and shows the actual engine image.

This is not a save-state: it does not accelerate simulation or change the
external game window. Only observed frames are cached. Eviction, closing a
session and takeover discard cached images; no cache files are added to .krec.
Full rewind still needs serialization of the VM and native scene graph.

Validation: bounded eviction, image ownership, oversized frames and invalidation
are covered by synthetic checks. No actual gameplay was performed.
