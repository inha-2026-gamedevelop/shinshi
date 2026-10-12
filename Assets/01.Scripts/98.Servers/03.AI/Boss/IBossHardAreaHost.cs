namespace Shinshi.AI
{
    /// <summary> 하드 P04의 예약과 활성 시 가장 오래된 동일 패턴 장판 교체를 맡는다 </summary>
    public interface IBossHardAreaHost : IBossAreaHost
    {
        /// <summary> 기존 활성 장판을 유지한 채 새 장판의 빈 슬롯을 예약한다 </summary>
        bool TryReserveReplacing(int castInstanceId, int areaId, string patternId);

        /// <summary> 활성 직전에 동일 패턴의 오래된 장판을 필요한 만큼 종료하고 새 장판을 활성한다 </summary>
        bool TryActivateReplacing(in BossAreaSpec spec, int maxActive);
    }
}
