// System
using System;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;
using DG.Tweening.Core;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 외부에서 지정한 두 발밑 좌표와 위험 단계만 표시한다. 거리를 피해나 연결 종료로 판정하지 않는다 </summary>
    public sealed class WolbaekLinkPlayer : ClientView
    {
        /****************************************
        *                Types
        ****************************************/
        public enum EPhase
        {
            Active  = 1,
            Expired = 2
        }

        public enum ESeverity
        {
            Normal  = 0,
            Warning = 1,
            Danger  = 2
        }

        /// <summary> 좌표는 투영하지 않는 실제 3D 발밑 앵커다. EndpointsVisible은 생존과 접속 상태를 확인한 외부 표시 입력이다 </summary>
        public readonly struct Snapshot
        {
            public ulong CastId { get; }
            public ulong Sequence { get; }
            public EWolbaekWardSign Sign { get; }
            public Color Color { get; }
            public Vector3 FirstEndpoint { get; }
            public Vector3 SecondEndpoint { get; }
            public double AppearedAt { get; }
            public double SnapshotAt { get; }
            public EPhase Phase { get; }
            public double PhaseStartedAt { get; }
            public ESeverity Severity { get; }
            public float WarningAmount { get; }
            public bool EndpointsVisible { get; }

            public Snapshot(ulong castId, ulong sequence, EWolbaekWardSign sign, Color color,
                Vector3 firstEndpoint, Vector3 secondEndpoint, double appearedAt, double snapshotAt,
                EPhase phase, double phaseStartedAt, ESeverity severity, float warningAmount,
                bool endpointsVisible)
            {
                CastId          = castId;
                Sequence        = sequence;
                Sign            = sign;
                Color           = color;
                FirstEndpoint   = firstEndpoint;
                SecondEndpoint  = secondEndpoint;
                AppearedAt      = appearedAt;
                SnapshotAt      = snapshotAt;
                Phase           = phase;
                PhaseStartedAt  = phaseStartedAt;
                Severity        = severity;
                WarningAmount   = warningAmount;
                EndpointsVisible = endpointsVisible;
            }
        }

        /****************************************
        *                Fields
        ****************************************/
        private const float MIN_SIZE         = 0.001f;
        private const float ENVELOPE_SECONDS = 1f;
        private const float TAU              = Mathf.PI * 2f;
        private const float HALF             = 0.5f;
        private const float FLOW_SECONDARY   = 0.61f;
        private const float FLOW_ALONG       = 7f;
        private const float PARALLEL_UP_LIMIT = 0.99f;
        private const int SIGN_COUNT         = 2;
        private const int ENDPOINT_COUNT     = 2;
        private const int RIBBON_LAYER       = 0;
        private const int MOTE_LAYER         = 1;
        private const int RIBBON_MOTION      = 0;
        private const int MOTE_MOTION        = 1;
        private const int GLYPH_MOTION       = 2;
        private static readonly int _colorId = Shader.PropertyToID("_Color");
        private static readonly int _mapStId = Shader.PropertyToID("_BaseMap_ST");

        [Header("표현 설정과 리그")]
        [SerializeField] private WolbaekLinkSettingsSO _settings = null;
        [SerializeField] private Transform _lengthRoot          = null;
        [SerializeField] private Transform _glyphRoot           = null;
        [SerializeField] private Renderer[] _layers             = null;
        [SerializeField] private int[] _layerKinds              = null;
        [SerializeField] private Renderer[] _glyphs             = null;
        [SerializeField] private Transform[] _bones             = null;
        [SerializeField] private Vector3[] _rest                = null;
        // x=리본 0, 광점 1, 문양 2, y=진폭 배율, z=위상, w=회전 방향. rest.z는 길이 비율이다
        [SerializeField] private Vector4[] _motion              = null;
        [SerializeField] private Transform _endpointA           = null;
        [SerializeField] private Transform _endpointB           = null;
        [SerializeField] private Renderer[] _endpointLayers     = null;

        private MaterialPropertyBlock _properties;
        private Tweener _appearTween;
        private Tweener _switchTween;
        private Tweener _fadeTween;
        private Snapshot _snapshot;
        private Snapshot _acceptedSnapshot;
        private Vector3 _displayedA;
        private Vector3 _displayedB;
        private Vector3 _rootPosition;
        private Quaternion _rootRotation;
        private Vector3 _rootScale;
        private Vector3 _lengthPosition;
        private Quaternion _lengthRotation;
        private Vector3 _lengthScale;
        private Vector3 _glyphPosition;
        private Quaternion _glyphRotation;
        private Vector3 _glyphScale;
        private Vector3 _endpointAPosition;
        private Quaternion _endpointARotation;
        private Vector3 _endpointAScale;
        private Vector3 _endpointBPosition;
        private Quaternion _endpointBRotation;
        private Vector3 _endpointBScale;
        private Quaternion[] _restRotations;
        private Vector3[] _restScales;
        private SkinnedMeshRenderer[] _skins;
        private Bounds[] _skinBounds;
        private uint _playbackId;
        private double _lastNow;
        private double _acceptedNow;
        private float _appear;
        private float _switch;
        private float _fade;
        private float _appearSeconds;
        private float _switchSeconds;
        private float _fadeSeconds;
        private float _flowCycle;
        private float _warningCycle;
        private float _waveAmplitude;
        private float _moteDrift;
        private float _linkHeight;
        private float _endpointRadius;
        private float _ribbonOpacity;
        private float _moteOpacity;
        private float _glyphOpacity;
        private float _endpointOpacity;
        private float _glyphStrength;
        private float _warningStrength;
        private float _dangerStrength;
        private float _warningWhiteShare;
        private bool _running;
        private bool _posesCached;
        private bool _hasAcceptedSnapshot;

        /****************************************
        *              Properties
        ****************************************/
        public WolbaekLinkSettingsSO Settings => _settings;
        public bool IsRunning => _running;
        public uint PlaybackId => _playbackId;
        public ulong CastId => _running ? _snapshot.CastId : 0;
        public Snapshot CurrentSnapshot => _snapshot;
        public Vector3 DisplayedFirstEndpoint => _displayedA;
        public Vector3 DisplayedSecondEndpoint => _displayedB;
        public event Action<uint> Completed;

        /****************************************
        *              Unity Event
        ****************************************/
        protected override void OnViewAwake()
        {
            _properties = new MaterialPropertyBlock();
            _appearTween = CreateEnvelope(() => _appear, value => _appear = value, Ease.OutCubic);
            _switchTween = CreateEnvelope(() => _switch, value => _switch = value, Ease.OutCubic);
            _fadeTween = CreateEnvelope(() => _fade, value => _fade = value, Ease.InCubic);
            Stop();
        }

        protected override void OnServerBuildAwake()
        {
            Stop();
        }

        private void OnDisable()
        {
            Stop();
        }

        private void OnDestroy()
        {
            _appearTween?.Kill();
            _switchTween?.Kill();
            _fadeTween?.Kill();
        }

        /****************************************
        *                Methods
        ****************************************/
        /// <summary> 대상 쌍과 색은 공급된 스냅샷을 사용한다. 늦은 복구도 공통 시계에서 직접 샘플링한다 </summary>
        public uint Begin(Snapshot snapshot, double now)
        {
            if ((!Ready(now)) || (!ValidSnapshot(snapshot, now)) || (!AcceptsBegin(snapshot, now)))
            {
                return 0;
            }

            CachePoses();
            Stop();
            ++_playbackId;
            if (_playbackId == 0)
            {
                ++_playbackId;
            }
            _snapshot = snapshot;
            _displayedA = snapshot.FirstEndpoint;
            _displayedB = snapshot.SecondEndpoint;
            _running = true;
            CacheSettings();
            Accept(snapshot, now);
            uint playbackId = _playbackId;
            Tick(playbackId, now);
            return playbackId;
        }

        /// <summary> 서버가 공급한 위험과 표시 상태를 적용한다. 거절된 입력은 표시 좌표와 시계를 바꾸지 않는다 </summary>
        public bool Reconcile(uint playbackId, Snapshot snapshot, double now)
        {
            if ((!Current(playbackId, now)) || (!ValidSnapshot(snapshot, now))
                || (!SameCast(_snapshot, snapshot)) || (!ValidTransition(_snapshot, snapshot)))
            {
                return false;
            }

            _snapshot = snapshot;
            _displayedA = snapshot.FirstEndpoint;
            _displayedB = snapshot.SecondEndpoint;
            Accept(snapshot, now);
            Tick(playbackId, now);
            return true;
        }

        /// <summary> 마지막 표시 좌표와 외부 공통 시계를 사용한다. 표시 상태를 로컬 시간으로 변경하지 않는다 </summary>
        public void Tick(uint playbackId, double now)
        {
            if (!Current(playbackId, now))
            {
                return;
            }

            _lastNow = now;
            _acceptedNow = now;
            double age = now - _snapshot.AppearedAt;
            double phaseAge = now - _snapshot.PhaseStartedAt;
            bool expired = _snapshot.Phase == EPhase.Expired;
            if ((expired) && (phaseAge >= _fadeSeconds))
            {
                Complete(playbackId);
                return;
            }

            _appearTween.Goto(Normalized(age, _appearSeconds), false);
            _switchTween.Goto(Normalized(phaseAge, _switchSeconds), false);
            _fadeTween.Goto(expired ? Normalized(phaseAge, _fadeSeconds) : 0f, false);
            float opacity = _appear * _switch * (1f - _fade);
            float phase = Phase(age, _flowCycle);
            float pulse = HALF + HALF * Mathf.Sin(Phase(age, _warningCycle));
            float severity = _snapshot.Severity == ESeverity.Danger ? _dangerStrength
                : _snapshot.Severity == ESeverity.Warning ? _warningStrength : 0f;
            float warning = _snapshot.WarningAmount * severity * pulse;
            Color color = Color.Lerp(_snapshot.Color, Color.white,
                _snapshot.WarningAmount * _warningWhiteShare * pulse);
            color *= 1f + warning;
            color.a = _snapshot.Color.a;
            bool hasLength = SetGeometry(_displayedA, _displayedB, out float length);
            float visibleOpacity = _snapshot.EndpointsVisible ? opacity : 0f;
            AnimateLayers(color, visibleOpacity, hasLength, phase);
            AnimateBones(phase, length, warning);
            UpdateBounds(length, warning);
        }

        /// <summary> 두 표시 좌표를 한 번에 갱신한다. 확정 스냅샷과 경고 상태는 수정하지 않는다 </summary>
        public bool Tick(uint playbackId, double now, Vector3 firstEndpoint, Vector3 secondEndpoint)
        {
            if ((!Current(playbackId, now)) || (!ValidEndpoints(firstEndpoint, secondEndpoint, _linkHeight)))
            {
                return false;
            }

            _displayedA = firstEndpoint;
            _displayedB = secondEndpoint;
            Tick(playbackId, now);
            return true;
        }

        /// <summary> 현재 재생 세대만 취소한다. 취소에는 완료 이벤트가 없다 </summary>
        public bool Stop(uint playbackId)
        {
            if ((!_running) || (playbackId == 0) || (playbackId != _playbackId))
            {
                return false;
            }
            Stop();
            return true;
        }

        /// <summary> 비활성화와 풀 반환에 사용한다. 속성 블록과 길이, 문양, 끝점의 포즈를 복원한다 </summary>
        [ContextMenu("Stop")]
        public void Stop()
        {
            _running = false;
            _snapshot = default;
            _displayedA = Vector3.zero;
            _displayedB = Vector3.zero;
            _lastNow = 0d;
            Hide(_layers);
            Hide(_glyphs);
            Hide(_endpointLayers);
            _appearTween?.Goto(0f, false);
            _switchTween?.Goto(0f, false);
            _fadeTween?.Goto(0f, false);
            RestorePoses();
        }

        private Tweener CreateEnvelope(DOGetter<float> getter, DOSetter<float> setter, Ease ease)
        {
            // 모든 인스턴스가 같은 절대 시계를 샘플링하도록 트윈을 자동 재생하지 않는다
            return DOTween.To(getter, setter, ENVELOPE_SECONDS, ENVELOPE_SECONDS)
                .SetEase(ease).SetUpdate(UpdateType.Manual).SetAutoKill(false)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy).Pause();
        }

        private void CacheSettings()
        {
            _appearSeconds = _settings.AppearSeconds;
            _switchSeconds = _settings.SwitchSeconds;
            _fadeSeconds = _settings.FadeSeconds;
            _flowCycle = _settings.FlowCycle;
            _warningCycle = _settings.WarningCycle;
            _waveAmplitude = _settings.WaveAmplitude;
            _moteDrift = _settings.MoteDrift;
            _linkHeight = _settings.LinkHeight;
            _endpointRadius = _settings.EndpointRadius;
            _ribbonOpacity = _settings.RibbonOpacity;
            _moteOpacity = _settings.MoteOpacity;
            _glyphOpacity = _settings.GlyphOpacity;
            _endpointOpacity = _settings.EndpointOpacity;
            _glyphStrength = _settings.GlyphStrength;
            _warningStrength = _settings.WarningStrength;
            _dangerStrength = _settings.DangerStrength;
            _warningWhiteShare = _settings.WarningWhiteShare;
        }

        private void CachePoses()
        {
            if (_posesCached)
            {
                return;
            }

            _rootPosition = transform.localPosition;
            _rootRotation = transform.localRotation;
            _rootScale = transform.localScale;
            _lengthPosition = _lengthRoot.localPosition;
            _lengthRotation = _lengthRoot.localRotation;
            _lengthScale = _lengthRoot.localScale;
            _glyphPosition = _glyphRoot.localPosition;
            _glyphRotation = _glyphRoot.localRotation;
            _glyphScale = _glyphRoot.localScale;
            _endpointAPosition = _endpointA.localPosition;
            _endpointARotation = _endpointA.localRotation;
            _endpointAScale = _endpointA.localScale;
            _endpointBPosition = _endpointB.localPosition;
            _endpointBRotation = _endpointB.localRotation;
            _endpointBScale = _endpointB.localScale;
            _restRotations = new Quaternion[_bones.Length];
            _restScales = new Vector3[_bones.Length];
            for (int i = 0; i < _bones.Length; ++i)
            {
                _restRotations[i] = _bones[i].localRotation;
                _restScales[i] = _bones[i].localScale;
            }
            _skins = new SkinnedMeshRenderer[_layers.Length + SIGN_COUNT];
            _skinBounds = new Bounds[_skins.Length];
            for (int i = 0; i < _skins.Length; ++i)
            {
                _skins[i] = (i < _layers.Length ? _layers[i] : _glyphs[i - _layers.Length]) as SkinnedMeshRenderer;
                if (_skins[i] != null)
                {
                    _skinBounds[i] = _skins[i].localBounds;
                }
            }
            _posesCached = true;
        }

        private void RestorePoses()
        {
            if (!_posesCached)
            {
                return;
            }

            transform.SetLocalPositionAndRotation(_rootPosition, _rootRotation);
            transform.localScale = _rootScale;
            Restore(_lengthRoot, _lengthPosition, _lengthRotation, _lengthScale);
            Restore(_glyphRoot, _glyphPosition, _glyphRotation, _glyphScale);
            Restore(_endpointA, _endpointAPosition, _endpointARotation, _endpointAScale);
            Restore(_endpointB, _endpointBPosition, _endpointBRotation, _endpointBScale);
            for (int i = 0; i < _bones.Length; ++i)
            {
                Restore(_bones[i], _rest[i], _restRotations[i], _restScales[i]);
            }
            for (int i = 0; i < _skins.Length; ++i)
            {
                if (_skins[i] != null)
                {
                    _skins[i].localBounds = _skinBounds[i];
                }
            }
        }

        private static void Restore(Transform target, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (target == null)
            {
                return;
            }
            target.SetLocalPositionAndRotation(position, rotation);
            target.localScale = scale;
        }

        private bool SetGeometry(Vector3 firstEndpoint, Vector3 secondEndpoint, out float length)
        {
            double dx = (double)secondEndpoint.x - firstEndpoint.x;
            double dy = (double)secondEndpoint.y - firstEndpoint.y;
            double dz = (double)secondEndpoint.z - firstEndpoint.z;
            double magnitude = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            length = (float)magnitude;
            bool hasLength = magnitude >= MIN_SIZE;
            Vector3 direction = hasLength
                ? new Vector3((float)(dx / magnitude), (float)(dy / magnitude), (float)(dz / magnitude))
                : Vector3.forward;
            Vector3 up = Mathf.Abs(direction.y) >= PARALLEL_UP_LIMIT ? Vector3.forward : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(direction, up);
            transform.SetPositionAndRotation(firstEndpoint, Quaternion.identity);
            Vector3 ribbonOrigin = firstEndpoint + Vector3.up * _linkHeight;
            _lengthRoot.SetPositionAndRotation(ribbonOrigin, rotation);
            _lengthRoot.localScale = new Vector3(_lengthScale.x, _lengthScale.y, hasLength ? length : MIN_SIZE);
            _glyphRoot.SetPositionAndRotation(ribbonOrigin, rotation);
            _glyphRoot.localScale = Vector3.one;
            _endpointA.SetPositionAndRotation(firstEndpoint, Quaternion.identity);
            _endpointB.SetPositionAndRotation(secondEndpoint, Quaternion.identity);
            Vector3 ringScale = new Vector3(_endpointRadius, 1f, _endpointRadius);
            _endpointA.localScale = ringScale;
            _endpointB.localScale = ringScale;
            return hasLength;
        }

        private void AnimateLayers(Color color, float opacity, bool hasLength, float phase)
        {
            for (int i = 0; i < _layers.Length; ++i)
            {
                Color tint = color;
                tint.a *= hasLength ? opacity * (_layerKinds[i] == RIBBON_LAYER ? _ribbonOpacity : _moteOpacity) : 0f;
                Paint(_layers[i], tint, _layerKinds[i] == RIBBON_LAYER ? phase / TAU : 0f);
            }
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                Color glyph = color * _glyphStrength;
                glyph.a = (hasLength) && (i == (int)_snapshot.Sign) ? color.a * opacity * _glyphOpacity : 0f;
                Paint(_glyphs[i], glyph, 0f);
            }
            Color endpoint = color;
            endpoint.a *= opacity * _endpointOpacity;
            for (int i = 0; i < ENDPOINT_COUNT; ++i)
            {
                Paint(_endpointLayers[i], endpoint, 0f);
            }
        }

        private void AnimateBones(float phase, float length, float warning)
        {
            for (int i = 0; i < _bones.Length; ++i)
            {
                Vector4 motion = _motion[i];
                Vector3 position = _rest[i];
                float wave = phase - position.z * FLOW_ALONG + motion.z;
                Quaternion rotation = _restRotations[i];
                if (motion.x == RIBBON_MOTION)
                {
                    // 양끝을 고정하고 길이 축은 유지해 띠만 옆과 위로 흐르게 한다
                    float amplitude = _waveAmplitude * motion.y * Mathf.Sin(position.z * Mathf.PI) * (1f + warning);
                    position.x += Mathf.Sin(wave) * amplitude;
                    position.y += Mathf.Sin(wave * FLOW_SECONDARY) * amplitude;
                }
                else
                {
                    // 문양의 위치만 길이에 맞춰 문양 자체의 너비가 늘어나지 않게 한다
                    position.z *= length;
                    if (motion.x == MOTE_MOTION)
                    {
                        position.x += Mathf.Sin(wave) * _moteDrift * motion.y;
                        position.y += Mathf.Cos(wave * FLOW_SECONDARY) * _moteDrift * motion.y;
                    }
                }
                _bones[i].SetLocalPositionAndRotation(position, rotation);
                _bones[i].localScale = _restScales[i];
            }
        }

        private void Paint(Renderer renderer, Color color, float flow)
        {
            renderer.enabled = color.a > MIN_SIZE;
            _properties.Clear();
            _properties.SetColor(_colorId, color);
            _properties.SetVector(_mapStId, new Vector4(1f, 1f, 0f, flow));
            renderer.SetPropertyBlock(_properties);
        }

        private void UpdateBounds(float length, float warning)
        {
            for (int i = 0; i < _skins.Length; ++i)
            {
                if (_skins[i] == null)
                {
                    continue;
                }
                Bounds bounds = _skinBounds[i];
                Vector3 extents = bounds.extents;
                Vector3 center = bounds.center;
                bool ribbon = (i < _layers.Length) && (_layerKinds[i] == RIBBON_LAYER);
                float margin = ribbon ? _waveAmplitude * (1f + warning) : _moteDrift;
                extents.x += margin;
                extents.y += margin;
                if (!ribbon)
                {
                    // 길이로 늘어나지 않는 문양의 이동 범위만 갱신해 먼 두 끝점에서도 컬링되지 않게 한다
                    center.z = length * HALF;
                    extents.z = (float)Math.Min(float.MaxValue * HALF, (double)length * HALF + extents.z);
                }
                bounds.center = center;
                bounds.extents = extents;
                _skins[i].localBounds = bounds;
            }
        }

        private static void Hide(Renderer[] renderers)
        {
            if (renderers == null)
            {
                return;
            }
            for (int i = 0; i < renderers.Length; ++i)
            {
                if (renderers[i] == null)
                {
                    continue;
                }
                renderers[i].enabled = false;
                renderers[i].SetPropertyBlock(null);
            }
        }

        private void Complete(uint playbackId)
        {
            Stop();
            Completed?.Invoke(playbackId);
        }

        private void Accept(Snapshot snapshot, double now)
        {
            _acceptedSnapshot = snapshot;
            _acceptedNow = now;
            _lastNow = now;
            _hasAcceptedSnapshot = true;
        }

        private bool AcceptsBegin(Snapshot snapshot, double now)
        {
            if (!_hasAcceptedSnapshot)
            {
                return true;
            }
            if ((now < _acceptedNow) || (snapshot.SnapshotAt < _acceptedSnapshot.SnapshotAt))
            {
                return false;
            }
            return (snapshot.CastId != _acceptedSnapshot.CastId)
                || (SameCast(_acceptedSnapshot, snapshot) && ValidTransition(_acceptedSnapshot, snapshot));
        }

        private bool Current(uint playbackId, double now)
        {
            return (!IsServerBuild) && (_running) && (isActiveAndEnabled) && (playbackId != 0)
                && (playbackId == _playbackId) && Finite(now) && (now >= _lastNow);
        }

        private bool Ready(double now)
        {
            return (!IsServerBuild) && (isActiveAndEnabled) && (_properties != null) && (_settings != null)
                && Finite(now) && ValidSettings() && ValidRig();
        }

        private bool ValidSettings()
        {
            return Sign(_settings.DefaultSign) && Valid(_settings.DefaultColor)
                && Positive(_settings.AppearSeconds) && Positive(_settings.SwitchSeconds)
                && Positive(_settings.FadeSeconds) && Positive(_settings.FlowCycle)
                && Positive(_settings.WarningCycle) && NonNegative(_settings.WaveAmplitude)
                && NonNegative(_settings.MoteDrift) && NonNegative(_settings.LinkHeight)
                && Positive(_settings.EndpointRadius) && Unit(_settings.RibbonOpacity)
                && Unit(_settings.MoteOpacity) && Unit(_settings.GlyphOpacity) && Unit(_settings.EndpointOpacity)
                && NonNegative(_settings.GlyphStrength) && NonNegative(_settings.WarningStrength)
                && NonNegative(_settings.DangerStrength) && Unit(_settings.WarningWhiteShare);
        }

        private bool ValidRig()
        {
            if ((_lengthRoot == null) || (_glyphRoot == null) || (_lengthRoot == _glyphRoot)
                || (!_lengthRoot.IsChildOf(transform)) || (!_glyphRoot.IsChildOf(transform))
                || _lengthRoot.IsChildOf(_glyphRoot) || _glyphRoot.IsChildOf(_lengthRoot)
                || (_endpointA == null) || (_endpointB == null) || (_endpointA == _endpointB)
                || (!_endpointA.IsChildOf(transform)) || (!_endpointB.IsChildOf(transform))
                || (_layers == null) || (_layers.Length == 0) || (_layerKinds == null)
                || (_layers.Length != _layerKinds.Length) || (_glyphs == null) || (_glyphs.Length != SIGN_COUNT)
                || (_endpointLayers == null) || (_endpointLayers.Length != ENDPOINT_COUNT)
                || (_bones == null) || (_rest == null) || (_motion == null)
                || (_bones.Length != _rest.Length) || (_bones.Length != _motion.Length)
                || (!ValidPose(transform)) || (!ValidPose(_lengthRoot)) || (!ValidPose(_glyphRoot))
                || (!ValidPose(_endpointA)) || (!ValidPose(_endpointB)))
            {
                return false;
            }
            for (int i = 0; i < _layers.Length; ++i)
            {
                if ((_layers[i] == null) || (_layerKinds[i] < RIBBON_LAYER) || (_layerKinds[i] > MOTE_LAYER)
                    || (!_layers[i].transform.IsChildOf(_layerKinds[i] == RIBBON_LAYER ? _lengthRoot : _glyphRoot)))
                {
                    return false;
                }
            }
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                if ((_glyphs[i] == null) || (!_glyphs[i].transform.IsChildOf(_glyphRoot)))
                {
                    return false;
                }
            }
            if ((_endpointLayers[0] == null) || (!_endpointLayers[0].transform.IsChildOf(_endpointA))
                || (_endpointLayers[1] == null) || (!_endpointLayers[1].transform.IsChildOf(_endpointB)))
            {
                return false;
            }
            for (int i = 0; i < _bones.Length; ++i)
            {
                Vector4 motion = _motion[i];
                if ((_bones[i] == null) || (!ValidPose(_bones[i])) || (!Valid(_rest[i])) || (!Unit(_rest[i].z))
                    || (!Finite(motion.x)) || (!NonNegative(motion.y)) || (!Finite(motion.z)) || (!Finite(motion.w))
                    || (motion.x < RIBBON_MOTION) || (motion.x > GLYPH_MOTION) || (motion.x != Mathf.Floor(motion.x))
                    || (!_bones[i].IsChildOf(motion.x == RIBBON_MOTION ? _lengthRoot : _glyphRoot)))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameCast(Snapshot previous, Snapshot next)
        {
            return (previous.CastId == next.CastId) && (previous.Sign == next.Sign)
                && previous.Color.Equals(next.Color) && (previous.AppearedAt == next.AppearedAt);
        }

        private static bool ValidTransition(Snapshot previous, Snapshot next)
        {
            if ((next.Sequence <= previous.Sequence) || (next.SnapshotAt < previous.SnapshotAt)
                || (next.PhaseStartedAt < previous.PhaseStartedAt) || (next.Phase < previous.Phase))
            {
                return false;
            }
            return (next.Phase != previous.Phase) || (next.PhaseStartedAt == previous.PhaseStartedAt);
        }

        private bool ValidSnapshot(Snapshot snapshot, double now)
        {
            return (snapshot.CastId != 0) && (snapshot.Sequence != 0) && Sign(snapshot.Sign)
                && Valid(snapshot.Color) && ValidEndpoints(snapshot.FirstEndpoint, snapshot.SecondEndpoint, _settings.LinkHeight)
                && Finite(snapshot.AppearedAt) && Finite(snapshot.SnapshotAt) && Finite(snapshot.PhaseStartedAt)
                && (snapshot.AppearedAt >= 0d) && (snapshot.AppearedAt <= snapshot.PhaseStartedAt)
                && (snapshot.PhaseStartedAt <= snapshot.SnapshotAt) && (snapshot.SnapshotAt <= now)
                && (snapshot.Phase >= EPhase.Active) && (snapshot.Phase <= EPhase.Expired)
                && (snapshot.Severity >= ESeverity.Normal) && (snapshot.Severity <= ESeverity.Danger)
                && Unit(snapshot.WarningAmount) && ((snapshot.Severity != ESeverity.Normal) || (snapshot.WarningAmount == 0f));
        }

        private static bool ValidEndpoints(Vector3 first, Vector3 second, float height)
        {
            if ((!Valid(first)) || (!Valid(second)) || (!NonNegative(height)))
            {
                return false;
            }
            double dx = (double)second.x - first.x;
            double dy = (double)second.y - first.y;
            double dz = (double)second.z - first.z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return (length <= float.MaxValue) && ((double)first.y + height <= float.MaxValue)
                && ((double)second.y + height <= float.MaxValue);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Positive(float value) => Finite(value) && (value >= MIN_SIZE);
        private static bool NonNegative(float value) => Finite(value) && (value >= 0f);
        private static bool Unit(float value) => NonNegative(value) && (value <= 1f);
        private static bool Sign(EWolbaekWardSign value) => (value >= EWolbaekWardSign.Diamond) && (value <= EWolbaekWardSign.Moon);
        private static bool Valid(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Valid(Color value) => NonNegative(value.r) && NonNegative(value.g) && NonNegative(value.b) && Unit(value.a);
        private static bool ValidPose(Transform value) => Valid(value.localPosition) && Positive(value.localScale.x)
            && Positive(value.localScale.y) && Positive(value.localScale.z) && Finite(value.localRotation.x)
            && Finite(value.localRotation.y) && Finite(value.localRotation.z) && Finite(value.localRotation.w);
        private static float Normalized(double age, float seconds) => (float)Math.Max(0d, Math.Min(ENVELOPE_SECONDS, age / seconds));
        private static float Phase(double age, float cycle) => (float)(age % cycle / cycle) * TAU;
    }
}
