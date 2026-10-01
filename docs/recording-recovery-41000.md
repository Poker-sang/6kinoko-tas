# Recovery of the 41,000+ frame session

Recovered into new files; originals and the running editor/game were not changed.
Source investigation inventory: `artifacts/recovery-41000-01/inventory.json`.
The saved `D:\最新.krec` contains 37,523 frames, while later complete branches
remain in the editor-35 session directory.

- `D:\最新-恢复-近期43007帧.krec`: 43,007 frames, 14 bookmarks from the exact
  replay-hash cache, including the bookmark at frame 41,090. Source:
  `artifacts/windows-editor-35/sessions/session-79c9218071284a9880d5db14c631bb02/source.krec`.
  Replay SHA256: `2A15509DEC7855D4A11E77856FF53288A29AFE7FA26D75CCEBCB7DE0D0564A6F`.
- `D:\最新-恢复-最长43085帧.krec`: earlier longest branch, 43,085 frames,
  13 bookmarks from the original saved package. Source:
  `artifacts/windows-editor-35/sessions/session-20261001-111832-ac07e5/run-20261001-112354-100-6157ff/branch.krec`.
  Replay SHA256: `F4A33F5588C20D2C87CC62C4C79AEEE5052FE231D3B05EDC48B1EAA6301646F4`.
- `D:\最新-恢复-未应用编辑43007帧.ktas`: preserved latest edit draft, 43,007
  frames, two pending input edits starting at frame 42,703. These edits have not
  been resimulated or misrepresented as applied checkpoints.

Both source sessions have empty initial-save directories, matching their session
metadata. The recovery tool validates frame hashes and the recording trailer,
packages the untouched replay and bookmarks, reloads the package and compares
replay bytes/bookmarks. Logs and build evidence are retained under
`artifacts/recovery-41000-01`. No real-game replay validation was performed.
This recovery does not establish the cause of the user's apparent save loss.
