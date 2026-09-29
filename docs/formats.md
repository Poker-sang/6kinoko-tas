# Formats and engine boundary

Source wire contract: 6kinoko-modern b6c525f0. KINORPL1, uint32 LE version 1,
uint32 action count 19, 64 ASCII hex identity. Each record: tag 1, uint64 frame,
191-byte payload, uint64 FNV-1a of frame+payload. Footer: tag 0, uint64 count,
uint64 rolling FNV-1a over all frame+payload bytes. No trailing data allowed.
Payload: 19 int32 holds, 19 byte releases, legacy x/y int32, 6 int32 buttons,
4 byte releases, 10 int32 digits, uint32 clock/RNG-before/RNG-after, uint64 checkpoint.
Maximum 1,000,000 frames. Checksums detect corruption, not authenticity.

.ktas v1 is a ZIP with exactly source.krec and project.json:
{"Version":1,"SourceName":"session.krec","Edits":[{"Frame":10,"Action":4,"Down":true}]}.
Edits are zero-based boolean intentions, not replacement hold counters. Omitted
cells retain source input. Duplicate/out-of-range/redundant edits are rejected.
No EXE, DAT or absolute source path is stored. Source export is byte-identical.

Changes invalidate simulation state from the earliest edited frame onward and
may change subsequent hold/release counters. No new RNG/checkpoints are fabricated.
The engine must rebuild input and re-execute before exporting verified replay;
legacy any-button/digit behavior needs an explicit integration contract.

IGameSession is a draft interface only. Future transport needs a local-only
versioned handshake, schema/build identity, request IDs and acknowledged steps.
UI timer ticks must never stand in for simulation completion. Coordinate with
the separate determinism investigation before editing the game core.
