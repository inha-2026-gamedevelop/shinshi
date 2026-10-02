// System
using System;

namespace Shinshi.AI
{
    // BossBrainDependencies.Effects는 하나뿐이라 투사체 관리자와 장판 관리자를 함께 넘길 때 쓴다. 정리와 Tick과 장판 수 조회가 빠지지 않게 모두 전달한다
    /// <summary> 여러 수명 관리자를 하나로 묶는다, 정리는 모두에게 전달하고 장판 수는 합산하며 Tick은 Tick을 구현한 관리자에게만 전달한다 </summary>
    public sealed class BossEffectComposite : IBossEffectLifetime, IBossTickableEffects
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossEffectLifetime[]  _effects;
        private readonly IBossTickableEffects[] _tickables;

        /****************************************
        *                Methods
        ****************************************/

        public BossEffectComposite(params IBossEffectLifetime[] effects)
        {
            if (effects == null)
            {
                throw new ArgumentNullException(nameof(effects));
            }

            _effects = (IBossEffectLifetime[])effects.Clone();

            int tickableCount = 0;
            for (int i = 0; i < _effects.Length; ++i)
            {
                if (_effects[i] == null)
                {
                    throw new ArgumentException("수명 관리자가 비어 있다", nameof(effects));
                }

                if (_effects[i] is IBossTickableEffects)
                {
                    ++tickableCount;
                }
            }

            _tickables = new IBossTickableEffects[tickableCount];
            int next = 0;
            for (int i = 0; i < _effects.Length; ++i)
            {
                if (_effects[i] is IBossTickableEffects tickable)
                {
                    _tickables[next] = tickable;
                    ++next;
                }
            }
        }

        public void Cancel(EBossInterruptReason reason, int castInstanceId)
        {
            for (int i = 0; i < _effects.Length; ++i)
            {
                _effects[i].Cancel(reason, castInstanceId);
            }
        }

        public int ActiveAreaCount(string patternId)
        {
            int count = 0;
            for (int i = 0; i < _effects.Length; ++i)
            {
                count += _effects[i].ActiveAreaCount(patternId);
            }

            return count;
        }

        public void Tick(double serverTime)
        {
            for (int i = 0; i < _tickables.Length; ++i)
            {
                _tickables[i].Tick(serverTime);
            }
        }
    }
}
