// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.AI
{
    // 프리뷰(Dev_MucheolMotionVfxPreview)에서 확정한 무철 일반 패턴 VFX를 서버 사건으로 재생한다. 사건을 받아 부르는 일은 네트워크 수신부가 한다.
    // 위치와 방향은 서버 사건의 원점과 조준 방향, 시각은 서버 시각이다. 타격은 서버 확정 사건(OnStruck, 단계 타격, 전장 타격, 돌진 정지)이 와야만 터진다.
    // 돌진 끝 타격은 정지 사건의 서버 정지 시각에 맞춘다. 시각이 없는 사건(NaN)만 서버 이동 모델로 계산한 추정값을 쓴다(StopRun).
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

        private const int    TEMPLATE_COUNT             = 8;
        private const int    MAX_INSTANCES_PER_TEMPLATE = 6;     // 한 원본을 동시에 쓰는 효과 수 상한, 꼬리가 겹치는 연타까지 덮는다
        private const int    ENDED_CAST_HISTORY         = 32;
        private const int    PLAYED_HISTORY             = 64;
        private const float  CONFIRM_GRACE_SECONDS      = 0.5f;  // 초, 예정 타격 뒤 서버 확정을 더 기다리는 시간, 기본 예고와 같다
        private const float  DASH_START_GRACE_SECONDS   = 2f;    // 초, 예고 끝 뒤 돌진 시작 사건을 더 기다리는 시간
        private const float  EARLY_STOP_EPSILON         = 0.05f; // m, 이보다 짧게 멈춰야 일찍 멈춘 돌진으로 본다
        private const float  MIN_DIRECTION_SQR          = 0.0001f;
        private const string CONTAINER_NAME             = "MucheolPatternVfx";
        private const int    MAX_RUSH_INSTANCES         = 3;     // 연속 돌진은 앞 돌진의 분출 꼬리가 다음 돌진과 겹친다
        private const string CHARGE_HEAD_NAME           = "ChargeHead";
        private const float  SWING_TRAIL_TAIL_SECONDS   = 1.3f;  // 초, 타격 뒤 스파크와 궤적이 사라질 때까지 손에 붙은 궤적을 켜 둔다
        private const float  SWING_FILL_MARGIN          = 0.001f; // 초, 확정 전에는 예고가 가득 차 번쩍이지 않게 남기는 여유
        private const float  SOUND_WHOOSH_LEAD_SECONDS  = 0.3f;  // 초, 휘두르는 소리가 타격보다 앞서 시작하는 시간
        private const float  SOUND_LATE_SECONDS         = 0.4f;  // 초, 이보다 늦게 받은 일회성 소리는 내지 않는다
        private const int    SOUND_KEY_STRIDE           = 16;    // 반복 소리 키 = 시전 번호 * 이 값 + 순번
        private const int    MAX_WAVE_IMPACTS           = 4;     // 원형 충격파 VFX 동시 수 상한, 작은 충격파와 큰 충격파가 이어진다
        private const int    MAX_FAN_IMPACTS            = 6;     // 부채꼴 착탄 VFX 동시 수 상한, 연타의 꼬리가 겹치는 만큼이다
        private const float  FAN_IMPACT_LATE_SECONDS    = 0.6f;  // 초, 확정이 이보다 늦게 알려진 착탄(복원, 늦은 사건)은 다시 그리지 않는다
        private const float  FAN_BASE_RADIUS            = 5f;    // m, 템플릿 부채꼴의 크기 1일 때 사거리
        private const float  FAN_BASE_HALF_TAN          = 1.7320508f; // 템플릿 부채꼴 폭 1은 120도, 반각 60도의 tan

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

        // 팔 휘두르기(P01) 한 번의 진행, 보스는 동시에 하나만 시전하므로 하나만 둔다
        private sealed class Swing
        {
            public int                  CastId;
            public int                  Index;
            public double               Start;
            public double               ImpactTime;
            public double               HitTime;
            public double               Deadline;
            public Vector3              Origin;
            public Vector3              Direction;
            public float                Yaw;
            public bool                 IsShown;
            public bool                 IsTelegraphDone;
            public MucheolFanTelegraph  Telegraph;
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
            public MucheolDashRushView    Rush;
            public Vector3                HitPoint;    // 타격 소리가 나는 자리, 돌진은 멈춘 자리다
            public float                  Radius;      // m, 서버가 알린 범위(전장 공격), 없으면 0
            public float                  Arc;         // 도, 서버가 알린 각도(전장 공격), 없으면 0
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Template[] _templates = new Template[TEMPLATE_COUNT]; // EMucheolVfxTemplate 순서

        // 비우면 돌진(06)은 원본의 달리는 머리를 그대로 쓴다. 채우면 달리는 구간만 이 분출로 바꾸고 예고와 끝 타격은 원본이 그린다
        [SerializeField] private GameObject _dashRushPrefab;

        // 팔 휘두르기(P01)는 땅 임팩트 대신 이 둘을 쓴다. 비우면 그 시전은 아무것도 그리지 않는다
        [SerializeField] private MucheolFanTelegraph _swingTelegraphPrefab; // 빨간 예고, 보스 계층 밖에 둔다
        [SerializeField] private MucheolArmTrail     _armTrailPrefab;       // 왼손 본에 붙는 궤적과 스파크

        // 모든 부채꼴 타격(팔 휘두르기, 연타 단계, 지면 강타)이 서버 확정 때 그 자리에서 터뜨리는 착탄 VFX. 비우면 터뜨리지 않는다
        [SerializeField] private MucheolFanImpactView _fanImpactPrefab;

        // 원형 충격파(작은 충격파, 큰 충격파)가 서버 확정 때 보스 둘레로 퍼뜨리는 고리 VFX. 큰 충격파는 360도 크레이터를 함께 터뜨린다. 비우면 터뜨리지 않는다
        [SerializeField] private MucheolWaveImpactView _waveImpactPrefab;

        [Header("효과음")]
        [SerializeField] private MucheolSoundPlayer _sound; // 비어 있으면 같은 보스 프리팹에서 찾고, 없으면 소리를 생략한다

        private readonly List<Slot>              _slots        = new List<Slot>();
        private readonly List<GameObject>[]      _idle         = new List<GameObject>[TEMPLATE_COUNT];
        private readonly int[]                   _created      = new int[TEMPLATE_COUNT];
        private readonly BossEndedIdHistory      _endedCasts   = new BossEndedIdHistory(ENDED_CAST_HISTORY);
        private readonly HashSet<long>           _played       = new HashSet<long>();
        private readonly Queue<long>             _playedOrder  = new Queue<long>();
        private readonly Dictionary<int, string> _castPatterns = new Dictionary<int, string>();

        private readonly List<MucheolDashRushView> _rushes = new List<MucheolDashRushView>();
        private readonly List<MucheolFanImpactView> _fanImpacts = new List<MucheolFanImpactView>();
        private readonly List<MucheolWaveImpactView> _waveImpacts = new List<MucheolWaveImpactView>();

        private Transform          _container;
        private Func<double>       _serverClock;
        private bool               _isDefeated;
        private Swing              _swing;
        private MucheolFanTelegraph _swingTelegraph;
        private readonly List<Swing>              _chainSteps = new List<Swing>();             // 연타의 아직 끝나지 않은 단계 예고
        private readonly List<MucheolFanTelegraph> _chainPool  = new List<MucheolFanTelegraph>(); // 단계 번호별 예고, 단계가 바뀌어도 다시 쓴다
        private MucheolArmTrail     _armTrail;        // 왼손, 팔 휘두르기(P01)와 연타가 함께 쓴다
        private MucheolArmTrail     _armTrailRight;   // 오른손, 연타만 쓴다
        private int                 _chainTrailCastId;
        private double              _chainTrailEnd;
        private bool                _hasChainTrail;
        private bool                _isRestoring;     // 복원 중에는 이미 지난 일회성 소리를 다시 내지 않는다

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

            if (_sound == null)
            {
                _sound = GetComponentInParent<MucheolSoundPlayer>();
            }
        }

        private void Update()
        {
            if ((_slots.Count == 0) && (_swing == null) && (!_hasChainTrail) && (_chainSteps.Count == 0))
            {
                return;
            }

            double now = ReadClock();
            TickSwing(now);
            TickChainSteps(now);
            if (_hasChainTrail && (now > _chainTrailEnd))
            {
                EndChainTrails();
            }

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
                if (!MucheolVfxCatalog.TryGetSource(patternId, out EMucheolVfxSource source))
                {
                    continue;
                }

                bool isSwing = (source == EMucheolVfxSource.ArmSwing) && (_swingTelegraphPrefab != null);
                bool isBasic = ((source == EMucheolVfxSource.BasicStrike) || (source == EMucheolVfxSource.BasicDash))
                    && MucheolVfxCatalog.TryGetCue(patternId, 0, out MucheolVfxCue cue) && HasTemplate(cue.Template);
                if (isSwing || isBasic)
                {
                    presenter.RegisterExternalVisual(patternId);

                    // 하드 정의(mucheol_hard_...)도 같은 표현이 그리므로 기본 예고가 같은 자리를 또 그리지 않게 함께 등록한다
                    string hardId = BossPatternCatalogSO.ToMucheolHardId(patternId);
                    if (hardId != null)
                    {
                        presenter.RegisterExternalVisual(hardId);
                    }
                }
            }
        }

        // 1페이즈 단발(P01, P02, P04)과 돌진(P03, P06)의 기본 예고다
        /// <summary> 기본 예고 시작, 단발은 예정 타격 시각까지, 돌진은 돌진 시작까지 예고를 그린다 </summary>
        public void BeginBasic(in BossTelegraphStart start)
        {
            if ((!MucheolVfxCatalog.TryGetSource(start.PatternId, out EMucheolVfxSource source))
                || ((source != EMucheolVfxSource.BasicStrike) && (source != EMucheolVfxSource.BasicDash)
                    && (source != EMucheolVfxSource.ArmSwing)))
            {
                return;
            }

            if (source == EMucheolVfxSource.ArmSwing)
            {
                BeginSwing(in start);
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
            if ((_swing != null) && (_swing.CastId == castId) && double.IsNaN(_swing.HitTime))
            {
                _swing.Direction = direction;
                PlaceSwing(_swing);
            }

            for (int i = 0; i < _chainSteps.Count; ++i)
            {
                Swing step = _chainSteps[i];
                if ((step.CastId == castId) && double.IsNaN(step.HitTime))
                {
                    step.Direction = direction;
                    PlaceSwing(step);
                }
            }

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
            if ((_swing != null) && (_swing.CastId == castId))
            {
                ConfirmSwing(serverTime);
                return;
            }

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

        /// <summary> 기본 돌진 정지, 멈춘 자리에서 서버 정지 시각에 끝 타격을 낸다. 시각을 모르면 NaN이다 </summary>
        public void StopBasicDash(int castId, Vector3 position, double stopServerTime)
        {
            StopRun(castId, 0, position, stopServerTime);
        }

        // 발동 뒤에 들어왔으면 단발은 이미 터졌으므로 만들지 않는다. 돌진은 아직 달리는 중일 때만 이어 그린다
        /// <summary> 기본 예고 복원 </summary>
        public void RestoreBasic(in BossTelegraphSnapshot snapshot)
        {
            _isRestoring = true;
            try
            {
                RestoreBasicCore(in snapshot);
            }
            finally
            {
                _isRestoring = false;
            }
        }

        private void RestoreBasicCore(in BossTelegraphSnapshot snapshot)
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

            bool isDash    = (snapshot.Phase == EBossTelegraphPhase.Execute) && (source == EMucheolVfxSource.BasicDash);
            bool isStopped = snapshot.DashStoppedDistance >= 0f;
            bool isRunning = isDash && (!isStopped) && (ReadClock() < snapshot.ExecuteServerTime + snapshot.DashSeconds);

            // 멈춘 시각을 알면 끝 타격의 남은 꼬리만 이어 그린다. 꼬리까지 지났으면 Confirm이 바로 거둔다
            bool isStopTail = isDash && isStopped && (!double.IsNaN(snapshot.DashStoppedServerTime));
            if ((!isRunning) && (!isStopTail))
            {
                Remember(Key(start.CastId, 0));
                return;
            }

            BeginBasic(in start);
            UpdateAim(start.CastId, snapshot.Direction);
            BeginRun(start.CastId, 0, snapshot.DashDistance, snapshot.DashSeconds, snapshot.DashSpeed, snapshot.ExecuteServerTime);
            if (isStopTail)
            {
                Vector3 stopAt = start.Origin + (Flatten(snapshot.Direction) * snapshot.DashStoppedDistance);
                StopRun(start.CastId, 0, stopAt, snapshot.DashStoppedServerTime);
            }
        }

        // firstStep 앞의 단계는 이미 터졌다(복원의 StruckCount)
        /// <summary> 콤보와 순차 타격 시작, 단계마다 앞 단계 타격부터 자기 타격까지 예고를 그린다 </summary>
        public void BeginStrikeChain(in BossStrikeChainStart start, int firstStep = 0)
        {
            if ((!MucheolVfxCatalog.TryGetSource(start.PatternId, out EMucheolVfxSource source))
                || ((source != EMucheolVfxSource.StrikeChain) && (source != EMucheolVfxSource.ArmChain)))
            {
                return;
            }

            if (source == EMucheolVfxSource.ArmChain)
            {
                BeginArmChain(in start, firstStep);
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
            Swing step = FindChainStep(castId, stepIndex);
            if (step != null)
            {
                if (double.IsNaN(step.HitTime))
                {
                    step.HitTime = serverTime;
                    Remember(Key(castId, stepIndex));
                    PlaySoundAt(EMucheolSound.Thud, step.Origin, serverTime);
                }

                return;
            }

            Slot slot = Find(castId, stepIndex);
            if (slot != null)
            {
                Confirm(slot, serverTime);
            }
        }

        /// <summary> 콤보와 순차 타격 복원, 이미 터진 단계는 만들지 않는다 </summary>
        public void RestoreStrikeChain(in BossStrikeChainSnapshot snapshot)
        {
            _isRestoring = true;
            try
            {
                RestoreStrikeChainCore(in snapshot);
            }
            finally
            {
                _isRestoring = false;
            }
        }

        private void RestoreStrikeChainCore(in BossStrikeChainSnapshot snapshot)
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

        public void StopMultiDashRun(int castId, int dashIndex, Vector3 position, double stopServerTime)
        {
            StopRun(castId, dashIndex, position, stopServerTime);
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
            _isRestoring = true;
            try
            {
                RestoreMultiDashCore(in snapshot);
            }
            finally
            {
                _isRestoring = false;
            }
        }

        private void RestoreMultiDashCore(in BossMultiDashSnapshot snapshot)
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

            // 멈춘 시각을 알면 끝 타격의 남은 꼬리만 이어 그린다. 꼬리까지 지났으면 Confirm이 바로 거둔다
            bool isStopped  = snapshot.HasDashStarted && (snapshot.StoppedDistance >= 0f);
            bool isStopTail = isStopped && (!double.IsNaN(snapshot.StoppedServerTime));
            if ((!isStopTail) && snapshot.HasDashStarted
                && (isStopped || (ReadClock() >= snapshot.DashStartServerTime + snapshot.DashSeconds)))
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

            if (isStopTail)
            {
                Vector3 stopAt = snapshot.Origin + (Flatten(snapshot.Direction) * snapshot.StoppedDistance);
                StopRun(start.CastId, snapshot.DashIndex, stopAt, snapshot.StoppedServerTime);
            }
        }

        /// <summary> 전장 공격 시작 </summary>
        public void BeginBattlefield(in BossBattlefieldStart start)
        {
            SpawnStrike(start.CastId, 0, MucheolMotionCatalog.P2_P07_ID, start.Origin, start.Direction, start.StartServerTime,
                start.StartServerTime + start.TelegraphSeconds, float.NaN);
            Slot field = Find(start.CastId, 0);
            if (field != null)
            {
                field.Radius = start.Radius;
                field.Arc    = start.ArcAngle;
            }
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
            _isRestoring = true;
            try
            {
                RestoreBattlefieldCore(in snapshot);
            }
            finally
            {
                _isRestoring = false;
            }
        }

        private void RestoreBattlefieldCore(in BossBattlefieldSnapshot snapshot)
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
            if (_sound != null)
            {
                _sound.StopAll();
            }

            EndSwing();
            EndChainTrails();
            EndChainSteps(false, 0);
            for (int i = _slots.Count - 1; i >= 0; --i)
            {
                Release(_slots[i]);
            }

            for (int i = 0; i < _fanImpacts.Count; ++i)
            {
                if (_fanImpacts[i] != null)
                {
                    _fanImpacts[i].Stop();
                }
            }

            for (int i = 0; i < _waveImpacts.Count; ++i)
            {
                if (_waveImpacts[i] != null)
                {
                    _waveImpacts[i].Stop();
                }
            }

            // 사망과 연결 끊김은 꼬리를 남기지 않고 분출을 모두 지운다
            for (int i = 0; i < _rushes.Count; ++i)
            {
                if (_rushes[i] != null)
                {
                    _rushes[i].Stop();
                }
            }
        }

        // 팔 휘두르기는 예고 시작부터 타격까지 빨간 예고를 채우고, 시전 내내 왼손 궤적을 켜 둔다. 궤적은 손이 빠를 때만 스스로 그린다.
        // 같은 (시전, 0번)이 이미 있거나 터졌으면 만들지 않는다, 복원과 늦은 사건이 같은 효과를 두 번 내지 않게 하는 기본 예고와 같은 기준이다
        private void BeginSwing(in BossTelegraphStart start)
        {
            int castId = start.CastId;
            if (_isDefeated || (_container == null) || (_swingTelegraphPrefab == null) || _endedCasts.Contains(castId)
                || ((_swing != null) && (_swing.CastId == castId)) || _played.Contains(Key(castId, 0))
                || (!MucheolVfxCatalog.Handles(start.PatternId)))
            {
                return;
            }

            double impact = start.StartServerTime + start.TelegraphSeconds;
            if (ReadClock() >= impact + CONFIRM_GRACE_SECONDS)
            {
                return;
            }

            EnsureTelegraph();
            EnsureTrails();
            if (_swingTelegraph == null)
            {
                return;
            }

            EndSwing();
            _swing = new Swing
            {
                CastId     = castId,
                Start      = start.StartServerTime,
                ImpactTime = impact,
                HitTime    = double.NaN,
                Deadline   = impact + CONFIRM_GRACE_SECONDS,
                Origin     = start.Origin,
                Direction  = start.Direction,
                Telegraph  = _swingTelegraph,
            };

            float rangeScale = MucheolVfxCatalog.GetRangeScale(start.PatternId);
            _swingTelegraph.Configure((float)(impact - start.StartServerTime));
            _swingTelegraph.SetRangeScale(rangeScale);
            PlaceSwing(_swing);
            _swingTelegraph.gameObject.SetActive(true);
            _swingTelegraph.SetElapsed((float)(ReadClock() - start.StartServerTime));
            ActivateTrail(_armTrail, rangeScale);
            PlayWhoosh(start.Origin, impact);
        }

        private void ConfirmSwing(double hitTime)
        {
            if (!double.IsNaN(_swing.HitTime))
            {
                return;
            }

            _swing.HitTime = hitTime;
            Remember(Key(_swing.CastId, 0));
            PlaySoundAt(EMucheolSound.Thud, _swing.Origin, hitTime);
        }

        // 확정 전에는 예고가 가득 차기 직전에서 기다리고, 확정이 오면 그 시각부터 번쩍임과 사라짐이 흐른다
        private void TickSwing(double now)
        {
            if (_swing == null)
            {
                return;
            }

            bool isPending = double.IsNaN(_swing.HitTime);
            if (isPending && (now > _swing.Deadline))
            {
                EndSwing();
                return;
            }

            if (!_swing.IsTelegraphDone)
            {
                float fill = (float)(_swing.ImpactTime - _swing.Start);
                float elapsed = isPending
                    ? Mathf.Min((float)(now - _swing.Start), fill - SWING_FILL_MARGIN)
                    : fill + (float)(now - _swing.HitTime);
                _swingTelegraph.SetElapsed(elapsed);
                if (_swingTelegraph.IsFinished)
                {
                    _swingTelegraph.gameObject.SetActive(false);
                    _swing.IsTelegraphDone = true;
                }
            }

            if ((!isPending) && (now > _swing.HitTime + SWING_TRAIL_TAIL_SECONDS))
            {
                EndSwing();
            }
        }

        private static void PlaceSwing(Swing swing)
        {
            Vector3 forward = Flatten(swing.Direction);
            Vector3 position = swing.Origin + (Vector3.up * MucheolVfxCatalog.GROUND_LIFT);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, swing.Yaw, 0f);
            swing.Telegraph.transform.SetPositionAndRotation(position, rotation);
        }

        private void EndSwing()
        {
            _swing = null;
            if (_swingTelegraph != null)
            {
                _swingTelegraph.gameObject.SetActive(false);
            }

            if (_armTrail != null)
            {
                _armTrail.gameObject.SetActive(false);
            }
        }

        // 연타는 단계마다 빨간 예고를 그린다. 단계 i의 예고는 앞 단계 타격부터(첫 단계는 시전 시작부터) 자기 타격까지 차오르고,
        // 각도와 사거리와 방향 오프셋은 서버 사건의 값을 그대로 쓴다. 부채꼴이 아닌 단계는 그리지 않는다
        private void BeginArmChain(in BossStrikeChainStart start, int firstStep)
        {
            int castId = start.CastId;
            if (_isDefeated || (_container == null) || (_swingTelegraphPrefab == null) || _endedCasts.Contains(castId))
            {
                return;
            }

            IReadOnlyList<EBossTelegraphShape> shapes = start.Shapes;
            IReadOnlyList<float> delays  = start.DelaySeconds;
            IReadOnlyList<float> offsets = start.AimOffsetDegrees;
            IReadOnlyList<float> ranges  = start.Ranges;
            IReadOnlyList<float> arcs    = start.ArcAngles;
            double execute = start.StartServerTime + start.TelegraphSeconds;
            double now     = ReadClock();

            for (int i = _chainSteps.Count - 1; i >= 0; --i)
            {
                if (_chainSteps[i].CastId != castId)
                {
                    EndChainStep(_chainSteps[i]);
                }
            }

            BeginChainTrails(castId, start.PatternId, execute, delays);
            for (int i = Math.Max(0, firstStep); i < start.StepCount; ++i)
            {
                if ((delays == null) || (i >= delays.Count) || (ranges == null) || (i >= ranges.Count) || (arcs == null)
                    || (i >= arcs.Count) || (shapes == null) || (i >= shapes.Count))
                {
                    break;
                }

                if ((shapes[i] != EBossTelegraphShape.Fan) || (FindChainStep(castId, i) != null) || _played.Contains(Key(castId, i)))
                {
                    continue;
                }

                double impact = execute + delays[i];
                if (now >= impact + CONFIRM_GRACE_SECONDS)
                {
                    continue;
                }

                MucheolFanTelegraph telegraph = GetChainTelegraph(i);
                if (telegraph == null)
                {
                    continue;
                }

                double stepStart = (i == 0) ? start.StartServerTime : execute + delays[i - 1];
                telegraph.SetShape(ranges[i], arcs[i]);
                telegraph.SetRangeScale(1f);
                telegraph.Configure((float)(impact - stepStart));
                _chainSteps.Add(new Swing
                {
                    CastId     = castId,
                    Index      = i,
                    Start      = stepStart,
                    ImpactTime = impact,
                    HitTime    = double.NaN,
                    Deadline   = impact + CONFIRM_GRACE_SECONDS,
                    Origin     = start.Origin,
                    Direction  = start.Direction,
                    Yaw        = ((offsets != null) && (i < offsets.Count) && (!float.IsNaN(offsets[i]))) ? offsets[i] : 0f,
                    Telegraph  = telegraph,
                });

                PlayWhoosh(start.Origin, impact);
            }
        }

        // 확정 전에는 가득 차기 직전에서 기다리고, 확정이 오면 그 시각부터 번쩍임과 사라짐이 흐른다. 단계 시작 전에는 켜지 않는다
        private void TickChainSteps(double now)
        {
            for (int i = _chainSteps.Count - 1; i >= 0; --i)
            {
                Swing step = _chainSteps[i];
                bool isPending = double.IsNaN(step.HitTime);
                if (isPending && (now > step.Deadline))
                {
                    EndChainStep(step);
                    continue;
                }

                if (!step.IsShown)
                {
                    if (now < step.Start)
                    {
                        continue;
                    }

                    PlaceSwing(step);
                    step.Telegraph.gameObject.SetActive(true);
                    step.IsShown = true;
                }

                float fill = (float)(step.ImpactTime - step.Start);
                float elapsed = isPending
                    ? Mathf.Min((float)(now - step.Start), fill - SWING_FILL_MARGIN)
                    : fill + (float)(now - step.HitTime);
                step.Telegraph.SetElapsed(elapsed);
                if (step.Telegraph.IsFinished)
                {
                    EndChainStep(step);
                }
            }
        }

        // onlyPending이 참이면 그 시전의 아직 확정되지 않은 단계만 거두고, 이미 터진 단계는 번쩍임을 끝까지 둔다
        private void EndChainSteps(bool onlyPending, int castId)
        {
            for (int i = _chainSteps.Count - 1; i >= 0; --i)
            {
                Swing step = _chainSteps[i];
                if ((!onlyPending) || ((step.CastId == castId) && double.IsNaN(step.HitTime)))
                {
                    EndChainStep(step);
                }
            }
        }

        private void EndChainStep(Swing step)
        {
            step.Telegraph.gameObject.SetActive(false);
            _chainSteps.Remove(step);
        }

        private Swing FindChainStep(int castId, int index)
        {
            for (int i = 0; i < _chainSteps.Count; ++i)
            {
                if ((_chainSteps[i].CastId == castId) && (_chainSteps[i].Index == index))
                {
                    return _chainSteps[i];
                }
            }

            return null;
        }

        private MucheolFanTelegraph GetChainTelegraph(int index)
        {
            while (_chainPool.Count <= index)
            {
                _chainPool.Add(null);
            }

            if (_chainPool[index] == null)
            {
                MucheolFanTelegraph created = Instantiate(_swingTelegraphPrefab, _container);
                created.gameObject.SetActive(false);
                _chainPool[index] = created;
            }

            return _chainPool[index];
        }

        // 양손 궤적은 마지막 타격 뒤 꼬리까지 켜 둔다. 궤적은 손이 빠를 때만 스스로 그린다
        private void BeginChainTrails(int castId, string patternId, double execute, IReadOnlyList<float> delays)
        {
            if (_isDefeated || (_container == null) || _endedCasts.Contains(castId) || (_hasChainTrail && (_chainTrailCastId == castId)))
            {
                return;
            }

            double lastDelay = 0d;
            for (int i = 0; (delays != null) && (i < delays.Count); ++i)
            {
                lastDelay = Math.Max(lastDelay, delays[i]);
            }

            double end = execute + lastDelay + SWING_TRAIL_TAIL_SECONDS;
            if (ReadClock() >= end)
            {
                return;
            }

            EnsureTrails();
            if ((_armTrail == null) && (_armTrailRight == null))
            {
                return;
            }

            EndChainTrails();
            _hasChainTrail    = true;
            _chainTrailCastId = castId;
            _chainTrailEnd    = end;

            float rangeScale = MucheolVfxCatalog.GetRangeScale(patternId);
            ActivateTrail(_armTrail, rangeScale);
            ActivateTrail(_armTrailRight, rangeScale);
        }

        private void EndChainTrails()
        {
            _hasChainTrail = false;
            if (_armTrail != null)
            {
                _armTrail.gameObject.SetActive(false);
            }

            if (_armTrailRight != null)
            {
                _armTrailRight.gameObject.SetActive(false);
            }
        }

        private void ActivateTrail(MucheolArmTrail trail, float rangeScale)
        {
            if (trail == null)
            {
                return;
            }

            trail.SetRangeScale(rangeScale);
            trail.SetFacing(transform);
            trail.gameObject.SetActive(true);
        }

        // 예고는 보스가 움직여도 서버가 알린 자리에 남아야 해서 보스 계층 밖에 둔다
        private void EnsureTelegraph()
        {
            if (_swingTelegraph == null)
            {
                _swingTelegraph = Instantiate(_swingTelegraphPrefab, _container);
                _swingTelegraph.gameObject.SetActive(false);
            }
        }

        // 궤적은 보스 모델의 양손 본에 붙인다. 두 손 모두 한 번만 만들고 켜고 끄기만 한다
        private void EnsureTrails()
        {
            if (_armTrailPrefab == null)
            {
                return;
            }

            Animator animator = GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                return;
            }

            if (_armTrail == null)
            {
                _armTrail = AttachTrail(animator.GetBoneTransform(HumanBodyBones.LeftHand));
            }

            if (_armTrailRight == null)
            {
                _armTrailRight = AttachTrail(animator.GetBoneTransform(HumanBodyBones.RightHand));
            }
        }

        private MucheolArmTrail AttachTrail(Transform hand)
        {
            if (hand == null)
            {
                return null;
            }

            MucheolArmTrail trail = Instantiate(_armTrailPrefab, hand);
            trail.transform.localPosition = Vector3.zero;
            trail.gameObject.SetActive(false);
            return trail;
        }

        // 원형 충격파가 서버에서 확정된 순간 보스 둘레로 고리를 퍼뜨린다
        private void PlayWaveImpact(Vector3 origin, Vector3 direction, float yawDegrees, float radius, bool isStrong, float arc, double hitTime)
        {
            if ((_waveImpactPrefab == null) || (_container == null) || (_isDefeated) || (ReadClock() - hitTime > FAN_IMPACT_LATE_SECONDS))
            {
                return;
            }

            MucheolWaveImpactView view = AcquireWaveImpact();
            if (view == null)
            {
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(Flatten(direction), Vector3.up) * Quaternion.Euler(0f, yawDegrees, 0f);
            view.transform.SetPositionAndRotation(origin + (Vector3.up * MucheolVfxCatalog.GROUND_LIFT), rotation);
            view.gameObject.SetActive(true);
            view.Setup(radius, isStrong, arc);
            view.Play();
        }

        private MucheolWaveImpactView AcquireWaveImpact()
        {
            for (int i = 0; i < _waveImpacts.Count; ++i)
            {
                if ((_waveImpacts[i] != null) && (!_waveImpacts[i].IsPlaying))
                {
                    return _waveImpacts[i];
                }
            }

            if (_waveImpacts.Count < MAX_WAVE_IMPACTS)
            {
                MucheolWaveImpactView created = Instantiate(_waveImpactPrefab, _container);
                created.gameObject.SetActive(false);
                _waveImpacts.Add(created);
                return created;
            }

            MucheolWaveImpactView oldest = _waveImpacts[0];
            _waveImpacts.RemoveAt(0);
            _waveImpacts.Add(oldest);
            oldest.Stop();
            return oldest;
        }

        // 부채꼴 타격이 서버에서 확정된 순간 그 자리에서 착탄 VFX를 터뜨린다. 위치와 방향은 예고와 같고 각도와 사거리는 서버 값이다
        private void PlayFanImpact(Vector3 origin, Vector3 direction, float yawDegrees, float angle, float radius, double hitTime)
        {
            if ((_fanImpactPrefab == null) || (_container == null) || (_isDefeated) || (ReadClock() - hitTime > FAN_IMPACT_LATE_SECONDS))
            {
                return;
            }

            MucheolFanImpactView view = AcquireFanImpact();
            if (view == null)
            {
                return;
            }

            Vector3 forward = Flatten(direction);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, yawDegrees, 0f);
            view.transform.SetPositionAndRotation(origin + (Vector3.up * MucheolVfxCatalog.GROUND_LIFT), rotation);
            view.gameObject.SetActive(true);
            view.Setup(angle, radius);
            view.Play();
        }

        // 놀고 있는 것을 먼저 쓰고, 없으면 상한까지 새로 만들고, 상한이면 가장 오래된 것을 다시 쓴다
        private MucheolFanImpactView AcquireFanImpact()
        {
            for (int i = 0; i < _fanImpacts.Count; ++i)
            {
                if ((_fanImpacts[i] != null) && (!_fanImpacts[i].IsPlaying))
                {
                    return _fanImpacts[i];
                }
            }

            if (_fanImpacts.Count < MAX_FAN_IMPACTS)
            {
                MucheolFanImpactView created = Instantiate(_fanImpactPrefab, _container);
                created.gameObject.SetActive(false);
                _fanImpacts.Add(created);
                return created;
            }

            MucheolFanImpactView oldest = _fanImpacts[0];
            _fanImpacts.RemoveAt(0);
            _fanImpacts.Add(oldest);
            oldest.Stop();
            return oldest;
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

            if (cue.Template == EMucheolVfxTemplate.FanSlam70)
            {
                PlayWhoosh(origin, impact);
            }

            StartChargeSound(slot, patternId);
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
            StartChargeSound(slot, patternId);
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
                HitPoint    = origin,
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
            StartRush(slot);

            StopChargeSound(slot);
            PlaySoundAt(EMucheolSound.Dash, slot.Origin, startServerTime);
        }

        // 끝 타격 시각은 서버가 정지를 판정한 시각(stopServerTime)이다. 서버 몸체도 그 틱에 멈춘 자리에 닿는다.
        // 시각이 없는 사건(NaN)이면 서버 이동 모델로 거꾸로 계산한 추정값을 쓴다:
        //   추정 정지 시각 = 돌진 시작 시각 + (시작점에서 정지 위치까지 조준 방향 거리 / 서버 속도)
        // 서버는 이 시각 이후 첫 틱에서 정지를 감지하므로 추정은 실제 감지 틱보다 최대 한 틱 이르다. 정의 거리를 다 갔으면 예정 끝 시각과 같다.
        // 일찍 멈추면 경로 끝의 타격 자리를 멈춘 곳으로 당긴다. 예고는 타격과 함께 사라지므로 경로 모양은 남지 않는다
        private void StopRun(int castId, int index, Vector3 position, double stopServerTime)
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

            slot.HitPoint = position;
            bool hasServerTime = !double.IsNaN(stopServerTime);
            slot.IsHitEstimated = !hasServerTime;
            Confirm(slot, hasServerTime ? stopServerTime : Math.Min(estimate, ReadClock()));
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

            StopChargeSound(slot);
            PlayImpactSounds(slot, hitTime);
            EMucheolVfxTemplate template = slot.Cue.Template;
            if ((template == EMucheolVfxTemplate.SmallWave04) || (template == EMucheolVfxTemplate.LargeWave05) || (template == EMucheolVfxTemplate.Circle02))
            {
                // 원형 템플릿의 크기는 균일 배율이고 1이 반경 5m다(하드는 1.3배). 큰 충격파와 원형 단발은 바닥도 갈라낸다
                float waveRadius = FAN_BASE_RADIUS * Mathf.Max(slot.Cue.Scale.x, 0.01f);
                bool  isStrong   = template != EMucheolVfxTemplate.SmallWave04;
                PlayWaveImpact(slot.Origin, slot.Direction, 0f, waveRadius, isStrong, 360f, hitTime);
                if (isStrong)
                {
                    PlayFanImpact(slot.Origin, slot.Direction, 0f, 360f, waveRadius, hitTime);
                }
            }
            else if (template == EMucheolVfxTemplate.Battlefield07)
            {
                // 전장 공격은 서버가 알린 반경과 각도를 쓴다. 크레이터는 기준 반경 공간에서 만들어 비율로 키우므로 범위가 넓어도 판 개수는 같다
                if (slot.Radius > 0f)
                {
                    float arc = (slot.Arc > 0f) ? slot.Arc : 360f;
                    PlayWaveImpact(slot.Origin, slot.Direction, 0f, slot.Radius, true, arc, hitTime);
                    PlayFanImpact(slot.Origin, slot.Direction, 0f, arc, slot.Radius, hitTime);
                }
            }

            if ((template == EMucheolVfxTemplate.FanSlam70) || (template == EMucheolVfxTemplate.Fan01))
            {
                // 템플릿 부채꼴의 크기는 폭(x)과 사거리(z) 배율이다. 폭 1이 120도, 사거리 1이 5m이고 하드는 둘 다 커진다
                float length = Mathf.Max(slot.Cue.Scale.z, 0.01f);
                float angle  = 2f * Mathf.Atan((slot.Cue.Scale.x / length) * FAN_BASE_HALF_TAN) * Mathf.Rad2Deg;
                PlayFanImpact(slot.Origin, slot.Direction, slot.YawDegrees, angle, FAN_BASE_RADIUS * length, hitTime);
            }
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

            TickRush(slot, now);

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
            if ((_swing != null) && (_swing.CastId == castId) && double.IsNaN(_swing.HitTime))
            {
                EndSwing();
            }

            if (_hasChainTrail && (_chainTrailCastId == castId))
            {
                EndChainTrails();
            }

            EndChainSteps(true, castId);

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

        // 예고만 거두는 취소나 정상 끝에서도 이미 나온 분출은 꼬리를 재생하고 스스로 거둔다
        private void Release(Slot slot)
        {
            _slots.Remove(slot);
            StopChargeSound(slot);
            if (slot.Rush != null)
            {
                slot.Rush.Finish();
                slot.Rush = null;
            }

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
            if ((template == (int)EMucheolVfxTemplate.DashPath06) && (_dashRushPrefab != null))
            {
                HideChargeHead(created);
            }

            return created;
        }

        // 달리는 머리는 원본 재생기가 켜고 끄므로 오브젝트를 끄지 않고 렌더러만 꺼 둔다. 새 분출이 그 자리를 대신한다.
        // 원본 프리팹을 고치지 않으려고 만들 때 한 번만 이름으로 찾는다
        private static void HideChargeHead(GameObject root)
        {
            Transform head = root.transform.Find(CHARGE_HEAD_NAME);
            if (head == null)
            {
                return;
            }

            Renderer[] renderers = head.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                renderers[i].enabled = false;
            }
        }

        // 돌진(06)이 달리기 시작하면 분출을 시작한다. 복원으로 늦게 시작해도 첫 Tick이 현재 거리로 옮긴다
        private void StartRush(Slot slot)
        {
            if ((_dashRushPrefab == null) || (slot.Rush != null) || (slot.Cue.Template != EMucheolVfxTemplate.DashPath06))
            {
                return;
            }

            MucheolDashRushView rush = AcquireRush();
            if (rush == null)
            {
                return;
            }

            Vector3 forward = Flatten(slot.Direction);
            Vector3 origin  = slot.Origin + (Vector3.up * MucheolVfxCatalog.GROUND_LIFT);
            rush.Begin(origin, Quaternion.LookRotation(forward, Vector3.up), slot.Cue.Scale.x);
            slot.Rush = rush;
        }

        // 서버 돌진처럼 시작 시각부터 속도로 일정하게 나아간다. 벽에서 멈출 것을 아는 DashHold와 서버 정지 시각까지만 진행한다
        private void TickRush(Slot slot, double now)
        {
            if (slot.Rush == null)
            {
                return;
            }

            double sample = Math.Min(now, slot.DashHold);
            if (!double.IsNaN(slot.HitTime))
            {
                sample = Math.Min(sample, slot.HitTime);
            }

            slot.Rush.SetDistance((float)(slot.DashSpeed * Math.Max(0d, sample - slot.DashStart)));

            bool isArrived = (now >= slot.DashHold) || ((!double.IsNaN(slot.HitTime)) && (now >= slot.HitTime));
            if (isArrived)
            {
                slot.Rush.Finish();
            }
        }

        private MucheolDashRushView AcquireRush()
        {
            for (int i = 0; i < _rushes.Count; ++i)
            {
                if ((_rushes[i] != null) && (!_rushes[i].IsPlaying))
                {
                    return _rushes[i];
                }
            }

            if (_rushes.Count >= MAX_RUSH_INSTANCES)
            {
                return null;
            }

            GameObject created = Instantiate(_dashRushPrefab, _container);
            created.SetActive(false);
            if (!created.TryGetComponent(out MucheolDashRushView rush))
            {
                Destroy(created);
                return null;
            }

            _rushes.Add(rush);
            return rush;
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

        // 소리는 연출이라 서버가 관여하지 않는다. 복원 중에는 이미 지난 일회성 소리를 내지 않는다
        private void PlaySound(EMucheolSound id, Vector3 position, float delaySeconds)
        {
            if ((_sound == null) || _isRestoring)
            {
                return;
            }

            _sound.Play(id, position, delaySeconds);
        }

        // 서버가 알린 시각에 맞춰 울린다. 시각이 조금 앞이면 그만큼 늦추고, 너무 지났으면 내지 않는다
        private void PlaySoundAt(EMucheolSound id, Vector3 position, double serverTime)
        {
            float delay = (float)(serverTime - ReadClock());
            if (delay < -SOUND_LATE_SECONDS)
            {
                return;
            }

            PlaySound(id, position, Mathf.Max(0f, delay));
        }

        // 휘두르는 소리는 타격보다 조금 앞서 시작한다
        private void PlayWhoosh(Vector3 position, double impactServerTime)
        {
            float untilImpact = (float)(impactServerTime - ReadClock());
            if (untilImpact <= 0f)
            {
                return;
            }

            PlaySound(EMucheolSound.Whoosh, position, Mathf.Max(0f, untilImpact - SOUND_WHOOSH_LEAD_SECONDS));
        }

        // 원과 파동, 전장 원본은 타격 소리에 지면 충격파를 겹치고, 직선 충격파(03)는 타격 소리 없이 파동만 낸다
        private void PlayImpactSounds(Slot slot, double hitTime)
        {
            EMucheolVfxTemplate template = slot.Cue.Template;
            if (template != EMucheolVfxTemplate.Line03)
            {
                PlaySoundAt(EMucheolSound.Thud, slot.HitPoint, hitTime);
            }

            bool hasWave = (template == EMucheolVfxTemplate.Circle02) || (template == EMucheolVfxTemplate.SmallWave04)
                || (template == EMucheolVfxTemplate.LargeWave05) || (template == EMucheolVfxTemplate.Battlefield07)
                || (template == EMucheolVfxTemplate.Line03);
            if (hasWave)
            {
                PlaySoundAt(EMucheolSound.Shockwave, slot.HitPoint, hitTime);
            }
        }

        // 힘 모으기 소리는 강한 예고에만 깐다. 모든 예고에 같은 크기로 깔면 묻힌다
        private static bool UsesChargeSound(string patternId)
        {
            string baseId = BossPatternCatalogSO.ToMucheolBaseId(patternId);
            return (baseId == MucheolMotionCatalog.P1_P02_ID) || (baseId == MucheolMotionCatalog.P1_P04_ID)
                || (baseId == MucheolMotionCatalog.P1_P06_ID) || (baseId == MucheolMotionCatalog.P2_P06_ID)
                || (baseId == MucheolMotionCatalog.P2_P07_ID);
        }

        private void StartChargeSound(Slot slot, string patternId)
        {
            if ((_sound == null) || _isRestoring || (!UsesChargeSound(patternId)))
            {
                return;
            }

            float delay = Mathf.Max(0f, (float)(slot.Start - ReadClock()));
            _sound.StartLoop(ChargeSoundKey(slot), EMucheolSound.ChargeLoop, slot.Origin, delay);
        }

        // 예고가 확정되거나 돌진이 시작되거나 취소되면 반드시 끊는다
        private void StopChargeSound(Slot slot)
        {
            if (_sound != null)
            {
                _sound.StopLoop(ChargeSoundKey(slot));
            }
        }

        private static int ChargeSoundKey(Slot slot)
        {
            return (slot.CastId * SOUND_KEY_STRIDE) + slot.Index;
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
