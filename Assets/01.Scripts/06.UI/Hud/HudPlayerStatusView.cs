// System
using System.Globalization;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;
using TMPro;

// 프로젝트 내부
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

        private Tween _trailTween = null;
        private float _hpRatio    = 0f;

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

        private void OnDisable()
        {
            _trailTween?.Kill();
            _trailTween = null;
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
