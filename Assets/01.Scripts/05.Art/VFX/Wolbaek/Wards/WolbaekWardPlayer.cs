// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;
using DG.Tweening.Core;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 서버가 확정한 한 지점의 결계를 표시한다. 다음 지점을 예측하거나 상태를 시간으로 승격하지 않는다 </summary>
    public sealed class WolbaekWardPlayer : ClientView
    {
        /****************************************
        *                Types
        ****************************************/
        public enum EPhase
        {
            Mark    = 1,
            Active  = 2,
            Expired = 3
        }

        /// <summary> 동일 Stage의 중심과 반경은 고정이다. 새로운 지점은 Begin으로 새로운 재생 세대를 시작한다 </summary>
        public readonly struct Snapshot
        {
            public ulong CastId { get; }
            public ulong Sequence { get; }
            public uint StageIndex { get; }
            public EWolbaekWardSign Sign { get; }
            public Color Color { get; }
            public Vector3 WorldCenter { get; }
            public float Radius { get; }
            public double AppearedAt { get; }
            public double SnapshotAt { get; }
            public EPhase Phase { get; }
            public double PhaseStartedAt { get; }

            public Snapshot(ulong castId, ulong sequence, uint stageIndex, EWolbaekWardSign sign,
                Color color, Vector3 worldCenter, float radius, double appearedAt, double snapshotAt,
                EPhase phase, double phaseStartedAt)
            {
                CastId         = castId;
                Sequence       = sequence;
                StageIndex     = stageIndex;
                Sign           = sign;
                Color          = color;
                WorldCenter    = worldCenter;
                Radius         = radius;
                AppearedAt     = appearedAt;
                SnapshotAt     = snapshotAt;
                Phase          = phase;
                PhaseStartedAt = phaseStartedAt;
            }
        }

        /****************************************
        *                Fields
        ****************************************/
        private const float MIN_SIZE         = 0.001f;
        private const float ENVELOPE_SECONDS = 1f;
        private const float TAU              = Mathf.PI * 2f;
        private const float FLOW_SECONDARY   = 0.61f;
        private const float PETAL_TURN       = 12f;
        private const int SIGN_COUNT         = 2;
        private const int SHELL_LAYER        = 0;
        private const int RIBBON_LAYER       = 1;
        private const int PETAL_LAYER        = 2;
        private const int MOTE_LAYER         = 3;
        private const int RIBBON_MOTION      = 0;
        private const int PETAL_MOTION       = 1;
        private const int MOTE_MOTION        = 2;
        private const int GLYPH_MOTION       = 3;
        private static readonly int _colorId = Shader.PropertyToID("_Color");
        private static readonly int _mapStId = Shader.PropertyToID("_BaseMap_ST");

        [Header("표현 설정과 리그")]
        [SerializeField] private WolbaekWardSettingsSO _settings = null;
        [SerializeField] private Transform _shapeRoot           = null;
        [SerializeField] private Transform _radiusRoot          = null;
        [SerializeField] private Renderer[] _layers             = null;
        [SerializeField] private int[] _layerKinds              = null;
        [SerializeField] private Renderer _boundary             = null;
        [SerializeField] private Renderer[] _glyphs             = null;
        [SerializeField] private Renderer[] _markGlyphs         = null;
        [SerializeField] private Transform[] _bones             = null;
        [SerializeField] private Vector3[] _rest                = null;
        // x=리본 0, 꽃잎 1, 광점 2, 문양 3, y=진폭 배율, z=위상, w=회전 방향
        [SerializeField] private Vector4[] _motion = null;

        private MaterialPropertyBlock _properties;
        private Tweener _appearTween;
        private Tweener _switchTween;
        private Tweener _fadeTween;
        private Snapshot _snapshot;
        private Snapshot _acceptedSnapshot;
        private Vector3 _rootPosition;
        private Quaternion _rootRotation;
        private Vector3 _rootScale;
        private Vector3 _shapeScale;
        private Vector3 _radiusScale;
        private Quaternion[] _restRotations;
        private Vector3[] _restScales;
        private uint _playbackId;
        private double _lastNow;
        private double _acceptedNow;
        private float _appear;
        private float _switch;
        private float _fade;
        private float _appearSeconds;
        private float _switchSeconds;
        private float _fadeSeconds;
        private float _domeHeight;
        private float _flowCycle;
        private float _waveAmplitude;
        private float _petalDrift;
        private float _shellOpacity;
        private float _ribbonOpacity;
        private float _petalOpacity;
        private float _moteOpacity;
        private float _glyphOpacity;
        private float _glyphStrength;
        private float _switchWhiteShare;
        private Color _petalTint;
        private float _assignedPetalTintShare;
        private static readonly List<WolbaekWardPlayer> _runningPlayers = new List<WolbaekWardPlayer>(4);

        private bool _running;
        private bool _posesCached;
        private bool _hasAcceptedSnapshot;

        /****************************************
        *              Properties
        ****************************************/
        public WolbaekWardSettingsSO Settings => _settings;
        public bool IsRunning => _running;

        /// <summary> 지금 표시 중인 색 결계, 표시가 끝나면 빠진다 </summary>
        public static IReadOnlyList<WolbaekWardPlayer> RunningPlayers => _runningPlayers;
        public uint PlaybackId => _playbackId;
        public ulong CastId => _running ? _snapshot.CastId : 0;
        public Snapshot CurrentSnapshot => _snapshot;
        public event Action<uint> Completed;

        /****************************************
        *              Unity Event
        ****************************************/
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _runningPlayers.Clear();
        }

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
        /// <summary> 확정 지점이나 재접속 스냅샷으로 시작한다. CastId의 숫자 크기로 시전 순서를 추정하지 않는다 </summary>
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
            _running = true;
            if (!_runningPlayers.Contains(this))
            {
                _runningPlayers.Add(this);
            }

            CacheSettings();
            _shapeRoot.localScale = new Vector3(snapshot.Radius, _domeHeight, snapshot.Radius);
            _radiusRoot.localScale = new Vector3(snapshot.Radius, 1f, snapshot.Radius);
            Accept(snapshot, now);
            uint playbackId = _playbackId;
            Tick(playbackId, now);
            return playbackId;
        }

        /// <summary> 같은 지점의 새로운 외부 상태만 수락한다. 중심 이동은 새 Stage의 Begin을 사용한다 </summary>
        public bool Reconcile(uint playbackId, Snapshot snapshot, double now)
        {
            if ((!Current(playbackId, now)) || (!ValidSnapshot(snapshot, now))
                || (!SameStage(_snapshot, snapshot)) || (!ValidTransition(_snapshot, snapshot)))
            {
                return false;
            }

            _snapshot = snapshot;
            Accept(snapshot, now);
            Tick(playbackId, now);
            return true;
        }

        /// <summary> 공유 시계의 절대 시점을 샘플링한다. Mark는 외부 Active 입력 전까지 계속 바닥 표식으로 남는다 </summary>
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
            _switchTween.Goto(_snapshot.Phase == EPhase.Active
                ? Normalized(phaseAge, _switchSeconds) : ENVELOPE_SECONDS, false);
            _fadeTween.Goto(expired ? Normalized(phaseAge, _fadeSeconds) : 0f, false);
            float opacity = _appear * (1f - _fade);
            float phase = Phase(age, _flowCycle);
            Color color = Color.Lerp(_snapshot.Color, Color.white, (1f - _switch) * _switchWhiteShare);
            color.a = _snapshot.Color.a;
            transform.SetPositionAndRotation(_snapshot.WorldCenter, Quaternion.identity);
            AnimateLayers(color, opacity, phase);
            AnimateBones(phase);
        }

        /// <summary> 지정된 재생 세대만 취소한다. 취소와 숨김에는 완료 이벤트가 없다 </summary>
        public bool Stop(uint playbackId)
        {
            if ((!_running) || (playbackId == 0) || (playbackId != _playbackId))
            {
                return false;
            }
            Stop();
            return true;
        }

        /// <summary> 풀 반환에 사용한다. 포즈와 속성 블록을 복원하고 수락한 Stage의 순서는 보존한다 </summary>
        [ContextMenu("Stop")]
        public void Stop()
        {
            _running = false;
            _runningPlayers.Remove(this);
            _snapshot = default;
            _lastNow = 0d;
            Hide(_layers);
            Hide(_glyphs);
            Hide(_markGlyphs);
            Hide(_boundary);
            _appearTween?.Goto(0f, false);
            _switchTween?.Goto(0f, false);
            _fadeTween?.Goto(0f, false);
            RestorePoses();
        }

        private Tweener CreateEnvelope(DOGetter<float> getter, DOSetter<float> setter, Ease ease)
        {
            // 재접속과 큰 시계 점프를 같은 포즈로 복원하기 위해 자동 재생을 사용하지 않는다
            return DOTween.To(getter, setter, ENVELOPE_SECONDS, ENVELOPE_SECONDS)
                .SetEase(ease).SetUpdate(UpdateType.Manual).SetAutoKill(false)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy).Pause();
        }

        private void CacheSettings()
        {
            _appearSeconds  = _settings.AppearSeconds;
            _switchSeconds  = _settings.SwitchSeconds;
            _fadeSeconds    = _settings.FadeSeconds;
            _domeHeight     = _settings.DomeHeight;
            _flowCycle      = _settings.FlowCycle;
            _waveAmplitude  = _settings.WaveAmplitude;
            _petalDrift     = _settings.PetalDrift;
            _shellOpacity   = _settings.ShellOpacity;
            _ribbonOpacity  = _settings.RibbonOpacity;
            _petalOpacity   = _settings.PetalOpacity;
            _moteOpacity    = _settings.MoteOpacity;
            _glyphOpacity   = _settings.GlyphOpacity;
            _glyphStrength  = _settings.GlyphStrength;
            _switchWhiteShare = _settings.SwitchWhiteShare;
            _petalTint = _settings.PetalTint;
            _assignedPetalTintShare = _settings.AssignedPetalTintShare;
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
            _shapeScale = _shapeRoot.localScale;
            _radiusScale = _radiusRoot.localScale;
            _restRotations = new Quaternion[_bones.Length];
            _restScales = new Vector3[_bones.Length];
            for (int i = 0; i < _bones.Length; ++i)
            {
                _restRotations[i] = _bones[i].localRotation;
                _restScales[i] = _bones[i].localScale;
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
            if (_shapeRoot != null)
            {
                _shapeRoot.localScale = _shapeScale;
            }
            if (_radiusRoot != null)
            {
                _radiusRoot.localScale = _radiusScale;
            }
            for (int i = 0; i < _bones.Length; ++i)
            {
                if (_bones[i] == null)
                {
                    continue;
                }
                _bones[i].SetLocalPositionAndRotation(_rest[i], _restRotations[i]);
                _bones[i].localScale = _restScales[i];
            }
        }

        private void AnimateLayers(Color color, float opacity, float phase)
        {
            bool active = _snapshot.Phase != EPhase.Mark;
            for (int i = 0; i < _layers.Length; ++i)
            {
                int kind = _layerKinds[i];
                float alpha = kind == SHELL_LAYER ? _shellOpacity : kind == RIBBON_LAYER
                    ? _ribbonOpacity : kind == PETAL_LAYER ? _petalOpacity : _moteOpacity;
                Color tint = kind == PETAL_LAYER
                    ? Color.Lerp(_petalTint, color, _assignedPetalTintShare) : color;
                tint.a *= active ? opacity * alpha : 0f;
                Paint(_layers[i], tint, kind == RIBBON_LAYER ? phase / TAU : 0f);
            }
            Color boundary = color;
            boundary.a *= opacity;
            // 장식의 불투명도와 분리해 돔이 투명해도 정확한 경계를 남긴다
            Paint(_boundary, boundary, 0f);
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                Color glyph = color * _glyphStrength;
                glyph.a = i == (int)_snapshot.Sign ? color.a * opacity * _glyphOpacity : 0f;
                Color activeGlyph = glyph;
                activeGlyph.a *= active ? 1f : 0f;
                Paint(_glyphs[i], activeGlyph, 0f);
                glyph.a *= active ? 0f : 1f;
                Paint(_markGlyphs[i], glyph, 0f);
            }
        }

        private void AnimateBones(float phase)
        {
            for (int i = 0; i < _bones.Length; ++i)
            {
                Vector4 motion = _motion[i];
                float wave = phase + motion.z;
                Vector3 offset = Vector3.zero;
                Quaternion rotation = Quaternion.identity;
                switch ((int)motion.x)
                {
                    case RIBBON_MOTION:
                        offset = new Vector3(Mathf.Sin(wave), Mathf.Sin(wave * FLOW_SECONDARY),
                            Mathf.Cos(wave)) * (_waveAmplitude * motion.y);
                        break;
                    case PETAL_MOTION:
                    case MOTE_MOTION:
                        offset = new Vector3(Mathf.Sin(wave), Mathf.Sin(wave * FLOW_SECONDARY),
                            Mathf.Cos(wave * FLOW_SECONDARY)) * (_petalDrift * motion.y);
                        rotation = Quaternion.Euler(Mathf.Sin(wave) * PETAL_TURN * motion.w,
                            Mathf.Cos(wave) * PETAL_TURN * motion.w, 0f);
                        break;
                    case GLYPH_MOTION:
                        break;
                }
                _bones[i].SetLocalPositionAndRotation(_rest[i] + offset, _restRotations[i] * rotation);
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

        private static void Hide(Renderer[] renderers)
        {
            if (renderers == null)
            {
                return;
            }
            for (int i = 0; i < renderers.Length; ++i)
            {
                Hide(renderers[i]);
            }
        }

        private static void Hide(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }
            renderer.enabled = false;
            renderer.SetPropertyBlock(null);
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
            if (snapshot.CastId != _acceptedSnapshot.CastId)
            {
                return true;
            }
            if (snapshot.StageIndex < _acceptedSnapshot.StageIndex)
            {
                return false;
            }
            if (snapshot.StageIndex > _acceptedSnapshot.StageIndex)
            {
                return snapshot.AppearedAt >= _acceptedSnapshot.AppearedAt;
            }
            return SameStage(_acceptedSnapshot, snapshot) && ValidTransition(_acceptedSnapshot, snapshot);
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
                && Positive(_settings.FadeSeconds) && Positive(_settings.DomeHeight)
                && Positive(_settings.FlowCycle) && NonNegative(_settings.WaveAmplitude)
                && NonNegative(_settings.PetalDrift) && Unit(_settings.ShellOpacity)
                && Unit(_settings.RibbonOpacity) && Unit(_settings.PetalOpacity)
                && Unit(_settings.MoteOpacity) && Unit(_settings.GlyphOpacity)
                && NonNegative(_settings.GlyphStrength) && Unit(_settings.SwitchWhiteShare)
                && Valid(_settings.PetalTint) && Unit(_settings.AssignedPetalTintShare);
        }

        private bool ValidRig()
        {
            if ((_shapeRoot == null) || (_radiusRoot == null) || (_shapeRoot == _radiusRoot)
                || (!_shapeRoot.IsChildOf(transform)) || (!_radiusRoot.IsChildOf(transform))
                || _shapeRoot.IsChildOf(_radiusRoot) || _radiusRoot.IsChildOf(_shapeRoot)
                || (_boundary == null) || (!_boundary.transform.IsChildOf(_radiusRoot))
                || (_layers == null) || (_layers.Length == 0) || (_layerKinds == null)
                || (_layers.Length != _layerKinds.Length) || (_glyphs == null)
                || (_glyphs.Length != SIGN_COUNT) || (_markGlyphs == null)
                || (_markGlyphs.Length != SIGN_COUNT) || (_bones == null) || (_rest == null)
                || (_motion == null) || (_bones.Length != _rest.Length) || (_bones.Length != _motion.Length)
                || (!ValidPose(transform)) || (!ValidPose(_shapeRoot)) || (!ValidPose(_radiusRoot)))
            {
                return false;
            }
            for (int i = 0; i < _layers.Length; ++i)
            {
                if ((_layers[i] == null) || (!_layers[i].transform.IsChildOf(_shapeRoot))
                    || (_layerKinds[i] < SHELL_LAYER) || (_layerKinds[i] > MOTE_LAYER))
                {
                    return false;
                }
            }
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                if ((_glyphs[i] == null) || (_markGlyphs[i] == null)
                    || (!_glyphs[i].transform.IsChildOf(_shapeRoot))
                    || (!_markGlyphs[i].transform.IsChildOf(_radiusRoot)))
                {
                    return false;
                }
            }
            for (int i = 0; i < _bones.Length; ++i)
            {
                Vector4 motion = _motion[i];
                if ((_bones[i] == null) || (!_bones[i].IsChildOf(_shapeRoot)) || (!ValidPose(_bones[i]))
                    || (!Valid(_rest[i])) || (!Finite(motion.x)) || (!NonNegative(motion.y))
                    || (!Finite(motion.z)) || (!Finite(motion.w)) || (motion.x < RIBBON_MOTION)
                    || (motion.x > GLYPH_MOTION) || (motion.x != Mathf.Floor(motion.x)))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameStage(Snapshot previous, Snapshot next)
        {
            return (previous.CastId == next.CastId) && (previous.StageIndex == next.StageIndex)
                && (previous.Sign == next.Sign) && previous.Color.Equals(next.Color)
                && previous.WorldCenter.Equals(next.WorldCenter) && (previous.Radius == next.Radius)
                && (previous.AppearedAt == next.AppearedAt);
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

        private static bool ValidSnapshot(Snapshot snapshot, double now)
        {
            return (snapshot.CastId != 0) && (snapshot.Sequence != 0) && Sign(snapshot.Sign)
                && Valid(snapshot.Color) && Valid(snapshot.WorldCenter) && Positive(snapshot.Radius)
                && Finite(snapshot.AppearedAt) && Finite(snapshot.SnapshotAt) && Finite(snapshot.PhaseStartedAt)
                && (snapshot.AppearedAt >= 0d) && (snapshot.AppearedAt <= snapshot.PhaseStartedAt)
                && (snapshot.PhaseStartedAt <= snapshot.SnapshotAt) && (snapshot.SnapshotAt <= now)
                && (snapshot.Phase >= EPhase.Mark) && (snapshot.Phase <= EPhase.Expired);
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
