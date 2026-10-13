// System
using System;
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 기본 정책(체력 0이면 사망)을 감싸 기믹의 보류를 먼저 읽는다. 기믹은 두뇌보다 늦게 만들어질 수 있어 보류 창구를 나중에 더할 수 있다.
    // 보류가 하나라도 참이면 체력이 0이어도 사망을 확정하지 않는다
    /// <summary> 기믹의 사망 보류를 존중하는 사망 정책 </summary>
    public sealed class BossGimmickDeathPolicy : IBossDeathPolicy
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossDeathPolicy     _inner;
        private readonly List<IBossDeathHold> _holds = new List<IBossDeathHold>();

        /****************************************
        *                Methods
        ****************************************/

        public BossGimmickDeathPolicy(IBossDeathPolicy inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary> 보류 창구를 더한다 </summary>
        public void AddHold(IBossDeathHold hold)
        {
            if (hold == null)
            {
                throw new ArgumentNullException(nameof(hold));
            }

            _holds.Add(hold);
        }

        public bool IsDeathConfirmed(IBossVitals vitals, double serverTime)
        {
            for (int i = 0; i < _holds.Count; ++i)
            {
                if (_holds[i].IsDeathHeld)
                {
                    return false;
                }
            }

            return _inner.IsDeathConfirmed(vitals, serverTime);
        }
    }
}
