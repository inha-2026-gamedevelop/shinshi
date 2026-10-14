// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 무철 개별 타격 재생과 돌진 머리의 표현 시계를 연결한다, 게임 판정은 하지 않는다 </summary>
    public sealed class MucheolBatchVfxPlayer : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_SECONDS = 0.0001f;

        [Header("개별 타격")]
        [SerializeField] private MucheolImpactVfxPlayer         _player  = null;
        [SerializeField] private MucheolPatternVfxSettingsSO    _profile = null;

        [Header("돌진 표현, 06에만 사용")]
        [SerializeField, Min(0f)] private float _chargeSeconds = 0f;
        [SerializeField] private Transform _chargeHead = null;
        [SerializeField] private MucheolImpactVfxPlayer.RockTrack _chargeTrack = null;
        [SerializeField] private Vector3   _chargeStart = Vector3.zero;
        [SerializeField] private Vector3   _chargeEnd   = Vector3.zero;

        [Header("수입한 전면 충격파 이동, 03에만 사용")]
        [SerializeField] private Transform _shockTransform = null;
        [SerializeField] private Vector3[] _shockPositions = null;
        [SerializeField] private Vector3[] _shockScales = null;

        [Header("미리보기")]
        [SerializeField] private bool  _repeatPreview     = true;
        [SerializeField] private bool  _playOnStart       = false;
        [SerializeField, Min(0f)] private float _previewGapSeconds = 0.8f;

        private bool   _running;
        private bool   _previewing;
        private bool   _confirmed;
        private double _startTime;
        private double _impactTime;
        private double _hitTime;
        private double _previewEnd;
        private float  _snapChargeSeconds;
        private float  _snapGapSeconds;
        private float  _snapExpansionSeconds;
        private Vector3 _snapChargeStart;
        private Vector3 _snapChargeEnd;

        /****************************************
        *              Properties
        ****************************************/

        public MucheolPatternVfxSettingsSO Profile => _profile;
        public float ChargeSeconds => FiniteSeconds(_chargeSeconds);
        public bool RepeatPreview  => _repeatPreview;
        public bool IsRunning      => _running;
        public bool IsPreviewing   => _previewing;

        public float PreviewDuration
        {
            get
            {
                if (_profile == null)
                {
                    return 0f;
                }

                float impactTail = Mathf.Max(
                    FiniteSeconds(_profile.ExpansionSeconds) + FiniteSeconds(_profile.HoldSeconds)
                    + FiniteSeconds(_profile.FadeSeconds), FiniteSeconds(_profile.DebrisSeconds));
                return FiniteSeconds(_profile.TelegraphSeconds) + ChargeSeconds + impactTail;
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            HideChargeHead();
        }

        // 자식 재생기의 Awake가 모두 끝난 다음에 시작해야 초기화 순서에 따라 첫 예고가 지워지지 않는다
        private void Start()
        {
            if (_playOnStart)
            {
                Preview();
            }
        }

        protected override void OnServerBuildAwake()
        {
            Stop();
        }

        private void OnDisable()
        {
            Stop();
        }

        private void Update()
        {
            if (!_previewing)
            {
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            if (_running)
            {
                if ((!_confirmed) && (now >= _impactTime))
                {
                    ConfirmImpact(_impactTime, now);
                }

                Tick(now);
            }

            if (now < _previewEnd + _snapGapSeconds)
            {
                return;
            }

            if (_repeatPreview)
            {
                Preview();
            }
            else
            {
                Stop();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이 개별 타격 하나를 로컬 미리보기로 재생한다 </summary>
        [ContextMenu("Preview")]
        public void Preview()
        {
            if (!CanPlay())
            {
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            SnapshotCharge();
            float telegraph = FiniteSeconds(_profile.TelegraphSeconds);
            Begin(now, now + telegraph + _snapChargeSeconds, now);
            _previewing = true;
            _snapGapSeconds = FiniteSeconds(_previewGapSeconds);
            _previewEnd = now + PreviewDuration;
        }

        /// <summary> 외부 시계의 예고와 예정 타격 시각을 받는다, 타격은 호출자의 확정을 기다린다 </summary>
        public void BeginTelegraph(double startTime, double impactTime, double now)
        {
            if ((!CanPlay()) || (!IsFinite(startTime)) || (!IsFinite(impactTime))
                || (!IsFinite(now)) || (impactTime < startTime))
            {
                return;
            }

            SnapshotCharge();
            Begin(startTime, impactTime, now);
        }

        /// <summary> 외부에서 확정한 개별 타격만 시작한다 </summary>
        public void ConfirmImpact(double hitTime, double now)
        {
            if ((IsServerBuild) || (!_running) || (_confirmed) || (_player == null)
                || (!IsFinite(hitTime)) || (!IsFinite(now)) || (hitTime > now) || (hitTime < _startTime))
            {
                return;
            }

            _player.ConfirmImpact(hitTime, now);
            _confirmed = true;
            _hitTime = hitTime;
            HideChargeHead();
            Tick(now);
        }

        /// <summary> 서버나 검증 호출자가 제공한 현재 시각을 그린다 </summary>
        public void Tick(double now)
        {
            if ((IsServerBuild) || (!_running) || (_player == null) || (!IsFinite(now)))
            {
                return;
            }

            _player.Tick(now);
            if (!_player.IsRunning)
            {
                _running = false;
                HideChargeHead();
                return;
            }

            ApplyCharge(now - (_impactTime - _snapChargeSeconds), _snapChargeSeconds,
                _snapChargeStart, _snapChargeEnd, !_confirmed);
            ApplyShock(_confirmed ? now - _hitTime : 0d, _snapExpansionSeconds, _confirmed);
        }

        /// <summary> Inspector에서 전체 예고, 이동, 타격 단계의 한 시점을 결정적으로 표시한다 </summary>
        public void SamplePreview(float elapsedSeconds)
        {
            if (!IsFinite(elapsedSeconds))
            {
                Stop();
                return;
            }

            if (!CanPlay())
            {
                return;
            }

            float elapsed = Mathf.Max(0f, elapsedSeconds);
            float telegraph = FiniteSeconds(_profile.TelegraphSeconds);
            float charge = ChargeSeconds;
            float impact = telegraph + charge;

            // 타격 재생기의 예고를 돌진 구간까지 유지하고 타격 이후 경과는 원래 프로필에 그대로 넘긴다
            float playerElapsed = (elapsed < impact)
                ? Mathf.Min(elapsed, Mathf.Max(0f, telegraph - MIN_SECONDS))
                : telegraph + elapsed - impact;
            if ((telegraph <= MIN_SECONDS) && (charge > MIN_SECONDS) && (elapsed < impact))
            {
                _player.BeginTelegraph(0d, impact, elapsed);
            }
            else
            {
                _player.SamplePreview(playerElapsed);
            }

            ApplyCharge(elapsed - telegraph, charge, _chargeStart, _chargeEnd, elapsed < impact);
            ApplyShock(elapsed - impact, FiniteSeconds(_profile.ExpansionSeconds), elapsed >= impact);
        }

        /// <summary> 타격, 파편, 돌진 머리를 모두 숨기고 미리보기를 끝낸다 </summary>
        [ContextMenu("Stop")]
        public void Stop()
        {
            _running    = false;
            _previewing = false;
            _confirmed  = false;
            if (_player != null)
            {
                _player.Stop();
            }

            HideChargeHead();
        }

        private bool CanPlay()
        {
            return (!IsServerBuild) && (isActiveAndEnabled) && (_player != null) && (_profile != null);
        }

        private void Begin(double startTime, double impactTime, double now)
        {
            Stop();
            _startTime   = startTime;
            _impactTime  = impactTime;
            _snapChargeSeconds = Mathf.Min(_snapChargeSeconds, (float)(impactTime - startTime));
            _running     = true;
            _player.BeginTelegraph(startTime, impactTime, now);
            Tick(now);
        }

        private void SnapshotCharge()
        {
            _snapChargeSeconds = ChargeSeconds;
            _snapChargeStart   = _chargeStart;
            _snapChargeEnd     = _chargeEnd;
            _snapExpansionSeconds = FiniteSeconds(_profile.ExpansionSeconds);
        }

        // 전체 경고 메시를 옮기면 3x18m 경로가 흔들리므로 이동하는 머리만 별도 Transform으로 그린다
        private void ApplyCharge(double elapsed, float seconds, Vector3 start, Vector3 end, bool active)
        {
            if (_chargeHead == null)
            {
                return;
            }

            bool visible = (active) && (seconds > MIN_SECONDS) && (elapsed >= 0d);
            if (elapsed >= 0d)
            {
                float progress = Mathf.Clamp01((float)(elapsed / Math.Max(seconds, MIN_SECONDS)));
                if (_chargeTrack != null)
                {
                    _chargeTrack.Apply(true, progress);
                }
                else
                {
                    _chargeHead.localPosition = Vector3.Lerp(start, end, progress);
                }
            }

            _chargeHead.gameObject.SetActive(visible);
        }

        private void ApplyShock(double elapsed, float expansion, bool active)
        {
            if ((_shockTransform == null) || (_shockPositions == null) || (_shockPositions.Length == 0)
                || (_shockScales == null) || (_shockScales.Length != _shockPositions.Length))
            {
                return;
            }

            float progress = active ? Mathf.Clamp01((float)(elapsed / Math.Max(expansion, MIN_SECONDS))) : 0f;
            int last = _shockPositions.Length - 1;
            float scaled = progress * last;
            int first = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, last);
            int next = Mathf.Min(first + 1, last);
            float fraction = scaled - first;
            _shockTransform.localPosition = Vector3.Lerp(_shockPositions[first], _shockPositions[next], fraction);
            _shockTransform.localScale = Vector3.Lerp(_shockScales[first], _shockScales[next], fraction);
        }

        private void HideChargeHead()
        {
            if (_chargeHead != null)
            {
                _chargeHead.gameObject.SetActive(false);
            }
        }

        private static float FiniteSeconds(float value)
        {
            return IsFinite(value) ? Mathf.Max(0f, value) : 0f;
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }
    }
}
