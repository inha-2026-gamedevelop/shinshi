// System
using System.Globalization;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 체력바 위 직업 스택 표시 한 번 분량, 값이 같으면 글자를 다시 만들지 않으려고 비교에 쓴다 </summary>
    public readonly struct ClassStackDisplay
    {
        /****************************************
        *              Properties
        ****************************************/

        public bool           IsVisible    { get; }
        public int            Stacks       { get; }
        public int            Max          { get; }
        public EArcherElement Element      { get; }
        public bool           ShowsElement { get; }
        public bool           IsFull       => (IsVisible) && (Stacks >= Max);

        public static ClassStackDisplay Hidden => default;

        /****************************************
        *                Methods
        ****************************************/

        public ClassStackDisplay(int stacks, int max, EArcherElement element, bool showsElement)
        {
            IsVisible    = true;
            Stacks       = stacks;
            Max          = max;
            Element      = element;
            ShowsElement = showsElement;
        }

        public bool SameAs(in ClassStackDisplay other)
        {
            return (IsVisible == other.IsVisible) && (Stacks == other.Stacks) && (Max == other.Max)
                && (Element == other.Element) && (ShowsElement == other.ShowsElement);
        }
    }

    /// <summary> 마검사, 궁수 스택을 체력바 위 글자로 바꾸는 순수 계산, 값은 서버가 복제한 표시 전용 스택이다 </summary>
    public static class ClassStackHudRules
    {
        /****************************************
        *              Constants
        ****************************************/

        // 하단 HUD 글꼴(LiberationSans)에는 한글이 없어 경험치 표시처럼 영문으로 쓴다
        private const string HUD_STACK_FORMAT          = "STACK {0} / {1}";
        // 궁수 스택은 속성과 무관한 총합이라 속성 이름과 떼어 "총 스택"으로 적는다
        private const string HUD_ELEMENT_FORMAT        = "{0}  TOTAL STACK {1} / {2}";
        private const string NAMEPLATE_STACK_FORMAT    = "스택 {0}/{1}";
        private const string NAMEPLATE_ELEMENT_FORMAT  = "{0} 총 스택 {1}/{2}";

        private const string HUD_ICE        = "ICE";
        private const string HUD_WIND       = "WIND";
        private const string HUD_FIRE       = "FIRE";
        private const string HUD_NONE       = "NO ELEMENT";
        private const string NAMEPLATE_ICE  = "얼음";
        private const string NAMEPLATE_WIND = "바람";
        private const string NAMEPLATE_FIRE = "불";
        private const string NAMEPLATE_NONE = "무속성";

        private static readonly Color G_DefaultColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color G_IceColor     = new Color(0.55f, 0.85f, 1f, 1f);
        private static readonly Color G_WindColor    = new Color(0.55f, 1f, 0.65f, 1f);
        private static readonly Color G_FireColor    = new Color(1f, 0.55f, 0.35f, 1f);
        private static readonly Color G_FullColor    = new Color(1f, 0.85f, 0.3f, 1f);
        // 속성 그림이 밝고 알록달록해 숫자는 눈에 띄는 빨간색에 외곽선을 둬서 읽히게 한다
        private static readonly Color G_BadgeColor   = new Color(1f, 0.2f, 0.2f, 1f);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 스택을 쓰는 직업인지, 마검사와 궁수만 보이고 검사를 비롯한 다른 직업은 숨긴다 </summary>
        public static bool ShowsStacks(EPlayerClass playerClass)
        {
            return (playerClass == EPlayerClass.Mageknight) || (playerClass == EPlayerClass.Archer);
        }

        /// <summary> 현재 속성을 함께 보이는 직업인지, 궁수만이다 </summary>
        public static bool ShowsElement(EPlayerClass playerClass)
        {
            return playerClass == EPlayerClass.Archer;
        }

        /// <summary> 복제 값으로 표시를 만든다, 스택을 쓰지 않는 직업이거나 최대치를 아직 모르면 숨김이다 </summary>
        public static ClassStackDisplay Build(EPlayerClass playerClass, int stacks, int max, EArcherElement element)
        {
            if ((!ShowsStacks(playerClass)) || (max <= 0))
            {
                return ClassStackDisplay.Hidden;
            }

            bool showsElement = ShowsElement(playerClass);
            return new ClassStackDisplay(Mathf.Clamp(stacks, 0, max), max, showsElement ? element : EArcherElement.None,
                showsElement);
        }

        /// <summary> 하단 HUD 체력바 위 글자, 숨김이면 빈 값이다 </summary>
        public static string FormatHud(in ClassStackDisplay display)
        {
            if (!display.IsVisible)
            {
                return string.Empty;
            }

            if (!display.ShowsElement)
            {
                return string.Format(CultureInfo.InvariantCulture, HUD_STACK_FORMAT, display.Stacks, display.Max);
            }

            return string.Format(CultureInfo.InvariantCulture, HUD_ELEMENT_FORMAT, HudElementName(display.Element),
                display.Stacks, display.Max);
        }

        /// <summary> 머리 위 이름표 위 글자, 이름표 글꼴은 한글을 그린다 </summary>
        public static string FormatNameplate(in ClassStackDisplay display)
        {
            if (!display.IsVisible)
            {
                return string.Empty;
            }

            if (!display.ShowsElement)
            {
                return string.Format(CultureInfo.InvariantCulture, NAMEPLATE_STACK_FORMAT, display.Stacks, display.Max);
            }

            return string.Format(CultureInfo.InvariantCulture, NAMEPLATE_ELEMENT_FORMAT, NameplateElementName(display.Element),
                display.Stacks, display.Max);
        }

        /// <summary> 다 차면 금색, 궁수는 속성 색, 속성이 없는 마검사는 빨간색이다 </summary>
        public static Color GetColor(in ClassStackDisplay display)
        {
            if (display.IsFull)
            {
                return G_FullColor;
            }

            return display.ShowsElement ? GetElementColor(display.Element) : G_BadgeColor;
        }

        /// <summary> 속성 아이콘 원화에서 고를 칸 번호, 아이콘을 그리지 않는 표시(궁수가 아니거나 속성이 없음)는 -1이다 </summary>
        public static int GetElementColumn(in ClassStackDisplay display)
        {
            if ((!display.IsVisible) || (!display.ShowsElement))
            {
                return -1;
            }

            switch (display.Element)
            {
                case EArcherElement.Ice:
                    return 0;
                case EArcherElement.Wind:
                    return 1;
                case EArcherElement.Fire:
                    return 2;
                default:
                    return -1;
            }
        }

        /// <summary> 속성 아이콘 옆에 스택 숫자를 보이는지, 궁수만이고 0이어도 보인다 </summary>
        public static bool ShowsBadgeCount(in ClassStackDisplay display)
        {
            return (display.IsVisible) && (display.ShowsElement);
        }

        /// <summary> 아이콘 옆 숫자색, 다 차면 금색이고 그 전에는 빨간색이다 </summary>
        public static Color GetBadgeColor(in ClassStackDisplay display)
        {
            return display.IsFull ? G_FullColor : G_BadgeColor;
        }

        /// <summary> 속성 고유색, 무속성이면 기본색이다 </summary>
        public static Color GetElementColor(EArcherElement element)
        {
            switch (element)
            {
                case EArcherElement.Ice:
                    return G_IceColor;
                case EArcherElement.Wind:
                    return G_WindColor;
                case EArcherElement.Fire:
                    return G_FireColor;
                default:
                    return G_DefaultColor;
            }
        }

        private static string HudElementName(EArcherElement element)
        {
            switch (element)
            {
                case EArcherElement.Ice:
                    return HUD_ICE;
                case EArcherElement.Wind:
                    return HUD_WIND;
                case EArcherElement.Fire:
                    return HUD_FIRE;
                default:
                    return HUD_NONE;
            }
        }

        private static string NameplateElementName(EArcherElement element)
        {
            switch (element)
            {
                case EArcherElement.Ice:
                    return NAMEPLATE_ICE;
                case EArcherElement.Wind:
                    return NAMEPLATE_WIND;
                case EArcherElement.Fire:
                    return NAMEPLATE_FIRE;
                default:
                    return NAMEPLATE_NONE;
            }
        }
    }
}
