// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Common;
using static Shinshi.Art.WolbaekSealVisualUtility;
using Pose = Shinshi.Art.WolbaekSealVisualUtility.Pose;

namespace Shinshi.Art
{
    /// <summary> Index와 Center로 식별되는 독립 봉인핵. 개별 Destroyed 외부 입력이 있을 때만 파편을 재생한다 </summary>
    public sealed class WolbaekSealCorePlayer : ClientView
    {
        /****************************************
        *                Types
        ****************************************/
        public enum EPhase
        {
            Dormant   = 1,
            Opened    = 2,
            Destroyed = 3,
            Expired   = 4
        }

        /// <summary> 동일 시전과 차례와 핵 번호의 중심, 등장 시각은 고정이다. 파괴 입력은 해당 핵의 외부 확인을 뜻한다 </summary>
        public readonly struct Snapshot
        {
            public ulong CastId { get; }
            public ulong Sequence { get; }
            public int TurnIndex { get; }
            public int CoreIndex { get; }
            public Vector3 WorldCenter { get; }
            public double AppearedAt { get; }
            public double SnapshotAt { get; }
            public EPhase Phase { get; }
            public double PhaseStartedAt { get; }

            public Snapshot(ulong castId, ulong sequence, int turnIndex, int coreIndex, Vector3 worldCenter,
                double appearedAt, double snapshotAt, EPhase phase, double phaseStartedAt)
            {
                CastId         = castId;
                Sequence       = sequence;
                TurnIndex      = turnIndex;
                CoreIndex      = coreIndex;
                WorldCenter    = worldCenter;
                AppearedAt     = appearedAt;
                SnapshotAt     = snapshotAt;
                Phase          = phase;
                PhaseStartedAt = phaseStartedAt;
            }
        }

        /****************************************
        *                Fields
        ****************************************/
        private const int DIRECTION_ALTERNATION_COUNT = 2;
        private const int STONE_KIND                  = 0;
        private const int BAND_KIND                   = 1;
        private const int FLOW_KIND                   = 2;
        private const int PETAL_KIND                  = 3;
        private const float MOTION_SECONDARY          = 0.61f;
        private const float PIECE_PHASE_STEP          = 0.73f;
        private const float SHELL_FLOW_RATE           = 0.7f;
        private const float SHELL_RADIAL_SHARE        = 0.3f;
        private const float PETAL_OPEN_SHARE          = 0.6f;
        private const int PETAL_RATE_COUNT            = 5;
        private const float PETAL_RATE_STEP           = 0.07f;
        private const float PETAL_VERTICAL_RATE       = 0.73f;
        private const float PETAL_DEPTH_RATE          = 1.31f;
        private const float PETAL_PHASE_SHARE         = 1.47f;
        private const float DIRECTION_EPSILON         = 0.000001f;

        [Header("표현 설정과 리그")]
        [SerializeField] private WolbaekSealCoreSettingsSO _settings = null;
        [SerializeField] private Transform _shapeRoot                = null;
        [SerializeField] private Transform[] _pieces                 = null;
        [SerializeField] private int[] _pieceKinds                   = null;
        [SerializeField] private Renderer[] _pieceRenderers          = null;
        [SerializeField] private Renderer _core                      = null;
        [SerializeField] private Renderer _ground                    = null;
        [SerializeField] private Renderer _flash                     = null;
        [SerializeField] private Light _coreLight                    = null;

