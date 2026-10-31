// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    /// <summary> 내 캐릭터(NetworkClient.localPlayer)의 체력을 읽기만 하는 어댑터, 전투 코드는 건드리지 않는다 </summary>
    public sealed class PrologueLocalVitals : IPrologueLocalVitals
    {
        /****************************************
        *                Fields
        ****************************************/

        private PlayerCombatState _combat = null;

        /****************************************
        *              Properties
        ****************************************/

        // 체력이 최대보다 낮으면 줄었다고 본다. 아직 내 캐릭터가 없거나 최대 체력이 정해지기 전이면 거짓이다
        public bool IsDamaged
        {
            get
            {
                if (_combat == null)
                {
                    NetworkIdentity local = NetworkClient.localPlayer;
                    if ((local == null) || (!local.TryGetComponent(out _combat)))
                    {
                        return false;
                    }
                }

                return (_combat.MaxHealth > 0f) && (_combat.Health < _combat.MaxHealth);
            }
        }
    }
}
