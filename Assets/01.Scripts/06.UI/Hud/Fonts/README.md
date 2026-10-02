# HUD quest fonts

The original Nanum Gothic Regular and Bold font files come from [Google Fonts](https://github.com/google/fonts/tree/main/ofl/nanumgothic), downloaded on 2026-10-03. Their unchanged source files and SIL Open Font License 1.1 are in `NanumGothic/Fonts/` and `NanumGothic/OFL.txt`.

`HudQuestKorean-Regular.asset` and `HudQuestKorean-Bold.asset` are TextMesh Pro dynamic SDF assets with the quest preview characters preloaded. The source fonts are retained for additional Korean glyphs. `HudQuestSymbols.asset` is a private static SDF asset containing plus and Unicode minus, derived from the existing `Assets/Plugins/TextMesh Pro/Fonts/LiberationSans.ttf`.

These assets are assigned locally to the quest UI. The global TMP fallback is unchanged. Editor rendering and collapse/expand events were verified; full glyph coverage and a Player build have not been verified.

The party HUD sample also uses the Korean Bold asset, with its nickname characters preloaded. The party sample does not use the Symbols asset.