        private readonly InputOrder _order = new InputOrder();
        private RendererState[] _pieceStates;
        private RendererState _coreState;
        private RendererState _groundState;
        private RendererState _flashState;
        private Pose[] _piecePoses;
        private Pose _rootPose;
        private Pose _shapePose;
        private Pose _corePose;
        private Pose _groundPose;
        private Pose _flashPose;
        private Pose _lightPose;
        private Light _cachedCoreLight;
        private Color _originalLightColor;
        private float _originalLightRange;
        private Transform _cachedShapeRoot;
        private Tweener _appearTween;
        private Tweener _openTween;
        private Tweener _shatterTween;
        private Tweener _shrinkTween;
        private Tweener _fadeTween;
        private Snapshot _snapshot;
        private Snapshot _acceptedSnapshot;
        private uint _playbackId;
        private float _appear;
        private float _open;
        private float _shatter;
        private float _shrink;
        private float _fade;
        private float _appearSeconds;
        private float _openSeconds;
        private float _shatterSeconds;
        private float _fadeSeconds;
        private float _coreHeight;
        private float _openDistance;
        private float _openTiltDegrees;
        private float _openLift;
        private float _openPieceScale;
        private float _shatterDistance;
        private float _shatterLift;
        private float _shatterTurns;
        private float _shatterShrinkStart;
        private float _flowCycle;
        private float _flowAmplitude;
        private float _shellDrift;
        private float _shellTiltDegrees;
        private float _petalDrift;
        private float _petalTurns;
        private float _dormantGlow;
        private float _openedGlow;
        private float _flowOpacity;
        private float _petalOpacity;
        private float _groundOpacity;
        private float _coreOpacity;
        private float _flashStrength;
        private float _lightRange;
        private float _dormantLightIntensity;
        private float _openedLightIntensity;
        private Color _color;
        private Color _lightColor;
        private static readonly List<WolbaekSealCorePlayer> _runningPlayers = new List<WolbaekSealCorePlayer>(4);

        private bool _running;
        private bool _posesCached;
        private bool _hasAcceptedSnapshot;

        /****************************************
        *              Properties
        ****************************************/
        public WolbaekSealCoreSettingsSO Settings => _settings;
        public bool IsRunning => _running;

        /// <summary> 지금 표시 중인 봉인 핵, 표시가 끝나면 빠진다 </summary>
        public static IReadOnlyList<WolbaekSealCorePlayer> RunningPlayers => _runningPlayers;
        public Color MarkerColor => _color;
        public uint PlaybackId => _playbackId;
        public ulong CastId => _running ? _snapshot.CastId : 0;
        public Snapshot CurrentSnapshot => _snapshot;
        public ulong LastAcceptedSequence => _acceptedSnapshot.Sequence;
        public double LastSampleTime => _order.LastNow;
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
            _appearTween  = Envelope(gameObject, () => _appear, value => _appear = value, Ease.OutCubic);
            _openTween    = Envelope(gameObject, () => _open, value => _open = value, Ease.OutCubic);
            _shatterTween = Envelope(gameObject, () => _shatter, value => _shatter = value, Ease.OutCubic);
            _shrinkTween  = Envelope(gameObject, () => _shrink, value => _shrink = value, Ease.InCubic);
            _fadeTween    = Envelope(gameObject, () => _fade, value => _fade = value, Ease.InCubic);
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
            _openTween?.Kill();
            _shatterTween?.Kill();
            _shrinkTween?.Kill();
            _fadeTween?.Kill();
        }

        /****************************************
        *                Methods
        ****************************************/
        /// <summary> 외부 절대 시각의 개별 핵 스냅샷을 재생한다. Sequence는 전달 창구 전체에서 단조 증가해야 한다 </summary>
        public uint Begin(Snapshot snapshot, double now)
        {
            if (((!Ready(now)) || (!ValidSnapshot(snapshot, now))) || (!AcceptsBegin(snapshot, now)))
            {
                return 0;
            }
            CachePoses();
            Stop();
            CacheSettings();
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

            Accept(snapshot, now);
            uint playbackId = _playbackId;
            Tick(playbackId, now);
            return playbackId;
        }

        /// <summary> 같은 핵의 새로운 확정 상태만 받는다. 총 파괴 개수나 낙하 위치로 Destroyed를 추론하지 않는다 </summary>
        public bool Reconcile(uint playbackId, Snapshot snapshot, double now)
        {
            if (((((!Current(playbackId, now)) || (!ValidSnapshot(snapshot, now)))
                || (!_order.Accepts(snapshot.Sequence, snapshot.SnapshotAt, now)))
                || (!SameCore(_snapshot, snapshot))) || (!ValidTransition(_snapshot, snapshot)))
            {
                return false;
            }
            _snapshot = snapshot;
            Accept(snapshot, now);
            Tick(playbackId, now);
            return true;
        }

