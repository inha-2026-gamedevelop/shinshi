// System
using System.Globalization;

// Unity
using UnityEngine;
using UnityEngine.UI;

// Third Party
using DG.Tweening;
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 씬에 배치하거나 필요한 HUD에 한 번 생성하는 경험치 표시 </summary>
    public sealed class HudExperienceView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float VALUE_FONT_SIZE = 14f;
        private const float NOTICE_FONT_SIZE = 22f;
        private const float TRACK_BOTTOM = 0.7f;
        private const float MIN_FADE_SECONDS = 0.01f;
        private const string MAXIMUM_TEXT = "EXP MAX";
        private const string PROGRESS_FORMAT = "EXP {0:N0} / {1:N0}";
        private const string LEVEL_UP_PREFIX = "LEVEL UP!  Lv.";
        private static readonly Vector2 G_OffsetMin = new Vector2(95f, -24f);
        private static readonly Vector2 G_OffsetMax = new Vector2(-11f, -2f);
        private static readonly Vector2 G_NoticePosition = new Vector2(0, 108);
        private static readonly Vector2 G_NoticeSize = new Vector2(0, 10);
        private static readonly Color G_TrackColor = new Color(0.08f, 0.09f, 0.11f, 0.95f);
        private static readonly Color G_FillColor = new Color(0.85f, 0.69f, 0.32f, 1f);

        [SerializeField] private float _noticeSeconds = 1.6f;
        [SerializeField] private float _fadeSeconds   = 0.4f;
        [SerializeField] private RectTransform _fill;
        [SerializeField] private TMP_Text      _value;
        [SerializeField] private TMP_Text      _notice;
        [SerializeField] private CanvasGroup   _noticeGroup;
        private Tween         _noticeTween;
        private long          _total = -1;
        private int           _level;
        private long          _current;
        private long          _required;
        private bool          _maximum;

        /****************************************
        *              Properties
        ****************************************/

        public bool NoticeVisible => (_noticeGroup != null) && (_noticeGroup.alpha > 0f);
        public string DisplayText => _value == null ? string.Empty : _value.text;

        /****************************************
        *                Methods
        ****************************************/

        public void Initialize(TMP_Text style, RectTransform originalFill)
        {
            if (_value != null)
            {
                return;
            }
            var root = (RectTransform)transform;
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(0.5f, 1f);
            root.offsetMin = G_OffsetMin;
            root.offsetMax = G_OffsetMax;
            RectTransform track = Rect("Track", root, new Vector2(0, TRACK_BOTTOM), Vector2.one);
            Image background = track.gameObject.AddComponent<Image>();
            background.raycastTarget = false;
            background.color = G_TrackColor;
            _fill = Rect("Fill", track, Vector2.zero, Vector2.one);
            Image fill = _fill.gameObject.AddComponent<Image>();
            fill.raycastTarget = false;
            fill.color = G_FillColor;
            if ((originalFill != null) && originalFill.TryGetComponent(out Image source))
            {
                background.sprite = source.sprite;
                fill.sprite = source.sprite;
                background.type = fill.type = source.type;
            }
            _value = Label("Experience", root, style, VALUE_FONT_SIZE);
            _value.alignment = TextAlignmentOptions.Center;
            _value.rectTransform.anchorMax = new Vector2(1, TRACK_BOTTOM);
            _notice = Label("LevelUp", root, style, NOTICE_FONT_SIZE);
            _notice.rectTransform.anchoredPosition = G_NoticePosition;
            _notice.rectTransform.sizeDelta = G_NoticeSize;
            _notice.alignment = TextAlignmentOptions.Center;
            _noticeGroup = _notice.gameObject.AddComponent<CanvasGroup>();
            _noticeGroup.blocksRaycasts = false;
            _noticeGroup.interactable = false;
            ResetDisplay();
        }

        public void Show(long total, in HudExperienceProgress progress)
        {
            if ((_value == null) || ((total == _total) && (progress.Level == _level)
                && (progress.Current == _current) && (progress.Required == _required) && (progress.IsMaximum == _maximum)))
            {
                return;
            }
            bool increased = (_total >= 0) && (total > _total) && (progress.Level > _level);
            _total = total;
            _level = progress.Level;
            _current = progress.Current;
            _required = progress.Required;
            _maximum = progress.IsMaximum;
            _fill.anchorMax = new Vector2(progress.Ratio, 1);
            _value.text = progress.IsMaximum ? MAXIMUM_TEXT : string.Format(CultureInfo.InvariantCulture,
                PROGRESS_FORMAT, progress.Current, progress.Required);
            if (!increased)
            {
                return;
            }
            _noticeTween?.Rewind();
            _notice.text = LEVEL_UP_PREFIX + progress.Level.ToString(CultureInfo.InvariantCulture);
            _noticeGroup.alpha = 1f;
            if (Application.isPlaying && isActiveAndEnabled)
            {
                if (_noticeTween == null)
                {
                    _noticeTween = DOTween.To(() => _noticeGroup.alpha, value => _noticeGroup.alpha = value,
                        0f, Mathf.Max(MIN_FADE_SECONDS, _fadeSeconds))
                        .SetDelay(Mathf.Max(0f, _noticeSeconds)).SetUpdate(true).SetAutoKill(false).Pause();
                    _noticeTween.ForceInit();
                }
                _noticeTween.Restart();
            }
        }

        public void ResetDisplay()
        {
            _total = -1;
            _level = 0;
            _noticeTween?.Rewind();
            _noticeTween?.Pause();
            if (_noticeGroup != null)
            {
                _noticeGroup.alpha = 0;
            }
            if (_value != null)
            {
                _value.text = string.Empty;
            }
            if (_fill != null)
            {
                _fill.anchorMax = new Vector2(0, 1);
            }
        }

        private void OnDisable() => ResetDisplay();
        private void OnDestroy() => _noticeTween?.Kill();

        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.hideFlags = HideFlags.DontSave;
            rect.SetParent(parent, false);
            rect.gameObject.layer = parent.gameObject.layer;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static TMP_Text Label(string name, Transform parent, TMP_Text style, float size)
        {
            RectTransform rect = Rect(name, parent, Vector2.zero, Vector2.one);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (style != null)
            {
                label.font = style.font;
                label.fontSharedMaterial = style.fontSharedMaterial;
                label.color = style.color;
            }
            label.fontSize = size;
            label.raycastTarget = false;
            return label;
        }
    }
}
