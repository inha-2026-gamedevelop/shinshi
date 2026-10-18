// System
using System;
using System.Globalization;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 로딩과 통신, 스킬 게이지, 시간이 걸리는 행동에 함께 쓰는 진행 막대. 채움 연출은 UiGaugeFollowLabel을 그대로 쓴다 </summary>
    // 단계로 오르는 값(로딩, 통신)은 SetStep으로 끊어 오르는 연출을 쓰고, 매 프레임 바뀌는 값(스킬, 행동)은 StartTimed가
    // 연출 없이 바로 채운다. 매 프레임 SetStep을 부르면 끊어 오르기가 계속 다시 시작해 막대가 떨려 보인다
    public sealed class UiProgressBar : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SECONDS_FORMAT  = "{0:0.0}초";
        private const string COMPLETE_TEXT   = "완료";
        private const float  TENTHS_DIVISOR  = 10f;
        private const float  SWEEP_WIDTH     = 0.28f;
        private const float  SWEEP_SECONDS   = 1.1f;
        private const float  FADE_MIN_SECONDS = 0.01f;
        private const int    NONE            = -1;

        [Header("막대")]
        [SerializeField] private UiGaugeFollowLabel _gauge       = null;
        [Tooltip("채움 막대 위에 그려 무한 대기 때 좌우로 오가는 띠, 비우면 무한 대기를 쓰지 않는다")]
        [SerializeField] private RectTransform      _sweep       = null;
        [Tooltip("채움 끝에 붙는 퍼센트 글자의 뿌리, 스킬 막대처럼 글자가 필요 없으면 끈다")]
        [SerializeField] private GameObject         _percentRoot = null;

        [Header("글자")]
        [SerializeField] private TMP_Text _title  = null;
        [SerializeField] private TMP_Text _detail = null;

        [Header("색")]
        [SerializeField] private Color _activeColor    = new Color(0.725f, 0.553f, 0.243f, 1f);
        [SerializeField] private Color _completedColor = new Color(0.922f, 0.788f, 0.478f, 1f);
        [SerializeField] private Color _failedColor    = new Color(0.86f, 0.42f, 0.38f, 1f);

        [Header("표시")]
        [SerializeField] private CanvasGroup _group       = null;
        [SerializeField] private float       _fadeSeconds = 0.25f;

        private EProgressBarState _state          = EProgressBarState.Idle;
        private Tweener           _sweepTween     = null;
        private Tweener           _fadeTween      = null;
        private float             _sweepPhase     = 0f;
        private float             _ratio          = 0f;
        private float             _elapsed        = 0f;
        private float             _duration       = 0f;
        private bool              _isDrain        = false;
        private bool              _isTimed        = false;
        private bool              _percentVisible = true;
        private int               _shownTenths    = NONE;

        /****************************************
        *              Properties
        ****************************************/

        public EProgressBarState State     => _state;
        public bool              IsRunning => _state == EProgressBarState.Active;
        public float             Ratio     => _ratio;

        public event Action Completed;
        public event Action Failed;

        /****************************************
        *              Unity Event
        ****************************************/

        // 시간 채움이 있을 때만 Update가 돈다
        private void Awake()
        {
            enabled = false;
            if (_percentRoot != null)
            {
                _percentVisible = _percentRoot.activeSelf;
            }

            if (_sweep != null)
            {
                _sweep.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 새 진행을 시작한다, 값을 0으로 되돌리고 보이게 한다 </summary>
        public void Begin(string title)
        {
            StopTimed();
            SetIndeterminate(false);
            _state = EProgressBarState.Active;
            _ratio = 0f;
            ApplyColor(_activeColor);
            SetText(_title, title);
            SetText(_detail, string.Empty);
            if (_gauge != null)
            {
                _gauge.SetRatioImmediate(0f);
            }

            Show();
        }

        /// <summary> 단계로 오르는 값, 끊어 오르는 연출과 함께 목표까지 오른다 </summary>
        public void SetStep(float ratio, string detail)
        {
            if (_state != EProgressBarState.Active)
            {
                return;
            }

            _ratio = ProgressBarRules.ClampRatio(ratio);
            SetIndeterminate(false);
            if (_detail != null)
            {
                SetText(_detail, detail);
            }

            if (_gauge != null)
            {
                _gauge.SetRatio(_ratio);
            }
        }

        /// <summary> 정해진 시간 동안 연출 없이 채운다. drain이면 가득에서 비워 간다. 끝나면 완료로 바뀐다 </summary>
        public void StartTimed(string title, float seconds, bool drain)
        {
            Begin(title);
            _duration    = seconds;
            _elapsed     = 0f;
            _isDrain     = drain;
            _isTimed     = true;
            _shownTenths = NONE;
            _ratio       = ProgressBarRules.TimedRatio(0f, seconds, drain);
            if (_gauge != null)
            {
                _gauge.SetRatioImmediate(_ratio);
            }

            if (seconds <= 0f)
            {
                Complete(null);
                return;
            }

            ShowRemaining(seconds);
            enabled = true;
        }

        /// <summary> 시간을 deltaTime만큼 보낸다. Update가 부르며 테스트도 직접 부른다 </summary>
        public void Advance(float deltaTime)
        {
            if ((!_isTimed) || (_state != EProgressBarState.Active))
            {
                return;
            }

            _elapsed += Mathf.Max(0f, deltaTime);
            _ratio    = ProgressBarRules.TimedRatio(_elapsed, _duration, _isDrain);
            if (_gauge != null)
            {
                _gauge.SetRatioImmediate(_ratio);
            }

            if (_elapsed >= _duration)
            {
                Complete(null);
                return;
            }

            ShowRemaining(ProgressBarRules.RemainingSeconds(_elapsed, _duration));
        }

        /// <summary> 끝났다고 알린다. 단계 진행은 가득 채우고, 시간 채움은 이미 끝난 값이다 </summary>
        public void Complete(string detail)
        {
            if (_state != EProgressBarState.Active)
            {
                return;
            }

            StopTimed();
            SetIndeterminate(false);
            _state = EProgressBarState.Completed;
            if ((!_isDrain) && (_gauge != null))
            {
                _ratio = 1f;
                _gauge.SetRatio(1f);
            }

            ApplyColor(_completedColor);
            SetText(_detail, string.IsNullOrEmpty(detail) ? COMPLETE_TEXT : detail);
            Completed?.Invoke();
        }

        /// <summary> 도중에 끊거나 실패했다고 알린다, 이유를 글자로 보여 준다. 색만으로 알리지 않는다 </summary>
        public void Fail(string reason)
        {
            if (_state != EProgressBarState.Active)
            {
                return;
            }

            StopTimed();
            SetIndeterminate(false);
            _state = EProgressBarState.Failed;
            ApplyColor(_failedColor);
            SetText(_detail, reason);
            Failed?.Invoke();
        }

        /// <summary> 진행 정도를 모를 때 좌우로 오가는 띠를 보여 준다. 퍼센트 글자는 감춘다 </summary>
        public void SetIndeterminate(bool on)
        {
            if (_sweep == null)
            {
                return;
            }

            if (_sweep.gameObject.activeSelf == on)
            {
                return;
            }

            _sweep.gameObject.SetActive(on);
            if (_percentRoot != null)
            {
                _percentRoot.SetActive((!on) && (_percentVisible));
            }

            if (!on)
            {
                if (_sweepTween != null)
                {
                    _sweepTween.Pause();
                }

                return;
            }

            ApplySweep(0f);
            if (_sweepTween == null)
            {
                _sweepTween = DOTween.To(GetSweepPhase, ApplySweep, 1f, SWEEP_SECONDS)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(NONE, LoopType.Yoyo)
                    .SetAutoKill(false)
                    .SetLink(gameObject);
                return;
            }

            _sweepTween.Restart();
        }

        public void SetPercentVisible(bool visible)
        {
            _percentVisible = visible;
            if ((_percentRoot != null) && ((_sweep == null) || (!_sweep.gameObject.activeSelf)))
            {
                _percentRoot.SetActive(visible);
            }
        }

        public void SetTitle(string title)
        {
            SetText(_title, title);
        }

        public void Show()
        {
            FadeTo(1f);
        }

        public void Hide()
        {
            FadeTo(0f);
        }

        private void FadeTo(float alpha)
        {
            if (_group == null)
            {
                return;
            }

            _group.blocksRaycasts = alpha > 0f;
            if (_fadeTween != null)
            {
                _fadeTween.Kill();
                _fadeTween = null;
            }

            if ((alpha >= 1f) || (_fadeSeconds < FADE_MIN_SECONDS))
            {
                _group.alpha = alpha;
                return;
            }

            _fadeTween = DOTween.To(() => _group.alpha, value => _group.alpha = value, alpha, _fadeSeconds).SetLink(gameObject);
        }

        private void StopTimed()
        {
            _isTimed = false;
            enabled  = false;
        }

        private void ShowRemaining(float remainingSeconds)
        {
            if (_detail == null)
            {
                return;
            }

            // 0.1초가 바뀔 때만 글자를 다시 만든다
            int tenths = ProgressBarRules.RemainingTenths(remainingSeconds);
            if (tenths == _shownTenths)
            {
                return;
            }

            _shownTenths = tenths;
            _detail.text = string.Format(CultureInfo.InvariantCulture, SECONDS_FORMAT, tenths / TENTHS_DIVISOR);
        }

        private void ApplyColor(Color color)
        {
            if (_gauge != null)
            {
                _gauge.SetFillColor(color);
            }
        }

        private float GetSweepPhase() { return _sweepPhase; }

        private void ApplySweep(float phase)
        {
            _sweepPhase = phase;
            if (_sweep == null)
            {
                return;
            }

            ProgressBarRules.SweepAnchors(phase, SWEEP_WIDTH, out float min, out float max);
            _sweep.anchorMin = new Vector2(min, _sweep.anchorMin.y);
            _sweep.anchorMax = new Vector2(max, _sweep.anchorMax.y);
            _sweep.offsetMin = new Vector2(0f, _sweep.offsetMin.y);
            _sweep.offsetMax = new Vector2(0f, _sweep.offsetMax.y);
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }
    }
}
