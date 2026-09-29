# Kinoko TAS
Independent C#/.NET 10 + Avalonia project. User authorized integration changes in 6kinoko-modern for embedded view, seeking and takeover. Never modify/push rebuild. Coordinate shared files with other threads.
Remote is not configured until the user chooses one. Game integration belongs
behind IGameSession. Never label cursor navigation as game stepping. Edited input
invalidates checkpoints; save .ktas until the engine supports re-execution.
Preserve original recordings. Commit before build/validation batches; retain
unique artifacts/ directories. Do not commit proprietary game DAT. No game
execution is needed for editor tests.