        /// <summary> 외부 공통 시계에서 포즈를 복원한다. 시간 경과만으로 Opened나 Destroyed로 전환하지 않는다 </summary>
        public void Tick(uint playbackId, double now)
        {
            if (!Current(playbackId, now))
            {
                return;
            }
            _order.Tick(now);
            double age = now - _snapshot.AppearedAt;
            double phaseAge = now - _snapshot.PhaseStartedAt;
            bool destroyed = _snapshot.Phase == EPhase.Destroyed;
            bool expired = _snapshot.Phase == EPhase.Expired;
            if (((destroyed) && (phaseAge >= _shatterSeconds)) || ((expired) && (phaseAge >= _fadeSeconds)))
            {
                Complete(playbackId);
                return;
            }
            _appearTween.Goto(Normalized(age, _appearSeconds), false);
            _openTween.Goto(_snapshot.Phase == EPhase.Opened ? Normalized(phaseAge, _openSeconds)
                : destroyed ? ENVELOPE_SECONDS : 0f, false);
            _shatterTween.Goto(destroyed ? Normalized(phaseAge, _shatterSeconds) : 0f, false);
            _shrinkTween.Goto(destroyed ? Normalized(phaseAge - _shatterSeconds * _shatterShrinkStart,
                _shatterSeconds * (1f - _shatterShrinkStart)) : 0f, false);
            _fadeTween.Goto(expired ? Normalized(phaseAge, _fadeSeconds) : 0f, false);
            transform.SetPositionAndRotation(_snapshot.WorldCenter, Quaternion.identity);
            _shapeRoot.localPosition = new Vector3(_shapePose.Position.x, _coreHeight, _shapePose.Position.z);
            float opacity = _appear * (1f - _fade);
            float phase = Phase(age, _flowCycle);
            AnimatePieces(age, phase, opacity);
            PaintLayers(phase, opacity, destroyed);
            PaintLight(opacity, destroyed);
        }

        /// <summary> 현재 재생 세대만 취소한다. 취소에는 Completed 이벤트가 없다 </summary>
        public bool Stop(uint playbackId)
        {
            if (((!_running) || (playbackId == 0)) || (playbackId != _playbackId))
            {
                return false;
            }
            Stop();
            return true;
        }

        /// <summary> 풀 반환용 정리. 원본 포즈와 속성 블록을 복원하고 이미 받은 입력 순서를 보존한다 </summary>
        [ContextMenu("Stop")]
        public void Stop()
        {
            _running = false;
            _runningPlayers.Remove(this);
            _snapshot = default;
            _appearTween?.Goto(0f, false);
            _openTween?.Goto(0f, false);
            _shatterTween?.Goto(0f, false);
            _shrinkTween?.Goto(0f, false);
            _fadeTween?.Goto(0f, false);
            RestoreLight();
            if (_posesCached)
            {
                _rootPose.Restore(transform);
                _shapePose.Restore(_cachedShapeRoot);
                RestorePose(_corePose, _coreState.Target);
                RestorePose(_groundPose, _groundState.Target);
                RestorePose(_flashPose, _flashState.Target);
                for (int i = 0; i < _pieceStates.Length; ++i)
                {
                    if (_pieceStates[i].Target != null)
                    {
                        _piecePoses[i].Restore(_pieceStates[i].Target.transform);
                    }
                    _pieceStates[i].Restore();
                }
                _coreState.Restore();
                _groundState.Restore();
                _flashState.Restore();
                return;
            }
            Hide(_core);
            Hide(_ground);
            Hide(_flash);
            if (_pieceRenderers != null)
            {
                for (int i = 0; i < _pieceRenderers.Length; ++i)
                {
                    Hide(_pieceRenderers[i]);
                }
            }
        }

        private void CachePoses()
        {
            if (_posesCached)
            {
                return;
            }
            _rootPose   = new Pose(transform);
            _shapePose  = new Pose(_shapeRoot);
            _corePose   = new Pose(_core.transform);
            _groundPose = new Pose(_ground.transform);
            _flashPose  = new Pose(_flash.transform);
            _cachedShapeRoot = _shapeRoot;
            _cachedCoreLight = _coreLight;
            if (_cachedCoreLight != null)
            {
                _lightPose = new Pose(_cachedCoreLight.transform);
                _originalLightColor = _cachedCoreLight.color;
                _originalLightRange = _cachedCoreLight.range;
            }
            _coreState   = new RendererState(_core);
            _groundState = new RendererState(_ground);
            _flashState  = new RendererState(_flash);
            _piecePoses  = new Pose[_pieces.Length];
            _pieceStates = new RendererState[_pieces.Length];
            for (int i = 0; i < _pieces.Length; ++i)
            {
                _piecePoses[i]  = new Pose(_pieces[i]);
                _pieceStates[i] = new RendererState(_pieceRenderers[i]);
            }
            _posesCached = true;
        }

