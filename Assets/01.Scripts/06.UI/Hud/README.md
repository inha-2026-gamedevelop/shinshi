# HUD party appearance

The party sample in `Assets/00.Scenes/Dev_HUD/Dev_ShinshiVillageQuarterView.unity` uses four member slots. Each slot contains a circular class badge, a Korean nickname, and one HP bar with centered text. The member background is transparent and the old MP placeholder is inactive.

The class badges and empty HP track come from `Assets/01.Scripts/05.Art/UI/HudParty/Textures/T_HudParty_Atlas.png` (Multiple Sprite: Warrior, Taoist, Spirit, HpTrack). The red fill reuses `Assets/01.Scripts/05.Art/UI/HudPlayerStatus/Textures/T_HudPlayerStatus_FillHp.png`. Keep both the PNG files and their `.meta` files when transferring the scene. The existing personal player HUD assets are unchanged.

Nickname and Value are TextMesh Pro objects using the local Korean Bold font documented in `Fonts/README.md`. Names, class icons and HP values are preview examples; there is no party data binding in this view yet. A data binder must update the displayed name, icon, HP ratio and Value together from authoritative party data.

Editor Play Mode rendering, text overflow, centered Value positions and inactive MP placeholders were checked. A Player build and live multiplayer party updates have not been verified.
