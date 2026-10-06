// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using DG.Tweening;
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 채움 게이지 끝에 퍼센트 글자를 붙여 둔다, 값이 바뀌면 채움과 글자가 함께 따라 오른다 </summary>
    public sealed class UiGaugeFollowLabel : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  PERCENT_SCALE  = 100f;
        private const float  ANCHOR_MIDDLE  = 0.5f;
        private const float  PIVOT_LEFT     = 0f;
        private const float  PIVOT_RIGHT    = 1f;
        private const string PERCENT_FORMAT = "{0:0}%";

        [Header("표시")]
        [SerializeField] private Image         _fill      = null;
        [Tooltip("채움 Image의 자식으로 둔다, 부모 너비를 막대 길이로 삼아 비율 위치에 붙는다")]
        [SerializeField] private RectTransform _label     = null;
        [SerializeField] private TMP_Text      _labelText = null;

        [Header("배치")]
        [Tooltip("채움 끝과 글자 사이 간격(px)")]
        [SerializeField] private float _gap        = 6f;
        [Tooltip("이 비율부터 글자를 채움 안쪽으로 넣는다, 막대 밖으로 넘치지 않게 한다")]
        [SerializeField] private float _insideFrom = 0.85f;

        [Header("연출")]
        [SerializeField] private float _duration = 0.4f;
        [SerializeField] private Ease  _ease     = Ease.OutCubic;

        private Tweener _tween   = null;
        private float   _ratio   = 0f;
        private int     _percent = -1;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_fill == null) || (_label == null) || (_labelText == null))
            {
                Debug.LogError("[UiGaugeFollowLabel] 채움, 글자 참조가 비어 있다", this);
                enabled = false;
                return;
            }

            Apply(_fill.fillAmount);

            // 처치마다 다시 부르므로 한 번 만들어 값만 바꿔 재사용한다
            _tween = DOTween.To(GetRatio, Apply, _ratio, _duration)
                .SetEase(_ease)
                .SetAutoKill(false)
                .Pause()
                .SetLink(gameObject);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 0~1 비율로 채움을 바꾼다, 현재 표시값에서 목표까지 따라 오른다 </summary>
        public void SetRatio(float ratio)
        {
            if (_tween == null)
            {
                return;
            }

            _tween.ChangeValues(_ratio, Mathf.Clamp01(ratio), _duration);
            _tween.Restart();
        }

        /// <summary> 연출 없이 바로 바꾼다, 재접속 복구처럼 지난 값을 보여 줄 때 쓴다 </summary>
        public void SetRatioImmediate(float ratio)
        {
            if (_tween != null)
            {
                _tween.Pause();
            }

            Apply(ratio);
        }

        private float GetRatio() { return _ratio; }

        private void Apply(float ratio)
        {
            if ((_fill == null) || (_label == null) || (_labelText == null))
            {
                return;
            }

            _ratio           = Mathf.Clamp01(ratio);
            _fill.fillAmount = _ratio;

            bool    isInside = _ratio >= _insideFrom;
            Vector2 anchor   = new Vector2(_ratio, ANCHOR_MIDDLE);

            _label.anchorMin        = anchor;
            _label.anchorMax        = anchor;
            _label.pivot            = new Vector2(isInside ? PIVOT_RIGHT : PIVOT_LEFT, ANCHOR_MIDDLE);
            _label.anchoredPosition = new Vector2(isInside ? -_gap : _gap, 0f);
            _labelText.alignment    = isInside ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;

            // 트윈이 매 프레임 부르므로 정수 퍼센트가 바뀔 때만 글자를 다시 만든다
            int percent = Mathf.RoundToInt(_ratio * PERCENT_SCALE);
            if (percent == _percent)
            {
                return;
            }

            _percent = percent;
            _labelText.SetText(PERCENT_FORMAT, percent);
        }
    }
}
