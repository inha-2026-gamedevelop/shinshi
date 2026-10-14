// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 무철 01 부채꼴 타격 VFX 재생기, 피해 판정은 하지 않고 서버가 준 시각만 그린다 </summary>
    public sealed class MucheolImpactVfxPlayer : ClientView
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> Blender에서 구운 파편 하나의 40샘플 변환 궤적 </summary>
        [Serializable]
        public sealed class RockTrack
        {
            [SerializeField] private Transform    _target;
            [SerializeField] private Vector3[]    _positions;
            [SerializeField] private Quaternion[] _rotations;
            [SerializeField] private Vector3[]    _scales;
            [SerializeField] private bool[]       _visible;

            /// <summary> 0~1 정규화 진행도로 샘플을 보간해 적용한다, 파싱이나 할당은 하지 않는다 </summary>
            public void Apply(bool active, float normalized)
            {
                if (_target == null)
                {
                    return;
                }

                if ((_positions == null) || (_rotations == null) || (_scales == null)
                    || (_positions.Length == 0)
                    || (_rotations.Length != _positions.Length)
                    || (_scales.Length != _positions.Length))
                {
                    _target.gameObject.SetActive(false);
                    return;
                }

                if (!active)
                {
                    _target.gameObject.SetActive(false);
                    return;
                }

                int last = _positions.Length - 1;
                float scaled = Mathf.Clamp01(normalized) * last;
                int i0 = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, last);
                int i1 = Mathf.Clamp(i0 + 1, 0, last);
                float frac = scaled - i0;
                bool visible = (_visible != null) && (_visible.Length > i0) && _visible[i0];

                _target.gameObject.SetActive(visible);
                if (!visible)
                {
                    return;
                }

                _target.localPosition = Vector3.Lerp(_positions[i0], _positions[i1], frac);
                _target.localRotation = Quaternion.Slerp(_rotations[i0], _rotations[i1], frac);
                _target.localScale    = Vector3.Lerp(_scales[i0], _scales[i1], frac);
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_EXTENT               = 0.0001f;
        private const float GRACE_SECONDS             = 1f;
        private const float PREVIEW_GAP_SECONDS        = 0.8f;
        private const float FLASH_DURATION_SECONDS     = 0.06f;
        private const float ROOT_MAX_SECONDS           = 0.5f;
        private const float SHOCK_SCALE_START          = 0.25f;
        private const float SHOCK_SCALE_END            = 1f;
        private const float SHOCK_FADE_HOLD_FRACTION   = 0.35f;
        private const float SHOCK_FADE_MAX_SECONDS     = 0.2333f;
        private static readonly int OPACITY_ID = Shader.PropertyToID("_Opacity");

        [Header("타이밍 프로필 (프리팹마다 다른 자산을 참조할 수 있다)")]
        [SerializeField] private MucheolPatternVfxSettingsSO _profile;

        [Header("경고 레이어, 타격 전까지 전체 범위를 그대로 보여준다")]
        [SerializeField] private MeshRenderer _warning;

        [Header("타격 레이어")]
        [SerializeField] private MeshRenderer _ground;
        [SerializeField] private MeshRenderer _cracks;
        [SerializeField] private MeshRenderer _shock;
        [SerializeField] private MeshRenderer _flash;
        [SerializeField] private MeshRenderer _root;

        [Header("파편 21개, 빌더가 Blender 내보내기에서 구운 궤적을 채운다")]
        [SerializeField] private RockTrack[] _rocks;

        [Header("타격 파티클, 선택 사항")]
        [SerializeField] private ParticleSystem[] _particles;

        [Header("미리보기")]
        [SerializeField] private bool _repeatPreview = true;
        [SerializeField] private bool _playOnStart   = false;

        private MaterialPropertyBlock _block;
        private bool   _previewMode;
        private bool   _running;
        private bool   _impacted;
        private bool   _sequenceDone;
        private bool   _waitingGap;
        private double _start;
        private double _impactTime;
        private double _hitTime;
        private double _gapEndTime;

        // Preview()나 BeginTelegraph() 호출 시점에 프로필 값을 복사해 둔다,
        // 재생 도중 인스펙터를 고쳐도 다음 재생부터만 적용되고 지금 터지는 폭발 속도는 그대로다
        private float _snapTelegraph;
        private float _snapExpansion;
        private float _snapHold;
        private float _snapFade;
        private float _snapDebris;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsRunning    => _running;
        public bool IsPreviewing => _previewMode;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _block = new MaterialPropertyBlock();
            HideAll();

            if (_playOnStart)
            {
                Preview();
            }
        }

        protected override void OnServerBuildAwake()
        {
            HideAll();
        }

        private void OnDisable()
        {
            Stop();
        }

        private void Update()
        {
            if (!_previewMode)
            {
                return;
            }

            double now = Time.unscaledTimeAsDouble;

            if ((_running) && (!_impacted) && (now >= _impactTime))
            {
                ConfirmImpact(_impactTime, now);
            }

            if (_running)
            {
                Tick(now);
            }

            if (!_sequenceDone)
            {
                return;
            }

            if (!_repeatPreview)
            {
                Stop();
                return;
            }

            if (!_waitingGap)
            {
                _waitingGap = true;
                _gapEndTime = now + PREVIEW_GAP_SECONDS;
                return;
            }

            if (now >= _gapEndTime)
            {
                Preview();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 로컬 시계로 예고부터 파편까지 전체 순환을 혼자 재생한다, 반복은 RepeatPreview를 따른다 </summary>
        [ContextMenu("Preview")]
        public void Preview()
        {
            if ((IsServerBuild) || (!isActiveAndEnabled) || (_profile == null))
            {
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            SnapshotProfile();
            _previewMode = true;
            _waitingGap  = false;
            ResetState(now, now + _snapTelegraph);
            Tick(now);
        }

        /// <summary> 외부(서버 확정) 시각으로 예고를 시작한다, 이 모드는 스스로 타격을 확정하지 않는다 </summary>
        public void BeginTelegraph(double startTime, double impactTime, double now)
        {
            if ((IsServerBuild) || (!isActiveAndEnabled) || (_profile == null))
            {
                return;
            }

            if ((!IsFinite(startTime)) || (!IsFinite(impactTime)) || (!IsFinite(now)) || (impactTime < startTime))
            {
                return;
            }

            SnapshotProfile();
            _previewMode = false;
            _waitingGap  = false;
            ResetState(startTime, impactTime);
            Tick(now);
        }

        /// <summary> 타격을 확정한다, 외부 모드는 호출자가 직접 불러야만 폭발이 시작된다 </summary>
        public void ConfirmImpact(double hitTime, double now)
        {
            if ((IsServerBuild) || (!_running) || (_impacted))
            {
                return;
            }

            if ((!IsFinite(hitTime)) || (!IsFinite(now)) || (hitTime > now) || (hitTime < _start))
            {
                return;
            }

            _impacted = true;
            _hitTime  = hitTime;
            PlayImpactParticles(Mathf.Max(0f, (float)(now - hitTime)));
            Tick(now);
        }

        /// <summary> 호출자가 넘긴 시각으로 상태를 그린다, 시간 도달만으로 타격을 만들지 않는다 </summary>
        public void Tick(double now)
        {
            if ((IsServerBuild) || (!_running) || (!IsFinite(now)))
            {
                return;
            }

            if ((!_impacted) && (now > _impactTime + GRACE_SECONDS))
            {
                // 기대한 타격 확정이 끝내 오지 않은 예측, 경고만 켠 채 영원히 남기지 않는다
                Stop();
                return;
            }

            double tSinceStart  = now - _start;
            double tSinceImpact = _impacted ? now - _hitTime : 0d;

            ApplyVisuals(tSinceStart, _impacted, tSinceImpact, _snapExpansion, _snapHold, _snapFade, _snapDebris);

            if (!_impacted)
            {
                return;
            }

            double explosionEnd = _snapExpansion + _snapHold + _snapFade;
            bool explosionDone = tSinceImpact >= explosionEnd;
            bool debrisDone     = tSinceImpact >= _snapDebris;

            if (explosionDone && debrisDone)
            {
                _sequenceDone = true;
                _running      = false;
                HideAll();
            }
        }

        /// <summary> 에디터 스크러빙 전용, 현재 프로필 값으로 경과 시간 하나를 결정적으로 그린다 </summary>
        public void SamplePreview(float elapsedSeconds)
        {
            if (IsServerBuild)
            {
                return;
            }

            if ((!enabled) || (!gameObject.activeInHierarchy) || (_profile == null))
            {
                return;
            }

            if (!IsFinite(elapsedSeconds))
            {
                Stop();
                return;
            }

            float telegraph = SanitizeSeconds(_profile.TelegraphSeconds, 0f);
            float expansion = SanitizeSeconds(_profile.ExpansionSeconds, MIN_EXTENT);
            float hold       = SanitizeSeconds(_profile.HoldSeconds, 0f);
            float fade       = SanitizeSeconds(_profile.FadeSeconds, MIN_EXTENT);
            float debris     = SanitizeSeconds(_profile.DebrisSeconds, MIN_EXTENT);
            float elapsed    = Mathf.Max(0f, elapsedSeconds);
            bool impacted    = elapsed >= telegraph;
            double tSinceImpact = impacted ? (elapsed - telegraph) : 0d;

            SampleParticles(impacted, (float)tSinceImpact);
            ApplyVisuals(elapsed, impacted, tSinceImpact, expansion, hold, fade, debris);
        }

        /// <summary> 모든 레이어, 파티클, 파편을 숨기고 갱신을 멈춘다 </summary>
        public void Stop()
        {
            _running      = false;
            _previewMode  = false;
            _impacted     = false;
            _sequenceDone = false;
            _waitingGap   = false;
            HideAll();
        }

        private void ResetState(double start, double impactTime)
        {
            HideAll();
            _start        = start;
            _impactTime   = impactTime;
            _hitTime      = 0d;
            _running      = true;
            _impacted     = false;
            _sequenceDone = false;
        }

        private void SnapshotProfile()
        {
            _snapTelegraph = SanitizeSeconds(_profile.TelegraphSeconds, 0f);
            _snapExpansion = SanitizeSeconds(_profile.ExpansionSeconds, MIN_EXTENT);
            _snapHold      = SanitizeSeconds(_profile.HoldSeconds, 0f);
            _snapFade      = SanitizeSeconds(_profile.FadeSeconds, MIN_EXTENT);
            _snapDebris    = SanitizeSeconds(_profile.DebrisSeconds, MIN_EXTENT);
        }

        private static float SanitizeSeconds(float value, float min)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return min;
            }

            return Mathf.Max(min, value);
        }

        private void ApplyVisuals(double tSinceStart, bool impacted, double tSinceImpact,
            float expansion, float hold, float fade, float debris)
        {
            bool showWarning = (!impacted) && (tSinceStart >= 0d);
            SetLayerOpacity(_warning, showWarning ? 1f : 0f);

            if (!impacted)
            {
                SetLayerOpacity(_ground, 0f);
                SetLayerOpacity(_cracks, 0f);
                SetLayerOpacity(_shock, 0f);
                SetLayerOpacity(_flash, 0f);
                SetLayerOpacity(_root, 0f);
                SetRocksActive(false, 0f, debris);
                return;
            }

            double holdEnd = expansion + hold;

            float groundCracksOpacity = (tSinceImpact < holdEnd)
                ? 1f
                : 1f - Mathf.Clamp01((float)((tSinceImpact - holdEnd) / Math.Max(fade, MIN_EXTENT)));
            SetLayerOpacity(_ground, groundCracksOpacity);
            SetLayerOpacity(_cracks, groundCracksOpacity);

            double explosionSpan = Math.Max(holdEnd + fade, MIN_EXTENT);
            double rootDuration = Math.Max(Math.Min(ROOT_MAX_SECONDS, explosionSpan), MIN_EXTENT);
            float rootFade = Mathf.Clamp01((float)(tSinceImpact / rootDuration));
            SetLayerOpacity(_root, 1f - rootFade);

            float shockScale;
            float shockOpacity;
            if (tSinceImpact < expansion)
            {
                float u = Mathf.Clamp01((float)(tSinceImpact / Math.Max(expansion, MIN_EXTENT)));
                shockScale   = Mathf.Lerp(SHOCK_SCALE_START, SHOCK_SCALE_END, u);
                shockOpacity = 1f;
            }
            else
            {
                // 유지가 길어도 충격파는 그 전부를 버티지 않고 유지 초반에만 빠르게 사라진다
                shockScale = SHOCK_SCALE_END;
                double holdElapsed = tSinceImpact - expansion;
                double shockFadeDuration = Math.Max(Math.Min(hold * SHOCK_FADE_HOLD_FRACTION, SHOCK_FADE_MAX_SECONDS), MIN_EXTENT);
                shockOpacity = 1f - Mathf.Clamp01((float)(holdElapsed / shockFadeDuration));
            }
            SetLayerOpacity(_shock, shockOpacity);
            SetLayerScale(_shock, shockScale);

            SetLayerOpacity(_flash, (tSinceImpact < FLASH_DURATION_SECONDS) ? 1f : 0f);

            SetRocksActive(true, (float)tSinceImpact, debris);
        }

        private void SetLayerOpacity(MeshRenderer renderer, float opacity)
        {
            if (renderer == null)
            {
                return;
            }

            bool visible = opacity > MIN_EXTENT;
            renderer.enabled = visible;
            if (!visible)
            {
                return;
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            _block.Clear();
            _block.SetFloat(OPACITY_ID, Mathf.Clamp01(opacity));
            renderer.SetPropertyBlock(_block);
        }

        private static void SetLayerScale(MeshRenderer renderer, float scale)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.transform.localScale = Vector3.one * scale;
        }

        private void SetRocksActive(bool active, float tSinceImpact, float debrisSeconds)
        {
            if (_rocks == null)
            {
                return;
            }

            bool withinWindow = (active) && (tSinceImpact >= 0f) && (tSinceImpact <= debrisSeconds);
            float normalized = withinWindow
                ? Mathf.Clamp01(tSinceImpact / Mathf.Max(debrisSeconds, MIN_EXTENT))
                : 0f;

            for (int i = 0; i < _rocks.Length; ++i)
            {
                if (_rocks[i] != null)
                {
                    _rocks[i].Apply(withinWindow, normalized);
                }
            }
        }

        private void PlayImpactParticles(float elapsed)
        {
            if (_particles == null)
            {
                return;
            }

            for (int i = 0; i < _particles.Length; ++i)
            {
                ParticleSystem particle = _particles[i];
                if (particle == null)
                {
                    continue;
                }

                particle.Simulate(elapsed, true, true, true);
                particle.Play(true);
            }
        }

        // 스크러빙은 재생하지 않고 그 순간으로 결정적으로 맞춰 둔다
        private void SampleParticles(bool impacted, float tSinceImpact)
        {
            if (_particles == null)
            {
                return;
            }

            for (int i = 0; i < _particles.Length; ++i)
            {
                ParticleSystem particle = _particles[i];
                if (particle == null)
                {
                    continue;
                }

                if (!impacted)
                {
                    particle.Simulate(0f, true, true, true);
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    continue;
                }

                particle.Simulate(Mathf.Max(0f, tSinceImpact), true, true, true);
            }
        }

        private void StopParticles()
        {
            if (_particles == null)
            {
                return;
            }

            for (int i = 0; i < _particles.Length; ++i)
            {
                if (_particles[i] != null)
                {
                    _particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private void HideAll()
        {
            SetLayerOpacity(_warning, 0f);
            SetLayerOpacity(_ground, 0f);
            SetLayerOpacity(_cracks, 0f);
            SetLayerOpacity(_shock, 0f);
            SetLayerOpacity(_flash, 0f);
            SetLayerOpacity(_root, 0f);
            SetRocksActive(false, 0f, MIN_EXTENT);
            StopParticles();
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }
    }
}
