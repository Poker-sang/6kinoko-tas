# Kinoko TAS
Independent C#/.NET 10 + Avalonia project. User authorized integration changes in 6kinoko-modern for embedded view, seeking and takeover. Never modify/push rebuild. Coordinate shared files with other threads.
Remote: https://github.com/Poker-sang/6kinoko-tas.git; default branch master. User authorized initial upload and publication. Game integration belongs
behind IGameSession. Never label cursor navigation as game stepping. Edited input
invalidates old checkpoints. Resimulate through edits-v1, then replay-verify the complete result before adoption; never fabricate checkpoints in the editor. .ktas stores pending intentions.
Preserve original recordings. Commit before build/validation batches; retain
unique artifacts/ directories. Do not commit proprietary game DAT. No game
execution is needed for editor tests.
