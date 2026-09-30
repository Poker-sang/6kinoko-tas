# Packaged bookmark loading fix

Delivery: `artifacts/windows-editor-31/KinokoTAS.App.exe`, source `6853784`.
Opening a packaged .krec now always reads its embedded bookmarks before legacy
sidecars or per-replay local bookmark caches. Raw recordings and .ktas keep their
existing legacy bookmark sources. Local caches remain retained.

The user's recording-20261001-002408 package passed all validation, with 26,612
frames and ten bookmarks. An existing empty per-replay cache (`[]`) previously
overrode those embedded bookmarks, explaining the missing list in editor-30.
The original recording and user's cache were not modified by this repair.

Release build, publication and complete synthetic checks passed; retained logs
are under artifacts/build-71 and fixtures under artifacts/checks-71. The regression
opens a package while an empty cache exists and verifies the editor bookmark list
equals the packaged list. No game engine update is required.
