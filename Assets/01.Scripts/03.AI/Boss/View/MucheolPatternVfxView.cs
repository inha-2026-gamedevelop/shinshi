// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 프리뷰(Dev_MucheolMotionVfxPreview)에서 확정한 무철 일반 패턴 VFX를 서버 사건으로 재생한다. 사건을 받아 부르는 일은 네트워크 수신부가 한다.
    // 위치와 방향은 서버 사건의 원점과 조준 방향, 시각은 서버 시각이다. 타격은 서버 확정 사건(OnStruck, 단계 타격, 전장 타격, 돌진 정지)이 와야만 터진다.
    // 단, 돌진 정지 사건에는 시각이 없어 끝 타격의 기준 시각은 서버 이동 모델로 계산한 추정값이다(StopRun).
    // 확정이 예정보다 늦으면 예고 끝 자세로 기다리고, 끝내 오지 않으면 예고를 거둔다. 로컬 타이머로 타격을 만들지 않는다.
    // 늦게 받은 사건과 늦은 접속 복원은 아직 터지지 않은 예고만 이어 그리고, 이미 지난 일회성 타격은 다시 만들지 않는다.
    // 효과는 보스를 따라 움직이지 않는 별도 루트 아래 둔다. 원본 프리팹과 프로필은 읽기만 한다
    /// <summary> 무철 일반 패턴 VFX 표현, 서버 시전 사건을 원본 VFX 재생기의 외부 시계 입력으로 바꾼다 </summary>
    [DisallowMultipleComponent]
    public sealed class MucheolPatternVfxView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    TEMPLATE_COUNT             = 7;
        private const int    MAX_INSTANCES_PER_TEMPLATE = 6;     // 한 원본을 동시에 쓰는 효과 수 상한, 꼬리가 겹치는 연타까지 덮는다
        private const int    ENDED_CAST_HISTORY         = 32;
        private const int    PLAYED_HISTORY             = 64;
        private const float  CONFIRM_GRACE_SECONDS      = 0.5f;  // 초, 예정 타격 뒤 서버 확정을 더 기다리는 시간, 기본 예고와 같다
        private const float  DASH_START_GRACE_SECONDS   = 2f;    // 초, 예고 끝 뒤 돌진 시작 사건을 더 기다리는 시간
        private const float  EARLY_STOP_EPSILON         = 0.05f; // m, 이보다 짧게 멈춰야 일찍 멈춘 돌진으로 본다
        private const float  MIN_DIRECTION_SQR          = 0.0001f;
        private const string CONTAINER_NAME             = "MucheolPatternVfx";

        /****************************************
        *              Nested Types
        ****************************************/

        [Serializable]
        private sealed class Template
        {
            [SerializeField] private GameObject                  _prefab;  // 02.Prefab/VFX/Mucheol의 원본, 읽기만 한다
            [SerializeField] private MucheolPatternVfxSettingsSO _profile; // 재생기 자체 프로필과 같은 에셋, 개별 타격 원본(01)의 예고 길이를 읽는다

            public GameObject                  Prefab  => _prefab;
            public MucheolPatternVfxSettingsSO Profile => _profile;
        }

        private sealed class Slot
        {
            public int                    CastId;
            public int                    Index;
            public MucheolVfxCue          Cue;
            public bool                   IsDash;
            public GameObject             Root;
            public MucheolBatchVfxPlayer  Batch;
            public MucheolImpactVfxPlayer Impact;
            public float                  Telegraph;
            public float                  Charge;
            public float                  Tail;
            public float                  YawDegrees;
            public double                 Start;
            public double                 TelegraphEnd;
            public double                 ImpactTime;
            public double                 HitTime;
            public double                 Deadline;
            public Vector3                Origin;
            public Vector3                Direction;
            public float                  BackShift;
            public double                 DashStart;
            public float                  DashSpeed;
            public double                 DashHold;
            public bool                   IsHitEstimated;
            public bool                   IsBegun;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Template[] _templates = new Template[TEMPLATE_COUNT]; // EMucheolVfxTemplate 순서

        private readonly List<Slot>              _slots        = new List<Slot>();
        private readonly List<GameObject>[]      _idle         = new List<GameObject>[TEMPLATE_COUNT];
        private readonly int[]                   _created      = new int[TEMPLATE_COUNT];
        private readonly BossEndedIdHistory      _endedCasts   = new BossEndedIdHistory(ENDED_CAST_HISTORY);
        private readonly HashSet<long>           _played       = new HashSet<long>();
        private readonly Queue<long>             _playedOrder  = new Queue<long>();
        private readonly Dictionary<int, string> _castPatterns = new Dictionary<int, string>();

        private Transform    _container;
        private Func<double> _serverClock;
        private bool         _isDefeated;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 그리거나 기다리는 효과 수, 시험과 진단용이다 </summary>
        public int ActiveCount => _slots.Count;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            for (int i = 0; i < TEMPLATE_COUNT; ++i)
            {
                _idle[i] = new List<GameObject>();
            }

            // 보스가 움직여도 바닥 효과는 서버가 알린 자리에 남아야 해서 보스 계층 밖에 둔다
            var container = new GameObject(CONTAINER_NAME);
            SceneManager.MoveGameObjectToScene(container, gameObject.scene);
            _container = container.transform;
        }

        private void Update()
        {
            if (_slots.Count == 0)
            {
                return;
            }

            double now = ReadClock();
            for (int i = _slots.Count - 1; i >= 0; --i)
            {
                TickSlot(_slots[i], now);
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnDestroy()
        {
            if (_container == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_container.gameObject);
            }
            else
            {
                DestroyImmediate(_container.gameObject);
            }

            _container = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이 표현이 VFX를 맡는 패턴이다 </summary>
        public static bool Handles(string patternId)
        {
            return MucheolVfxCatalog.Handles(patternId);
        }

        /// <summary> 서버 시각을 읽는 함수, 비우면 로컬 시각이다 </summary>
        public void SetServerClock(Func<double> serverClock)
        {
            _serverClock = serverClock;
        }

        // 이 표현이 바닥 예고까지 그리는 1페이즈 기본 패턴은 기본 예고 표현이 같은 자리를 또 그리지 않게 등록한다
        /// <summary> 기본 예고 표현에 외부 표현 패턴을 알린다 </summary>
        public void BindPresenter(BossTelegraphPresenter presenter)
        {
            if (presenter == null)
            {
                return;
            }

            foreach (string patternId in MucheolVfxCatalog.PatternIds)
            {
                if (MucheolVfxCatalog.TryGetSource(patternId, out EMucheolVfxSource source)
                    && ((source == EMucheolVfxSource.BasicStrike) || (source == EMucheolVfxSource.BasicDash))
                    && MucheolVfxCatalog.TryGetCue(patternId, 0, out MucheolVfxCue cue) && HasTemplate(cue.Template))
                {
                    presenter.RegisterExternalVisual(patternId);
                }
            }
        }

        // 1페이즈 단발(P01, P02, P04)과 돌진(P03, P06)의 기본 예고다
        /// <summary> 기본 예고 시작, 단발은 예정 타격 시각까지, 돌진은 돌진 시작까지 예고를 그린다 </summary>
        public void BeginBasic(in BossTelegraphStart start)
        {
            if ((!MucheolVfxCatalog.TryGetSource(start.PatternId, out EMucheolVfxSource source))
                || ((source != EMucheolVfxSource.BasicStrike) && (source != EMucheolVfxSource.BasicDash)))
            {
                return;
            }

            double telegraphEnd = start.StartServerTime + start.TelegraphSeconds;
            if (source == EMucheolVfxSource.BasicDash)
            {
                SpawnDash(start.CastId, 0, start.PatternId, start.Origin, start.Direction, start.StartServerTime, telegraphEnd);
                return;
            }

            SpawnStrike(start.CastId, 0, start.PatternId, start.Origin, start.Direction, start.StartServerTime, telegraphEnd,
                float.NaN);
        }

        /// <summary> 아직 터지지 않은 예고의 조준 방향을 바꾼다, 기본 예고와 순차 타격 모두 쓴다 </summary>
        public void UpdateAim(int castId, Vector3 direction)
        {
            for (int i = 0; i < _slots.Count; ++i)
            {
                Slot slot = _slots[i];
                if ((slot.CastId == castId) && double.IsNaN(slot.HitTime) && double.IsNaN(slot.DashStart))
                {
                    slot.Direction = direction;
                    Place(slot);
                }
            }
        }

        /// <summary> 기본 단발의 서버 타격 확정 </summary>
        public void ConfirmBasicStrike(int castId, double serverTime)
        {
            Slot slot = Find(castId, 0);
            if ((slot != null) && (!slot.IsDash))
            {
                Confirm(slot, serverTime);
            }
        }

        /// <summary> 기본 돌진 시작, 예고를 멈추고 돌진 머리를 서버 돌진 시간에 맞춰 움직인다 </summary>
        public void BeginBasicDash(int castId, float distance, float seconds, float speed, double startServerTime)
        {
            BeginRun(castId, 0, distance, seconds, speed, startServerTime);
        }

        /// <summary> 기본 돌진 정지, 멈춘 자리에서 끝 타격을 낸다 </summary>
        public void StopBasicDash(int castId, Vector3 position)
        {
            StopRun(castId, 0, position);
        }

        // 발동 뒤에 들어왔으면 단발은 이미 터졌으므로 만들지 않는다. 돌진은 아직 달리는 중일 때만 이어 그린다
        /// <summary> 기본 예고 복원 </summary>
        public void RestoreBasic(in BossTelegraphSnapshot snapshot)
        {
            BossTelegraphStart start = snapshot.Start;
            if (!MucheolVfxCatalog.TryGetSource(start.PatternId, out EMucheolVfxSource source))
            {
                return;
            }

            if (snapshot.Phase == EBossTelegraphPhase.Telegraph)
            {
                BeginBasic(in start);
                UpdateAim(start.CastId, snapshot.Direction);
                return;
            }

            bool isRunning = (snapshot.Phase == EBossTelegraphPhase.Execute) && (source == EMucheolVfxSource.BasicDash)
                && (snapshot.DashStoppedDistance <= 0f)
                && (ReadClock() < snapshot.ExecuteServerTime + snapshot.DashSeconds);
            if (!isRunning)
            {
                Remember(Key(start.CastId, 0));
                return;
            }

            BeginBasic(in start);
            UpdateAim(start.CastId, snapshot.Direction);
            BeginRun(start.CastId, 0, snapshot.DashDistance, snapshot.DashSeconds, snapshot.DashSpeed, snapshot.ExecuteServerTime);
        }

        // firstStep 앞의 단계는 이미 터졌다(복원의 StruckCount)
        /// <summary> 콤보와 순차 타격 시작, 단계마다 앞 단계 타격부터 자기 타격까지 예고를 그린다 </summary>
        public void BeginStrikeChain(in BossStrikeChainStart start, int firstStep = 0)
        {
            if ((!MucheolVfxCatalog.TryGetSource(start.PatternId, out EMucheolVfxSource source))
                || (source != EMucheolVfxSource.StrikeChain))
            {
                return;
            }

            IReadOnlyList<float> delays  = start.DelaySeconds;
            IReadOnlyList<float> offsets = start.AimOffsetDegrees;
            double execute = start.StartServerTime + start.TelegraphSeconds;
            for (int i = Math.Max(0, firstStep); i < start.StepCount; ++i)
            {
                if ((delays == null) || (i >= delays.Count))
                {
                    break;
                }

                double impact    = execute + delays[i];
                double stepStart = (i == 0) ? start.StartServerTime : execute + delays[i - 1];
                float  yaw       = ((offsets != null) && (i < offsets.Count)) ? offsets[i] : float.NaN;
                SpawnStrike(start.CastId, i, start.PatternId, start.Origin, start.Direction, stepStart, impact, yaw);
            }
        }

        /// <summary> 단계 타격의 서버 확정 </summary>
        public void ConfirmStrikeChainHit(int castId, int stepIndex, double serverTime)
        {
            Slot slot = Find(castId, stepIndex);
            if (slot != null)
            {
                Confirm(slot, serverTime);
            }
        }

        /// <summary> 콤보와 순차 타격 복원, 이미 터진 단계는 만들지 않는다 </summary>
        public void RestoreStrikeChain(in BossStrikeChainSnapshot snapshot)
        {
            BossStrikeChainStart start = snapshot.Start;
            for (int i = 0; i < snapshot.StruckCount; ++i)
            {
                Remember(Key(start.CastId, i));
            }

            BeginStrikeChain(in start, snapshot.StruckCount);
            UpdateAim(start.CastId, snapshot.Direction);
        }

        /// <summary> 연속 돌진 시작, 첫 돌진의 예고를 그린다 </summary>
        public void BeginMultiDash(in BossMultiDashStart start)
        {
            if ((!MucheolVfxCatalog.TryGetSource(start.PatternId, out EMucheolVfxSource source))
                || (source != EMucheolVfxSource.MultiDash))
            {
                return;
            }

            _castPatterns[start.CastId] = start.PatternId;
            SpawnDash(start.CastId, 0, start.PatternId, start.Origin, start.Direction, start.StartServerTime,
                start.StartServerTime + start.TelegraphSeconds);
        }

        /// <summary> 연속 돌진의 한 돌진 조준 방향 </summary>
        public void UpdateMultiDashAim(int castId, int dashIndex, Vector3 direction)
        {
            Slot slot = Find(castId, dashIndex);
            if ((slot != null) && double.IsNaN(slot.HitTime) && double.IsNaN(slot.DashStart))
            {
                slot.Direction = direction;
                Place(slot);
            }
        }

        /// <summary> 다음 돌진의 재조준 예고, 서버가 알린 새 시작점과 방향에 놓는다 </summary>
        public void BeginMultiDashReaim(in BossMultiDashReaim reaim)
        {
            if (!_castPatterns.TryGetValue(reaim.CastId, out string patternId))
            {
                return;
            }

            SpawnDash(reaim.CastId, reaim.DashIndex, patternId, reaim.Origin, reaim.Direction, reaim.StartServerTime,
                reaim.StartServerTime + reaim.ReaimSeconds);
        }

        public void BeginMultiDashRun(int castId, int dashIndex, float distance, float seconds, float speed, double startServerTime)
        {
            BeginRun(castId, dashIndex, distance, seconds, speed, startServerTime);
        }

        public void StopMultiDashRun(int castId, int dashIndex, Vector3 position)
        {
            StopRun(castId, dashIndex, position);
        }

        // 남은 돌진을 하지 않는다. 이미 낸 타격은 끝까지 두고 기다리던 예고만 거둔다
        /// <summary> 연속 돌진이 계획보다 일찍 끝났다 </summary>
        public void EndMultiDashEarly(int castId)
        {
            ReleasePending(castId);
        }

        /// <summary> 연속 돌진 복원, 지금 돌진 하나만 아직 끝나지 않았을 때 이어 그린다 </summary>
        public void RestoreMultiDash(in BossMultiDashSnapshot snapshot)
        {
            BossMultiDashStart start = snapshot.Start;
            if ((!MucheolVfxCatalog.TryGetSource(start.PatternId, out EMucheolVfxSource source))
                || (source != EMucheolVfxSource.MultiDash) || snapshot.IsTruncated)
            {
                return;
            }

            _castPatterns[start.CastId] = start.PatternId;
            for (int i = 0; i < snapshot.DashIndex; ++i)
            {
                Remember(Key(start.CastId, i));
            }

            if (snapshot.HasDashStarted
                && ((snapshot.StoppedDistance > 0f) || (ReadClock() >= snapshot.DashStartServerTime + snapshot.DashSeconds)))
            {
                Remember(Key(start.CastId, snapshot.DashIndex));
                return;
            }

            SpawnDash(start.CastId, snapshot.DashIndex, start.PatternId, snapshot.Origin, snapshot.Direction,
                snapshot.TelegraphStartServerTime, snapshot.TelegraphStartServerTime + snapshot.TelegraphSeconds);
            if (snapshot.HasDashStarted)
            {
                BeginRun(start.CastId, snapshot.DashIndex, snapshot.DashDistance, snapshot.DashSeconds, snapshot.DashSpeed,
                    snapshot.DashStartServerTime);
            }
        }

        /// <summary> 전장 공격 시작 </summary>
        public void BeginBattlefield(in BossBattlefieldStart start)
        {
            SpawnStrike(start.CastId, 0, MucheolMotionCatalog.P2_P07_ID, start.Origin, start.Direction, start.StartServerTime,
                start.StartServerTime + start.TelegraphSeconds, float.NaN);
        }

        public void ConfirmBattlefield(int castId, double serverTime)
        {
            Slot slot = Find(castId, 0);
            if (slot != null)
            {
                Confirm(slot, serverTime);
            }
        }

        /// <summary> 전장 공격 복원, 이미 터졌으면 만들지 않는다 </summary>
        public void RestoreBattlefield(in BossBattlefieldSnapshot snapshot)
        {
            BossBattlefieldStart start = snapshot.Start;
            if (snapshot.IsStruck)
            {
                Remember(Key(start.CastId, 0));
                return;
            }

            BeginBattlefield(in start);
        }

        // 정상 종료다. 확정을 받지 못한 예고는 거둔다. 돌진 정지 사건이 오지 않았으면 끝 타격을 만들지 않는다
        /// <summary> 시전 정상 종료, 이미 낸 타격은 끝까지 둔다 </summary>
        public void FinishCast(int castId)
        {
            ReleasePending(castId);
            EndCast(castId);
        }

        /// <summary> 시전 취소, 기다리던 예고를 거두고 이미 낸 타격은 끝까지 둔다 </summary>
        public void CancelCast(int castId)
        {
            ReleasePending(castId);
            EndCast(castId);
        }

        // 사망 순간 남은 효과를 모두 지운다. 사망 중에 온 늦은 시작은 받지 않는다
        /// <summary> 보스 사망 상태, 바뀔 때만 처리한다 </summary>
        public void SetDefeated(bool isDefeated)
        {
            if (_isDefeated == isDefeated)
            {
                return;
            }

            _isDefeated = isDefeated;
            if (isDefeated)
            {
                Clear();
            }
        }

        /// <summary> 연결이 바뀔 때 효과와 끝난 시전 기록을 모두 비운다 </summary>
        public void ResetSession()
        {
            Clear();
            _endedCasts.Clear();
            _played.Clear();
            _playedOrder.Clear();
            _castPatterns.Clear();
            _isDefeated = false;
        }

        /// <summary> 모든 효과를 멈추고 풀에 돌려준다 </summary>
        public void Clear()
        {
            for (int i = _slots.Count - 1; i >= 0; --i)
            {
                Release(_slots[i]);
            }
        }

        private void SpawnStrike(int castId, int index, string patternId, Vector3 origin, Vector3 direction, double start,
            double impact, float serverYaw)
        {
            if (!MucheolVfxCatalog.TryGetCue(patternId, index, out MucheolVfxCue cue))
            {
                return;
            }

            // 확정을 기다릴 시간까지 지난 예고는 그리지 않는다, 기본 예고 표현과 같은 기준이다
            if (ReadClock() >= impact + CONFIRM_GRACE_SECONDS)
            {
                return;
            }

            Slot slot = Spawn(castId, index, cue, origin, direction, float.IsNaN(serverYaw) ? cue.YawDegrees : serverYaw);
            if (slot == null)
            {
                return;
            }

            slot.Start        = start;
            slot.TelegraphEnd = impact;
            slot.ImpactTime   = impact;
            slot.Deadline     = impact + CONFIRM_GRACE_SECONDS;
        }

        private void SpawnDash(int castId, int index, string patternId, Vector3 origin, Vector3 direction, double start,
            double telegraphEnd)
        {
            if (!MucheolVfxCatalog.TryGetCue(patternId, index, out MucheolVfxCue cue))
            {
                return;
            }

            if (ReadClock() >= telegraphEnd + DASH_START_GRACE_SECONDS)
            {
                return;
            }

            Slot slot = Spawn(castId, index, cue, origin, direction, 0f);
            if (slot == null)
            {
                return;
            }

            slot.IsDash       = true;
            slot.Start        = start;
            slot.TelegraphEnd = telegraphEnd;
            slot.Deadline     = telegraphEnd + DASH_START_GRACE_SECONDS;
        }

        // 같은 (시전, 순번)이 이미 있거나 이미 터졌으면 새로 만들지 않는다. 복원과 늦은 사건이 같은 효과를 두 번 내지 않게 한다
        private Slot Spawn(int castId, int index, MucheolVfxCue cue, Vector3 origin, Vector3 direction, float yaw)
        {
            if (_isDefeated || (_container == null) || _endedCasts.Contains(castId)
                || (Find(castId, index) != null) || _played.Contains(Key(castId, index)))
            {
                return null;
            }

            int template = (int)cue.Template;
            GameObject root = Acquire(template);
            if (root == null)
            {
                return null;
            }

            var slot = new Slot
            {
                CastId     = castId,
                Index      = index,
                Cue        = cue,
                Root       = root,
                Batch      = root.GetComponent<MucheolBatchVfxPlayer>(),
                Impact     = root.GetComponentInChildren<MucheolImpactVfxPlayer>(true),
                YawDegrees = yaw,
                Origin     = origin,
                Direction  = direction,
                ImpactTime = double.NaN,
                HitTime    = double.NaN,
                DashStart  = double.NaN,
                DashHold   = double.NaN,
            };

            MucheolPatternVfxSettingsSO profile = (slot.Batch != null) ? slot.Batch.Profile : _templates[template].Profile;
            if ((profile == null) || (slot.Impact == null))
            {
                ReturnToPool(template, root);
                return null;
            }

            slot.Telegraph = Mathf.Max(0f, profile.TelegraphSeconds);
            slot.Charge    = (slot.Batch != null) ? slot.Batch.ChargeSeconds : 0f;
            slot.Tail      = Mathf.Max(profile.ExpansionSeconds + profile.HoldSeconds + profile.FadeSeconds, profile.DebrisSeconds);
            Place(slot);
            _slots.Add(slot);
            return slot;
        }

        // 서버 돌진은 시작 시각부터 속도 = 정의 거리 / 돌진 시간으로 일정하게 나아간다(서버 DistanceAt).
        // distance는 서버가 시작 순간 잰 막힘 없는 거리라, 정적인 벽이면 그 지점에서 멈출 것을 미리 안다. 그 시각까지만 머리를 진행시켜
        // 정지 사건이 늦게 와도 머리가 벽을 지나가지 않게 한다. 머리 진행 비율 자체는 정의 거리 기준이라 보스 위치와 같다
        private void BeginRun(int castId, int index, float distance, float seconds, float speed, double startServerTime)
        {
            Slot slot = Find(castId, index);
            if ((slot == null) || (!slot.IsDash) || (!double.IsNaN(slot.HitTime)))
            {
                return;
            }

            slot.DashStart    = startServerTime;
            slot.DashSpeed    = speed;
            slot.TelegraphEnd = startServerTime;
            slot.ImpactTime   = startServerTime + seconds;
            slot.DashHold     = ((speed > 0f) && (distance >= 0f))
                ? Math.Min(slot.ImpactTime, startServerTime + (distance / speed)) : slot.ImpactTime;
            slot.Deadline     = slot.ImpactTime + CONFIRM_GRACE_SECONDS;
        }

        // 정지 사건에는 서버 시각이 없다. 끝 타격 시각은 서버 이동 모델로 거꾸로 계산한 추정값이다:
        //   추정 정지 시각 = 돌진 시작 시각 + (시작점에서 정지 위치까지 조준 방향 거리 / 서버 속도)
        // 서버는 이 시각 이후 첫 틱에서 정지를 감지하므로 실제 감지 틱보다 최대 한 틱 이르다. 정의 거리를 다 갔으면 예정 끝 시각과 같다.
        // 일찍 멈추면 경로 끝의 타격 자리를 멈춘 곳으로 당긴다. 예고는 타격과 함께 사라지므로 경로 모양은 남지 않는다
        private void StopRun(int castId, int index, Vector3 position)
        {
            Slot slot = Find(castId, index);
            if ((slot == null) || (!slot.IsDash) || double.IsNaN(slot.DashStart))
            {
                return;
            }

            Vector3 forward = Flatten(slot.Direction);
            Vector3 travel  = position - slot.Origin;
            travel.y = 0f;
            float traveled   = Mathf.Max(0f, Vector3.Dot(travel, forward));
            float range      = (float)(slot.DashSpeed * (slot.ImpactTime - slot.DashStart));
            float pathLength = MucheolVfxCatalog.DASH_PATH_LENGTH * slot.Cue.Scale.z;
            double estimate = slot.ImpactTime;
            if ((slot.DashSpeed > 0f) && (traveled < range - EARLY_STOP_EPSILON))
            {
                estimate = slot.DashStart + (traveled / slot.DashSpeed);
                slot.BackShift = Mathf.Max(0f, pathLength - traveled);
                Place(slot);
            }

            slot.IsHitEstimated = true;
            Confirm(slot, Math.Min(estimate, ReadClock()));
        }

        private void Confirm(Slot slot, double hitTime)
        {
            if (!double.IsNaN(slot.HitTime))
            {
                return;
            }

            double now = ReadClock();
            if (MucheolVfxTimeline.IsImpactOver(now, hitTime, slot.Tail))
            {
                Release(slot);
                return;
            }

            if (!slot.IsBegun)
            {
                BeginPlayer(slot, now);
            }

            slot.HitTime = hitTime;
            float profileTime = ProfileTime(slot, now);
            float impact = MucheolVfxTimeline.ProfileImpact(slot.Telegraph, slot.Charge);
            if (slot.Batch != null)
            {
                slot.Batch.ConfirmImpact(impact, profileTime);
            }
            else
            {
                slot.Impact.ConfirmImpact(impact, profileTime);
            }

            Remember(Key(slot.CastId, slot.Index));
        }

        private void TickSlot(Slot slot, double now)
        {
            bool isPending = double.IsNaN(slot.HitTime);
            if (isPending && (now > slot.Deadline))
            {
                Release(slot);
                return;
            }

            if (!slot.IsBegun)
            {
                if (now < slot.Start)
                {
                    return;
                }

                BeginPlayer(slot, now);
            }

            float profileTime = ProfileTime(slot, now);
            if (slot.Batch != null)
            {
                slot.Batch.Tick(profileTime);
            }
            else
            {
                slot.Impact.Tick(profileTime);
            }

            bool isRunning = (slot.Batch != null) ? slot.Batch.IsRunning : slot.Impact.IsRunning;
            if (!isRunning)
            {
                Release(slot);
            }
        }

        private void BeginPlayer(Slot slot, double now)
        {
            slot.IsBegun = true;
            slot.Root.SetActive(true);
            float impact = MucheolVfxTimeline.ProfileImpact(slot.Telegraph, slot.Charge);
            float profileTime = ProfileTime(slot, now);
            if (slot.Batch != null)
            {
                slot.Batch.BeginTelegraph(0d, impact, profileTime);
            }
            else
            {
                slot.Impact.BeginTelegraph(0d, impact, profileTime);
            }
        }

        private float ProfileTime(Slot slot, double now)
        {
            double sample = (double.IsNaN(slot.HitTime) && (!double.IsNaN(slot.DashHold))) ? Math.Min(now, slot.DashHold) : now;
            return MucheolVfxTimeline.ToProfileTime(sample, slot.Start, slot.TelegraphEnd, slot.ImpactTime, slot.HitTime,
                slot.Telegraph, slot.Charge);
        }

        private void Place(Slot slot)
        {
            Vector3 forward = Flatten(slot.Direction);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, slot.YawDegrees, 0f);
            Vector3 position = slot.Origin + (Vector3.up * MucheolVfxCatalog.GROUND_LIFT) - (forward * slot.BackShift);
            slot.Root.transform.SetPositionAndRotation(position, rotation);
            slot.Root.transform.localScale = slot.Cue.Scale;
        }

        private void ReleasePending(int castId)
        {
            for (int i = _slots.Count - 1; i >= 0; --i)
            {
                Slot slot = _slots[i];
                if ((slot.CastId == castId) && double.IsNaN(slot.HitTime))
                {
                    Release(slot);
                }
            }
        }

        private void EndCast(int castId)
        {
            _endedCasts.Remember(castId);
            _castPatterns.Remove(castId);
        }

        private void Release(Slot slot)
        {
            _slots.Remove(slot);
            if (slot.Batch != null)
            {
                slot.Batch.Stop();
            }
            else if (slot.Impact != null)
            {
                slot.Impact.Stop();
            }

            ReturnToPool((int)slot.Cue.Template, slot.Root);
        }

        private Slot Find(int castId, int index)
        {
            for (int i = 0; i < _slots.Count; ++i)
            {
                if ((_slots[i].CastId == castId) && (_slots[i].Index == index))
                {
                    return _slots[i];
                }
            }

            return null;
        }

        private bool HasTemplate(EMucheolVfxTemplate template)
        {
            int index = (int)template;
            return (_templates != null) && (index < _templates.Length) && (_templates[index] != null)
                && (_templates[index].Prefab != null);
        }

        private GameObject Acquire(int template)
        {
            List<GameObject> idle = _idle[template];
            if (idle.Count > 0)
            {
                GameObject reused = idle[idle.Count - 1];
                idle.RemoveAt(idle.Count - 1);
                return reused;
            }

            if ((!HasTemplate((EMucheolVfxTemplate)template)) || (_created[template] >= MAX_INSTANCES_PER_TEMPLATE))
            {
                return null;
            }

            GameObject created = Instantiate(_templates[template].Prefab, _container);
            created.SetActive(false);
            ++_created[template];
            return created;
        }

        private void ReturnToPool(int template, GameObject root)
        {
            if (root == null)
            {
                return;
            }

            root.SetActive(false);
            _idle[template].Add(root);
        }

        private void Remember(long key)
        {
            if (!_played.Add(key))
            {
                return;
            }

            _playedOrder.Enqueue(key);
            while (_playedOrder.Count > PLAYED_HISTORY)
            {
                _played.Remove(_playedOrder.Dequeue());
            }
        }

        private double ReadClock()
        {
            return (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? Vector3.forward : direction.normalized;
        }

        private static long Key(int castId, int index)
        {
            return ((long)castId << 32) | (uint)index;
        }
    }
}
