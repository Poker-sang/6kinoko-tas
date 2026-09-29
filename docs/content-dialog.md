# Pixeval ContentDialog delivery

Editor: artifacts/windows-editor-10/KinokoTAS.App.exe, source 7072fd9. Game unchanged.
Copied nine ContentDialog source/theme files from C:/WorkSpace/Pixeval/src/Pixeval/Controls/ContentDialog.
Retained Pixeval copyright and GPL-3.0 license in licenses/Pixeval and published package.
Adapted namespace, compact Panel selector, Avalonia 12 content adorner anchor, and dark palette resources.
Replaced discard-edit and replay-confirmation windows with in-editor modal dialogs.
Dialog blocks background controls/shortcuts, defaults to cancellation and pauses active game input.
Native file/folder selection remains the operating-system picker.

build-31 compiled with zero warnings/errors; checks-31 passed including primary/cancel results and background disable/restore. Dialog screenshot inspected; earlier invisible adorner and missing brushes corrected before delivery. Other replay/package/bookmark regressions passed. No actual game run.
