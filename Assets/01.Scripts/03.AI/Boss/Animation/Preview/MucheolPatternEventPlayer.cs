#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.AI
{
    /// <summary>
    /// 실제 무철 프리팹의 모션 뷰와 VFX 뷰에 서버 사건을 시간표대로 넣어 패턴을 재생하는 시험 도구다. 판정과 네트워크는 없고,
    /// 바인더가 부르는 것과 같은 뷰 API만 부른다. 시간 값은 시험 카탈로그(MucheolDevCatalog)의 임시 시작안이며 하드 전용 정의의
    /// 예고와 회복 시간은 따로 읽지 않고 이지와 같게 둔다.
    /// </summary>
    public sealed class MucheolPatternEventPlayer : MonoBehaviour
    {
        /****************************************
        *              Nested Types
        ****************************************/

        private enum EKind
        {
            Strike,
            Dash,
            Chain,
            MultiDash,
            Battlefield,
        }

        private readonly struct Step
        {
            public readonly EBossTelegraphShape Shape;
            public readonly float               Range;
            public readonly float               Arc;
            public readonly float               Width;
            public readonly float               Aim;
            public readonly float               Delay;

            public Step(EBossTelegraphShape shape, float range, float arc, float width, float aim, float delay)
            {
                Shape = shape;
                Range = range;
                Arc   = arc;
                Width = width;
                Aim   = aim;
                Delay = delay;
            }
        }

        private sealed class Plan
        {
            public string        Label;
            public string        BaseId;
            public EKind         Kind;
            public float         Telegraph;
            public float         Active;      // 실행 구간, 예고가 끝난 뒤 마지막 타격이나 돌진 끝까지
            public float         Recovery;
            public float         Distance;    // 돌진 한 번의 거리, 전장 공격은 반경
            public float         DashSeconds;
            public float         Width;
            public float         HardWidth;
            public float         Arc;
            public int           DashCount;
            public float         ReaimSeconds;
            public EBossStrikeChainKind ChainKind;
            public Step[]        Steps;
            public Step[]        HardSteps;
        }

        private readonly struct Action
        {
            public readonly double Time;
            public readonly System.Action Run;

            public Action(double time, System.Action run)
            {
                Time = time;
                Run  = run;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const int    BOSS_ID           = 1;
        private const float  PANEL_WIDTH       = 300f;
        private const float  PANEL_MARGIN      = 12f;
        private const float  MIN_SPEED         = 0.1f;
        private const float  MAX_SPEED         = 2f;
        private const float  IDLE_HOLD_SECONDS = 1.5f;   // 끝난 뒤 반복 재생까지 쉬는 시간
        private const float  CIRCLE_ARC        = 360f;
        private const float  LINE_WIDTH        = 3f;

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private MucheolMotionView     _motion;
        [SerializeField] private MucheolPatternVfxView _vfx;
        [SerializeField] private Transform            _boss;
        [SerializeField] private bool                 _isHard;
        [SerializeField] private bool                 _repeat;
        [SerializeField, Range(MIN_SPEED, MAX_SPEED)] private float _speed = 1f;

        private readonly List<Plan>   _plans   = new List<Plan>();
        private readonly List<Action> _actions = new List<Action>();

        private double     _clock;
        private double     _finishTime;
        private double     _restartTime;
        private double     _moveStart;
        private double     _moveEnd;
        private int        _nextAction;
        private int        _castId;
        private int        _planIndex = -1;
        private bool       _isPlaying;
        private Vector3    _homePosition;
        private Quaternion _homeRotation;
        private Vector3    _moveFrom;
        private Vector3    _moveTo;
        private Vector2    _scroll;
        private string     _status = "버튼을 눌러 패턴을 재생한다";

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            BuildPlans();
        }

        private void Start()
        {
            _homePosition = _boss.position;
            _homeRotation = _boss.rotation;
            _motion.SetServerClock(ReadClock);
            _vfx.SetServerClock(ReadClock);
        }

        private void Update()
        {
            _clock += Time.unscaledDeltaTime * _speed;
            if (!_isPlaying)
            {
                return;
            }

            while ((_nextAction < _actions.Count) && (_actions[_nextAction].Time <= _clock))
            {
                _actions[_nextAction].Run();
                ++_nextAction;
            }

            UpdateMove();
            if (_clock >= _finishTime)
            {
                EndPattern();
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(PANEL_MARGIN, PANEL_MARGIN, PANEL_WIDTH, Screen.height - (PANEL_MARGIN * 2f)), GUI.skin.box);
            GUILayout.Label("무철 패턴 재생 (실제 프리팹)");
            GUILayout.Label(_status);
            _isHard = GUILayout.Toggle(_isHard, "하드 (범위 1.4배)");
            _repeat = GUILayout.Toggle(_repeat, "끝나면 반복");
            GUILayout.Label("재생 속도: " + _speed.ToString("F2") + "x");
            _speed = GUILayout.HorizontalSlider(_speed, MIN_SPEED, MAX_SPEED);
            if (GUILayout.Button("정지 / 초기화"))
            {
                StopAll();
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _plans.Count; ++i)
            {
                if (GUILayout.Button(_plans[i].Label))
                {
                    Play(i);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void OnDisable()
        {
            // 재생을 끌 때 보스가 먼저 파괴돼 있을 수 있다
            if ((_boss == null) || (_motion == null) || (_vfx == null))
            {
                return;
            }

            StopAll();
        }

        /****************************************
        *                Methods
        ****************************************/

        private double ReadClock()
        {
            return _clock;
        }

        private void Play(int index)
        {
            StopAll();
            _planIndex = index;
            Plan plan = _plans[index];
            ++_castId;
            _actions.Clear();
            _nextAction = 0;

            // 같은 서버 시각 기준에서 지금 시각에 시작한다
            double start = _clock;
            Vector3 origin = _boss.position;
            Vector3 direction = _boss.forward;
            switch (plan.Kind)
            {
                case EKind.Strike:
                    BuildStrike(plan, start, origin, direction);
                    break;
                case EKind.Dash:
                    BuildDash(plan, start, origin, direction);
                    break;
                case EKind.Chain:
                    BuildChain(plan, start, origin, direction);
                    break;
                case EKind.MultiDash:
                    BuildMultiDash(plan, start, origin, direction);
                    break;
                case EKind.Battlefield:
                    BuildBattlefield(plan, start, origin, direction);
                    break;
            }

            int cast = _castId;
            _finishTime = start + plan.Telegraph + plan.Active + plan.Recovery;
            _actions.Add(new Action(_finishTime, () => { _motion.FinishCast(cast); _vfx.FinishCast(cast); }));
            _actions.Sort((a, b) => a.Time.CompareTo(b.Time));
            _isPlaying = true;
            _status = plan.Label + (_isHard ? " (하드)" : string.Empty);
        }

        private void StopAll()
        {
            if (_isPlaying)
            {
                _motion.CancelCast(_castId);
                _vfx.CancelCast(_castId);
            }

            _motion.ResetSession();
            _vfx.ResetSession();
            _isPlaying = false;
            _boss.SetPositionAndRotation(_homePosition, _homeRotation);
            _moveEnd = 0d;
        }

        private void EndPattern()
        {
            _isPlaying = false;
            _boss.SetPositionAndRotation(_homePosition, _homeRotation);
            _moveEnd = 0d;
            if (_repeat)
            {
                int index = _planIndex;
                _restartTime = _clock + IDLE_HOLD_SECONDS;
                _actions.Clear();
                _actions.Add(new Action(_restartTime, () => Play(index)));
                _nextAction = 0;
                _finishTime = double.MaxValue;
                _isPlaying = true;
                _status = "대기 후 반복";
            }
        }

        private string Id(Plan plan)
        {
            return _isHard ? (BossPatternCatalogSO.ToMucheolHardId(plan.BaseId) ?? plan.BaseId) : plan.BaseId;
        }

        private void At(double time, System.Action run)
        {
            _actions.Add(new Action(time, run));
        }

        private void BuildStrike(Plan plan, double start, Vector3 origin, Vector3 direction)
        {
            int cast = _castId;
            string id = Id(plan);
            float scale = _isHard ? MucheolVfxCatalog.HARD_FAN_SCALE : 1f;
            double impact = start + plan.Telegraph;
            var telegraph = new BossTelegraphStart(BOSS_ID, cast, id,
                plan.Arc >= CIRCLE_ARC ? EBossTelegraphShape.Circle : EBossTelegraphShape.Fan, origin, direction,
                plan.Distance * scale, 0f, plan.Arc, start, plan.Telegraph);
            At(start, () => { _motion.BeginTelegraph(cast, id, start, plan.Telegraph); _vfx.BeginBasic(in telegraph); });
            At(impact, () => _vfx.ConfirmBasicStrike(cast, impact));
        }

        private void BuildDash(Plan plan, double start, Vector3 origin, Vector3 direction)
        {
            int cast = _castId;
            string id = Id(plan);
            float width = _isHard ? plan.HardWidth : plan.Width;
            double run = start + plan.Telegraph;
            double stop = run + plan.DashSeconds;
            float speed = plan.Distance / plan.DashSeconds;
            Vector3 end = origin + (direction * plan.Distance);
            var telegraph = new BossTelegraphStart(BOSS_ID, cast, id, EBossTelegraphShape.Dash, origin, direction,
                plan.Distance, width, 0f, start, plan.Telegraph);
            At(start, () => { _motion.BeginTelegraph(cast, id, start, plan.Telegraph); _vfx.BeginBasic(in telegraph); });
            At(run, () =>
            {
                _motion.BeginDash(cast, plan.Distance, plan.DashSeconds, speed, run);
                _vfx.BeginBasicDash(cast, plan.Distance, plan.DashSeconds, speed, run);
                Move(origin, end, run, stop);
            });
            At(stop, () => { _motion.StopDash(cast, 0, stop); _vfx.StopBasicDash(cast, end, stop); });
        }

        private void BuildChain(Plan plan, double start, Vector3 origin, Vector3 direction)
        {
            int cast = _castId;
            string id = Id(plan);
            Step[] steps = (_isHard && (plan.HardSteps != null)) ? plan.HardSteps : plan.Steps;
            var shapes  = new EBossTelegraphShape[steps.Length];
            var ranges  = new float[steps.Length];
            var arcs    = new float[steps.Length];
            var widths  = new float[steps.Length];
            var aims    = new float[steps.Length];
            var delays  = new float[steps.Length];
            for (int i = 0; i < steps.Length; ++i)
            {
                shapes[i] = steps[i].Shape;
                ranges[i] = steps[i].Range;
                arcs[i]   = steps[i].Arc;
                widths[i] = steps[i].Width;
                aims[i]   = steps[i].Aim;
                delays[i] = steps[i].Delay;
            }

            double execute = start + plan.Telegraph;
            var chain = new BossStrikeChainStart(BOSS_ID, cast, id, plan.ChainKind, origin, direction, start, plan.Telegraph,
                shapes, ranges, arcs, widths, aims, delays);
            At(start, () => { _motion.BeginStrikeChain(in chain); _vfx.BeginStrikeChain(in chain); });
            for (int i = 0; i < steps.Length; ++i)
            {
                int step = i;
                double hit = execute + steps[i].Delay;
                At(hit, () => _vfx.ConfirmStrikeChainHit(cast, step, hit));
            }
        }

        private void BuildMultiDash(Plan plan, double start, Vector3 origin, Vector3 direction)
        {
            int cast = _castId;
            string id = Id(plan);
            float width = _isHard ? plan.HardWidth : plan.Width;
            float speed = plan.Distance / plan.DashSeconds;
            var begin = new BossMultiDashStart(BOSS_ID, cast, id, origin, direction, plan.Distance, width, start, plan.Telegraph,
                plan.DashCount);
            At(start, () => { _motion.BeginMultiDash(in begin); _vfx.BeginMultiDash(in begin); });

            double run = start + plan.Telegraph;
            Vector3 from = origin;
            for (int i = 0; i < plan.DashCount; ++i)
            {
                int index = i;
                double runStart = run;
                double stop = runStart + plan.DashSeconds;
                Vector3 to = from + (direction * plan.Distance);
                Vector3 legFrom = from;
                At(runStart, () =>
                {
                    _motion.BeginMultiDashRun(cast, index, plan.Distance, plan.DashSeconds, speed, runStart);
                    _vfx.BeginMultiDashRun(cast, index, plan.Distance, plan.DashSeconds, speed, runStart);
                    Move(legFrom, to, runStart, stop);
                });
                At(stop, () => { _motion.StopDash(cast, index, stop); _vfx.StopMultiDashRun(cast, index, to, stop); });

                from = to;
                run = stop;
                if (i + 1 < plan.DashCount)
                {
                    int next = i + 1;
                    double reaimStart = stop;
                    Vector3 reaimOrigin = to;
                    var reaim = new BossMultiDashReaim(cast, next, reaimOrigin, -direction, plan.Distance, width, reaimStart,
                        plan.ReaimSeconds);
                    At(reaimStart, () => { _motion.BeginMultiDashReaim(in reaim); _vfx.BeginMultiDashReaim(in reaim); });
                    run = stop + plan.ReaimSeconds;
                    direction = -direction;
                }
            }
        }

        private void BuildBattlefield(Plan plan, double start, Vector3 origin, Vector3 direction)
        {
            int cast = _castId;
            double impact = start + plan.Telegraph;
            var field = new BossBattlefieldStart(BOSS_ID, cast, origin, direction, plan.Distance, plan.Arc, start, plan.Telegraph);
            At(start, () => { _motion.BeginBattlefield(in field); _vfx.BeginBattlefield(in field); });
            At(impact, () => _vfx.ConfirmBattlefield(cast, impact));
        }

        // 서버가 움직이는 보스 몸체를 대신해 돌진 구간만 직선으로 옮긴다
        private void Move(Vector3 from, Vector3 to, double start, double end)
        {
            _moveFrom  = from;
            _moveTo    = to;
            _moveStart = start;
            _moveEnd   = end;
        }

        private void UpdateMove()
        {
            if ((_moveEnd <= 0d) || (_clock < _moveStart))
            {
                return;
            }

            float t = Mathf.Clamp01((float)((_clock - _moveStart) / (_moveEnd - _moveStart)));
            _boss.position = Vector3.Lerp(_moveFrom, _moveTo, t);
        }

        private static Step Fan(float range, float arc, float aim, float delay)
        {
            return new Step(EBossTelegraphShape.Fan, range, arc, 0f, aim, delay);
        }

        private static Step CircleStep(float radius, float delay)
        {
            return new Step(EBossTelegraphShape.Circle, radius, CIRCLE_ARC, 0f, 0f, delay);
        }

        private static Step LineStep(float length, float delay)
        {
            return new Step(EBossTelegraphShape.Line, length, 0f, LINE_WIDTH, 0f, delay);
        }

        private void BuildPlans()
        {
            _plans.Clear();

            // 1페이즈, 시간은 MucheolDevCatalog의 예고와 회복 시작안이다
            _plans.Add(new Plan { Label = "P1 P01 팔 휘두르기", BaseId = MucheolMotionCatalog.P1_P01_ID, Kind = EKind.Strike,
                Telegraph = 1.4f, Active = 0f, Recovery = 1.2f, Distance = 5f, Arc = 120f });
            _plans.Add(new Plan { Label = "P1 P02 양손 내려찍기", BaseId = MucheolMotionCatalog.P1_P02_ID, Kind = EKind.Strike,
                Telegraph = 2.4f, Active = 0f, Recovery = 2.4f, Distance = 5f, Arc = 70f });
            _plans.Add(new Plan { Label = "P1 P03 어깨 돌진", BaseId = MucheolMotionCatalog.P1_P03_ID, Kind = EKind.Dash,
                Telegraph = 1.8f, Active = 0.5f, Recovery = 1.6f, Distance = 8f, DashSeconds = 0.5f, Width = 2.5f, HardWidth = 3f });
            _plans.Add(new Plan { Label = "P1 P04 발구르기 충격파", BaseId = MucheolMotionCatalog.P1_P04_ID, Kind = EKind.Strike,
                Telegraph = 2f, Active = 0f, Recovery = 1.6f, Distance = 5f, Arc = CIRCLE_ARC });
            _plans.Add(new Plan { Label = "P1 P05 2연타", BaseId = MucheolMotionCatalog.P1_P05_ID, Kind = EKind.Chain,
                ChainKind = EBossStrikeChainKind.Combo, Telegraph = 1.6f, Active = 0.5f, Recovery = 2.4f,
                Steps = new[] { Fan(5f, 120f, 0f, 0f), Fan(5f, 70f, 0f, 0.5f) },
                HardSteps = new[] { Fan(7f, 120f, 0f, 0f), Fan(7f, 70f, 0f, 0.5f) } });
            _plans.Add(new Plan { Label = "P1 P06 뿔 들이받기", BaseId = MucheolMotionCatalog.P1_P06_ID, Kind = EKind.Dash,
                Telegraph = 3.2f, Active = 0.75f, Recovery = 3f, Distance = 18f, DashSeconds = 0.75f, Width = 3f, HardWidth = 3.5f });

            // 2페이즈
            _plans.Add(new Plan { Label = "P2 P01 연속 휘두르기", BaseId = MucheolMotionCatalog.P2_P01_ID, Kind = EKind.Chain,
                ChainKind = EBossStrikeChainKind.Sequence, Telegraph = 1.8f, Active = 0.45f, Recovery = 1.4f,
                Steps = new[] { Fan(5f, 60f, -30f, 0f), Fan(5f, 60f, 30f, 0.45f) },
                HardSteps = new[] { Fan(7f, 60f, -30f, 0f), Fan(7f, 60f, 30f, 0.35f) } });
            _plans.Add(new Plan { Label = "P2 P02 충격 내려찍기", BaseId = MucheolMotionCatalog.P2_P02_ID, Kind = EKind.Chain,
                ChainKind = EBossStrikeChainKind.Sequence, Telegraph = 2.4f, Active = 0.5f, Recovery = 2.4f,
                Steps = new[] { Fan(5f, 70f, 0f, 0f), LineStep(6f, 0.5f) },
                HardSteps = new[] { Fan(7f, 70f, 0f, 0f), LineStep(8f, 0.5f) } });
            _plans.Add(new Plan { Label = "P2 P03 연속 어깨 돌진", BaseId = MucheolMotionCatalog.P2_P03_ID, Kind = EKind.MultiDash,
                Telegraph = 1.8f, Active = 1.4f, Recovery = 1.8f, Distance = 8f, DashSeconds = 0.5f, Width = 2.5f, HardWidth = 3f,
                DashCount = 2, ReaimSeconds = 0.4f });
            _plans.Add(new Plan { Label = "P2 P04 이중 지면 충격파", BaseId = MucheolMotionCatalog.P2_P04_ID, Kind = EKind.Chain,
                ChainKind = EBossStrikeChainKind.Sequence, Telegraph = 2f, Active = 0.7f, Recovery = 1.8f,
                Steps = new[] { CircleStep(5f, 0f), CircleStep(6f, 0.7f) },
                HardSteps = new[] { CircleStep(6.5f, 0f), CircleStep(7.8f, 0.7f) } });
            _plans.Add(new Plan { Label = "P2 P05 폭주 연타", BaseId = MucheolMotionCatalog.P2_P05_ID, Kind = EKind.Chain,
                ChainKind = EBossStrikeChainKind.Sequence, Telegraph = 1.8f, Active = 1f, Recovery = 3f,
                Steps = new[] { Fan(5f, 60f, -30f, 0f), Fan(5f, 60f, 30f, 0.45f), Fan(5f, 70f, 0f, 1f) },
                HardSteps = new[] { Fan(7f, 60f, -30f, 0f), Fan(7f, 60f, 30f, 0.35f), Fan(7f, 70f, 0f, 0.85f) } });
            _plans.Add(new Plan { Label = "P2 P06 폭주 돌진", BaseId = MucheolMotionCatalog.P2_P06_ID, Kind = EKind.MultiDash,
                Telegraph = 4f, Active = 3.45f, Recovery = 3.6f, Distance = 18f, DashSeconds = 0.75f, Width = 3f, HardWidth = 3.5f,
                DashCount = 2, ReaimSeconds = 0.6f });
            _plans.Add(new Plan { Label = "P2 P07 전장 공격 (하드 전용)", BaseId = MucheolMotionCatalog.P2_P07_ID, Kind = EKind.Battlefield,
                Telegraph = 8f, Active = 0f, Recovery = 3.6f, Distance = 44f, Arc = 315f });
        }
    }
}
#endif
