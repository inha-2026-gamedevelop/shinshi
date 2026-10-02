namespace Shinshi.Combat
{
    /// <summary> 동료 칸을 가진 서버 실행 객체, 스폰 추적기가 칸별 활성 동료를 읽고 네트워크 상태를 붙였다 뗀다 </summary>
    public interface ICompanionSlotSource
    {
        /****************************************
        *              Properties
        ****************************************/

        int ParticipantId      { get; }
        int CompanionSlotCount { get; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 활성 동료의 전투 번호, 비어 있거나 복귀했으면 CombatIds.NONE. 재호출하면 새 번호가 된다 </summary>
        int GetActiveCompanionId(int slot);

        /// <summary> 칸에 네트워크 상태를 붙이고 현재 값을 바로 써 넣는다, 스폰 전에 불러 첫 스폰 메시지에 값이 실리게 한다 </summary>
        bool BindCompanionState(int slot, CompanionCombatState state);

        /// <summary> 칸의 네트워크 상태를 뗀다, 내린 개체를 풀에서 다시 쓸 때 옛 소유자가 값을 쓰지 않게 한다 </summary>
        void UnbindCompanionState(int slot);
    }
}
