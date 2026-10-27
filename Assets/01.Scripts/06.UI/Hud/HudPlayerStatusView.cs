// System
using System.Globalization;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using DG.Tweening;
using TMPro;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.UI
{
    /// <summary>
    /// 상시 화면 아래쪽의 레벨, HP, MP 표시. 값은 플레이어 상태를 가진 쪽이 Set 메서드로 넣는다
    /// </summary>
    public sealed class HudPlayerStatusView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string VALUE_FORMAT = "{0:N0} / {1:N0}";
        private const float TRAIL_HIDE_EPSILON = 0.001f;

        // 체력바 바로 위 직업 스택 글자. 씬과 프리팹을 고치지 않으려고 처음 보일 때 HP 막대 위에 만든다
        private const float STACK_FONT_SIZE = 14f;
        private const float STACK_GAP       = 2f;
        private const float STACK_HEIGHT    = 18f;

        // 궁수 속성 아이콘은 BuffGroup 줄 맨 앞에 둔다. 씬과 프리팹을 고치지 않으려고 처음 보일 때 만든다.
        // 아이콘 원화는 같은 정사각형 세 칸(얼음, 바람, 불)이라 UV로 골라 쓴다
        private const string STATUS_LAYOUT_PATH      = "Layout_AlwaysVisible";
        private const string BUFF_GROUP_PATH         = "Layout_AlwaysVisible/StatusEffectGroup/BuffGroup";
        private const string ELEMENT_TEXTURE_PATH    = "T_ArcherElementOverhead";
        private const int    ELEMENT_ATLAS_COLUMNS   = 3;
        private const float  ELEMENT_ICON_SIZE       = 32f;
        private const float  ELEMENT_LEFT_MARGIN     = 8f;
        private const float  ELEMENT_COUNT_FONT_SIZE = 22f;
        private const float  ELEMENT_COUNT_OFFSET_Y  = 0f;   // 숫자는 아이콘 오른쪽 아래 모서리에 겹친다
        private const float  ELEMENT_COUNT_OUTLINE   = 0.55f; // 밝은 그림 위에서도 읽히게 하는 외곽선
        private const string ELEMENT_COUNT_FORMAT    = "{0}";
        // 다 차면 아이콘이 숨 쉬듯 커졌다 줄어든다
        private const float  FULL_PULSE_SCALE        = 1.15f;
        private const float  FULL_PULSE_SECONDS      = 0.5f;
        private const float  FULL_GLOW_SIZE          = 48f;
        private const float  FULL_GLOW_ALPHA         = 0.6f;
        private const int    FULL_GLOW_TEXTURE_SIZE  = 32;
        private static Texture2D s_fullGlowTexture;

        [Header("레벨")]
        [SerializeField] private TMP_Text _levelText = null;

        [Header("HP")]
        [SerializeField] private RectTransform _hpFill = null;
        [SerializeField] private TMP_Text      _hpText = null;

        [Header("HP 피해 잔상 (맞은 만큼 하얗게 남았다가 서서히 줄어든다)")]
        [SerializeField] private RectTransform _hpTrail         = null; // 채움 칸 뒤에 겹쳐 둔 하얀 칸
        [SerializeField, Min(0f)] private float _trailDelay     = 0.35f; // 초, 맞은 뒤 줄어들기 시작하기까지
        [SerializeField, Min(0f)] private float _trailDuration  = 0.8f;  // 초, 하얀 칸이 다 줄어드는 시간

        [Header("MP")]
        [SerializeField] private RectTransform _mpFill = null;
        [SerializeField] private TMP_Text      _mpText = null;

        [SerializeField] private HudExperienceView _experience;
        private int _level     = -1;
        private int _hpCurrent = -1;
        private int _hpMax     = -1;
        private int _mpCurrent = -1;
        private int _mpMax     = -1;

        private TMP_Text          _stackText;
        private ClassStackDisplay _stackDisplay = ClassStackDisplay.Hidden;
        private Tween             _trailTween   = null;
        private float             _hpRatio      = 0f;

        private RectTransform _elementBadge;
        private RawImage      _elementImage;
        private RawImage      _elementGlow;
        private TMP_Text      _elementCount;
        private Tween         _fullPulse = null;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 로컬 바인더가 소유한 임시 HUD인지 나타낸다 </summary>
        public bool IsRuntimeOwned { get; private set; }

        /// <summary> 씬 HUD로 다른 바인더가 가져가지 않도록 런타임 소유를 표시한다 </summary>
        public void MarkRuntimeOwned()
        {
            IsRuntimeOwned = true;
        }

        public void SetExperience(CharacterLevelSettings settings, long total)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }
            if (!HudExperienceRules.TryRead(settings, total, out HudExperienceProgress progress))
            {
                ResetExperience();
                if (_levelText != null)
                {
                    _levelText.text = "-";
                }
                _level = -1;
                return;
            }
            if (_experience == null)
            {
                var child = new GameObject("ExperienceDisplay", typeof(RectTransform));
                child.hideFlags = HideFlags.DontSave;
                child.layer = gameObject.layer;
                child.transform.SetParent(transform, false);
                _experience = child.AddComponent<HudExperienceView>();
                _experience.Initialize(_mpText != null ? _mpText : _levelText, _mpFill);
            }
            SetLevel(progress.Level);
            _experience.Show(total, in progress);
        }

        public void ResetExperience()
        {
            if (_experience != null)
            {
                _experience.ResetDisplay();
            }
        }

        // 그림이 없는 버프, 디버프 자리 표시 칸의 회색 바탕은 항상 끈다
        private void OnEnable()
        {
            HudModernLayout.HidePlaceholderIcons(transform.root.Find(STATUS_LAYOUT_PATH));
        }

        private void OnDisable()
        {
            _trailTween?.Kill();
            _trailTween = null;
            StopFullPulse();
            ResetExperience();
        }

        public void SetLevel(int level)
        {
            if (level == _level)
            {
                return;
            }

            _level = level;
            if (_levelText != null)
            {
                _levelText.SetText(level.ToString(CultureInfo.InvariantCulture));
            }
        }

        public void SetHp(int current, int max)
        {
            if ((current == _hpCurrent) && (max == _hpMax))
            {
                return;
            }

            bool isDamaged = (_hpCurrent >= 0) && (max == _hpMax) && (current < _hpCurrent);

            _hpCurrent = current;
            _hpMax = max;
            ApplyBar(_hpFill, _hpText, current, max);
            ApplyTrail(current, max, isDamaged);
        }

        // 줄어든 만큼은 하얀 칸이 이전 길이에 그대로 남아 있다가, 잠깐 있다가 새 길이까지 줄어든다.
        // 연달아 맞으면 지금 길이에서 이어서 줄여 잔상이 끊기지 않는다. 늘거나 최대치가 바뀌면 바로 맞춘다.
        // 잔상이 채움보다 길 때만 켠다. 같은 길이로 겹쳐 두면 채움 가장자리의 반투명 픽셀 뒤로 잔상 색이 비쳐 튀어나와 보인다
        private void ApplyTrail(int current, int max, bool isDamaged)
        {
            if (_hpTrail == null)
            {
                return;
            }

            _trailTween?.Kill();
            _trailTween = null;

            float ratio = (max > 0) ? Mathf.Clamp01((float)current / max) : 0f;
            _hpRatio = ratio;
            if (!isDamaged)
            {
                SetTrailRatio(ratio);
                return;
            }

            _hpTrail.gameObject.SetActive(_hpTrail.anchorMax.x > ratio + TRAIL_HIDE_EPSILON);
            _trailTween = DOTween.To(() => _hpTrail.anchorMax.x, SetTrailRatio, ratio, _trailDuration)
                .SetDelay(_trailDelay)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
        }

        private void SetTrailRatio(float ratio)
        {
            Vector2 anchorMax = _hpTrail.anchorMax;
            anchorMax.x = ratio;
            _hpTrail.anchorMax = anchorMax;
            _hpTrail.gameObject.SetActive(ratio > _hpRatio + TRAIL_HIDE_EPSILON);
        }

        public void SetMp(int current, int max)
        {
            if ((current == _mpCurrent) && (max == _mpMax))
            {
                return;
            }

            _mpCurrent = current;
            _mpMax = max;
            ApplyBar(_mpFill, _mpText, current, max);
        }

        /// <summary> 체력바 위 직업 스택 글자를 갱신한다, 값이 같으면 바로 돌아온다 </summary>
        public void SetClassStacks(in ClassStackDisplay display)
        {
            if (display.SameAs(in _stackDisplay))
            {
                return;
            }

            _stackDisplay = display;
            ApplyElementIcon(in display);
            if ((!display.IsVisible) || ((display.ShowsElement) && (display.Element == EArcherElement.None)))
            {
                if (_stackText != null)
                {
                    _stackText.enabled = false;
                }

                return;
            }

            if ((_stackText == null) && (!TryCreateStackText()))
            {
                return;
            }

            // 속성 아이콘을 BuffGroup에 보이는 동안은 같은 정보라 체력바 위 글자를 감춘다
            _stackText.enabled = (_elementBadge == null) || (!_elementBadge.gameObject.activeSelf);
            _stackText.color   = ClassStackHudRules.GetColor(in display);
            _stackText.SetText(ClassStackHudRules.FormatHud(in display));
        }

        // HP 막대의 트랙(채움 칸의 부모) 위쪽 바깥에 붙인다. 씬마다 HUD 배치가 달라도 막대 기준이라 위치가 같다
        private bool TryCreateStackText()
        {
            if ((_hpFill == null) || (_hpText == null) || (!(_hpFill.parent is RectTransform track)))
            {
                return false;
            }

            var child = new GameObject("ClassStackDisplay", typeof(RectTransform));
            child.hideFlags = HideFlags.DontSave;
            child.layer     = gameObject.layer;
            var rect = (RectTransform)child.transform;
            rect.SetParent(track, false);
            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(1f, 1f);
            rect.pivot            = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, STACK_GAP);
            rect.sizeDelta        = new Vector2(0f, STACK_HEIGHT);

            var text = child.AddComponent<TextMeshProUGUI>();
            text.font               = _hpText.font;
            text.fontSharedMaterial = _hpText.fontSharedMaterial;
            text.fontSize           = STACK_FONT_SIZE;
            text.alignment          = TextAlignmentOptions.BottomLeft;
            text.textWrappingMode   = TextWrappingModes.NoWrap;
            text.raycastTarget      = false;
            _stackText = text;
            return true;
        }

        // 속성이 정해진 궁수만 아이콘과 숫자를 BuffGroup에 보인다. 값이 바뀔 때만 불린다
        private void ApplyElementIcon(in ClassStackDisplay display)
        {
            int column = ClassStackHudRules.GetElementColumn(in display);
            if (column < 0)
            {
                if (_elementBadge != null)
                {
                    _elementBadge.gameObject.SetActive(false);
                    StopFullPulse();
                }

                return;
            }

            if ((_elementBadge == null) && (!TryCreateElementBadge()))
            {
                return;
            }

            _elementBadge.gameObject.SetActive(true);
            float cellWidth = 1f / ELEMENT_ATLAS_COLUMNS;
            _elementImage.uvRect = new Rect(column * cellWidth, 0f, cellWidth, 1f);
            Color glowColor = ClassStackHudRules.GetElementColor(display.Element);
            glowColor.a = FULL_GLOW_ALPHA;
            _elementGlow.color = glowColor;
            _elementGlow.gameObject.SetActive(display.IsFull);

            bool showsCount = ClassStackHudRules.ShowsBadgeCount(in display);
            _elementCount.enabled = showsCount;
            if (showsCount)
            {
                _elementCount.SetText(ELEMENT_COUNT_FORMAT, display.Stacks);
            }

            _elementCount.color = ClassStackHudRules.GetBadgeColor(in display);
            SetFullPulse(display.IsFull);
        }

        // BuffGroup이 없는 HUD(런타임 임시 HUD, 시험 구성)에서는 아이콘을 그리지 않는다
        private bool TryCreateElementBadge()
        {
            Transform group   = transform.root.Find(BUFF_GROUP_PATH);
            var       texture = Resources.Load<Texture2D>(ELEMENT_TEXTURE_PATH);
            if ((group == null) || (texture == null) || (_hpText == null))
            {
                return false;
            }

            var badgeObject = new GameObject("ClassElementBadge", typeof(RectTransform));
            badgeObject.hideFlags = HideFlags.DontSave;
            badgeObject.layer     = gameObject.layer;
            _elementBadge = (RectTransform)badgeObject.transform;
            _elementBadge.SetParent(group, false);
            _elementBadge.anchorMin        = new Vector2(0f, 0.5f);
            _elementBadge.anchorMax        = new Vector2(0f, 0.5f);
            _elementBadge.pivot            = new Vector2(0f, 0.5f);
            _elementBadge.anchoredPosition = new Vector2(ELEMENT_LEFT_MARGIN, 0f);
            _elementBadge.sizeDelta        = new Vector2(ELEMENT_ICON_SIZE, ELEMENT_ICON_SIZE);

            var glowObject = new GameObject("Glow", typeof(RectTransform));
            glowObject.layer = gameObject.layer;
            var glowRect = (RectTransform)glowObject.transform;
            glowRect.SetParent(_elementBadge, false);
            glowRect.anchorMin        = new Vector2(0.5f, 0.5f);
            glowRect.anchorMax        = new Vector2(0.5f, 0.5f);
            glowRect.pivot            = new Vector2(0.5f, 0.5f);
            glowRect.anchoredPosition = Vector2.zero;
            glowRect.sizeDelta        = new Vector2(FULL_GLOW_SIZE, FULL_GLOW_SIZE);
            _elementGlow = glowObject.AddComponent<RawImage>();
            _elementGlow.texture = GetFullGlowTexture();
            _elementGlow.raycastTarget = false;
            glowObject.SetActive(false);

            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.layer = gameObject.layer;
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(_elementBadge, false);
            iconRect.anchorMin        = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax        = new Vector2(0.5f, 0.5f);
            iconRect.pivot            = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta        = new Vector2(ELEMENT_ICON_SIZE, ELEMENT_ICON_SIZE);
            _elementImage = iconObject.AddComponent<RawImage>();
            _elementImage.texture       = texture;
            _elementImage.raycastTarget = false;

            var countObject = new GameObject("Count", typeof(RectTransform));
            countObject.layer = gameObject.layer;
            var countRect = (RectTransform)countObject.transform;
            countRect.SetParent(_elementBadge, false);
            countRect.anchorMin        = new Vector2(0.5f, 0.5f);
            countRect.anchorMax        = new Vector2(0.5f, 0.5f);
            countRect.pivot            = new Vector2(0.5f, 0.5f);
            countRect.anchoredPosition = new Vector2(0f, ELEMENT_COUNT_OFFSET_Y);
            countRect.sizeDelta        = new Vector2(ELEMENT_ICON_SIZE, ELEMENT_ICON_SIZE);
            var count = countObject.AddComponent<TextMeshProUGUI>();
            count.font               = _hpText.font;
            count.fontSharedMaterial = _hpText.fontSharedMaterial;
            count.fontSize           = ELEMENT_COUNT_FONT_SIZE;
            count.fontStyle          = FontStyles.Bold;
            count.alignment          = TextAlignmentOptions.BottomRight;
            count.outlineWidth       = ELEMENT_COUNT_OUTLINE;
            count.outlineColor       = Color.black;
            count.textWrappingMode   = TextWrappingModes.NoWrap;
            count.raycastTarget      = false;
            count.enabled            = false;
            _elementCount = count;
            return true;
        }

        private static Texture2D GetFullGlowTexture()
        {
            if (s_fullGlowTexture != null)
            {
                return s_fullGlowTexture;
            }

            var texture = new Texture2D(FULL_GLOW_TEXTURE_SIZE, FULL_GLOW_TEXTURE_SIZE, TextureFormat.RGBA32, false);
            texture.name = "ClassStackFullGlow";
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[FULL_GLOW_TEXTURE_SIZE * FULL_GLOW_TEXTURE_SIZE];
            for (int y = 0; y < FULL_GLOW_TEXTURE_SIZE; ++y)
            {
                for (int x = 0; x < FULL_GLOW_TEXTURE_SIZE; ++x)
                {
                    float nx = ((x + 0.5f) / FULL_GLOW_TEXTURE_SIZE * 2f) - 1f;
                    float ny = ((y + 0.5f) / FULL_GLOW_TEXTURE_SIZE * 2f) - 1f;
                    float alpha = Mathf.Clamp01(1f - Mathf.Sqrt((nx * nx) + (ny * ny)));
                    pixels[(y * FULL_GLOW_TEXTURE_SIZE) + x] = new Color(1f, 1f, 1f, alpha * alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            s_fullGlowTexture = texture;
            return s_fullGlowTexture;
        }

        private void SetFullPulse(bool full)
        {
            if (!full)
            {
                StopFullPulse();
                return;
            }

            if ((_fullPulse != null) && (_fullPulse.IsActive()))
            {
                return;
            }

            _fullPulse = _elementImage.rectTransform.DOScale(FULL_PULSE_SCALE, FULL_PULSE_SECONDS)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(_elementImage.gameObject);
        }

        private void StopFullPulse()
        {
            _fullPulse?.Kill();
            _fullPulse = null;
            if (_elementImage != null)
            {
                _elementImage.rectTransform.localScale = Vector3.one;
            }
        }

        // Image의 Filled 방식은 바 끝의 둥근 모양을 잘라 버린다.
        // 그래서 9분할 채움 칸의 오른쪽 앵커를 줄여 끝 모양이 현재 값 위치를 따라가게 한다
        private static void ApplyBar(RectTransform fill, TMP_Text text, int current, int max)
        {
            float ratio = (max > 0) ? Mathf.Clamp01((float)current / max) : 0f;
            Vector2 anchorMax = fill.anchorMax;
            anchorMax.x = ratio;
            fill.anchorMax = anchorMax;
            text.SetText(string.Format(CultureInfo.InvariantCulture, VALUE_FORMAT, current, max));
        }
    }
}
