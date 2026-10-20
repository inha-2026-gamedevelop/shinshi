# HUD party appearance

The party sample in `Assets/00.Scenes/Dev_HUD/Dev_ShinshiVillageQuarterView.unity` uses four member slots. Each slot contains a circular class badge, a Korean nickname, and one HP bar with centered text. The member background is transparent and the old MP placeholder is inactive.

The class badges and empty HP track come from `Assets/01.Scripts/05.Art/UI/HudParty/Textures/T_HudParty_Atlas.png` (Multiple Sprite: Warrior, Taoist, Spirit, HpTrack). The red fill reuses `Assets/01.Scripts/05.Art/UI/HudPlayerStatus/Textures/T_HudPlayerStatus_FillHp.png`. Keep both the PNG files and their `.meta` files when transferring the scene. The existing personal player HUD assets are unchanged.

Nickname and Value are TextMesh Pro objects using the local Korean Bold font documented in `Fonts/README.md`. Names, class icons and HP values in the development scene are preview examples. In Release/ShinshiTown, PartyHudBinder updates HudPartyView from town party and dungeon state; live multiplayer remains unverified. HudPartyView currently handles only the three legacy class badges; class IDs 3–5 still need badge mappings and art.

Editor Play Mode rendering, text overflow, centered Value positions and inactive MP placeholders were checked. A Player build and live multiplayer party updates have not been verified.

# Class stack display (temporary)

Mageknight and Archer stacks (`PlayerCombatState.ClassStacks`, max `SkillCatalogSO.ClassStackMax`) are shown as text until stack icons exist. `HudPlayerStatusView` creates a `ClassStackDisplay` label above the HP track at runtime (ASCII, because the HUD font has no Korean glyphs), and `NetworkOverlayView` draws a Korean stack line above each overhead nameplate. Archers also show the current element. Other classes show nothing. Skills with `RequiredStacks` (Archer R) are dimmed by `HudSkillSlotView.SetLocked` and the input reader does not send them until the replicated stacks are enough; the server still rejects on its own count. Play Mode rendering is not verified.

# Archer overhead element

`NetworkOverlayView` also shows one 36 px icon above each archer: Q ice, W wind, E fire. The display reads `PlayerCombatState.ArcherElement`; no element hides the icon. It follows the existing head projection and nameplate canvas, stays upright, and does not receive pointer input. When a nickname is present, the icon sits above the nickname and stack line. Without a nickname, it sits just above the head.

The original transparent image is `Resources/T_ArcherElementOverhead.png` in this folder. Its three equal square cells are ordered ice, wind, fire; the view selects a cell with `RawImage.uvRect`. Keep this order, the transparent background, and the PNG's `.meta`. The texture is loaded once through Resources, so no scene or player prefab reference is needed. Do not replace it with the rejected particle variant.

Editor-only offline previews discover existing combat states once when a `Dev_` scene loads. In `Dev_ArcherFeedback`, skill execution through the combat host's existing Q/W/E request path, the three rendered icons, movement tracking, and the initial no-element display were verified on 2026-10-20. `NetworkOverlayViewTests` and `ClassStackHudTests` passed 16/16. A Player build, other camera angles/resolutions, and live multiplayer remain unverified.

Nameplate text outlines (and `DamageNumberPool`) switch the text material to `TextMeshPro/Distance Field` with `Shader.Find`, because the mobile SDF shader cannot draw outlines. No font or material in the project uses that shader, so a Player build would strip it and the outline would silently disappear. `Resources/M_TextOutlineShaderRef.mat` exists only to keep the shader in builds: do not delete it. A Player build has not been run to confirm the outline there.