        private void CacheSettings()
        {
            _color         = _settings.DefaultColor;
            _appearSeconds = _settings.AppearSeconds;
            _openSeconds   = _settings.OpenSeconds;
            _shatterSeconds = _settings.ShatterSeconds;
            _fadeSeconds   = _settings.FadeSeconds;
            _coreHeight    = _settings.CoreHeight;
            _openDistance  = _settings.OpenDistance;
            _openTiltDegrees = _settings.OpenTiltDegrees;
            _openLift      = _settings.OpenLift;
            _openPieceScale = _settings.OpenPieceScale;
            _shatterDistance = _settings.ShatterDistance;
            _shatterLift   = _settings.ShatterLift;
            _shatterTurns  = _settings.ShatterTurns;
            _shatterShrinkStart = _settings.ShatterShrinkStart;
            _flowCycle     = _settings.FlowCycle;
            _flowAmplitude = _settings.FlowAmplitude;
            _shellDrift = _settings.ShellDrift;
            _shellTiltDegrees = _settings.ShellTiltDegrees;
            _petalDrift    = _settings.PetalDrift;
            _petalTurns    = _settings.PetalTurns;
            _dormantGlow   = _settings.DormantGlow;
            _openedGlow    = _settings.OpenedGlow;
            _flowOpacity   = _settings.FlowOpacity;
            _petalOpacity  = _settings.PetalOpacity;
            _groundOpacity = _settings.GroundOpacity;
            _coreOpacity   = _settings.CoreOpacity;
            _flashStrength = _settings.FlashStrength;
            _lightRange = _settings.LightRange;
            _dormantLightIntensity = _settings.DormantLightIntensity;
            _openedLightIntensity = _settings.OpenedLightIntensity;
            float colorPeak = Mathf.Max(MIN_SIZE, Mathf.Max(_color.r, Mathf.Max(_color.g, _color.b)));
            _lightColor = new Color(_color.r / colorPeak, _color.g / colorPeak, _color.b / colorPeak, 1f);
        }

