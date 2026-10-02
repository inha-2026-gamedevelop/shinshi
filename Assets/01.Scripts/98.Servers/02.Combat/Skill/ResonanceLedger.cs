// System
using System;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 최초 유효 적중만 공명으로 인정한다. 기록은 끝난 시전의 ReleaseCast에서 회수한다 </summary>
    public sealed class ResonanceLedger
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int NO_RESERVATION = -1;

        private struct HitRecord
        {
            public int              CastId;
            public int              ActionId;
            public int              TargetId;
            public EResonanceSource Source;
        }

        private readonly HitRecord[] _records;
        private readonly float       _maximum;
        private readonly float       _basicGain;
        private readonly float       _skillGain;
        private readonly float       _companionGain;
        private readonly float       _castCap;

        private int   _count           = 0;
        private int   _releasedThrough = CombatIds.NONE;
        private float _value           = 0f;

        /****************************************
        *              Properties
        ****************************************/

        public float Value => _value;

        /****************************************
        *                Methods
        ****************************************/

        public ResonanceLedger(SkillCatalogSO catalog, int capacity)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "공명 기록 상한은 1 이상이다");
            }

            if ((!IsFinite(catalog.ResonanceMax)) || (catalog.ResonanceMax <= 0f)
                || (!IsNonNegativeFinite(catalog.ResonancePerBasicHit))
                || (!IsNonNegativeFinite(catalog.ResonancePerSkillHit))
                || (!IsNonNegativeFinite(catalog.ResonancePerCompanionHit))
                || (!IsNonNegativeFinite(catalog.ResonancePerCastCap))
                || (catalog.ResonancePerCastCap < catalog.ResonancePerSkillHit)
                || (catalog.ResonancePerCastCap > catalog.ResonanceMax))
            {
                throw new ArgumentException("공명 수치는 유한한 카탈로그 범위여야 한다", nameof(catalog));
            }

            _records       = new HitRecord[capacity];
            _maximum       = catalog.ResonanceMax;
            _basicGain     = catalog.ResonancePerBasicHit;
            _skillGain     = catalog.ResonancePerSkillHit;
            _companionGain = catalog.ResonancePerCompanionHit;
            _castCap       = catalog.ResonancePerCastCap;
        }

        /// <summary> 시전 확정 전에 예약한다. 늦게 처음 적중하는 장판도 종료 고수위 아래에서 살아 있게 한다 </summary>
        public bool OpenCast(int castId)
        {
            if (castId <= CombatIds.NONE)
            {
                return false;
            }

            for (int i = 0; i < _count; ++i)
            {
                if (_records[i].CastId == castId)
                {
                    return true;
                }
            }

            if ((castId <= _releasedThrough) || (_count == _records.Length))
            {
                return false;
            }

            // action과 target의 NONE은 아직 적중하지 않은 예약이며 첫 유효 적중이 이 칸을 채운다
            _records[_count] = new HitRecord { CastId = castId };
            ++_count;
            return true;
        }

        /// <summary> 인정한 최초 적중이면 true다. 공명 상한에 도달해도 적중 기록은 남긴다 </summary>
        public bool Record(in DamageRequest request, in DamageResult result, EResonanceSource source, float bonus = 0f)
        {
            if ((request.AttackerId <= CombatIds.NONE) || (request.HitTargetId <= CombatIds.NONE)
                || (request.CastInstanceId <= CombatIds.NONE) || (request.ActionInstanceId <= CombatIds.NONE)
                || (!request.GrantsResonance) || (!result.CountsAsHit) || (!IsNonNegativeFinite(bonus)))
            {
                return false;
            }

            float gain;
            switch (source)
            {
                case EResonanceSource.Basic:
                    gain = _basicGain;
                    break;

                case EResonanceSource.Skill:
                    // 덧셈 전에 여유분으로 제한해 유한한 큰 bonus도 overflow하지 않게 한다
                    gain = _skillGain + Math.Min(bonus, _castCap - _skillGain);
                    break;

                case EResonanceSource.Companion:
                    gain = _companionGain;
                    break;

                case EResonanceSource.Ascension:
                default:
                    return false;
            }

            if (gain <= 0f)
            {
                return false;
            }

            bool hasLiveCast     = false;
            int  reservationSlot = NO_RESERVATION;
            for (int i = 0; i < _count; ++i)
            {
                ref HitRecord record = ref _records[i];
                if (record.CastId != request.CastInstanceId)
                {
                    continue;
                }

                hasLiveCast = true;
                if (record.ActionId == CombatIds.NONE)
                {
                    reservationSlot = i;
                    continue;
                }

                if ((record.ActionId == request.ActionInstanceId) && (record.TargetId == request.HitTargetId))
                {
                    return false;
                }

                // 한 동작은 대상을 늘리거나 source를 바꿔도 공명을 다시 얻지 않는다
                if (record.ActionId == request.ActionInstanceId)
                {
                    return false;
                }

                // 장판의 다음 틱도 같은 시전으로 묶지만 동료는 별도 동작마다 계속 얻는다
                if ((source == EResonanceSource.Skill) && (record.Source == EResonanceSource.Skill))
                {
                    return false;
                }
            }

            // 발급 ID는 단조 증가하고 재사용하지 않는다. 고수위 이하의 미등록 시전은 종료 replay와
            // 구별할 수 없어 보수적으로 거절한다. 이미 기록된 오래된 활성 시전은 계속 허용한다.
            if ((request.CastInstanceId <= _releasedThrough) && (!hasLiveCast))
            {
                return false;
            }

            if ((reservationSlot < 0) && (_count == _records.Length))
            {
                return false;
            }

            int recordSlot = (reservationSlot >= 0) ? reservationSlot : _count;
            _records[recordSlot] = new HitRecord
            {
                CastId   = request.CastInstanceId,
                ActionId = request.ActionInstanceId,
                TargetId = request.HitTargetId,
                Source   = source
            };
            if (reservationSlot < 0)
            {
                ++_count;
            }

            _value += Math.Min(gain, _maximum - _value);
            return true;
        }

        /// <summary> 공명이 충분하면 양수 비용을 뺀다. 잘못된 값이나 부족한 비용은 상태를 바꾸지 않는다 </summary>
        public bool TrySpend(float amount)
        {
            if ((!IsFinite(amount)) || (amount <= 0f) || (amount > _value))
            {
                return false;
            }

            _value -= amount;
            return true;
        }

        /// <summary> 보존한 공명 값을 복원한다. 적중 기록과 종료 ID 경계는 초기화하지 않는다 </summary>
        public void Restore(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            _value = Math.Max(0f, Math.Min(value, _maximum));
        }

        /// <summary> 서버가 모든 후속 타격을 끝낸 시전만 회수한다. 시전 ID는 다시 발급하지 않는다 </summary>
        public void ReleaseCast(int castId)
        {
            if (castId <= CombatIds.NONE)
            {
                return;
            }

            _releasedThrough = Math.Max(_releasedThrough, castId);

            int remaining = 0;
            for (int i = 0; i < _count; ++i)
            {
                if (_records[i].CastId == castId)
                {
                    continue;
                }

                _records[remaining] = _records[i];
                ++remaining;
            }

            // 남은 칸은 값 형식뿐이며 다음 Record가 덮어쓰므로 배열을 새로 만들지 않는다
            _count = remaining;
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }

        private static bool IsNonNegativeFinite(float value)
        {
            return (IsFinite(value)) && (value >= 0f);
        }
    }
}
