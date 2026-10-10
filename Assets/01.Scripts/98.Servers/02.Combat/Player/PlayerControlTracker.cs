// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 플레이어 한 명의 기절, 속박 종료 시각과 면역 구간, 시각을 인자로 받는 순수 상태라 엔진 없이 시험한다 </summary>
    public sealed class PlayerControlTracker
    {
        // 기절과 속박은 종류별 종료 시각을 따로 센다. 기획이 동시에 걸린 경우를 정하지 않아, 다른 종류가 걸려 있는 동안에도 새 종류를 받고
        // 적용 중인 제어는 기절이 속박보다 먼저다. 면역은 둘 다 끝난 시각부터 센다

        /****************************************
        *                Fields
        ****************************************/

        private readonly double _immunitySeconds;

        private double _stunEndsAt  = 0.0;
        private double _rootEndsAt  = 0.0;
        private double _immuneUntil = 0.0;

        // 제어가 걸린 뒤 면역 시작 시각을 아직 정하지 않았다
        private bool   _isEngaged   = false;

        /****************************************
        *              Properties
        ****************************************/

        public double StunEndsAt  => _stunEndsAt;
        public double RootEndsAt  => _rootEndsAt;
        public double ImmuneUntil => _immuneUntil;

        /****************************************
        *                Methods
        ****************************************/

        public PlayerControlTracker(float immunitySeconds)
        {
            _immunitySeconds = (immunitySeconds > 0f) ? immunitySeconds : 0.0;
        }

        /// <summary> 지금 적용 중인 제어, 모든 제어가 막 끝났으면 이때 면역 구간을 확정한다 </summary>
        public EPlayerControlKind Evaluate(double now)
        {
            Settle(now);
            return PlayerControlRules.Resolve(_stunEndsAt, _rootEndsAt, now);
        }

        /// <summary> 종류의 종료 시각, 걸린 적이 없으면 0 </summary>
        public double GetEndsAt(EPlayerControlKind kind)
        {
            switch (kind)
            {
                case EPlayerControlKind.Stun:
                    return _stunEndsAt;
                case EPlayerControlKind.Root:
                    return _rootEndsAt;
                default:
                    return 0.0;
            }
        }

        /// <summary> 제어를 건다, 시간은 안전 상한으로 자른다. 거절 사유는 결과로 돌려주고 상태는 바꾸지 않는다 </summary>
        public EPlayerControlResult TryApply(EPlayerControlKind kind, float seconds, double now)
        {
            if (((kind != EPlayerControlKind.Stun) && (kind != EPlayerControlKind.Root)) || (!PlayerControlRules.IsValidDuration(seconds)))
            {
                return EPlayerControlResult.Invalid;
            }

            Settle(now);

            bool anyActive = now < Math.Max(_stunEndsAt, _rootEndsAt);
            if ((!anyActive) && (now < _immuneUntil))
            {
                return EPlayerControlResult.Immune;
            }

            double duration  = Math.Min(seconds, Constants.Combat.PLAYER_CONTROL_MAX_SECONDS);
            double current   = GetEndsAt(kind);
            bool   wasActive = now < current;
            double merged    = PlayerControlRules.MergeEndsAt(current, now, duration);
            if ((wasActive) && (merged <= current))
            {
                return EPlayerControlResult.Unchanged;
            }

            if (kind == EPlayerControlKind.Stun)
            {
                _stunEndsAt = merged;
            }
            else
            {
                _rootEndsAt = merged;
            }

            _isEngaged = true;
            return wasActive ? EPlayerControlResult.Extended : EPlayerControlResult.Applied;
        }

        /// <summary> 사망처럼 제어를 끊는 사건, 면역도 두지 않는다 </summary>
        public void Clear()
        {
            _stunEndsAt  = 0.0;
            _rootEndsAt  = 0.0;
            _immuneUntil = 0.0;
            _isEngaged   = false;
        }

        // 면역은 제어가 끝난 시각부터 센다, 틱이 늦어도 끝난 시각 기준이다
        private void Settle(double now)
        {
            if (!_isEngaged)
            {
                return;
            }

            double lastEnd = Math.Max(_stunEndsAt, _rootEndsAt);
            if (now < lastEnd)
            {
                return;
            }

            _immuneUntil = lastEnd + _immunitySeconds;
            _isEngaged   = false;
        }
    }
}
