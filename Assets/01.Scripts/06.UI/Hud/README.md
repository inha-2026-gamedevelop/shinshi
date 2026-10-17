# HUD party appearance

The party sample in `Assets/00.Scenes/Dev_HUD/Dev_ShinshiVillageQuarterView.unity` uses four member slots. Each slot contains a circular class badge, a Korean nickname, and one HP bar with centered text. The member background is transparent and the old MP placeholder is inactive.

The class badges and empty HP track come from `Assets/01.Scripts/05.Art/UI/HudParty/Textures/T_HudParty_Atlas.png` (Multiple Sprite: Warrior, Taoist, Spirit, HpTrack). The red fill reuses `Assets/01.Scripts/05.Art/UI/HudPlayerStatus/Textures/T_HudPlayerStatus_FillHp.png`. Keep both the PNG files and their `.meta` files when transferring the scene. The existing personal player HUD assets are unchanged.

Nickname and Value are TextMesh Pro objects using the local Korean Bold font documented in `Fonts/README.md`. Names, class icons and HP values in the development scene are preview examples. In Release/ShinshiTown, PartyHudBinder updates HudPartyView from town party and dungeon state; live multiplayer remains unverified. HudPartyView currently handles only the three legacy class badges; class IDs 3–5 still need badge mappings and art.

Editor Play Mode rendering, text overflow, centered Value positions and inactive MP placeholders were checked. A Player build and live multiplayer party updates have not been verified.

# Class stack display (temporary)

Mageknight and Archer stacks (`PlayerCombatState.ClassStacks`, max `SkillCatalogSO.ClassStackMax`) are shown as text until stack icons exist. `HudPlayerStatusView` creates a `ClassStackDisplay` label above the HP track at runtime (ASCII, because the HUD font has no Korean glyphs), and `NetworkOverlayView` draws a Korean stack line above each overhead nameplate. Archers also show the current element. Other classes show nothing. Skills with `RequiredStacks` (Archer R) are dimmed by `HudSkillSlotView.SetLocked` and the input reader does not send them until the replicated stacks are enough; the server still rejects on its own count. Play Mode rendering is not verified.
