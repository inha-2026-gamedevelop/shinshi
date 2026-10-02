// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 협동 보스전의 파티 공용 부활 횟수와 전멸 판정 </summary>
    public sealed class ReviveBudget
    {
        // 한 틱 처리 순서는 피해, 사망 반영 -> EvaluateWipe -> 부활 승인(CanRevive, TryCommitRevive)이다.
        // 같은 틱에 마지막 생존자가 쓰러지고 다른 사람의 부활이 겹치면 전멸이 먼저 확정되어 부활은 SessionTerminal로 거절된다.
        // 솔로는 한 명이 쓰러지면 생존자가 0이 되므로 같은 경로로 즉시 실패한다

        /****************************************
        *                Fields
        ****************************************/

        private int  _remaining  = 0;
        private bool _isTerminal = false;

        /****************************************
        *              Properties
        ****************************************/

        public int  Remaining  => _remaining;
        public bool IsTerminal => _isTerminal;

        /****************************************
        *                Methods
        ****************************************/

        // SessionConfigSO.BossSharedRevives를 넘긴다
        public ReviveBudget(int sharedRevives)
        {
            if (sharedRevives < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sharedRevives), "공용 부활 횟수는 0 이상이다");
            }

            _remaining = sharedRevives;
        }

        /// <summary> 이번 틱 사망을 모두 반영한 뒤의 생존 참가자 수로 전멸을 판정한다, 전멸이면 전투가 끝난다 </summary>
        public bool EvaluateWipe(int livingParticipantCount)
        {
            if (_isTerminal)
            {
                return true;
            }

            if (livingParticipantCount > 0)
            {
                return false;
            }

            _isTerminal = true;
            return true;
        }

        /// <summary> 처치 성공, 시간 초과처럼 다른 이유로 전투가 끝났을 때 부른다 </summary>
        public void MarkTerminal()
        {
            _isTerminal = true;
        }

        /// <summary> 사망한 참가자가 들어갈 상태, 공용 횟수가 남았으면 부활 대기, 없으면 관전 </summary>
        public EParticipantLifeState LifeStateOnDeath()
        {
            return (_remaining > 0) ? EParticipantLifeState.Dead : EParticipantLifeState.Spectating;
        }

        /// <summary> 부활 요청을 받을 수 있는지, 횟수는 줄이지 않는다 </summary>
        public ERejectReason CanRevive(double deathTime, double now, float reviveDelay)
        {
            if (_isTerminal)
            {
                return ERejectReason.SessionTerminal;
            }

            if (_remaining <= 0)
            {
                return ERejectReason.NoRevives;
            }

            if (!PlayerLifeRules.IsReviveReady(deathTime, now, reviveDelay))
            {
                return ERejectReason.TooEarly;
            }

            return ERejectReason.None;
        }

        // 안전 지점을 찾아 실제로 되살리는 순간에만 부른다. 기다리는 동안 다른 사람이 마지막 횟수를 쓰면
        // NoRevives가 돌아오고, 부른 쪽이 그 참가자를 관전으로 옮긴다

        /// <summary> 공용 횟수 하나를 써서 부활을 확정한다 </summary>
        public ERejectReason TryCommitRevive()
        {
            if (_isTerminal)
            {
                return ERejectReason.SessionTerminal;
            }

            if (_remaining <= 0)
            {
                return ERejectReason.NoRevives;
            }

            --_remaining;
            return ERejectReason.None;
        }
    }
}
