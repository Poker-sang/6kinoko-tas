# Self-contained .ktas initial state

Source `d1da3f1`. Delivery:
`artifacts/windows-editor-41-aot/6kinokoTAS.App.exe`.

.krec recording packages already included initial saves and bookmarks. Their
version-1 package format and raw KINORPL1 compatibility remain unchanged.

.ktas draft version 3 has two entries: project.json carries frame intents and
draft bookmarks; source.krec contains a validated recording package with the
original replay and initial saves. Empty initial state is explicit. Bookmarks
on appended draft frames are allowed even beyond the original replay's EOF.
Drafts no longer require external initial directories or bookmark sidecars.
Opening/resaving a moved draft extracts its own initial state automatically.
Application exports and internal resimulation drafts use the session's actual
initial save directory. As requested, draft versions 1/2 are not supported.

NativeAOT suite `artifacts/checks-aot-91` passed in full, including nonempty and
empty initial state, relocated draft load/resave, embedded bookmarks, and retained
.krec unsaved star after draft export. Existing package hash-corruption checks and
failure-safe recording saves still pass.
No real game was run. Build/publish evidence retained in
`artifacts/aot-checks-publish-91.log`, `artifacts/build-91`,
`artifacts/aot-publish-92.log`, `artifacts/build-92`.

Additional real-file compatibility check reloaded and repackaged the user's
previously recovered 43,007-frame .krec to `artifacts/krec-compatibility-93.krec`.
All 14 bookmarks and replay bytes survived exactly, with SHA256
`2A15509DEC7855D4A11E77856FF53288A29AFE7FA26D75CCEBCB7DE0D0564A6F`.
Source files were read only. Evidence: `artifacts/krec-compatibility-93.log`.
