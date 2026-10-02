// System
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    /// <summary> 동료 스폰 추적 테스트용 개체 관리자, Mirror 없이 스폰과 해제 호출만 기록한다 </summary>
    public sealed class FakeCompanionObjectHost : ICompanionObjectHost
    {
        /****************************************
        *                Fields
        ****************************************/

        public struct Call
        {
            public ICompanionSlotSource Source;
            public int                  Slot;
            public int                  CompanionId;
        }

        /****************************************
        *              Properties
        ****************************************/

        public List<Call> Spawns       { get; } = new List<Call>();
        public List<Call> Despawns     { get; } = new List<Call>();
        public bool       FailSpawn    { get; set; }
        public int        PoolDestroys { get; private set; }
        public int        Live         => Spawns.Count - Despawns.Count;

        /****************************************
        *                Methods
        ****************************************/

        public bool TrySpawn(ICompanionSlotSource source, int slot, out CompanionCombatState state)
        {
            state = null;
            if (FailSpawn)
            {
                return false;
            }

            Spawns.Add(new Call { Source = source, Slot = slot, CompanionId = source.GetActiveCompanionId(slot) });
            return true;
        }

        public void Despawn(ICompanionSlotSource source, int slot, CompanionCombatState state)
        {
            Despawns.Add(new Call { Source = source, Slot = slot, CompanionId = source.GetActiveCompanionId(slot) });
        }

        public void DestroyPool() { ++PoolDestroys; }
    }
}
