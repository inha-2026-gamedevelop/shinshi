// System
using System;

namespace Shinshi.AI
{
    // 한 보스 몸체에 체력 기믹이 둘 이상 붙으면(75% 부적진과 50% 색 결계) 몸체는 상한 계약을 하나만 읽을 수 있어 이 합성을 거친다.
    // 어느 하나라도 막으면 막고, 피해는 모든 상한을 차례로 지나며 가장 낮은 경계에서 멈춘다
    /// <summary> 여러 체력 경계 상한을 하나로 합친다 </summary>
    public sealed class BossDamageCapComposite : IBossDamageCap
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossDamageCap[] _caps;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsDamageBlocked
        {
            get
            {
                for (int i = 0; i < _caps.Length; ++i)
                {
                    if (_caps[i].IsDamageBlocked)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public BossDamageCapComposite(params IBossDamageCap[] caps)
        {
            if (caps == null)
            {
                throw new ArgumentNullException(nameof(caps));
            }

            for (int i = 0; i < caps.Length; ++i)
            {
                if (caps[i] == null)
                {
                    throw new ArgumentNullException(nameof(caps), "상한 계약이 비어 있다");
                }
            }

            _caps = (IBossDamageCap[])caps.Clone();
        }

        public float CapDamage(float currentHealth, float maxHealth, float damage)
        {
            float capped = damage;
            for (int i = 0; i < _caps.Length; ++i)
            {
                capped = _caps[i].CapDamage(currentHealth, maxHealth, capped);
                if (!(capped > 0f))
                {
                    return 0f;
                }
            }

            return capped;
        }
    }
}
