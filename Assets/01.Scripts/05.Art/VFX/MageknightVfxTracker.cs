// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 마검사 효과 종류, 기본 공격과 기술 Q, W, E, R에 하나씩 대응한다 </summary>
    public enum EMageknightVfxKind
    {
        Basic,
        Arcane,
        Field,
        Seal,
        Wave
    }

    /// <summary> 공개 전투 상태의 현재값만으로 마검사 효과의 재생 시점과 취소를 정하는 순수 추적기, 엔진 오브젝트에 의존하지 않아 EditMode에서 시험한다 </summary>
    public sealed class MageknightVfxTracker
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_PENDING = 4;

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
            public Vector3               SkillOrigin;
            public Vector3               SkillAim;
            public Vector3               Facing;
            public double                Now;
        }

        /// <summary> 지금 재생할 효과 하나, 시각은 모두 서버 시각이다 </summary>
        public struct Play
        {
            public EMageknightVfxKind Kind;
            public Vector3            Origin;
            public Vector3            Direction;
            public double             StartAt;
            // 장판만 서버와 같은 종료 시각을 갖고, 나머지는 0이다
            public double             EndAt;
        }

        private struct Pending
        {
            public bool               Active;
            public bool               IsBasic;
            public EMageknightVfxKind Kind;
            public int                CastId;
            // 기본 공격은 관찰 시각에서 정한 절대 재생 시각, 기술은 쓰지 않는다
            public double             Time;
            public Vector3            Origin;
            public Vector3            Direction;
        }

        private readonly Pending[] _pending = new Pending[MAX_PENDING];
        private readonly Play[]    _plays   = new Play[MAX_PENDING];

        private float _basicDelay;
        private float _fieldDuration;
        private float _waveSpeed;
        private float _waveRange;
        private float _maxLate;

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

        /// <summary> 기본 공격 준비 시간, 장판 지속 시간(0 이하면 재생하지 않음), 검기 속도와 사거리(0 이하면 재생하지 않음), 재생 시각을 넘겨 관찰해도 허용하는 시간 </summary>
        public void Configure(float basicDelay, float fieldDuration, float waveSpeed, float waveRange, float maxLateSeconds)
        {
            _basicDelay    = Mathf.Max(0f, basicDelay);
            _fieldDuration = fieldDuration;
            _waveSpeed     = waveSpeed;
            _waveRange     = waveRange;
            _maxLate       = Mathf.Max(0f, maxLateSeconds);
        }

        /// <summary> 대기 중인 효과를 모두 버리고 다음 관찰을 새 기준값으로 삼는다 </summary>
        public void Reset()
        {
            ClearPending();
            _hasBaseline = false;
        }

        public Play GetPlay(int index)
        {
            return _plays[index];
        }

        /// <summary> 기본 공격은 재생 순간의 캐릭터 위치, 나머지는 서버가 공개한 시전 위치에서 터진다 </summary>
        public static bool UsesActorPosition(EMageknightVfxKind kind)
        {
            return kind == EMageknightVfxKind.Basic;
        }

        /// <summary> 출발 후 경과 시간에 대해 검기가 나아간 거리, 감속 없이 속도 곱하기 시간이며 사거리에서 멈춘다 </summary>
        public static float WaveDistance(float speed, float range, double elapsed)
        {
            if ((speed <= 0f) || (range <= 0f) || (elapsed <= 0.0))
            {
                return 0f;
            }

            return Mathf.Min(range, (float)(speed * elapsed));
        }

        /// <summary> 검기가 사거리 끝까지 나아가는 데 걸리는 시간, 속도나 사거리가 없으면 0 </summary>
        public static double WaveTravelSeconds(float speed, float range)
        {
            if ((speed <= 0f) || (range <= 0f))
            {
                return 0.0;
            }

            return range / speed;
        }

        /// <summary> 이번 프레임의 현재값을 반영하고 지금 재생할 효과의 개수를 돌려준다, 효과는 GetPlay로 읽는다 </summary>
        public int Update(in Observation obs)
        {
            if (obs.PlayerClass != EPlayerClass.Mageknight)
            {
                // 다른 직업이면 기준값을 버려, 마검사로 돌아온 첫 관찰이 새 기준값이 된다
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
                // 회피는 준비 중인 기술과 기본 공격을 끊는다
                ClearPending();
                ConsumeBaselines(in obs);
                return 0;
            }

            if (obs.ActionCancelSequence != _cancelBaseline)
            {
                // 서버가 기절로 준비 중인 기술과 기본 공격을 끊었다
                ClearPending();
                ConsumeBaselines(in obs);
                return 0;
            }

            bool isCastNew = (obs.CastInstanceId != 0) && (obs.CastInstanceId != _castBaseline);
            if (isCastNew)
            {
                // 다른 시전이 시작되면 이전 시전과 기본 공격 준비는 서버에서도 끊긴다
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

            int slot = FreeSlot();
            if (slot < 0)
            {
                return;
            }

            _pending[slot].Active    = true;
            _pending[slot].IsBasic   = true;
            _pending[slot].Kind      = EMageknightVfxKind.Basic;
            _pending[slot].CastId    = 0;
            _pending[slot].Time      = obs.Now + _basicDelay;
            _pending[slot].Origin    = Vector3.zero;
            _pending[slot].Direction = obs.Facing;
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

            if (!TryGetKind(obs.SkillSlot, out EMageknightVfxKind kind))
            {
                return;
            }

            int slot = FreeSlot();
            if (slot < 0)
            {
                return;
            }

            _pending[slot].Active    = true;
            _pending[slot].IsBasic   = false;
            _pending[slot].Kind      = kind;
            _pending[slot].CastId    = obs.CastInstanceId;
            _pending[slot].Time      = 0.0;
            _pending[slot].Origin    = obs.SkillOrigin;
            _pending[slot].Direction = obs.SkillAim;
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
                    count = ResolveSkill(i, count, in obs);
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

            if (obs.Now < _pending[index].Time)
            {
                return count;
            }

            _pending[index].Active = false;
            if ((obs.Now - _pending[index].Time) > _maxLate)
            {
                return count;
            }

            return Emit(in _pending[index], _pending[index].Time, 0.0, count);
        }

        private int ResolveSkill(int index, int count, in Observation obs)
        {
            // 다른 시전으로 바뀌었거나 Idle이면 취소 또는 이미 끝난 시전이다.
            // 이미 나간 장판과 검기는 추적기가 아니라 풀이 수명을 가지므로 여기서 건드리지 않는다
            if ((obs.CastInstanceId != _pending[index].CastId) || (obs.SkillPhase == ESkillPhase.Idle))
            {
                _pending[index].Active = false;
                return count;
            }

            // 서버가 Windup을 지났다는 신호(Active, Recovery)만으로도 도달한 것이다. Active 0 기술도 Recovery에서 잡는다
            double startAt = obs.SkillResolveAt;
            bool   isDue   = (obs.Now >= startAt) || (obs.SkillPhase != ESkillPhase.Windup);
            if (!isDue)
            {
                return count;
            }

            _pending[index].Active = false;
            double elapsed = Math.Max(0.0, obs.Now - startAt);

            switch (_pending[index].Kind)
            {
                case EMageknightVfxKind.Field:
                    return ResolveField(index, count, startAt, elapsed);
                case EMageknightVfxKind.Wave:
                    return ResolveWave(index, count, startAt, elapsed);
                default:
                    // 짧은 베기는 한참 지난 뒤에 관찰하면 이미 지난 일이다
                    if (elapsed > _maxLate)
                    {
                        return count;
                    }

                    return Emit(in _pending[index], startAt, 0.0, count);
            }
        }

        private int ResolveField(int index, int count, double startAt, double elapsed)
        {
            // 서버 장판은 resolveAt부터 지속 시간만큼 살아 있으므로 늦게 관찰해도 남은 시간만큼은 보인다
            if ((_fieldDuration <= 0f) || (elapsed >= _fieldDuration))
            {
                return count;
            }

            return Emit(in _pending[index], startAt, startAt + _fieldDuration, count);
        }

        private int ResolveWave(int index, int count, double startAt, double elapsed)
        {
            // 검기는 관찰이 늦어도 원점이 아니라 지나온 거리만큼 나간 자리에서 시작하며, 이미 사거리를 넘겼으면 재생하지 않는다
            if ((_waveSpeed <= 0f) || (_waveRange <= 0f) || (elapsed >= WaveTravelSeconds(_waveSpeed, _waveRange)))
            {
                return count;
            }

            return Emit(in _pending[index], startAt, 0.0, count);
        }

        private int Emit(in Pending pending, double startAt, double endAt, int count)
        {
            _plays[count].Kind      = pending.Kind;
            _plays[count].Origin    = pending.Origin;
            _plays[count].Direction = pending.Direction;
            _plays[count].StartAt   = startAt;
            _plays[count].EndAt     = endAt;
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

        private static bool TryGetKind(byte slot, out EMageknightVfxKind kind)
        {
            switch (slot)
            {
                case Constants.Combat.SKILL_SLOT_Q:
                    kind = EMageknightVfxKind.Arcane;
                    return true;
                case Constants.Combat.SKILL_SLOT_W:
                    kind = EMageknightVfxKind.Field;
                    return true;
                case Constants.Combat.SKILL_SLOT_E:
                    kind = EMageknightVfxKind.Seal;
                    return true;
                case Constants.Combat.SKILL_SLOT_R:
                    kind = EMageknightVfxKind.Wave;
                    return true;
                default:
                    kind = EMageknightVfxKind.Basic;
                    return false;
            }
        }
    }
}
