// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Events;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버 규칙(2P P07): 시전 시작 순간 보스가 착지 위치로 즉시 워프하고 같은 순간 착지 위치에 원형 예고를 시작한다. 충격파는 예고 시간이 끝난 판정 시각이다
    // 클라이언트 표현은 서버 위치가 먼저 옮겨 간 뒤에 모션을 따라간다. 공중제비 한가운데서 월백이 분해되어 사라지고 착지 위치에서 재결합해 착지한다
    // 이 도구는 서버 알림이 없어서 분해, 재결합, 모델 표시 시각을 패턴 시작 기준 초로 직접 정한다. 실제 보스 모델의 메시는 워프 VFX가 바꾸지 않으므로 이 도구가 숨기고 보인다
    /// <summary> 워프 후 충격파 패턴의 분해, 재결합, 착지 예고, 충격파 확정을 시각에 맞춰 호출하는 확인용 도구 </summary>
    public sealed class WarpCue : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private Animator _animator;
        [SerializeField] private Transform _bossRoot;
        [SerializeField] private WolbaekWarpPlayer _breakup;
        [SerializeField] private WolbaekWarpPlayer _reform;
        [SerializeField] private WolbaekWarpPlayer _landing;
        [SerializeField] private string[] _patternLabels;
        // 보스 위치가 순간 이동하는 같은 프레임에 불린다. 치마 같은 위치 기억 표현이 끌려오지 않게 초기화하는 용도다
        [SerializeField] private UnityEvent _onTeleported;
        [SerializeField] private Vector3 _landingOffset = new Vector3(8f, 0f, 4f);
        [SerializeField] private float _radius = 5f;
        [SerializeField] private float _breakupSeconds = 1f;
        [SerializeField] private float _reformSeconds = 1.15f;
        [SerializeField] private float _showSeconds = 1.4f;
        [SerializeField] private float _restoreSeconds = 2.5f;

        private Renderer[] _renderers;
        private ulong      _castId;
        private double     _castStart;
        private bool       _isCasting;
        private bool       _isTicking;
        private bool       _didBreakup;
        private bool       _didReform;
        private bool       _didShow;
        private uint       _breakupId;
        private uint       _reformId;
        private uint       _landingId;
        private Bounds     _standingBounds;
        private Vector3    _homePosition;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_pattern == null)
            {
                _pattern = GetComponent<PatternMotionPreview>();
            }

            _renderers = _animator.GetComponentsInChildren<Renderer>(true);
        }

        private void OnEnable()
        {
            _pattern.Started += OnStarted;
            _pattern.Struck += OnStruck;
        }

        private void OnDisable()
        {
            _pattern.Started -= OnStarted;
            _pattern.Struck -= OnStruck;
            StopAll();
        }

        private void Update()
        {
            if (!_isTicking)
            {
                return;
            }

            double now = Time.timeAsDouble;

            if (_isCasting)
            {
                UpdateCast(now, now - _castStart);
            }

            _breakup.Tick(_breakupId, now);
            _reform.Tick(_reformId, now);
            _landing.Tick(_landingId, now);

            // 복원이 끝난 뒤에도 분해 잔류가 남아 있으면 끝날 때까지 시계를 계속 전달한다
            if ((!_isCasting) && (!_breakup.IsRunning) && (!_reform.IsRunning) && (!_landing.IsRunning))
            {
                _isTicking = false;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnStarted(string label, float strikeSeconds)
        {
            // 다른 패턴이 시작되면 진행 중이던 워프를 끝내 보스를 제자리로 돌려놓는다
            StopAll();

            if (Array.IndexOf(_patternLabels, label) < 0)
            {
                return;
            }

            double now = Time.timeAsDouble;
            ++_castId;
            _castStart = now;
            _isCasting = true;
            _isTicking = true;
            _didBreakup = false;
            _didReform = false;
            _didShow = false;
            _breakupId = 0;
            _reformId = 0;
            _homePosition = _bossRoot.position;
            _standingBounds = CalculateBounds();
            _landingId = _landing.PlayLandedWarning(_castId, _homePosition + _landingOffset, _radius, now, now + strikeSeconds, now);
        }

        private void OnStruck(string label, int shotIndex)
        {
            if ((!_isCasting) || (Array.IndexOf(_patternLabels, label) < 0))
            {
                return;
            }

            double now = Time.timeAsDouble;
            _landing.ConfirmStrike(_landingId, now, now);
        }

        private void UpdateCast(double now, double elapsed)
        {
            if ((!_didBreakup) && (elapsed >= _breakupSeconds))
            {
                _didBreakup = true;
                _breakupId = _breakup.PlayBody(_castId, CalculateBounds(), now, now);
                SetRenderers(false);
                _bossRoot.position = _homePosition + _landingOffset;
                _onTeleported.Invoke();
            }

            if ((!_didReform) && (elapsed >= _reformSeconds))
            {
                _didReform = true;
                Bounds landed = _standingBounds;
                landed.center += _landingOffset;
                _reformId = _reform.PlayBody(_castId, landed, now, now);
            }

            if ((!_didShow) && (elapsed >= _showSeconds))
            {
                _didShow = true;
                SetRenderers(true);
            }

            if (elapsed >= _restoreSeconds)
            {
                RestoreBoss();
            }
        }

        private Bounds CalculateBounds()
        {
            Bounds bounds = _renderers[0].bounds;

            for (int i = 1; i < _renderers.Length; ++i)
            {
                bounds.Encapsulate(_renderers[i].bounds);
            }

            return bounds;
        }

        private void SetRenderers(bool isVisible)
        {
            for (int i = 0; i < _renderers.Length; ++i)
            {
                _renderers[i].enabled = isVisible;
            }
        }

        // 모델과 위치만 되돌리고 분해 잔류 같은 효과는 자연 소멸에 맡긴다
        private void RestoreBoss()
        {
            _isCasting = false;
            SetRenderers(true);
            _bossRoot.position = _homePosition;
            _onTeleported.Invoke();
        }

        private void StopAll()
        {
            if (_isCasting)
            {
                RestoreBoss();
            }

            _isTicking = false;
            _breakup.Stop();
            _reform.Stop();
            _landing.Stop();
        }
    }
}
