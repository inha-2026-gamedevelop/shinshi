// System
using System;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 무사 넓게 베기 연출 한 번을 재생한다, 피해 판정과 네트워크는 다루지 않는다 </summary>
    public sealed class WideSlashVfx : ClientView
    {
        // 휘두름 연출은 입력 즉시 모든 클라이언트가 로컬로 재생하고, 타격 연출은 서버가 확정한 지점에만 붙인다.
        // 시간표는 기술 시작을 0으로 둔 하나의 트윈이 끌고 간다. 층마다 트윈을 따로 두면 히트스톱 때
        // 모든 트윈의 속도를 함께 바꿔야 하고, 파티클 시작 시점이 층 트윈과 어긋난다

        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private sealed class ArcLayer
        {
            [SerializeField] private MeshFilter   _filter;
            [SerializeField] private MeshRenderer _renderer;

            [Header("모양")]
            [SerializeField, Min(0f)] private float _outerRadius = 3f;
            [SerializeField, Min(0f)] private float _innerRadius = 1f;

            [Header("진행")]
            [SerializeField, Min(0f)]     private float _sweepStart    = 0.2f;
            [SerializeField, Min(0.001f)] private float _sweepDuration = 0.09f;
            [SerializeField]              private Ease  _sweepEase     = Ease.OutCubic;
            [SerializeField, Min(0.01f)]  private float _tailLength    = 1.3f;

            [Header("사라짐")]
            [SerializeField, Min(0f)]     private float _erodeStart    = 0.35f;
            [SerializeField, Min(0.001f)] private float _erodeDuration = 0.35f;
            [SerializeField]              private Ease  _erodeEase     = Ease.InQuad;

            [Header("밝기")]
            [SerializeField, Min(0f)] private float _peakIntensity = 1.5f;
            [SerializeField, Min(0f)] private float _restIntensity = 1f;
            [SerializeField]          private float _noiseDrift    = 0.5f;

            public MeshFilter Filter      => _filter;
            public float      OuterRadius => _outerRadius;
            public float      InnerRadius => _innerRadius;
            public float      SweepStart  => _sweepStart;
            public float      EndTime     => _erodeStart + _erodeDuration;

            public void Hide()
            {
                if (_renderer != null)
                {
                    _renderer.enabled = false;
                }
            }

            public void Apply(float time, float brightness, MaterialPropertyBlock block)
            {
                if (_renderer == null)
                {
                    return;
                }

                float erodeT = Mathf.Clamp01((time - _erodeStart) / _erodeDuration);
                if ((time < _sweepStart) || (erodeT >= 1f))
                {
                    _renderer.enabled = false;
                    return;
                }

                float sweepT = Mathf.Clamp01((time - _sweepStart) / _sweepDuration);

                block.Clear();
                block.SetFloat(HEAD_ID, DOVirtual.EasedValue(0f, HEAD_END, sweepT, _sweepEase));
                block.SetFloat(TAIL_LENGTH_ID, _tailLength);
                block.SetFloat(ERODE_ID, DOVirtual.EasedValue(0f, 1f, erodeT, _erodeEase));
                block.SetFloat(INTENSITY_ID, Mathf.Lerp(_peakIntensity, _restIntensity, erodeT) * brightness);
                block.SetVector(NOISE_OFFSET_ID, new Vector4(0f, time * _noiseDrift, 0f, 0f));

                _renderer.SetPropertyBlock(block);
                _renderer.enabled = true;
            }
        }

        [Serializable]
        private sealed class ParticleCue
        {
            [SerializeField]          private ParticleSystem _system;
            [SerializeField, Min(0f)] private float          _time;

            // 칼끝 반짝임처럼 손을 따라가야 하는 것만 켠다. 나머지는 기술 시작 위치에 남는다
            [SerializeField] private bool _atAnchor;

            public ParticleSystem System   => _system;
            public float          Time     => _time;
            public bool           AtAnchor => _atAnchor;
        }

        /****************************************
        *                Fields
        ****************************************/

        // 머리가 끝을 조금 지나야 머리 쪽 부드러운 경계가 호 끝에서 잘리지 않는다
        private const float HEAD_END            = 1.05f;
        private const float NOT_STARTED         = -1f;
        private const float NORMAL_SPEED        = 1f;
        private const float FULL_BRIGHTNESS     = 1f;
        private const float SPARK_RADIAL_SHARE  = 0.3f;

        private static readonly int HEAD_ID         = Shader.PropertyToID("_Head");
        private static readonly int TAIL_LENGTH_ID  = Shader.PropertyToID("_TailLength");
        private static readonly int ERODE_ID        = Shader.PropertyToID("_Erode");
        private static readonly int INTENSITY_ID    = Shader.PropertyToID("_Intensity");
        private static readonly int NOISE_OFFSET_ID = Shader.PropertyToID("_NoiseOffset");

        [Header("호")]
        [SerializeField]          private float      _startDegrees = 65f;
        [SerializeField]          private float      _endDegrees   = -65f;
        [SerializeField, Min(2)]  private int        _segments     = 48;
        [SerializeField]          private ArcLayer[] _layers       = Array.Empty<ArcLayer>();

        [Header("파티클")]
        [SerializeField] private ParticleCue[] _cues = Array.Empty<ParticleCue>();

        [Header("타격")]
        [SerializeField]          private ParticleSystem _hitFlare;
        [SerializeField]          private ParticleSystem _hitCore;
        [SerializeField]          private ParticleSystem _hitSparks;
        [SerializeField, Min(0)]  private int            _sparkCount  = 8;
        [SerializeField, Min(0f)] private float          _sparkSpeed  = 7f;
        [SerializeField, Min(0f)] private float          _sparkSpread = 2.5f;
        [SerializeField, Min(0f)] private float          _sparkLift   = 1.5f;

        // 서버가 주는 타격 지점은 대상 중심이라 섬광이 몸에 가려진다. 공격자 쪽 표면으로 당긴다
        [SerializeField, Min(0f)] private float _hitSurfaceOffset = 0.4f;

        // 칼끝 위치를 모를 때 반짝임을 띄울 높이
        [SerializeField, Min(0f)] private float _fallbackAnchorHeight = 1.1f;

        private MaterialPropertyBlock _block;
        private Mesh[]                _meshes;
        private Tweener               _timeline;
        private Transform             _anchor;
        private float                 _time        = 0f;
        private float                 _lastTime    = NOT_STARTED;
        private float                 _duration    = 0f;
        private float                 _firstSweep  = float.MaxValue;
        private float                 _brightness  = FULL_BRIGHTNESS;

        public bool IsPlaying => (_timeline != null) && (_timeline.IsPlaying());

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _block  = new MaterialPropertyBlock();
            _meshes = new Mesh[_layers.Length];

            for (int i = 0; i < _layers.Length; ++i)
            {
                ArcLayer layer = _layers[i];
                if (layer.Filter == null)
                {
                    Debug.LogError($"[VFX] 넓게 베기 {i}번 층에 MeshFilter가 없다", this);
                    continue;
                }

                _meshes[i] = SlashArcMeshBuilder.Build(_startDegrees, _endDegrees, layer.OuterRadius, layer.InnerRadius, _segments);
                layer.Filter.sharedMesh = _meshes[i];
                layer.Hide();
                _duration   = Mathf.Max(_duration, layer.EndTime);
                _firstSweep = Mathf.Min(_firstSweep, layer.SweepStart);
            }

            for (int i = 0; i < _cues.Length; ++i)
            {
                _duration = Mathf.Max(_duration, _cues[i].Time);
            }

            // 한 번 만들어 재생할 때마다 처음부터 다시 튼다
            _timeline = DOTween.To(GetTime, SetTime, _duration, _duration)
                .SetEase(Ease.Linear)
                .SetAutoKill(false)
                .Pause()
                .SetLink(gameObject);
        }

        private void OnDestroy()
        {
            if (_meshes == null)
            {
                return;
            }

            for (int i = 0; i < _meshes.Length; ++i)
            {
                if (_meshes[i] != null)
                {
                    Destroy(_meshes[i]);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // brightness는 남의 캐릭터 연출이나 효과 밝기 낮추기 설정에서 1보다 작게 준다
        /// <summary> 기술 시작 시점에 부른다, 위치와 방향은 시작 순간의 캐릭터 발밑 기준이다 </summary>
        public void Play(Vector3 position, Quaternion rotation, Transform anchor, float brightness)
        {
            if (_timeline == null)
            {
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
            _anchor             = anchor;
            _brightness         = brightness;
            _time               = 0f;
            _lastTime           = NOT_STARTED;
            _timeline.timeScale = NORMAL_SPEED;
            _timeline.Restart();
        }

        /// <summary> 서버가 확정한 타격 지점에 주홍 타격점과 불티를 남긴다 </summary>
        public void PlayHit(Vector3 point)
        {
            if (_hitSparks == null)
            {
                return;
            }

            Vector3 radial = point - transform.position;
            radial.y = 0f;
            radial   = (radial.sqrMagnitude > 0f) ? radial.normalized : transform.forward;

            // 오른쪽에서 왼쪽으로 휘두르므로 호의 접선은 바깥 방향을 왼쪽으로 90도 돌린 쪽이다
            Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);

            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                position             = point - (radial * _hitSurfaceOffset),
                applyShapeToPosition = false
            };

            if (_hitFlare != null)
            {
                _hitFlare.Emit(emit, 1);
            }

            if (_hitCore != null)
            {
                _hitCore.Emit(emit, 1);
            }

            for (int i = 0; i < _sparkCount; ++i)
            {
                Vector3 jitter = UnityEngine.Random.insideUnitSphere * _sparkSpread;
                emit.velocity = (tangent * _sparkSpeed) + (radial * (_sparkSpeed * SPARK_RADIAL_SHARE)) + (Vector3.up * _sparkLift) + jitter;
                _hitSparks.Emit(emit, 1);
            }
        }

        // 준비 중 회피나 서버 거절로 기술이 취소되면 부른다. 이미 휘두른 뒤라면 잔상은 그대로 끝까지 둔다
        /// <summary> 휘두름 전이면 연출을 거둔다, 휘두른 뒤면 아무것도 하지 않는다 </summary>
        public void Cancel()
        {
            if ((_timeline == null) || (!_timeline.IsPlaying()) || (_time >= _firstSweep))
            {
                return;
            }

            _timeline.Pause();

            for (int i = 0; i < _layers.Length; ++i)
            {
                _layers[i].Hide();
            }

            for (int i = 0; i < _cues.Length; ++i)
            {
                if (_cues[i].System != null)
                {
                    _cues[i].System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        /// <summary> 히트스톱 동안 시간표를 늦춘다, 1이면 원래 속도다 </summary>
        public void SetTimeScale(float scale)
        {
            if (_timeline == null)
            {
                return;
            }

            _timeline.timeScale = scale;
        }

        private float GetTime()
        {
            return _time;
        }

        private void SetTime(float time)
        {
            _time = time;

            for (int i = 0; i < _layers.Length; ++i)
            {
                _layers[i].Apply(time, _brightness, _block);
            }

            for (int i = 0; i < _cues.Length; ++i)
            {
                ParticleCue cue = _cues[i];
                if ((cue.System == null) || (_lastTime >= cue.Time) || (time < cue.Time))
                {
                    continue;
                }

                if (cue.AtAnchor)
                {
                    cue.System.transform.position = (_anchor != null)
                        ? _anchor.position
                        : transform.position + (Vector3.up * _fallbackAnchorHeight);
                }

                cue.System.Play(true);
            }

            _lastTime = time;
        }
    }
}
