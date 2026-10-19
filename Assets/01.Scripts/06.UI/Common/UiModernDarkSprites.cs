// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 공통 어두운 스타일의 9분할 스프라이트와 장식 이미지를 코드로 만든다. 장면에 에셋 참조를 남기지 않고 쓰려고 런타임에서도 만든다 </summary>
    public static class UiModernDarkSprites
    {
        // Editor 도구는 같은 픽셀을 PNG로 구워 프리팹에 연결한다. 런타임은 한 번 만든 스프라이트를 이름으로 재사용한다

        /****************************************
        *                Fields
        ****************************************/

        public static readonly string[] G_AllNames =
        {
            UiModernDark.SPRITE_PANEL, UiModernDark.SPRITE_INSET, UiModernDark.SPRITE_BUTTON, UiModernDark.SPRITE_BUTTON_PRIMARY,
            UiModernDark.SPRITE_TAB, UiModernDark.SPRITE_SLOT, UiModernDark.SPRITE_SELECTED, UiModernDark.SPRITE_CORNER,
            UiModernDark.SPRITE_DIAMOND, UiModernDark.SPRITE_GLOW,
        };

        private const int   BOX_SIZE            = 32;
        private const int   PANEL_SIZE          = 48;
        private const int   CORNER_SIZE         = 24;
        private const int   CORNER_ARM          = 14;
        private const int   DIAMOND_SIZE        = 16;
        private const int   GLOW_WIDTH          = 8;
        private const int   GLOW_HEIGHT         = 32;
        private const float GLOW_PEAK_ALPHA     = 0.6f;
        private const float PIXELS_PER_UNIT     = 100f;
        private const float DIAMOND_RADIUS      = 7f;
        private const float DIAMOND_CENTER      = 7.5f;
        private const float INNER_LINE_ALPHA    = 0.5f;
        private const float SELECTED_FILL_ALPHA = 0.1f;

        private static readonly Color OUTLINE_SOFT   = new Color(0.17f, 0.22f, 0.29f, 1f);
        private static readonly Color OUTLINE_BUTTON = new Color(0.23f, 0.29f, 0.38f, 1f);
        private static readonly Color FILL_PRIMARY   = new Color(0.07f, 0.20f, 0.23f, 1f);
        private static readonly Color FILL_TAB       = new Color(0.09f, 0.125f, 0.175f, 1f);
        private static readonly Color CLEAR          = new Color(0f, 0f, 0f, 0f);

        private static readonly Dictionary<string, Sprite> G_Cache = new Dictionary<string, Sprite>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이름의 스프라이트를 한 번만 만들어 돌려준다 </summary>
        public static Sprite Get(string name)
        {
            if ((G_Cache.TryGetValue(name, out Sprite cached)) && (cached != null))
            {
                return cached;
            }

            Color32[] pixels = BuildPixels(name, out int width, out int height, out int border);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name       = name,
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
                hideFlags  = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), PIXELS_PER_UNIT, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name      = name;
            sprite.hideFlags = HideFlags.DontSave;
            G_Cache[name] = sprite;
            return sprite;
        }

        /// <summary> 캐시의 스프라이트와 텍스처를 모두 해제한다. 도메인 리로드, 종료처럼 쓰는 쪽이 없어지는 때만 부른다 </summary>
        public static void ReleaseAll()
        {
            foreach (KeyValuePair<string, Sprite> pair in G_Cache)
            {
                Sprite sprite = pair.Value;
                if (sprite == null)
                {
                    continue;
                }

                Texture2D texture = sprite.texture;
                UnityEngine.Object.DestroyImmediate(sprite);
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            G_Cache.Clear();
        }

        // 도메인 리로드를 끄고 플레이에 들어가면 정적 캐시가 남으므로 시작할 때 비우고, 끝날 때도 해제한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnStart()
        {
            ReleaseAll();
            Application.quitting -= ReleaseAll;
            Application.quitting += ReleaseAll;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditorRelease()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseAll;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
            UnityEditor.EditorApplication.quitting -= ReleaseAll;
            UnityEditor.EditorApplication.quitting += ReleaseAll;
        }
#endif

        public static Color32[] BuildPixels(string name, out int width, out int height, out int border)
        {
            Color inner = new Color(UiModernDark.GoldDim.r, UiModernDark.GoldDim.g, UiModernDark.GoldDim.b, INNER_LINE_ALPHA);
            Color selectedFill = new Color(UiModernDark.Cyan.r, UiModernDark.Cyan.g, UiModernDark.Cyan.b, SELECTED_FILL_ALPHA);
            width  = BOX_SIZE;
            height = BOX_SIZE;

            switch (name)
            {
                case UiModernDark.SPRITE_PANEL:
                    width = height = PANEL_SIZE;
                    border = 14;
                    return Box(PANEL_SIZE, UiModernDark.PanelFill, UiModernDark.Gold, 1, inner, 4, 6);
                case UiModernDark.SPRITE_INSET:
                    border = 8;
                    return Box(BOX_SIZE, UiModernDark.InsetFill, OUTLINE_SOFT, 1, CLEAR, 0, 3);
                case UiModernDark.SPRITE_BUTTON:
                    border = 8;
                    return Box(BOX_SIZE, UiModernDark.ButtonFill, OUTLINE_BUTTON, 1, CLEAR, 0, 3);
                case UiModernDark.SPRITE_BUTTON_PRIMARY:
                    border = 8;
                    return Box(BOX_SIZE, FILL_PRIMARY, UiModernDark.Cyan, 1, CLEAR, 0, 3);
                case UiModernDark.SPRITE_TAB:
                    border = 8;
                    return Box(BOX_SIZE, FILL_TAB, OUTLINE_SOFT, 1, CLEAR, 0, 3);
                case UiModernDark.SPRITE_SLOT:
                    border = 6;
                    return Box(BOX_SIZE, UiModernDark.InsetFill, OUTLINE_SOFT, 1, CLEAR, 0, 2);
                case UiModernDark.SPRITE_SELECTED:
                    border = 8;
                    return Box(BOX_SIZE, selectedFill, UiModernDark.Cyan, 2, CLEAR, 0, 2);
                case UiModernDark.SPRITE_CORNER:
                    width = height = CORNER_SIZE;
                    border = 0;
                    return Corner();
                case UiModernDark.SPRITE_DIAMOND:
                    width = height = DIAMOND_SIZE;
                    border = 0;
                    return Diamond();
                case UiModernDark.SPRITE_GLOW:
                    width  = GLOW_WIDTH;
                    height = GLOW_HEIGHT;
                    border = 0;
                    return Glow();
                default:
                    throw new ArgumentException("알 수 없는 공통 스타일 스프라이트: " + name);
            }
        }

        private static Color32[] Box(int size, Color fill, Color outline, int outlineWidth, Color inner, int innerOffset, int cut)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    int cx     = Mathf.Min(x, size - 1 - x);
                    int cy     = Mathf.Min(y, size - 1 - y);
                    int corner = cx + cy;
                    if (corner < cut)
                    {
                        pixels[(y * size) + x] = CLEAR;
                        continue;
                    }

                    int edge = Mathf.Min(cx, cy);
                    if ((cx < cut) && (cy < cut))
                    {
                        edge = Mathf.Min(edge, corner - cut);
                    }

                    Color color = fill;
                    if (edge < outlineWidth)
                    {
                        color = outline;
                    }
                    else if ((innerOffset > 0) && (edge == innerOffset))
                    {
                        color = inner;
                    }

                    pixels[(y * size) + x] = color;
                }
            }

            return pixels;
        }

        private static Color32[] Corner()
        {
            var pixels = new Color32[CORNER_SIZE * CORNER_SIZE];
            for (int y = 0; y < CORNER_SIZE; ++y)
            {
                for (int x = 0; x < CORNER_SIZE; ++x)
                {
                    int  fromTop = CORNER_SIZE - 1 - y;
                    bool arm     = ((fromTop < 2) && (x < CORNER_ARM)) || ((x < 2) && (fromTop < CORNER_ARM));
                    bool dot     = (x >= 4) && (x < 6) && (fromTop >= 4) && (fromTop < 6);
                    pixels[(y * CORNER_SIZE) + x] = (arm || dot) ? UiModernDark.Gold : CLEAR;
                }
            }

            return pixels;
        }

        private static Color32[] Diamond()
        {
            var pixels = new Color32[DIAMOND_SIZE * DIAMOND_SIZE];
            for (int y = 0; y < DIAMOND_SIZE; ++y)
            {
                for (int x = 0; x < DIAMOND_SIZE; ++x)
                {
                    float distance = Mathf.Abs(x - DIAMOND_CENTER) + Mathf.Abs(y - DIAMOND_CENTER);
                    pixels[(y * DIAMOND_SIZE) + x] = (distance <= DIAMOND_RADIUS) ? UiModernDark.Gold : CLEAR;
                }
            }

            return pixels;
        }

        private static Color32[] Glow()
        {
            var pixels = new Color32[GLOW_WIDTH * GLOW_HEIGHT];
            for (int y = 0; y < GLOW_HEIGHT; ++y)
            {
                float t     = 1f - ((float)y / (GLOW_HEIGHT - 1));
                float alpha = GLOW_PEAK_ALPHA * t * t;
                Color color = new Color(UiModernDark.TextGold.r, UiModernDark.TextGold.g, UiModernDark.TextGold.b, alpha);
                for (int x = 0; x < GLOW_WIDTH; ++x)
                {
                    pixels[(y * GLOW_WIDTH) + x] = color;
                }
            }

            return pixels;
        }
    }
}