        private void AnimatePieces(double age, float phase, float opacity)
        {
            float glow = Mathf.Lerp(_dormantGlow, _openedGlow, _open);
            for (int i = 0; i < _pieces.Length; ++i)
            {
                Pose rest = _piecePoses[i];
                int kind = _pieceKinds[i];
                float wave = phase + i * PIECE_PHASE_STEP;
                Vector3 radial = new Vector3(rest.Position.x, 0f, rest.Position.z);
                radial = radial.sqrMagnitude > DIRECTION_EPSILON ? radial.normalized
                    : new Vector3(Mathf.Sin(i * PIECE_PHASE_STEP), 0f, Mathf.Cos(i * PIECE_PHASE_STEP));
                Vector3 offset = radial * (_openDistance * _open);
                Quaternion rotation = Quaternion.identity;
                float pieceOpacity = opacity;
                float size = opacity * (1f - _shrink);
                Color tint = _pieceStates[i].BaseColor;
                float direction = (i % DIRECTION_ALTERNATION_COUNT) == 0 ? 1f : -1f;
                if ((kind == STONE_KIND) || (kind == BAND_KIND))
                {
                    size *= Mathf.Lerp(1f, _openPieceScale, _open);
                    float verticalShare = rest.Position.y / Mathf.Max(MIN_SIZE, rest.Position.magnitude);
                    offset += Vector3.up * ((_openDistance * verticalShare + _openLift) * _open);
                    rotation = Quaternion.AngleAxis(_openTiltDegrees * _open * direction,
                        new Vector3(-radial.z, 0f, radial.x));
                    float shellWave = Phase(age, _flowCycle / SHELL_FLOW_RATE) + i * PIECE_PHASE_STEP;
                    float motionShare = 1f - _shatter;
                    offset += new Vector3(radial.x * Mathf.Cos(shellWave) * SHELL_RADIAL_SHARE,
                        Mathf.Sin(shellWave), radial.z * Mathf.Sin(shellWave) * SHELL_RADIAL_SHARE)
                        * (_shellDrift * motionShare);
                    rotation *= Quaternion.Euler(Mathf.Sin(shellWave) * _shellTiltDegrees * motionShare,
                        0f, Mathf.Cos(shellWave) * _shellTiltDegrees * motionShare * direction);
                }
                if (kind == FLOW_KIND)
                {
                    offset = new Vector3(Mathf.Sin(wave), Mathf.Sin(wave * MOTION_SECONDARY), Mathf.Cos(wave))
                        * _flowAmplitude;
                    rotation = Quaternion.Euler(0f, Turns(age, _flowCycle, 1f), 0f);
                    tint = _color * glow;
                    tint.a = _color.a;
                    pieceOpacity *= _flowOpacity;
                    if (_snapshot.Phase == EPhase.Destroyed)
                    {
                        pieceOpacity = 0f;
                    }
                }
                else if (kind == PETAL_KIND)
                {
                    offset *= PETAL_OPEN_SHARE;
                    float seed = i * PIECE_PHASE_STEP;
                    float rate = 1f + (i % PETAL_RATE_COUNT) * PETAL_RATE_STEP;
                    float xWave = Phase(age, _flowCycle / rate) + seed;
                    float yWave = Phase(age, _flowCycle / (rate * PETAL_VERTICAL_RATE)) + seed * PETAL_PHASE_SHARE;
                    float zWave = Phase(age, _flowCycle / (rate * PETAL_DEPTH_RATE)) - seed;
                    offset += new Vector3(Mathf.Sin(xWave), Mathf.Sin(yWave), Mathf.Cos(zWave))
                        * (_petalDrift * (1f - _shatter));
                    Vector3 spinAxis = new Vector3(Mathf.Cos(seed), Mathf.Sin(seed * PETAL_PHASE_SHARE),
                        Mathf.Sin(seed)).normalized;
                    rotation = Quaternion.AngleAxis(Turns(age, _flowCycle, _petalTurns * rate) * direction, spinAxis);
                    pieceOpacity *= _petalOpacity;
                }
                offset += radial * (_shatterDistance * _shatter) + Vector3.up * (_shatterLift * _shatter);
                rotation *= Quaternion.Euler(_shatter * _shatterTurns * FULL_TURN * direction,
                    _shatter * _shatterTurns * FULL_TURN, 0f);
                _pieces[i].SetLocalPositionAndRotation(rest.Position + offset, rest.Rotation * rotation);
                _pieces[i].localScale = rest.Scale * size;
                _pieceStates[i].Paint(tint, pieceOpacity, kind == FLOW_KIND ? phase / TAU : 0f);
            }
        }

        private void PaintLayers(float phase, float opacity, bool destroyed)
        {
            float glow = Mathf.Lerp(_dormantGlow, _openedGlow, _open);
            Color coreColor = _color * glow;
            coreColor.a = _color.a;
            _core.transform.localScale = _corePose.Scale * (_appear * (1f - _shatter) * (1f - _fade));
            _coreState.Paint(coreColor, destroyed ? 0f : opacity * _coreOpacity, phase / TAU);
            // 핵 지면은 장식이다. 서버 CoreHitRadius나 공격 가능한 경계를 표현하지 않는다
            _groundState.Paint(_color, opacity * _groundOpacity * (1f - _shrink));
            Color flash = _color * _flashStrength;
            flash.a = _color.a;
            _flashState.Paint(flash, destroyed ? opacity * (1f - _shatter) : 0f);
        }

        private bool Ready(double now)
        {
            return ((((((!IsServerBuild) && (isActiveAndEnabled)) && (_appearTween != null)) && (_settings != null))
                && (Finite(now))) && (ValidSettings())) && (ValidRig());
        }

