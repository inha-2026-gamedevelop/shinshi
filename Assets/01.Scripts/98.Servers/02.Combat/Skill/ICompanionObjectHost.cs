namespace Shinshi.Combat
{
    /// <summary> 동료 네트워크 개체의 실제 생성, 스폰, 해제를 맡는다. 서버 빌드는 Mirror 스포너, 테스트는 기록용 구현을 쓴다 </summary>
    public interface ICompanionObjectHost
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 칸의 동료 개체를 만들어 상태를 붙이고 스폰한다, 실패하면 아무것도 남기지 않고 false </summary>
        bool TrySpawn(ICompanionSlotSource source, int slot, out CompanionCombatState state);

        /// <summary> 칸에서 상태를 떼고 개체를 내린다, 개체는 다음 소환에 다시 쓴다 </summary>
        void Despawn(ICompanionSlotSource source, int slot, CompanionCombatState state);

        /// <summary> 서버 전투가 끝날 때 남은 개체를 모두 지운다 </summary>
        void DestroyPool();
    }
}
