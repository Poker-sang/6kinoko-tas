# Public master and CommandBar styles — 2026-09-30

Published source to https://github.com/Poker-sang/6kinoko-tas with master as the
default branch. GPL-3.0 license and Pixeval attribution are included; proprietary
game data, recordings and local artifacts are excluded.

Source b50411f combines Pixeval Controls/CommandBarStyles.axaml with the existing
Themes/CommandBar.axaml: compact tooltips, 20px overflow icons and disabled
foregrounds. App loads this combined Styles file after FluentTheme.

Release build and synthetic/headless checks passed (artifacts/build-39 and
artifacts/checks-39). Published framework-dependent Windows editor:
artifacts/windows-editor-15/KinokoTAS.App.exe (requires .NET 10).
No game gameplay was performed. All previous artifacts are retained.