        private void PaintLight(float opacity, bool destroyed)
        {
            if (_cachedCoreLight == null)
            {
                return;
            }
            float intensity = destroyed ? 0f
                : Mathf.Lerp(_dormantLightIntensity, _openedLightIntensity, _open) * opacity;
            _cachedCoreLight.color = _lightColor;
            _cachedCoreLight.range = _lightRange;
            _cachedCoreLight.intensity = intensity;
            _cachedCoreLight.enabled = (intensity > MIN_SIZE) && (_lightRange > MIN_SIZE);
        }

        private void RestoreLight()
        {
            if (_cachedCoreLight != null)
            {
                _lightPose.Restore(_cachedCoreLight.transform);
                _cachedCoreLight.color = _originalLightColor;
                _cachedCoreLight.range = _originalLightRange;
                _cachedCoreLight.intensity = 0f;
                _cachedCoreLight.enabled = false;
                return;
            }
            if ((_coreLight != null) && (_coreLight.transform.IsChildOf(transform)))
            {
                _coreLight.intensity = 0f;
                _coreLight.enabled = false;
            }
        }

        private bool Current(uint playbackId, double now)
        {
            return ((((((!IsServerBuild) && (_running)) && (isActiveAndEnabled)) && (playbackId != 0))
                && (playbackId == _playbackId)) && (Finite(now))) && (now >= _order.LastNow);
        }

        private bool ValidSettings()
        {
            return (((((((((((((((((((((((((((((((Valid(_settings.DefaultColor)) && (Positive(_settings.AppearSeconds))) && (Positive(_settings.OpenSeconds)))
                && (Positive(_settings.ShatterSeconds))) && (Positive(_settings.FadeSeconds))) && (NonNegative(_settings.CoreHeight)))
                && (NonNegative(_settings.OpenDistance))) && (NonNegative(_settings.ShatterDistance))) && (NonNegative(_settings.ShatterLift)))
                && (NonNegative(_settings.OpenTiltDegrees))) && (NonNegative(_settings.OpenLift)))
                && (Positive(_settings.OpenPieceScale))) && (_settings.OpenPieceScale <= 1f))
                && (NonNegative(_settings.ShatterTurns))) && (Unit(_settings.ShatterShrinkStart))) && (_settings.ShatterShrinkStart < 1f))
                && (Positive(_settings.FlowCycle))) && (NonNegative(_settings.FlowAmplitude))) && (NonNegative(_settings.PetalDrift)))
                && (NonNegative(_settings.ShellDrift))) && (NonNegative(_settings.ShellTiltDegrees)))
                && (NonNegative(_settings.PetalTurns))) && (NonNegative(_settings.DormantGlow))) && (NonNegative(_settings.OpenedGlow)))
                && (Unit(_settings.FlowOpacity))) && (Unit(_settings.PetalOpacity))) && (Unit(_settings.GroundOpacity)))
                && (Unit(_settings.CoreOpacity))) && (NonNegative(_settings.FlashStrength)))
                && (NonNegative(_settings.LightRange))) && (NonNegative(_settings.DormantLightIntensity)))
                && (NonNegative(_settings.OpenedLightIntensity));
        }

