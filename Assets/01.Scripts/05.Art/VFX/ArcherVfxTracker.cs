// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 궁수 효과 모양, 화살과 적중은 속성(Element)에 따라 다른 프리팹을 쓴다 </summary>
    public enum EArcherVfxKind
    {
        // 날아가는 화살, IsMuzzle이면 시위를 놓는 순간의 총구 효과
        Arrow,
        // 서버가 알린 화살 적중 위치의 효과
        Hit,
        // 속성 부여 기술이 발동한 순간 시전자 위치의 효과
        Switch,
        // 속성 폭발의 준비(차지) 동안 시전자 위치의 효과
        Charge,
        // 속성 폭발 지점의 큰 폭발
        Burst
    }

    /// <summary> 공개 전투 상태의 현재값만으로 궁수 화살 발사 시점과 취소를 정하는 순수 추적기, 엔진 오브젝트에 의존하지 않아 EditMode에서 시험한다 </summary>
    public sealed class ArcherVfxTracker
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int    SLOT_COUNT                = 4;
        public const int    MAX_PRONGS                = 9;
        public const int    MAX_REPEAT                = 8;
        public const int    NO_SLOT                   = -1;
        // 이 속도 이하의 이동 속도는 정지로 본다
        public const float  LOCOMOTION_EPSILON        = 0.05f;

        // 한 프레임에 화살 갈래와 함께 나올 수 있는 화살 밖 재생(적중, 발사, 폭발 차지, 폭발) 자리
        private const int NON_ARROW_PLAYS = 4;
        private const int MAX_PENDING     = 3;
        private const int MAX_PLAYS       = (MAX_REPEAT * (MAX_PRONGS + 1)) + NON_ARROW_PLAYS;

        private const float CENTER_INDEX_SCALE = 0.5f;
        // 준비 시간을 틱 수로 올림할 때 부동소수 오차로 한 틱 더 올라가지 않게 빼는 값
        private const double TICK_ROUNDING     = 1e-4;
        // 서버 틱 시각으로 가른 이동 시작과 발사 사이에 두는 여유, 틱 간격에 곱한다
        private const float  TICK_MARGIN_SCALE = 0.5f;

        private enum EPendingKind : byte
        {
            Basic,
            Shot,
            Switch,
            Burst
        }

        /// <summary> 한 번의 발사 규격, 기본 공격과 기술이 같은 모양을 쓴다. 값은 모두 게임 데이터에서 읽는다 </summary>
        public struct ShotSpec
        {
            public float Speed;
            public float Range;
            // 한 발사에서 동시에 나가는 갈래 수, 0이나 1이면 한 갈래
            public int   ProjectileCount;
            // 인접한 갈래 사이 각도(도)
            public float SpreadAngle;
            // 순차 발사 횟수, 0이나 1이면 한 번
            public int   RepeatCount;
            public float RepeatInterval;
        }

        public struct Config
        {
            public float    BasicWindup;
            // 서버 콤보 단계별 준비 시간, 비어 있거나 단계가 넘치면 BasicWindup을 쓴다
            public float[]  BasicStepWindups;
            public int[]    BasicStepShotCounts;
            public float[]  BasicStepShotIntervals;
            public bool     BasicCancelOnMove;
            public ShotSpec Basic;
            // 발사 시각을 이만큼 넘겨 관찰한 총구 효과는 이미 지난 일이라 재생하지 않는다
            public float    MaxLateSeconds;
            // 바람 속성 기본 공격 화살 속도 배율, 0 이하이면 1로 본다
            public float    WindSpeedScale;
            // 참일 때만 BurstSlot을 속성 폭발 슬롯으로 쓴다. 기본값 0이 Q로 읽히지 않게 따로 둔다
            public bool     HasBurstSlot;
            public int      BurstSlot;
            // 폭발 시각을 이만큼 넘겨 관찰하면 폭발을 재생하지 않는다
            public float    MaxBurstLateSeconds;
            // 서버 전투 틱 간격, 서버는 준비가 끝난 뒤 첫 틱에 쏜다. 0이면 준비 시간 그대로 본다
            public float    ServerTickSeconds;
        }

        /// <summary> 한 프레임에 모은 공개 상태 현재값 </summary>
        public struct Observation
        {
            public EPlayerClass          PlayerClass;
            public EParticipantLifeState LifeState;
            public byte                  AttackSequence;
            public byte                  AttackComboStep;
            // 서버가 공격을 승인한 시각과 이동을 시작한 마지막 시각(서버 시각), 둘 중 하나라도 0이면 관찰된 이동을 모두 취소로 본다
            public double                AttackStartedAt;
            public double                LocomotionStartedAt;
            public byte                  DodgeSequence;
            public byte                  ActionCancelSequence;
            public int                   CastInstanceId;
            public ESkillPhase           SkillPhase;
            public ESkillRequestKind     SkillKind;
            public byte                  SkillSlot;
            public double                SkillResolveAt;
            public Vector3               SkillAim;
            public Vector3               SkillPoint;
            public Vector3               Facing;
            public float                 LocomotionSpeed;
            public EArcherElement        ArcherElement;
            public byte                  ArrowHitSequence;
            public Vector3               ArrowHitPosition;
            public EArcherElement        ArrowHitElement;
            public bool                  ArrowHitIsHeavy;
            public double                Now;
        }

        /// <summary> 지금 재생할 효과 하나, 시각은 모두 서버 시각이고 출발 위치는 재생 순간의 캐릭터 위치다 </summary>
        public struct Play
        {
            public EArcherVfxKind Kind;
            public EArcherElement Element;
            // 참이면 총구 효과, 거짓이면 날아가는 투사체. 화살에만 쓴다
            public bool           IsMuzzle;
            public Vector3        Direction;
            // 서버가 이 발사를 내보낸 시각
            public double         StartAt;
            public float          Speed;
            public float          Range;
            // 참이면 서버 화살 적중 신호로 멈추는 기본 공격 화살이다. 기술 화살은 적중 신호를 보내지 않는다
            public bool           StopsOnHit;
            // 참이면 강공격 단계의 화살과 총구, 적중 효과다
            public bool           IsHeavy;
            // 적중과 폭발의 월드 위치. 화살, 부여, 차지는 캐릭터 위치에서 재생하므로 쓰지 않는다
            public bool           HasPosition;
            public Vector3        Position;
        }

        private struct Pending
        {
            public bool           Active;
            public EPendingKind   Kind;
            public int            CastId;
            public int            Slot;
            public Vector3        Direction;
            public Vector3        Point;
            public EArcherElement Element;
            public bool           IsHeavy;
            // 기본 공격은 관찰 시각에서 정한 발사 시각, 기술은 서버 SkillResolveAt
            public double         ResolveAt;
            public int            NextShot;
            // 기본 공격만 쓴다. 서버가 발사하는 틱에 맞춘 재생 판단 시각, 승인 서버 시각, 틱으로 올림한 준비 시간
            public double         DecideAt;
            public double         ServerStartAt;
            public float          FireDelay;
            public float          BasicWindup;
            public int            ShotCount;
            public float          ShotInterval;
        }

        private readonly Pending[]        _pending  = new Pending[MAX_PENDING];
        private readonly Play[]           _plays    = new Play[MAX_PLAYS];
        private readonly ShotSpec[]       _skills   = new ShotSpec[SLOT_COUNT];
        private readonly EArcherElement[] _elements = new EArcherElement[SLOT_COUNT];

        private ShotSpec _basic;
        private float    _basicWindup;
        private float[]  _basicStepWindups;
        private int[]    _basicStepShotCounts;
        private float[]  _basicStepShotIntervals;
        private bool     _basicCancelOnMove;
        private bool     _emitHeavy;
        private float    _maxLate;
        private float    _windSpeedScale = 1f;
        private int      _burstSlot      = NO_SLOT;
        private float    _maxBurstLate;
        private float    _serverTick;

        private bool _hasBaseline;
        private bool _wasAlive;
        private byte _attackBaseline;
        private byte _dodgeBaseline;
        private byte _cancelBaseline;
        private byte _hitBaseline;
        private int  _castBaseline;

        /****************************************
        *              Properties
        ****************************************/

        public bool HasPending
        {
            get
            {
                for (int i = 0; i < _pending.Length; ++i)
                {
                    if (_pending[i].Active)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary> 첫 관찰을 기준값으로 소비했으면 참 </summary>
        public bool HasBaseline => _hasBaseline;

        /// <summary> 이 콤보 단계의 기본 공격은 강공격 효과로 그린다. -1이면 모든 단계가 보통 효과다 </summary>
        public int HeavyBasicStep { get; set; } = -1;

        /****************************************
        *                Methods
        ****************************************/

        public void Configure(in Config config)
        {
            _basic             = Normalize(config.Basic);
            _basicWindup       = Mathf.Max(0f, config.BasicWindup);
            _basicStepWindups  = config.BasicStepWindups;
            _basicStepShotCounts = config.BasicStepShotCounts;
            _basicStepShotIntervals = config.BasicStepShotIntervals;
            _basicCancelOnMove = config.BasicCancelOnMove;
            _maxLate           = Mathf.Max(0f, config.MaxLateSeconds);
            _windSpeedScale    = (config.WindSpeedScale > 0f) ? config.WindSpeedScale : 1f;
            _burstSlot         = ((config.HasBurstSlot) && (config.BurstSlot >= 0) && (config.BurstSlot < SLOT_COUNT)) ? config.BurstSlot : NO_SLOT;
            _maxBurstLate      = Mathf.Max(0f, config.MaxBurstLateSeconds);
            _serverTick        = Mathf.Max(0f, config.ServerTickSeconds);
        }

        /// <summary> 기술 슬롯(Q 0, W 1, E 2, R 3)의 발사 규격, 속도나 사거리가 0 이하이면 그 기술은 화살을 재생하지 않는다 </summary>
        public void SetSkill(int slot, in ShotSpec spec)
        {
            if ((slot < 0) || (slot >= SLOT_COUNT))
            {
                return;
            }

            _skills[slot] = Normalize(spec);
        }

        /// <summary> 기술 슬롯이 부여하는 속성, None이면 부여 효과를 재생하지 않는다 </summary>
        public void SetSkillElement(int slot, EArcherElement element)
        {
            if ((slot < 0) || (slot >= SLOT_COUNT))
            {
                return;
            }

            _elements[slot] = element;
        }

        /// <summary> 대기 중인 발사를 모두 버리고 다음 관찰을 새 기준값으로 삼는다 </summary>
        public void Reset()
        {
            ClearPending();
            _hasBaseline = false;
        }

        public Play GetPlay(int index)
        {
            return _plays[index];
        }

        /// <summary> 출발 후 경과 시간에 대해 투사체가 나아간 거리, 감속 없이 속도 곱하기 시간이며 사거리에서 멈춘다 </summary>
        public static float Distance(float speed, float range, double elapsed)
        {
            if ((speed <= 0f) || (range <= 0f) || (elapsed <= 0.0))
            {
                return 0f;
            }

            return Mathf.Min(range, (float)(speed * elapsed));
        }

        /// <summary> 적중 위치를 투사체 진행선에 투영해 멈출 거리를 구한다, 진행선에서 옆으로 maxLateral보다 멀거나 진행 구간 밖이면 거짓 </summary>
        public static bool TryGetHitStopDistance(Vector3 origin, Vector3 direction, float range, Vector3 hitPosition,
            float maxLateral, out float stopDistance, out float lateral)
        {
            float deltaX = hitPosition.x - origin.x;
            float deltaZ = hitPosition.z - origin.z;
            float along  = (deltaX * direction.x) + (deltaZ * direction.z);

            lateral      = Mathf.Sqrt(Mathf.Max(0f, (deltaX * deltaX) + (deltaZ * deltaZ) - (along * along)));
            stopDistance = Mathf.Clamp(along, 0f, Mathf.Max(0f, range));

            // 몸체가 큰 대상은 중심이 발사 지점 뒤나 사거리 너머여도 몸체가 진행선에 걸린다
            return (lateral <= maxLateral) && (along >= -maxLateral) && (along <= range + maxLateral);
        }

        /// <summary> 이번 프레임의 현재값을 반영하고 지금 재생할 효과의 개수를 돌려준다, 효과는 GetPlay로 읽는다 </summary>
        public int Update(in Observation obs)
        {
            if (obs.PlayerClass != EPlayerClass.Archer)
            {
                // 다른 직업이면 기준값을 버려, 궁수로 돌아온 첫 관찰이 새 기준값이 된다
                if (_hasBaseline)
                {
                    Reset();
                }

                return 0;
            }

            bool isAlive = obs.LifeState == EParticipantLifeState.Alive;
            if (!_hasBaseline)
            {
                _hasBaseline = true;
                _wasAlive    = isAlive;
                _hitBaseline = obs.ArrowHitSequence;
                ConsumeBaselines(in obs);
                return 0;
            }

            // 적중은 이미 날아간 화살의 결과라 회피, 사망, 취소와 상관없이 보여 준다
            int count = TakeHit(in obs, 0);

            if ((isAlive != _wasAlive) || (!isAlive))
            {
                // 쓰러져 있는 동안 생긴 행동은 재생하지 않고 기준값만 따라간다
                _wasAlive = isAlive;
                ClearPending();
                ConsumeBaselines(in obs);
                return count;
            }

            if (obs.DodgeSequence != _dodgeBaseline)
            {
                // 회피는 준비 중인 기본 공격과 아직 나가지 않은 순차 발사를 끊는다
                ClearPending();
                ConsumeBaselines(in obs);
                return count;
            }

            if (obs.ActionCancelSequence != _cancelBaseline)
            {
                // 서버가 기절로 준비와 연사를 끊었다. 기술 단계는 같은 프레임에 Idle로 보이지만, 기본 공격 준비는 이 신호만이 알린다
                ClearPending();
                ConsumeBaselines(in obs);
                return count;
            }

            bool isCastNew = (obs.CastInstanceId != 0) && (obs.CastInstanceId != _castBaseline);
            if (isCastNew)
            {
                // 다른 시전이 시작되면 이전 시전의 남은 발사와 기본 공격 준비는 서버에서도 끊긴다
                ClearPending();
                _castBaseline = obs.CastInstanceId;
                count = RegisterCast(in obs, count);
            }

            bool isAttackNew = obs.AttackSequence != _attackBaseline;
            _attackBaseline = obs.AttackSequence;
            if (isAttackNew && (!isCastNew) && (obs.SkillPhase == ESkillPhase.Idle))
            {
                count = RegisterBasic(in obs, count);
            }

            return Resolve(in obs, count);
        }

        private static ShotSpec Normalize(ShotSpec spec)
        {
            spec.ProjectileCount = Mathf.Clamp(spec.ProjectileCount, 1, MAX_PRONGS);
            spec.RepeatCount     = Mathf.Clamp(spec.RepeatCount, 1, MAX_REPEAT);
            spec.RepeatInterval  = Mathf.Max(0f, spec.RepeatInterval);
            spec.SpreadAngle     = Mathf.Max(0f, spec.SpreadAngle);
            return spec;
        }

        private static bool IsUsable(in ShotSpec spec)
        {
            return (spec.Speed > 0f) && (spec.Range > 0f);
        }

        private void ConsumeBaselines(in Observation obs)
        {
            _attackBaseline = obs.AttackSequence;
            _dodgeBaseline  = obs.DodgeSequence;
            _cancelBaseline = obs.ActionCancelSequence;
            _castBaseline   = obs.CastInstanceId;
        }

        private int TakeHit(in Observation obs, int count)
        {
            if (obs.ArrowHitSequence == _hitBaseline)
            {
                return count;
            }

            _hitBaseline = obs.ArrowHitSequence;
            _plays[count] = new Play
            {
                Kind = EArcherVfxKind.Hit, Element = obs.ArrowHitElement, StartAt = obs.Now, HasPosition = true,
                Position = obs.ArrowHitPosition, Direction = ToHorizontal(obs.Facing, Vector3.forward), IsHeavy = obs.ArrowHitIsHeavy
            };
            return count + 1;
        }

        private float GetBasicWindup(int step)
        {
            if ((_basicStepWindups == null) || (step >= _basicStepWindups.Length))
            {
                return _basicWindup;
            }

            return Mathf.Max(0f, _basicStepWindups[step]);
        }

        private int RegisterBasic(in Observation obs, int count)
        {
            // 서버는 단계 주기가 준비 시간보다 길어 앞 공격을 쏜 뒤에만 다음 공격을 승인한다. 지터로 앞 준비가 아직 남아 있으면
            // 취소 신호(이동, 회피, 기절, 기술)를 받지 않은 것이므로 서버에서는 이미 나간 화살이다. 버리지 않고 지금 내보낸다
            for (int i = 0; i < _pending.Length; ++i)
            {
                while (_pending[i].Active && (_pending[i].Kind == EPendingKind.Basic))
                {
                    count = FireBasic(i, count, obs.Now);
                }
            }

            if (!IsUsable(in _basic))
            {
                return count;
            }

            int slot = FreeSlot();
            if (slot < 0)
            {
                return count;
            }

            // 화살 위치는 적중 신호와 같은 수신 기준이 되도록 승인을 처음 본 시각에 준비 시간을 더한 시각에서 출발시키고,
            // 재생 판단은 서버가 실제로 쏘는 틱까지 미뤄 그 사이에 도착한 이동 취소를 놓치지 않는다.
            // 속성은 승인 순간 값이다. 준비 중에 속성 기술을 쓰면 서버가 이 공격을 버린다
            float windup    = GetBasicWindup(obs.AttackComboStep);
            float fireDelay = GetFireDelay(windup);
            bool isHeavy = obs.AttackComboStep == HeavyBasicStep;
            _pending[slot] = new Pending
            {
                Active = true, Kind = EPendingKind.Basic, IsHeavy = isHeavy, Direction = obs.Facing, Element = obs.ArcherElement,
                ResolveAt = obs.Now + windup, DecideAt = obs.Now + fireDelay, ServerStartAt = obs.AttackStartedAt,
                FireDelay = fireDelay, BasicWindup = windup,
                ShotCount = ((_basicStepShotCounts != null) && (obs.AttackComboStep < _basicStepShotCounts.Length))
                    ? Mathf.Clamp(_basicStepShotCounts[obs.AttackComboStep], 1, MAX_REPEAT) : 1,
                ShotInterval = ((_basicStepShotIntervals != null) && (obs.AttackComboStep < _basicStepShotIntervals.Length))
                    ? Mathf.Max(0f, _basicStepShotIntervals[obs.AttackComboStep]) : 0f
            };
            return count;
        }

        // 서버는 준비 시각이 지난 첫 틱에 쏘므로 준비 시간을 틱 간격의 배수로 올린다
        private float GetFireDelay(float windup)
        {
            if ((_serverTick <= 0f) || (windup <= 0f))
            {
                return windup;
            }

            double ticks = Math.Ceiling((windup / (double)_serverTick) - TICK_ROUNDING);
            return (float)(Math.Max(1.0, ticks) * _serverTick);
        }

        // 관찰한 이동이 서버에서 준비를 끊은 이동이면 참. 서버 시각이 없으면 관찰된 이동을 모두 취소로 본다.
        // 준비를 끊은 이동은 발사 틱까지 시작하고, 발사 뒤의 이동은 그다음 틱부터 시작하므로 그 사이 반 틱을 경계로 삼는다
        private bool IsMoveBeforeFire(in Observation obs, int index)
        {
            double startedAt = _pending[index].ServerStartAt;
            if ((startedAt <= 0.0) || (obs.LocomotionStartedAt <= startedAt))
            {
                return true;
            }

            double limit = _pending[index].FireDelay + (_serverTick * TICK_MARGIN_SCALE);
            return (obs.LocomotionStartedAt - startedAt) < limit;
        }

        private int RegisterCast(in Observation obs, int count)
        {
            if (obs.SkillKind != ESkillRequestKind.Regular)
            {
                return count;
            }

            // 이미 끝났거나 취소된 시전, 시작 전 단계는 재생하지 않는다
            if ((obs.SkillPhase != ESkillPhase.Windup) && (obs.SkillPhase != ESkillPhase.Active) && (obs.SkillPhase != ESkillPhase.Recovery))
            {
                return count;
            }

            if (obs.SkillSlot >= SLOT_COUNT)
            {
                return count;
            }

            EPendingKind kind;
            if (obs.SkillSlot == _burstSlot)
            {
                kind = EPendingKind.Burst;
            }
            else if (_elements[obs.SkillSlot] != EArcherElement.None)
            {
                kind = EPendingKind.Switch;
            }
            else if (IsUsable(in _skills[obs.SkillSlot]))
            {
                kind = EPendingKind.Shot;
            }
            else
            {
                return count;
            }

            int slot = FreeSlot();
            if (slot < 0)
            {
                return count;
            }

            // 후속 발사의 방향은 시전 순간의 조준으로 고정한다
            _pending[slot] = new Pending
            {
                Active = true, Kind = kind, CastId = obs.CastInstanceId, Slot = obs.SkillSlot,
                Direction = ToHorizontal(obs.SkillAim, obs.Facing), Point = obs.SkillPoint, Element = _elements[obs.SkillSlot],
                ResolveAt = obs.SkillResolveAt
            };

            // 차지는 준비 중에 처음 본 순간 시작한다. 준비가 이미 끝난 뒤에 처음 봤으면 폭발만 보인다
            if ((kind == EPendingKind.Burst) && (obs.SkillPhase == ESkillPhase.Windup) && (obs.Now < obs.SkillResolveAt))
            {
                _plays[count] = new Play
                {
                    Kind = EArcherVfxKind.Charge, StartAt = obs.Now, Direction = _pending[slot].Direction
                };
                ++count;
            }

            return count;
        }

        private int Resolve(in Observation obs, int count)
        {
            for (int i = 0; i < _pending.Length; ++i)
            {
                if (!_pending[i].Active)
                {
                    continue;
                }

                switch (_pending[i].Kind)
                {
                    case EPendingKind.Basic:
                        count = ResolveBasic(i, count, in obs);
                        break;
                    case EPendingKind.Shot:
                        count = ResolveCast(i, count, in obs);
                        break;
                    default:
                        count = ResolveOnce(i, count, in obs);
                        break;
                }
            }

            return count;
        }

        private int ResolveBasic(int index, int count, in Observation obs)
        {
            // 준비 중에 기술이 시작되면 서버가 기본 공격을 버린다
            if (obs.SkillPhase != ESkillPhase.Idle)
            {
                _pending[index].Active = false;
                return count;
            }

            // 준비 중에 시작한 이동은 기본 공격을 발사 없이 취소한다. 발사 뒤에 시작한 이동은 지터로 먼저 보여도 취소가 아니다
            if ((_basicCancelOnMove) && (obs.LocomotionSpeed > LOCOMOTION_EPSILON) && (IsMoveBeforeFire(in obs, index)))
            {
                _pending[index].Active = false;
                return count;
            }

            while ((_pending[index].Active) && (obs.Now >= _pending[index].DecideAt))
            {
                count = FireBasic(index, count, obs.Now);
            }

            return count;
        }

        private int FireBasic(int index, int count, double now)
        {
            ShotSpec spec = _basic;
            if (_pending[index].Element == EArcherElement.Wind)
            {
                spec.Speed *= _windSpeedScale;
            }

            _emitHeavy = _pending[index].IsHeavy;
            count      = EmitShot(in spec, _pending[index].Element, ToHorizontal(_pending[index].Direction, Vector3.forward),
                _pending[index].ResolveAt + (_pending[index].NextShot * (double)_pending[index].ShotInterval), now, count, true);
            _emitHeavy = false;
            ++_pending[index].NextShot;
            _pending[index].Active = _pending[index].NextShot < _pending[index].ShotCount;
            _pending[index].FireDelay = GetFireDelay(_pending[index].BasicWindup
                + (_pending[index].NextShot * _pending[index].ShotInterval));
            _pending[index].DecideAt = _pending[index].ResolveAt - _pending[index].BasicWindup + _pending[index].FireDelay;
            return count;
        }

        private int ResolveCast(int index, int count, in Observation obs)
        {
            // 다른 시전으로 바뀌었거나 Idle이면 취소 또는 이미 끝난 시전이다. 서버도 아직 나가지 않은 발사만 버린다
            if ((obs.CastInstanceId != _pending[index].CastId) || (obs.SkillPhase == ESkillPhase.Idle))
            {
                _pending[index].Active = false;
                return count;
            }

            ShotSpec spec = _skills[_pending[index].Slot];
            while (_pending[index].NextShot < spec.RepeatCount)
            {
                int    shot     = _pending[index].NextShot;
                double launchAt = _pending[index].ResolveAt + (shot * (double)spec.RepeatInterval);

                // 첫 발은 서버가 Windup을 지났다는 신호(Active, Recovery)만으로도 도달한 것이다. Active 0 기술도 Recovery에서 잡는다
                bool isDue = (obs.Now >= launchAt) || ((shot == 0) && (obs.SkillPhase != ESkillPhase.Windup));
                if (!isDue)
                {
                    break;
                }

                count = EmitShot(in spec, EArcherElement.None, _pending[index].Direction, launchAt, obs.Now, count, false);
                _pending[index].NextShot = shot + 1;
            }

            if (_pending[index].NextShot >= spec.RepeatCount)
            {
                _pending[index].Active = false;
            }

            return count;
        }

        // 속성 부여와 폭발은 발동 순간 한 번 재생한다. 늦게 본 부여는 생략하고 폭발은 허용 시간 안이면 보인다
        private int ResolveOnce(int index, int count, in Observation obs)
        {
            if (obs.CastInstanceId != _pending[index].CastId)
            {
                _pending[index].Active = false;
                return count;
            }

            bool isDue = (obs.Now >= _pending[index].ResolveAt) || (obs.SkillPhase != ESkillPhase.Windup);
            if (!isDue)
            {
                return count;
            }

            _pending[index].Active = false;

            // Windup에서 바로 Idle이 보이면 회피, 기절로 끊긴 시전이다
            if ((obs.SkillPhase == ESkillPhase.Idle) && (obs.Now < _pending[index].ResolveAt))
            {
                return count;
            }

            double elapsed  = Math.Max(0.0, obs.Now - _pending[index].ResolveAt);
            bool   isBurst  = _pending[index].Kind == EPendingKind.Burst;
            float  maxLate  = isBurst ? _maxBurstLate : _maxLate;
            if (elapsed > maxLate)
            {
                return count;
            }

            _plays[count] = new Play
            {
                Kind = isBurst ? EArcherVfxKind.Burst : EArcherVfxKind.Switch, Element = _pending[index].Element,
                Direction = _pending[index].Direction, StartAt = _pending[index].ResolveAt, HasPosition = isBurst,
                Position = _pending[index].Point
            };
            return count + 1;
        }

        // 한 번의 발사를 내보낸다. 늦게 관찰하면 총구는 생략하고, 투사체는 지나온 거리만큼 나간 자리에서 시작하며 이미 사거리를 넘겼으면 버린다
        private int EmitShot(in ShotSpec spec, EArcherElement element, Vector3 direction, double launchAt, double now, int count,
            bool stopsOnHit)
        {
            double elapsed = Math.Max(0.0, now - launchAt);
            if ((spec.Speed * elapsed) >= spec.Range)
            {
                return count;
            }

            int prongs = spec.ProjectileCount;
            if ((count + prongs + 1) > _plays.Length)
            {
                return count;
            }

            if (elapsed <= _maxLate)
            {
                count = Write(count, element, true, direction, launchAt, in spec, stopsOnHit);
            }

            for (int i = 0; i < prongs; ++i)
            {
                float   angle = (prongs > 1) ? ((i - ((prongs - 1) * CENTER_INDEX_SCALE)) * spec.SpreadAngle) : 0f;
                Vector3 aim   = (angle != 0f) ? (Quaternion.AngleAxis(angle, Vector3.up) * direction) : direction;
                count = Write(count, element, false, aim, launchAt, in spec, stopsOnHit);
            }

            return count;
        }

        private int Write(int count, EArcherElement element, bool isMuzzle, Vector3 direction, double startAt, in ShotSpec spec,
            bool stopsOnHit)
        {
            _plays[count] = new Play
            {
                Kind = EArcherVfxKind.Arrow, Element = element, IsMuzzle = isMuzzle, Direction = direction, StartAt = startAt,
                Speed = spec.Speed, Range = spec.Range, StopsOnHit = stopsOnHit, IsHeavy = _emitHeavy
            };
            return count + 1;
        }

        private int FreeSlot()
        {
            for (int i = 0; i < _pending.Length; ++i)
            {
                if (!_pending[i].Active)
                {
                    return i;
                }
            }

            return -1;
        }

        private void ClearPending()
        {
            for (int i = 0; i < _pending.Length; ++i)
            {
                _pending[i].Active = false;
            }
        }

        private static Vector3 ToHorizontal(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                fallback.y = 0f;
                if (fallback.sqrMagnitude <= Mathf.Epsilon)
                {
                    return Vector3.forward;
                }

                return fallback.normalized;
            }

            return direction.normalized;
        }
    }
}
