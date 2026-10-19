// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 어두운 청회 바탕, 금색 얇은 프레임, 청록 선택 강조를 쓰는 공통 창 스타일의 색과 크기 값 </summary>
    public static class UiModernDark
    {
        // 창 프리팹을 고치는 Editor 도구와 테스트가 같은 값을 쓰도록 한곳에 둔다. 런타임 동작은 없다

        /****************************************
        *                Fields
        ****************************************/

        public const string TEXTURE_FOLDER = "Assets/01.Scripts/05.Art/UI/ModernDark/Textures/";

        public const string SPRITE_PANEL          = "T_MdPanel";
        public const string SPRITE_INSET          = "T_MdInset";
        public const string SPRITE_BUTTON         = "T_MdButton";
        public const string SPRITE_BUTTON_PRIMARY = "T_MdButtonPrimary";
        public const string SPRITE_TAB            = "T_MdTab";
        public const string SPRITE_SLOT           = "T_MdSlot";
        public const string SPRITE_SELECTED       = "T_MdSelected";
        public const string SPRITE_CORNER         = "T_MdCorner";
        public const string SPRITE_DIAMOND        = "T_MdDiamond";
        public const string SPRITE_GLOW           = "T_MdGlow";

        public const string DECOR_PREFIX = "MD_";

        public static readonly Color PanelFill  = new Color(0.075f, 0.098f, 0.133f, 1f);
        public static readonly Color InsetFill  = new Color(0.051f, 0.071f, 0.098f, 1f);
        public static readonly Color ButtonFill = new Color(0.106f, 0.145f, 0.200f, 1f);
        public static readonly Color Gold       = new Color(0.784f, 0.647f, 0.353f, 1f);
        public static readonly Color GoldDim    = new Color(0.478f, 0.396f, 0.208f, 1f);
        public static readonly Color Cyan       = new Color(0.310f, 0.839f, 0.894f, 1f);
        public static readonly Color TextMain   = new Color(0.902f, 0.918f, 0.941f, 1f);
        public static readonly Color TextMuted  = new Color(0.557f, 0.600f, 0.659f, 1f);
        public static readonly Color TextGold   = new Color(0.890f, 0.773f, 0.478f, 1f);
        public static readonly Color TextOnCyan = new Color(0.051f, 0.098f, 0.118f, 1f);
        public static readonly Color Disabled   = new Color(0.55f, 0.55f, 0.55f, 0.55f);
    }
}
