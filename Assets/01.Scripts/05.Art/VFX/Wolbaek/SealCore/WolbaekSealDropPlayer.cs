// System
using System;

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
    /// <summary> 핵 좌표와 독립인 낙하 목표를 표시한다. Tracking은 외부 갱신, Locked 이후 목표는 고정이다 </summary>
    public sealed class WolbaekSealDropPlayer : ClientView
    {
        /****************************************
        *                Types
        ****************************************/
        public enum EPhase
        {
            Tracking = 1,
            Locked   = 2,
            Resolved = 3,
            Expired  = 4
        }

        /// <summary> 반경과 시간표는 동일 낙하 안에서 고정이다. 예정 ResolveAt은 낙하 포즈에만 쓰고 판정을 뜻하지 않는다 </summary>
        public readonly struct Snapshot
        {
            public ulong CastId { get; }
            public ulong Sequence { get; }
            public int TurnIndex { get; }
            public int DropIndex { get; }
            public Vector3 WorldTarget { get; }
            public float BlastRadius { get; }
            public double AppearedAt { get; }
            public double LockAt { get; }
            public double ResolveAt { get; }
            public double SnapshotAt { get; }
            public EPhase Phase { get; }
            public double PhaseStartedAt { get; }

            public Snapshot(ulong castId, ulong sequence, int turnIndex, int dropIndex, Vector3 worldTarget,
                float blastRadius, double appearedAt, double lockAt, double resolveAt, double snapshotAt,
                EPhase phase, double phaseStartedAt)
            {
                CastId         = castId;
                Sequence       = sequence;
                TurnIndex      = turnIndex;
                DropIndex      = dropIndex;
                WorldTarget    = worldTarget;
                BlastRadius    = blastRadius;
                AppearedAt     = appearedAt;
                LockAt         = lockAt;
                ResolveAt      = resolveAt;
                SnapshotAt     = snapshotAt;
                Phase          = phase;
                PhaseStartedAt = phaseStartedAt;
            }
        }

        /****************************************
        *                Fields
        ****************************************/
        [Header("표현 설정과 리그")]
        [SerializeField] private WolbaekSealDropSettingsSO _settings = null;
        [SerializeField] private Transform _dropRoot                 = null;
        [SerializeField] private Renderer[] _flame                   = null;
        [SerializeField] private Renderer _ground                    = null;
        [SerializeField] private Renderer _flash                     = null;

        private readonly InputOrder _order = new InputOrder();
        private UnitHierarchy _unitHierarchy;
        private RendererState[] _flameStates;
        private RendererState _groundState;
        private RendererState _flashState;
        private Pose _rootPose;
        private Pose _dropPose;
        private Pose _groundPose;
        private Pose _flashPose;
        private Pose[] _flamePoses;
        private Transform _cachedDropRoot;
        private Tweener _appearTween;
        private Tweener _fallTween;
        private Tweener _resolveTween;
        private Tweener _fadeTween;
        private Snapshot _snapshot;
        private Snapshot _acceptedSnapshot;
        private uint _playbackId;
        private float _appear;
        private float _fall;
        private float _resolve;
        private float _fade;
        private float _appearSeconds;
        private float _resolvedSeconds;
        private float _fadeSeconds;
        private float _dropHeight;
        private float _hoverAmplitude;
        private float _flowCycle;
        private float _flameOpacity;
        private float _groundOpacity;
        private float _flashStrength;
        private Color _color;
        private bool _running;
        private bool _posesCached;
        private bool _hasAcceptedSnapshot;

        /****************************************
        *              Properties
        ****************************************/
        public WolbaekSealDropSettingsSO Settings => _settings;
        public bool IsRunning => _running;
        public uint PlaybackId => _playbackId;
        public ulong CastId => _running ? _snapshot.CastId : 0;
        public Snapshot CurrentSnapshot => _snapshot;
        public ulong LastAcceptedSequence => _acceptedSnapshot.Sequence;
        public double LastSampleTime => _order.LastNow;
        public event Action<uint> Completed;

        /****************************************
        *              Unity Event
        ****************************************/
        protected override void OnViewAwake()
        {
            _appearTween  = Envelope(gameObject, () => _appear, value => _appear = value, Ease.OutCubic);
            _fallTween    = Envelope(gameObject, () => _fall, value => _fall = value, Ease.InCubic);
            _resolveTween = Envelope(gameObject, () => _resolve, value => _resolve = value, Ease.OutCubic);
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
            _fallTween?.Kill();
            _resolveTween?.Kill();
            _fadeTween?.Kill();
        }

        /****************************************
        *                Methods
        ****************************************/
        /// <summary> root와 모든 부모의 localScale이 1인 인스턴스에 재생한다. Sequence는 전달 창구 전체에서 단조 증가해야 한다 </summary>
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
            Accept(snapshot, now);
            uint playbackId = _playbackId;
            Tick(playbackId, now);
            return playbackId;
        }

        /// <summary> Tracking에서만 목표를 갱신할 수 있다. Locked 이후에는 외부 확정 상태만 진행한다 </summary>
        public bool Reconcile(uint playbackId, Snapshot snapshot, double now)
        {
            if (((((!Current(playbackId, now)) || (!ValidSnapshot(snapshot, now)))
                || (!_order.Accepts(snapshot.Sequence, snapshot.SnapshotAt, now)))
                || (!SameDrop(_snapshot, snapshot))) || (!ValidTransition(_snapshot, snapshot)))
            {
                return false;
            }
            _snapshot = snapshot;
            Accept(snapshot, now);
            Tick(playbackId, now);
            return true;
        }

        /// <summary> LockAt과 ResolveAt으로 불꽃 낙하만 보간한다. Resolved 입력이 없으면 예정 시각 뒤에도 플래시가 없다 </summary>
        public void Tick(uint playbackId, double now)
        {
            if (!Current(playbackId, now))
            {
                return;
            }
            if (!_unitHierarchy.IsUnchanged())
            {
                // 월드 반경이 달라진 표시가 판정 경계처럼 남지 않게 즉시 정리한다
                Stop();
                return;
            }
            _order.Tick(now);
            double age = now - _snapshot.AppearedAt;
            double phaseAge = now - _snapshot.PhaseStartedAt;
            bool resolved = _snapshot.Phase == EPhase.Resolved;
            bool expired = _snapshot.Phase == EPhase.Expired;
            if (((resolved) && (phaseAge >= _resolvedSeconds)) || ((expired) && (phaseAge >= _fadeSeconds)))
            {
                Complete(playbackId);
                return;
            }
            _appearTween.Goto(Normalized(age, _appearSeconds), false);
            double fallNow = expired ? _snapshot.PhaseStartedAt : now;
            _fallTween.Goto(_snapshot.Phase == EPhase.Tracking ? 0f
                : Normalized(fallNow - _snapshot.LockAt, _snapshot.ResolveAt - _snapshot.LockAt), false);
            _resolveTween.Goto(resolved ? Normalized(phaseAge, _resolvedSeconds) : 0f, false);
            _fadeTween.Goto(expired ? Normalized(phaseAge, _fadeSeconds) : 0f, false);
            transform.SetPositionAndRotation(_snapshot.WorldTarget, Quaternion.identity);
            float phase = Phase(age, _flowCycle);
            float height = _dropHeight * (1f - _fall);
            if (_snapshot.Phase == EPhase.Tracking)
            {
                height += Mathf.Sin(phase) * _hoverAmplitude;
            }
            _dropRoot.SetLocalPositionAndRotation(_dropPose.Position + Vector3.up * height, _dropPose.Rotation);
            _dropRoot.localScale = _dropPose.Scale * (_appear * (1f - _resolve) * (1f - _fade));
            // 메시 원본 반경은 1m이며 전달된 실제 BlastRadius를 정확한 경계에 적용한다
            _ground.transform.localScale = new Vector3(_groundPose.Scale.x * _snapshot.BlastRadius,
                _groundPose.Scale.y, _groundPose.Scale.z * _snapshot.BlastRadius);
            _ground.transform.SetLocalPositionAndRotation(_groundPose.Position, _groundPose.Rotation);
            float opacity = _appear * (1f - _fade);
            for (int i = 0; i < _flameStates.Length; ++i)
            {
                _flameStates[i].Paint(_color, opacity * _flameOpacity * (1f - _resolve), phase / TAU);
            }
            _groundState.Paint(_color, opacity * _groundOpacity * (1f - _resolve));
            Color flash = _color * _flashStrength;
            flash.a = _color.a;
            _flashState.Paint(flash, resolved ? opacity * (1f - _resolve) : 0f);
        }

        /// <summary> 현재 재생 세대만 취소한다. 이전 세대의 Stop은 새 재생에 영향을 주지 않는다 </summary>
        public bool Stop(uint playbackId)
        {
            if (((!_running) || (playbackId == 0)) || (playbackId != _playbackId))
            {
                return false;
            }
            Stop();
            return true;
        }

        /// <summary> 풀 반환용 정리. 포즈와 원본 속성 블록을 복원하고 수락한 입력 순서는 유지한다 </summary>
        [ContextMenu("Stop")]
        public void Stop()
        {
            _running = false;
            _snapshot = default;
            _appearTween?.Goto(0f, false);
            _fallTween?.Goto(0f, false);
            _resolveTween?.Goto(0f, false);
            _fadeTween?.Goto(0f, false);
            if (_posesCached)
            {
                _rootPose.Restore(transform);
                _dropPose.Restore(_cachedDropRoot);
                RestorePose(_groundPose, _groundState.Target);
                RestorePose(_flashPose, _flashState.Target);
                for (int i = 0; i < _flameStates.Length; ++i)
                {
                    RestorePose(_flamePoses[i], _flameStates[i].Target);
                    _flameStates[i].Restore();
                }
                _groundState.Restore();
                _flashState.Restore();
                return;
            }
            Hide(_ground);
            Hide(_flash);
            if (_flame != null)
            {
                for (int i = 0; i < _flame.Length; ++i)
                {
                    Hide(_flame[i]);
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
            _dropPose   = new Pose(_dropRoot);
            _groundPose = new Pose(_ground.transform);
            _flashPose  = new Pose(_flash.transform);
            _cachedDropRoot = _dropRoot;
            _unitHierarchy = new UnitHierarchy(transform);
            _groundState = new RendererState(_ground);
            _flashState  = new RendererState(_flash);
            _flameStates = new RendererState[_flame.Length];
            _flamePoses  = new Pose[_flame.Length];
            for (int i = 0; i < _flame.Length; ++i)
            {
                _flameStates[i] = new RendererState(_flame[i]);
                _flamePoses[i]  = new Pose(_flame[i].transform);
            }
            _posesCached = true;
        }

        private void CacheSettings()
        {
            _color           = _settings.DefaultColor;
            _appearSeconds   = _settings.AppearSeconds;
            _resolvedSeconds = _settings.ResolvedSeconds;
            _fadeSeconds     = _settings.FadeSeconds;
            _dropHeight      = _settings.DropHeight;
            _hoverAmplitude  = _settings.HoverAmplitude;
            _flowCycle       = _settings.FlowCycle;
            _flameOpacity    = _settings.FlameOpacity;
            _groundOpacity   = _settings.GroundOpacity;
            _flashStrength   = _settings.FlashStrength;
        }

        private bool Ready(double now)
        {
            return (((((((!IsServerBuild) && (isActiveAndEnabled)) && (_appearTween != null)) && (_settings != null))
                && (Finite(now))) && (ValidSettings())) && (ValidRig()))
                && (_posesCached ? _unitHierarchy.IsUnchanged() : UnitHierarchy.IsUnit(transform));
        }

        private bool Current(uint playbackId, double now)
        {
            return ((((((!IsServerBuild) && (_running)) && (isActiveAndEnabled)) && (playbackId != 0))
                && (playbackId == _playbackId)) && (Finite(now))) && (now >= _order.LastNow);
        }

        private bool ValidSettings()
        {
            return (((((((((Valid(_settings.DefaultColor)) && (Positive(_settings.AppearSeconds)))
                && (Positive(_settings.ResolvedSeconds))) && (Positive(_settings.FadeSeconds)))
                && (NonNegative(_settings.DropHeight))) && (NonNegative(_settings.HoverAmplitude)))
                && (Positive(_settings.FlowCycle))) && (Unit(_settings.FlameOpacity)))
                && (Unit(_settings.GroundOpacity))) && (NonNegative(_settings.FlashStrength));
        }

        private bool ValidRig()
        {
            bool validatePose = !_posesCached;
            if ((((((((_dropRoot == null) || (_dropRoot.parent != transform)) || (_flame == null)) || (_flame.Length == 0))
                || (!ValidRenderer(_ground, transform, validatePose))) || (!ValidRenderer(_flash, transform, validatePose)))
                || (_ground == _flash)) || ((validatePose) && ((!ValidPose(transform)) || (!ValidPose(_dropRoot)))))
            {
                return false;
            }
            if ((_posesCached) && ((((_cachedDropRoot != _dropRoot) || (_flameStates.Length != _flame.Length))
                || (_groundState.Target != _ground)) || (_flashState.Target != _flash)))
            {
                return false;
            }
            if ((validatePose) && (((((_ground.transform.localScale.x != 1f) || (_ground.transform.localScale.z != 1f))
                || (_ground.transform.localPosition.x != 0f)) || (_ground.transform.localPosition.z != 0f))
                || ((Vector3.up - _ground.transform.localRotation * Vector3.up).sqrMagnitude > MIN_SIZE * MIN_SIZE)))
            {
                return false;
            }
            for (int i = 0; i < _flame.Length; ++i)
            {
                if ((!ValidRenderer(_flame[i], _dropRoot, validatePose))
                    || ((_posesCached) && (_flameStates[i].Target != _flame[i])))
                {
                    return false;
                }
                for (int previous = 0; previous < i; ++previous)
                {
                    if (_flame[previous] == _flame[i])
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
            if ((!_running) && (snapshot.DropIndex != _acceptedSnapshot.DropIndex))
            {
                return snapshot.AppearedAt >= _acceptedSnapshot.AppearedAt;
            }
            return (SameDrop(_acceptedSnapshot, snapshot)) && (ValidTransition(_acceptedSnapshot, snapshot));
        }

        private void Accept(Snapshot snapshot, double now)
        {
            _acceptedSnapshot = snapshot;
            _hasAcceptedSnapshot = true;
            _order.Accept(snapshot.Sequence, snapshot.SnapshotAt, now);
        }

        private static bool SameDrop(Snapshot previous, Snapshot next)
        {
            return ((((((previous.CastId == next.CastId) && (previous.TurnIndex == next.TurnIndex))
                && (previous.DropIndex == next.DropIndex)) && (previous.BlastRadius == next.BlastRadius))
                && (previous.AppearedAt == next.AppearedAt)) && (previous.LockAt == next.LockAt))
                && (previous.ResolveAt == next.ResolveAt);
        }

        private static bool ValidTransition(Snapshot previous, Snapshot next)
        {
            if (((previous.Phase == EPhase.Resolved) || (previous.Phase == EPhase.Expired))
                && (next.Phase != previous.Phase))
            {
                return false;
            }
            return (((next.Phase >= previous.Phase) && (next.PhaseStartedAt >= previous.PhaseStartedAt))
                && ((next.Phase != previous.Phase) || (next.PhaseStartedAt == previous.PhaseStartedAt)))
                && ((previous.Phase == EPhase.Tracking) || (previous.WorldTarget.Equals(next.WorldTarget)));
        }

        private static bool ValidSnapshot(Snapshot snapshot, double now)
        {
            return (((((((((((((((((((((snapshot.CastId != 0) && (snapshot.Sequence != 0)) && (snapshot.TurnIndex >= 0))
                && (snapshot.DropIndex >= 0)) && (Valid(snapshot.WorldTarget))) && (Positive(snapshot.BlastRadius)))
                && (Finite(snapshot.AppearedAt))) && (Finite(snapshot.LockAt))) && (Finite(snapshot.ResolveAt)))
                && (Finite(snapshot.SnapshotAt))) && (Finite(snapshot.PhaseStartedAt))) && (snapshot.AppearedAt >= 0d))
                && (snapshot.AppearedAt <= snapshot.LockAt)) && (snapshot.LockAt < snapshot.ResolveAt))
                && (snapshot.AppearedAt <= snapshot.PhaseStartedAt)) && (snapshot.PhaseStartedAt <= snapshot.SnapshotAt))
                && (snapshot.SnapshotAt <= now)) && (snapshot.Phase >= EPhase.Tracking)) && (snapshot.Phase <= EPhase.Expired))
                && ((snapshot.Phase != EPhase.Tracking) || (snapshot.PhaseStartedAt == snapshot.AppearedAt)))
                && ((snapshot.Phase != EPhase.Locked) || (snapshot.PhaseStartedAt == snapshot.LockAt)))
                && ((snapshot.Phase != EPhase.Resolved) || (snapshot.PhaseStartedAt >= snapshot.ResolveAt));
        }

        private void Complete(uint playbackId)
        {
            Stop();
            Completed?.Invoke(playbackId);
        }
    }
}
