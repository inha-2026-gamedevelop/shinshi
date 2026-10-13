#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Dev
{
    // 월백 하드 2페이즈 P01~P06 중 하나만 골라 반복 재생하는 개발용 연습장이다. 사운드, 이펙트, 모션의 타이밍을 맞출 때 쓴다
    // 서버 AI와 판정은 실행하지 않는다. 서버가 보낼 알림을 서버 패턴(BossGridBeamPattern 등)이 알리는 순서와 시각 규칙 그대로 시각표로 흉내 내
    // 표시 뷰(BossHardPhaseView)의 공개 메서드만 부른다. 시간과 크기는 패턴 카탈로그 에셋에서 읽으므로 에셋을 고치면 다음 재생에 바로 반영된다
    // 서버 어셈블리를 참조할 수 없어 13개 원 배치와 난사 방향은 서버 규칙을 옮겨 만든 값이다. 사분면 원 좌표와 난사 구역 분할은 서버와 같고, 난사 구역 순서는 시드로 고정할 수 있다
    // 각 알림 시각은 CueRaised 이벤트로도 나가므로 사운드, 이펙트, 모션 재생기를 이 이벤트에 연결해 타이밍을 확인할 수 있다
    /// <summary> 월백 하드 2페이즈 패턴 하나를 골라 반복 재생하는 연습장, 화면 버튼과 단축키로 조작한다 </summary>
    public sealed class DevBossHardPatternLab : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 연습장이 재생하는 패턴, 번호는 카탈로그 정의 ID의 끝 두 자리와 같다 </summary>
        public enum EHardPattern
        {
            P01StarStage,
            P02GridBeam,
            P03ChainFan,
            P04TrackingArea,
            P05Barrage,
            P06CircleBurst
        }

        private struct Scheduled
        {
            public double Time;
            public string Cue;
            public int    Index;
            public Action Call;
        }

        private const int    SAMPLE_BOSS_ID      = 1;
        private const int    FIRST_ID            = 1000;
        private const int    NO_INDEX            = -1;
        private const int    LOG_CAPACITY        = 18;
        private const float  RUN_TAIL_SECONDS    = 0.8f;   // 마지막 알림 뒤 표현이 사라지길 기다리는 시간
        private const float  AREA_UPDATE_STEP    = 0.1f;   // 서버가 추적 장판 중심을 알리는 간격
        private const float  TARGET_ORBIT        = 8f;     // m, 가상 대상이 전장 중심 둘레를 도는 반경
        private const float  TARGET_SPEED        = 0.7f;   // 라디안/초
        private const float  FULL_TURN           = 360f;
        private const float  MIN_SPEED           = 0.1f;
        private const float  MAX_SPEED           = 2f;
        private const float  MAX_GAP             = 5f;
        private const float  MIN_PATH_LENGTH     = 0.5f;   // m, 벽이 코앞이어도 예고가 보이게 하는 최소 길이
        private const float  MAX_SECTOR_FRACTION = 0.9999f; // 구역 끝 경계는 다음 구역이라 구역 안에 머물게 한다

        // 동시 원형 폭발 배치, 서버 BossCircleBurstLayout과 같다
        private const int    BURST_QUADRANTS      = 4;
        private const int    BURST_SPOTS_PER_QUAD = 3;
        private const int    BURST_CIRCLE_COUNT   = 1 + (BURST_QUADRANTS * BURST_SPOTS_PER_QUAD);
        private const int    BURST_POSITIVE_X_QUADRANTS = 2;
        private const int    BURST_NEGATIVE_Z_FIRST     = 1;
        private const int    BURST_NEGATIVE_Z_LAST      = 2;
        private const int    BURST_FAR_X_SPOT           = 1;
        private const int    BURST_FAR_Z_SPOT           = 2;

        // 화면 조작판
        private const float  UI_SCALE        = 1.4f;
        private const float  PANEL_MARGIN    = 8f;
        private const float  PANEL_WIDTH     = 330f;
        private const float  OPEN_BUTTON_WIDTH  = 120f;
        private const float  OPEN_BUTTON_HEIGHT = 30f;
        private const float  PANEL_GAP       = 6f;

        private static readonly string[] PATTERN_IDS =
        {
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P01_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P02_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P03_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P04_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P05_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P06_ID
        };

        private static readonly string[] PATTERN_LABELS =
        {
            "P01 단계형 별 빔",
            "P02 격자 순차 빔",
            "P03 잔류 부채꼴 연쇄",
            "P04 추적 장판",
            "P05 전방위 난사",
            "P06 동시 원형 폭발"
        };

        [Header("대상")]
        [SerializeField] private BossHardPhaseView      _hardView;
        [SerializeField] private BossTelegraphPresenter _presenter; // 비면 P04 장판과 P05 투사체의 활성 뒤 모습은 생략한다
        [SerializeField] private BossPatternCatalogSO   _catalog;
        [SerializeField] private Transform              _boss;
        [SerializeField] private Transform              _arenaCenter;

        [Header("전장")]
        [SerializeField, Min(1f)]            private float _arenaRadius = 20f;
        [SerializeField, Range(-180f, 180f)] private float _chainSafeAngle;
        [SerializeField, Min(0.5f)]          private float _areaPreviewSeconds = 6f; // 카탈로그의 장판 지속 시간은 60초라 연습에서는 줄인다
        [SerializeField, Tooltip("0이면 재생마다 난사 구역 순서가 바뀐다")] private int _barrageSeed;

        [Header("재생")]
        [SerializeField] private EHardPattern _selected    = EHardPattern.P06CircleBurst;
        [SerializeField] private bool         _autoLoop    = true;
        [SerializeField] private bool         _playOnStart = true;
        [SerializeField, Range(0f, MAX_GAP)]            private float _loopGap = 1.5f;
        [SerializeField, Range(MIN_SPEED, MAX_SPEED)]   private float _speed   = 1f;

        private readonly List<Scheduled> _queue = new List<Scheduled>();
        private readonly List<string>    _log   = new List<string>();

        private int           _nextId = FIRST_ID;
        private double        _runStart;
        private double        _runEnd;
        private double        _nextPlayAt;
        private bool          _isRunning;
        private bool          _isPaused;
        private bool          _isWaitingLoop;
        private bool          _isShowingPanel = true;
        private System.Random _random;

        /****************************************
        *              Properties
        ****************************************/

        public EHardPattern Selected => _selected;

        public bool IsRunning => _isRunning;

        public bool IsPaused => _isPaused;

        /// <summary> 알림 시각이 되면 알린다, 패턴, 알림 이름, 알림 번호. 사운드, 이펙트, 모션 연결용이다 </summary>
        public event Action<EHardPattern, string, int> CueRaised;

        private Vector3 ArenaCenter => (_arenaCenter != null) ? _arenaCenter.position : Vector3.zero;

        private Vector3 BossPosition => (_boss != null) ? _boss.position : ArenaCenter;

        private Vector3 BossForward => (_boss != null) ? _boss.forward : Vector3.forward;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if ((_hardView == null) || (_catalog == null))
            {
                Debug.LogError("[BossHardPatternLab] 표시 뷰와 패턴 카탈로그가 지정되지 않았다", this);
                enabled = false;
                return;
            }

            _hardView.SetServerClock(ReadClock);
            if (_presenter != null)
            {
                _presenter.SetServerClock(ReadClock);
            }

            Time.timeScale = _speed;
            if (_playOnStart)
            {
                Play(_selected);
            }
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
        }

        private void Update()
        {
            HandleKeys();

            double now = ReadClock();
            while ((_queue.Count > 0) && (_queue[0].Time <= now))
            {
                Scheduled item = _queue[0];
                _queue.RemoveAt(0);
                item.Call?.Invoke();
                Raise(item.Cue, item.Index, item.Time);
            }

            if (!_isRunning)
            {
                return;
            }

            if ((_queue.Count == 0) && (now >= _runEnd))
            {
                _isRunning = false;
                if (_autoLoop)
                {
                    _isWaitingLoop = true;
                    _nextPlayAt    = now + _loopGap;
                }
            }
        }

        private void LateUpdate()
        {
            if (_isWaitingLoop && (ReadClock() >= _nextPlayAt))
            {
                Play(_selected);
            }
        }

        private void OnGUI()
        {
            if (!_isShowingPanel)
            {
                if (GUI.Button(new Rect(PANEL_MARGIN, PANEL_MARGIN, OPEN_BUTTON_WIDTH, OPEN_BUTTON_HEIGHT), "패널 열기 (H)"))
                {
                    _isShowingPanel = true;
                }

                return;
            }

            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(UI_SCALE, UI_SCALE, 1f));
            DrawPanel();
            GUI.matrix = saved;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지금 진행 중인 표현을 지우고 패턴을 처음부터 다시 재생한다 </summary>
        public void Play(EHardPattern pattern)
        {
            Stop();
            _selected = pattern;

            if (!_catalog.TryGetPattern(PATTERN_IDS[(int)pattern], out PatternDefinition definition))
            {
                Debug.LogError($"[BossHardPatternLab] 카탈로그에 {PATTERN_IDS[(int)pattern]} 정의가 없다", this);
                return;
            }

            _random    = (_barrageSeed != 0) ? new System.Random(_barrageSeed) : new System.Random();
            _runStart  = ReadClock();
            _runEnd    = _runStart;
            _isRunning = true;
            _log.Clear();

            switch (pattern)
            {
                case EHardPattern.P01StarStage:
                    BuildStar(definition, _runStart);
                    break;
                case EHardPattern.P02GridBeam:
                    BuildGrid(definition, _runStart);
                    break;
                case EHardPattern.P03ChainFan:
                    BuildChainFan(definition, _runStart);
                    break;
                case EHardPattern.P04TrackingArea:
                    BuildTrackingArea(definition, _runStart);
                    break;
                case EHardPattern.P05Barrage:
                    BuildBarrage(definition, _runStart);
                    break;
                default:
                    BuildBurst(definition, _runStart);
                    break;
            }

            _runEnd += RUN_TAIL_SECONDS;
        }

        /// <summary> 재생을 멈추고 화면의 표현을 모두 지운다 </summary>
        public void Stop()
        {
            _queue.Clear();
            _isRunning     = false;
            _isWaitingLoop = false;

            if (_hardView != null)
            {
                _hardView.ResetNetworkSession();
            }

            if (_presenter != null)
            {
                _presenter.ClearAll();
            }
        }

        public void TogglePause()
        {
            _isPaused      = !_isPaused;
            Time.timeScale = _isPaused ? 0f : _speed;
        }

        private double ReadClock()
        {
            return Time.timeAsDouble;
        }

        private int NextId()
        {
            int id = _nextId;
            ++_nextId;
            return id;
        }

        // 시각표에 줄을 더한다. 같은 시각은 더한 순서를 지킨다
        private void Schedule(double time, string cue, int index, Action call)
        {
            Scheduled item = new Scheduled { Time = time, Cue = cue, Index = index, Call = call };
            int position = _queue.Count;
            while ((position > 0) && (_queue[position - 1].Time > time))
            {
                --position;
            }

            _queue.Insert(position, item);
            _runEnd = Math.Max(_runEnd, time);
        }

        private void Raise(string cue, int index, double scheduledTime)
        {
            if (string.IsNullOrEmpty(cue))
            {
                return;
            }

            string suffix = (index >= 0) ? $" {index}" : string.Empty;
            _log.Add($"+{(scheduledTime - _runStart):0.00}s  {cue}{suffix}");
            if (_log.Count > LOG_CAPACITY)
            {
                _log.RemoveAt(0);
            }

            CueRaised?.Invoke(_selected, cue, index);
        }

        /****************************************
        *            Pattern Builders
        ****************************************/

        // 서버 BossStarStagePattern: 단계 k의 타격은 시작 + 첫 타격 시간 + k x 간격, 위치 예고는 이전 타격부터 자기 타격까지다
        private void BuildStar(PatternDefinition definition, double start)
        {
            int castId = NextId();
            int stageCount = definition.RawStarStageCount;
            float telegraph = definition.TelegraphSeconds;
            float step = definition.StarStageDegrees; // 서버 계약상 단계 각도는 항상 양수이고 기준 방향에서 오른쪽으로 돈다
            BossStarShape shape = new BossStarShape(ArenaCenter, _arenaRadius, BossForward, definition.StarRingRadius,
                definition.StarVertexCount, definition.StarConnectStep, definition.Shape.Width, 0f);

            Schedule(start, "예고 시작", NO_INDEX, () => _hardView.StarStage.Begin(castId, NextId(), in shape, step, stageCount, start, telegraph,
                definition.StarStageIntervalSeconds));

            for (int k = 0; k < stageCount; ++k)
            {
                int stage = k;
                double at = start + telegraph + (k * (double)definition.StarStageIntervalSeconds);
                Schedule(at, "단계 타격", stage, () => _hardView.StarStage.Struck(castId, stage, step * stage, at));
            }
        }

        // 서버 BossGridBeamPattern: 줄 k의 타격은 시작 + 첫 타격 시간 + 오프셋, 예고는 타격보다 첫 타격 시간만큼 앞선다. 가로 N줄 뒤 세로 N줄이다
        private void BuildGrid(PatternDefinition definition, double start)
        {
            int castId = NextId();
            int lineCount = definition.RawGridLineCount;
            float telegraph = definition.TelegraphSeconds;
            float interval = definition.GridStrikeIntervalSeconds;
            float axisGap = definition.GridAxisGapSeconds;

            Schedule(start, "시전 시작", NO_INDEX, () => _hardView.GridBeam.Begin(castId, ArenaCenter, _arenaRadius, lineCount, definition.Shape.Width,
                definition.GridLineSpacing, start, telegraph, interval, axisGap));

            for (int k = 0; k < (lineCount * 2); ++k)
            {
                bool isHorizontal = k < lineCount;
                int lineIndex = k % lineCount;
                double offset = isHorizontal ? (k * (double)interval) : (((lineCount - 1) * (double)interval) + axisGap + ((k - lineCount) * (double)interval));
                double strikeAt = start + telegraph + offset;
                double telegraphAt = strikeAt - telegraph;
                int lineId = NextId();
                string axisName = isHorizontal ? "가로" : "세로";

                Schedule(telegraphAt, axisName + " 줄 예고", lineIndex, () => _hardView.GridBeam.Telegraph(castId, lineId, isHorizontal, lineIndex, telegraphAt, strikeAt));
                Schedule(strikeAt, axisName + " 줄 타격", lineIndex, () => _hardView.GridBeam.Strike(castId, lineId, isHorizontal, lineIndex, strikeAt));
            }
        }

        // 서버 BossChainFanPattern: 피난 방향 공개 -> 구역 전체 예고 -> 구역 k의 폭발은 시작 + 첫 폭발 시간 + k x 간격 -> 마지막 폭발 뒤 잔류 시간이 끝나면 위험 종료
        private void BuildChainFan(PatternDefinition definition, double start)
        {
            int castId = NextId();
            int zoneCount = definition.SweepZoneCount;
            float zoneArc = definition.Shape.ArcAngle / zoneCount;
            float firstBlast = definition.TelegraphSeconds;
            Vector3 safeDirection = Quaternion.AngleAxis(_chainSafeAngle, Vector3.up) * BossForward;
            Vector3 origin = BossPosition;

            Schedule(start, "피난 방향 공개", NO_INDEX, () => _hardView.ChainFan.Begin(castId, origin, safeDirection, definition.Shape.Range, zoneCount, zoneArc,
                start, definition.ChainSafeRevealSeconds, firstBlast, definition.SweepIntervalSeconds, definition.ChainLingerSeconds));
            Schedule(start + definition.ChainSafeRevealSeconds, "폭발 구역 예고", NO_INDEX, null);

            double lastBlast = start;
            for (int k = 0; k < zoneCount; ++k)
            {
                int zone = k;
                double at = start + firstBlast + (k * (double)definition.SweepIntervalSeconds);
                lastBlast = at;
                Schedule(at, "구역 폭발", zone, () => _hardView.ChainFan.Blast(castId, zone, at));
            }

            double endAt = lastBlast + definition.ChainLingerSeconds;
            Schedule(endAt, "위험 종료", NO_INDEX, () => _hardView.ChainFan.EndDanger(castId, endAt));
        }

        // 서버 BossHardAreaPattern: 대상을 따라가는 예고(추적 시간), 중심 고정(고정 시간), 활성. 활성 뒤의 장판은 기존 장판 표현이 그린다
        private void BuildTrackingArea(PatternDefinition definition, double start)
        {
            int castId = NextId();
            int targetCount = definition.AreaTargetCount;
            float follow = definition.TrackingSeconds;
            float fixedSeconds = definition.ActiveSeconds;
            float radius = definition.Shape.Range;
            double fixAt = start + follow;
            double activateAt = fixAt + fixedSeconds;

            for (int j = 0; j < targetCount; ++j)
            {
                int target = j;
                int areaId = NextId();
                float phase = (j * FULL_TURN / targetCount) * Mathf.Deg2Rad;

                Schedule(start, "추적 시작", target, () => _hardView.TrackingArea.BeginMoving(castId, areaId, target + 1, TargetPosition(phase, start, start),
                    radius, start, follow, fixedSeconds));

                for (double t = start + AREA_UPDATE_STEP; t < fixAt; t += AREA_UPDATE_STEP)
                {
                    double at = t;
                    Schedule(at, null, NO_INDEX, () => _hardView.TrackingArea.UpdateMoving(areaId, TargetPosition(phase, start, at), at));
                }

                Vector3 fixedCenter = TargetPosition(phase, start, fixAt);
                Schedule(fixAt, "중심 고정", target, () => _hardView.TrackingArea.FixCenter(areaId, fixedCenter, fixAt));
                Schedule(activateAt, "장판 활성", target, () => ActivateArea(definition, castId, areaId, fixedCenter, radius, activateAt));
            }
        }

        private Vector3 TargetPosition(float phase, double start, double at)
        {
            float angle = phase + ((float)(at - start) * TARGET_SPEED);
            return ArenaCenter + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * TARGET_ORBIT);
        }

        private void ActivateArea(PatternDefinition definition, int castId, int areaId, Vector3 center, float radius, double activateAt)
        {
            _hardView.NotifyAreaActivated(areaId);
            if (_presenter == null)
            {
                return;
            }

            BossAreaStart area = new BossAreaStart(SAMPLE_BOSS_ID, areaId, castId, center, radius, _areaPreviewSeconds,
                definition.AreaTickInterval, activateAt);
            _presenter.ActivateArea(in area);

            int ticks = Mathf.CeilToInt(_areaPreviewSeconds / definition.AreaTickInterval);
            for (int i = 0; i < ticks; ++i)
            {
                int tick = i;
                double at = activateAt + (i * (double)definition.AreaTickInterval);
                Schedule(Math.Max(at, ReadClock()), "장판 피해 회차", tick, () => _presenter.TickArea(areaId, tick, at));
            }

            double endAt = activateAt + _areaPreviewSeconds;
            Schedule(endAt, "장판 소멸", NO_INDEX, () => _presenter.EndArea(areaId, EBossAreaFate.Expired));
        }

        // 서버 BossBarragePattern: 발 k의 예고는 시작 + k x 간격, 발사는 시작 + 예고 시간 + k x 간격. 구역 순서는 섞고 구역 안 각도는 무작위다
        private void BuildBarrage(PatternDefinition definition, double start)
        {
            int castId = NextId();
            int shotCount = definition.ProjectileCount;
            float telegraph = definition.TelegraphSeconds;
            float interval = definition.ProjectileIntervalSeconds;
            float width = definition.Shape.Width;
            float speed = Mathf.Max(MIN_SPEED, definition.Shape.ProjectileSpeed);
            Vector3 origin = BossPosition;

            int[] sectors = new int[shotCount];
            for (int i = 0; i < shotCount; ++i)
            {
                sectors[i] = i;
            }

            for (int i = shotCount - 1; i > 0; --i)
            {
                int swap = _random.Next(i + 1);
                (sectors[i], sectors[swap]) = (sectors[swap], sectors[i]);
            }

            float sectorArc = FULL_TURN / shotCount;

            Schedule(start, "시전 시작", NO_INDEX, () => _hardView.Barrage.Begin(castId, origin, start, shotCount, telegraph, interval, width));

            for (int k = 0; k < shotCount; ++k)
            {
                int shot = k;
                int sector = sectors[k];
                float angle = (sector + Mathf.Clamp((float)_random.NextDouble(), 0f, MAX_SECTOR_FRACTION)) * sectorArc;
                Vector3 direction = (Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward).normalized;
                float path = PathToWall(origin, direction, definition.Shape.Range);
                double telegraphAt = start + (k * (double)interval);
                double fireAt = start + telegraph + (k * (double)interval);
                int projectileId = NextId();

                Schedule(telegraphAt, "발 예고", shot, () => _hardView.Barrage.TelegraphShot(castId, shot, sector, direction, path, telegraphAt));
                Schedule(fireAt, "발사", shot, () => FireShot(castId, shot, projectileId, origin, direction, speed, width, path, definition.Shape.Range, fireAt));
            }
        }

        private void FireShot(int castId, int shot, int projectileId, Vector3 origin, Vector3 direction, float speed, float width,
            float path, float range, double fireAt)
        {
            _hardView.Barrage.FireShot(castId, shot, true, fireAt);
            if (_presenter == null)
            {
                return;
            }

            BossProjectileStart start = new BossProjectileStart(SAMPLE_BOSS_ID, projectileId, castId, origin, direction, speed, width, path, fireAt);
            _presenter.LaunchProjectile(in start);

            EBossProjectileFate fate = (path < range) ? EBossProjectileFate.Wall : EBossProjectileFate.Range;
            Vector3 endPosition = origin + (direction * path);
            Schedule(fireAt + (path / speed), "투사체 소멸", shot, () => _presenter.EndProjectile(projectileId, fate, endPosition));
        }

        // 전장 원 안에서 원점부터 방향으로 벽까지의 거리, 최대 사거리를 넘지 않는다. 서버는 벽 검사로 얻는 값이고 연습장은 전장 원으로 가정한다
        private float PathToWall(Vector3 origin, Vector3 direction, float maxRange)
        {
            Vector3 offset = origin - ArenaCenter;
            offset.y = 0f;
            float b = Vector3.Dot(offset, direction);
            float c = offset.sqrMagnitude - (_arenaRadius * _arenaRadius);
            float discriminant = (b * b) - c;
            float distance = (discriminant > 0f) ? (-b + Mathf.Sqrt(discriminant)) : maxRange;
            return Mathf.Clamp(distance, MIN_PATH_LENGTH, maxRange);
        }

        // 서버 BossCircleBurstLayout: 0번은 보스 중심 원, 1~12번은 전장 중심 기준 네 사분면에 세 개씩이다
        private void BuildBurst(PatternDefinition definition, double start)
        {
            int castId = NextId();
            Vector3[] centers = new Vector3[BURST_CIRCLE_COUNT];
            float[] radii = new float[BURST_CIRCLE_COUNT];

            centers[0] = BossPosition;
            radii[0]   = definition.Shape.Range;
            for (int i = 1; i < BURST_CIRCLE_COUNT; ++i)
            {
                int spotIndex = i - 1;
                int quadrant = spotIndex / BURST_SPOTS_PER_QUAD;
                int spot = spotIndex % BURST_SPOTS_PER_QUAD;
                float signX = (quadrant < BURST_POSITIVE_X_QUADRANTS) ? 1f : -1f;
                float signZ = ((quadrant == BURST_NEGATIVE_Z_FIRST) || (quadrant == BURST_NEGATIVE_Z_LAST)) ? -1f : 1f;
                float offsetX = (spot == BURST_FAR_X_SPOT) ? definition.BurstFarOffset : definition.BurstNearOffset;
                float offsetZ = (spot == BURST_FAR_Z_SPOT) ? definition.BurstFarOffset : definition.BurstNearOffset;
                centers[i] = ArenaCenter + new Vector3(signX * offsetX, 0f, signZ * offsetZ);
                radii[i]   = definition.BurstSpotRadius;
            }

            double strikeAt = start + definition.TelegraphSeconds;
            Schedule(start, "예고 시작", NO_INDEX, () => _hardView.CircleBurst.Begin(castId, centers, radii, start, definition.TelegraphSeconds));
            Schedule(strikeAt, "동시 폭발", NO_INDEX, () => _hardView.CircleBurst.Struck(castId, strikeAt));
        }

        /****************************************
        *                Input
        ****************************************/

        private void HandleKeys()
        {
            for (int i = 0; i < PATTERN_IDS.Length; ++i)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    Play((EHardPattern)i);
                }
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                Play(_selected);
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                TogglePause();
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                _autoLoop = !_autoLoop;
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                _isShowingPanel = !_isShowingPanel;
            }
        }

        /****************************************
        *                  GUI
        ****************************************/

        private void DrawPanel()
        {
            GUILayout.BeginArea(new Rect(PANEL_MARGIN, PANEL_MARGIN, PANEL_WIDTH, (Screen.height / UI_SCALE) - (PANEL_MARGIN * 2f)), GUI.skin.box);
            GUILayout.Label("월백 하드 2페이즈 패턴 연습장");

            for (int i = 0; i < PATTERN_LABELS.Length; ++i)
            {
                bool isSelected = (int)_selected == i;
                string label = $"{i + 1}  {PATTERN_LABELS[i]}" + (isSelected ? "  <" : string.Empty);
                if (GUILayout.Button(label))
                {
                    Play((EHardPattern)i);
                }
            }

            GUILayout.Space(PANEL_GAP);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("다시 재생 (R)"))
            {
                Play(_selected);
            }

            if (GUILayout.Button(_isPaused ? "재개 (Space)" : "일시정지 (Space)"))
            {
                TogglePause();
            }

            if (GUILayout.Button("정지"))
            {
                Stop();
            }

            GUILayout.EndHorizontal();

            _autoLoop = GUILayout.Toggle(_autoLoop, "반복 재생 (L)");
            GUILayout.Label($"반복 간격 {_loopGap:0.0}초");
            _loopGap = GUILayout.HorizontalSlider(_loopGap, 0f, MAX_GAP);

            GUILayout.Label($"재생 속도 x{_speed:0.00}");
            float speed = GUILayout.HorizontalSlider(_speed, MIN_SPEED, MAX_SPEED);
            if (!Mathf.Approximately(speed, _speed))
            {
                _speed = speed;
                if (!_isPaused)
                {
                    Time.timeScale = _speed;
                }
            }

            double elapsed = Math.Max(0.0, ReadClock() - _runStart);
            string state = _isPaused ? "일시정지" : (_isRunning ? "재생 중" : (_isWaitingLoop ? "다음 반복 대기" : "정지"));
            GUILayout.Space(PANEL_GAP);
            GUILayout.Label($"{state}   {elapsed:0.00}초 / {Math.Max(0.0, _runEnd - _runStart):0.00}초");
            if ((_presenter == null) && ((_selected == EHardPattern.P04TrackingArea) || (_selected == EHardPattern.P05Barrage)))
            {
                GUILayout.Label("표현 진입점이 없어 활성 뒤 장판과 투사체는 생략됩니다");
            }

            GUILayout.Space(PANEL_GAP);
            GUILayout.Label("알림 시각 (패턴 시작 기준)");
            for (int i = 0; i < _log.Count; ++i)
            {
                GUILayout.Label(_log[i]);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("1~6 패턴 선택, R 다시, Space 일시정지, L 반복, H 패널");
            GUILayout.EndArea();
        }
    }
}
#endif