        private bool ValidRig()
        {
            bool validatePose = !_posesCached;
            if ((((((((((((((_shapeRoot == null) || (_shapeRoot.parent != transform)) || (_pieces == null)) || (_pieces.Length == 0))
                || (_pieceKinds == null)) || (_pieceRenderers == null)) || (_pieces.Length != _pieceKinds.Length))
                || (_pieces.Length != _pieceRenderers.Length))
                || (!ValidRenderer(_core, _shapeRoot, validatePose))) || (!ValidRenderer(_ground, transform, validatePose)))
                || (!ValidRenderer(_flash, _shapeRoot, validatePose))) || (_core == _flash)) || (_ground == _flash))
                || ((validatePose) && ((!ValidPose(transform)) || (!ValidPose(_shapeRoot)))))
            {
                return false;
            }
            if ((_posesCached) && ((((((_cachedShapeRoot != _shapeRoot) || (_pieceStates.Length != _pieces.Length))
                || (_coreState.Target != _core)) || (_groundState.Target != _ground)) || (_flashState.Target != _flash))
                || (_cachedCoreLight != _coreLight)))
            {
                return false;
            }
            if ((_coreLight != null) && (((_coreLight.type != LightType.Point) || (_coreLight.transform.parent != _shapeRoot))
                || ((validatePose) && ((((!ValidPose(_coreLight.transform)) || (!Valid(_coreLight.color)))
                    || (!NonNegative(_coreLight.range))) || (!NonNegative(_coreLight.intensity))))))
            {
                return false;
            }
            for (int i = 0; i < _pieces.Length; ++i)
            {
                if ((((((((_pieces[i] == null) || (_pieceKinds[i] < STONE_KIND)) || (_pieceKinds[i] > PETAL_KIND))
                    || (!ValidRenderer(_pieceRenderers[i], _shapeRoot, validatePose)))
                    || (_pieceRenderers[i].transform != _pieces[i])) || (_pieceRenderers[i] == _core))
                    || (_pieceRenderers[i] == _flash)) || ((_posesCached) && (_pieceStates[i].Target != _pieceRenderers[i])))
                {
                    return false;
                }
                for (int previous = 0; previous < i; ++previous)
                {
                    if (_pieces[previous] == _pieces[i])
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private bool AcceptsBegin(Snapshot snapshot, double now)
        {
            if (!_order.Accepts(snapshot.Sequence, snapshot.SnapshotAt, now))
            {
                return false;
            }
            if ((!_hasAcceptedSnapshot) || (snapshot.CastId != _acceptedSnapshot.CastId))
            {
                return true;
            }
            if (snapshot.TurnIndex != _acceptedSnapshot.TurnIndex)
            {
                return (snapshot.TurnIndex > _acceptedSnapshot.TurnIndex)
                    && (snapshot.AppearedAt >= _acceptedSnapshot.AppearedAt);
            }
            if ((!_running) && (snapshot.CoreIndex != _acceptedSnapshot.CoreIndex))
            {
                return snapshot.AppearedAt >= _acceptedSnapshot.AppearedAt;
            }
            return (SameCore(_acceptedSnapshot, snapshot)) && (ValidTransition(_acceptedSnapshot, snapshot));
        }

        private void Accept(Snapshot snapshot, double now)
        {
            _acceptedSnapshot = snapshot;
            _hasAcceptedSnapshot = true;
            _order.Accept(snapshot.Sequence, snapshot.SnapshotAt, now);
        }

        private static bool SameCore(Snapshot previous, Snapshot next)
        {
            return ((((previous.CastId == next.CastId) && (previous.TurnIndex == next.TurnIndex))
                && (previous.CoreIndex == next.CoreIndex)) && (previous.WorldCenter.Equals(next.WorldCenter)))
                && (previous.AppearedAt == next.AppearedAt);
        }

        private static bool ValidTransition(Snapshot previous, Snapshot next)
        {
            if (((previous.Phase == EPhase.Destroyed) || (previous.Phase == EPhase.Expired))
                && (next.Phase != previous.Phase))
            {
                return false;
            }
            return ((next.Phase >= previous.Phase) && (next.PhaseStartedAt >= previous.PhaseStartedAt))
                && ((next.Phase != previous.Phase) || (next.PhaseStartedAt == previous.PhaseStartedAt));
        }

        private static bool ValidSnapshot(Snapshot snapshot, double now)
        {
            return ((((((((((((((snapshot.CastId != 0) && (snapshot.Sequence != 0)) && (snapshot.TurnIndex >= 0)) && (snapshot.CoreIndex >= 0))
                && (Valid(snapshot.WorldCenter))) && (Finite(snapshot.AppearedAt))) && (Finite(snapshot.SnapshotAt)))
                && (Finite(snapshot.PhaseStartedAt))) && (snapshot.AppearedAt >= 0d))
                && (snapshot.AppearedAt <= snapshot.PhaseStartedAt)) && (snapshot.PhaseStartedAt <= snapshot.SnapshotAt))
                && (snapshot.SnapshotAt <= now)) && (snapshot.Phase >= EPhase.Dormant)) && (snapshot.Phase <= EPhase.Expired))
                && ((snapshot.Phase != EPhase.Dormant) || (snapshot.PhaseStartedAt == snapshot.AppearedAt));
        }

        private void Complete(uint playbackId)
        {
            Stop();
            Completed?.Invoke(playbackId);
        }
    }
}
