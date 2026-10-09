// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 궁수 투사체 모양, 기본 공격과 Q, W, R은 화살이고 E만 바람이다 </summary>
    public enum EArcherVfxKind
    {
        Arrow,
        Wind
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
        // 이 속도 이하의 이동 속도는 정지로 본다
        public const float  LOCOMOTION_EPSILON        = 0.05f;

        private const int MAX_PENDING = 3;
        private const int MAX_PLAYS   = (MAX_REPEAT * (MAX_PRONGS + 1)) + 2;

        private const float CENTER_INDEX_SCALE = 0.5f;

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
            public bool     BasicCancelOnMove;
            public ShotSpec Basic;
            // 발사 시각을 이만큼 넘겨 관찰한 총구 효과는 이미 지난 일이라 재생하지 않는다
            public float    MaxLateSeconds;
        }

        /// <summary> 한 프레임에 모은 공개 상태 현재값 </summary>
        public struct Observation
        {
            public EPlayerClass          PlayerClass;
            public EParticipantLifeState LifeState;
            public byte                  AttackSequence;
            public byte                  DodgeSequence;
            public byte                  ActionCancelSequence;
            public int                   CastInstanceId;
            public ESkillPhase           SkillPhase;
            public ESkillRequestKind     SkillKind;
            public byte                  SkillSlot;
            public double                SkillResolveAt;
            public Vector3               SkillAim;
            public Vector3               Facing;
            public float                 LocomotionSpeed;
            public double                Now;
        }

        /// <summary> 지금 재생할 효과 하나, 시각은 모두 서버 시각이고 출발 위치는 재생 순간의 캐릭터 위치다 </summary>
        public struct Play
        {
            public EArcherVfxKind Kind;
            // 참이면 총구 효과, 거짓이면 날아가는 투사체
            public bool           IsMuzzle;
            public Vector3        Direction;
            // 서버가 이 발사를 내보낸 시각
            public double         StartAt;
            public float          Speed;
            public float          Range;
        }

        private struct Pending
        {
            public bool    Active;
            public bool    IsBasic;
            public int     CastId;
            public int     Slot;
            public Vector3 Direction;
            // 기본 공격은 관찰 시각에서 정한 발사 시각, 기술은 서버 SkillResolveAt
            public double  ResolveAt;
            public int     NextShot;
        }

        private readonly Pending[]  _pending = new Pending[MAX_PENDING];
        private readonly Play[]     _plays   = new Play[MAX_PLAYS];
        private readonly ShotSpec[] _skills  = new ShotSpec[SLOT_COUNT];

        private ShotSpec _basic;
        private float    _basicWindup;
        private bool     _basicCancelOnMove;
        private float    _maxLate;

        private bool _hasBaseline;
        private bool _wasAlive;
        private byte _attackBaseline;
        private byte _dodgeBaseline;
        private byte _cancelBaseline;
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

        /****************************************
        *                Methods
        ****************************************/

        public void Configure(in Config config)
        {
            _basic             = Normalize(config.Basic);
            _basicWindup       = Mathf.Max(0f, config.BasicWindup);
            _basicCancelOnMove = config.BasicCancelOnMove;
            _maxLate           = Mathf.Max(0f, config.MaxLateSeconds);
        }

        /// <summary> 기술 슬롯(Q 0, W 1, E 2, R 3)의 발사 규격, 속도나 사거리가 0 이하이면 그 기술은 재생하지 않는다 </summary>
        public void SetSkill(int slot, in ShotSpec spec)
        {
            if ((slot < 0) || (slot >= SLOT_COUNT))
            {
                return;
            }

            _skills[slot] = Normalize(spec);
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
                ConsumeBaselines(in obs);
                return 0;
            }

            if ((isAlive != _wasAlive) || (!isAlive))
            {
                // 쓰러져 있는 동안 생긴 행동은 재생하지 않고 기준값만 따라간다
                _wasAlive = isAlive;
                ClearPending();
                ConsumeBaselines(in obs);
                return 0;
            }

            if (obs.DodgeSequence != _dodgeBaseline)
            {
                // 회피는 준비 중인 기본 공격과 아직 나가지 않은 순차 발사를 끊는다
                ClearPending();
                ConsumeBaselines(in obs);
                return 0;
            }

            if (obs.ActionCancelSequence != _cancelBaseline)
            {
                // 서버가 기절로 준비와 연사를 끊었다. 기술 단계는 같은 프레임에 Idle로 보이지만, 기본 공격 준비는 이 신호만이 알린다
                ClearPending();
                ConsumeBaselines(in obs);
                return 0;
            }

            bool isCastNew = (obs.CastInstanceId != 0) && (obs.CastInstanceId != _castBaseline);
            if (isCastNew)
            {
                // 다른 시전이 시작되면 이전 시전의 남은 발사와 기본 공격 준비는 서버에서도 끊긴다
                ClearPending();
                _castBaseline = obs.CastInstanceId;
                RegisterCast(in obs);
            }

            bool isAttackNew = obs.AttackSequence != _attackBaseline;
            _attackBaseline = obs.AttackSequence;
            if (isAttackNew && (!isCastNew) && (obs.SkillPhase == ESkillPhase.Idle))
            {
                RegisterBasic(in obs);
            }

            return Resolve(in obs);
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

        private void RegisterBasic(in Observation obs)
        {
            // 기본 공격은 한 번에 하나만 준비되므로 이전 준비는 새 값으로 바꾼다
            for (int i = 0; i < _pending.Length; ++i)
            {
                if (_pending[i].Active && _pending[i].IsBasic)
                {
                    _pending[i].Active = false;
                }
            }

            if (!IsUsable(in _basic))
            {
                return;
            }

            int slot = FreeSlot();
            if (slot < 0)
            {
                return;
            }

            // 서버가 공개하는 값에는 윈드업 시작 시각이 없어, 승인을 처음 본 시각에 윈드업을 더해 발사 시각으로 삼는다
            _pending[slot].Active       = true;
            _pending[slot].IsBasic      = true;
            _pending[slot].CastId       = 0;
            _pending[slot].Slot         = 0;
            _pending[slot].Direction    = obs.Facing;
            _pending[slot].ResolveAt    = obs.Now + _basicWindup;
            _pending[slot].NextShot     = 0;
        }

        private void RegisterCast(in Observation obs)
        {
            if (obs.SkillKind != ESkillRequestKind.Regular)
            {
                return;
            }

            // 이미 끝났거나 취소된 시전, 시작 전 단계는 재생하지 않는다
            if ((obs.SkillPhase != ESkillPhase.Windup) && (obs.SkillPhase != ESkillPhase.Active) && (obs.SkillPhase != ESkillPhase.Recovery))
            {
                return;
            }

            if ((obs.SkillSlot >= SLOT_COUNT) || (!IsUsable(in _skills[obs.SkillSlot])))
            {
                return;
            }

            int slot = FreeSlot();
            if (slot < 0)
            {
                return;
            }

            // 후속 발사의 방향은 시전 순간의 조준으로 고정한다
            _pending[slot].Active       = true;
            _pending[slot].IsBasic      = false;
            _pending[slot].CastId       = obs.CastInstanceId;
            _pending[slot].Slot         = obs.SkillSlot;
            _pending[slot].Direction    = ToHorizontal(obs.SkillAim, obs.Facing);
            _pending[slot].ResolveAt    = obs.SkillResolveAt;
            _pending[slot].NextShot     = 0;
        }

        private int Resolve(in Observation obs)
        {
            int count = 0;
            for (int i = 0; i < _pending.Length; ++i)
            {
                if (!_pending[i].Active)
                {
                    continue;
                }

                if (_pending[i].IsBasic)
                {
                    count = ResolveBasic(i, count, in obs);
                }
                else
                {
                    count = ResolveCast(i, count, in obs);
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

            // 이동하면 준비 중인 기본 공격은 발사 없이 취소된다
            if (_basicCancelOnMove && (obs.LocomotionSpeed > LOCOMOTION_EPSILON))
            {
                _pending[index].Active = false;
                return count;
            }

            if (obs.Now < _pending[index].ResolveAt)
            {
                return count;
            }

            _pending[index].Active = false;
            return EmitShot(in _basic, EArcherVfxKind.Arrow, ToHorizontal(_pending[index].Direction, Vector3.forward), _pending[index].ResolveAt, obs.Now, count);
        }

        private int ResolveCast(int index, int count, in Observation obs)
        {
            // 다른 시전으로 바뀌었거나 Idle이면 취소 또는 이미 끝난 시전이다. 서버도 아직 나가지 않은 발사만 버린다
            if ((obs.CastInstanceId != _pending[index].CastId) || (obs.SkillPhase == ESkillPhase.Idle))
            {
                _pending[index].Active = false;
                return count;
            }

            ShotSpec       spec = _skills[_pending[index].Slot];
            EArcherVfxKind kind = (_pending[index].Slot == Constants.Combat.SKILL_SLOT_E) ? EArcherVfxKind.Wind : EArcherVfxKind.Arrow;
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

                count = EmitShot(in spec, kind, _pending[index].Direction, launchAt, obs.Now, count);
                _pending[index].NextShot = shot + 1;
            }

            if (_pending[index].NextShot >= spec.RepeatCount)
            {
                _pending[index].Active = false;
            }

            return count;
        }

        // 한 번의 발사를 내보낸다. 늦게 관찰하면 총구는 생략하고, 투사체는 지나온 거리만큼 나간 자리에서 시작하며 이미 사거리를 넘겼으면 버린다
        private int EmitShot(in ShotSpec spec, EArcherVfxKind kind, Vector3 direction, double launchAt, double now, int count)
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
                count = Write(count, kind, true, direction, launchAt, in spec);
            }

            for (int i = 0; i < prongs; ++i)
            {
                float   angle = (prongs > 1) ? ((i - ((prongs - 1) * CENTER_INDEX_SCALE)) * spec.SpreadAngle) : 0f;
                Vector3 aim   = (angle != 0f) ? (Quaternion.AngleAxis(angle, Vector3.up) * direction) : direction;
                count = Write(count, kind, false, aim, launchAt, in spec);
            }

            return count;
        }

        private int Write(int count, EArcherVfxKind kind, bool isMuzzle, Vector3 direction, double startAt, in ShotSpec spec)
        {
            _plays[count].Kind      = kind;
            _plays[count].IsMuzzle  = isMuzzle;
            _plays[count].Direction = direction;
            _plays[count].StartAt   = startAt;
            _plays[count].Speed     = spec.Speed;
            _plays[count].Range     = spec.Range;
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
