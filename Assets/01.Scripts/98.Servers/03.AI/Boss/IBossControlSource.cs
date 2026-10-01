namespace Shinshi.AI
{
    // 플레이어 기술이 보스에 제어를 거는 계약은 코드에 아직 없다
    // TODO(민성): 제어 적용 계약이 정해지면 이 창구를 구현한다 (U05, U07)
    // TODO(기웅): 기획 미정 - 직접 제어가 걸린 동안 보스가 멈추는지, 제어 지속 시간을 어떻게 쓰는지 정해지지 않았다
    /// <summary> 보스에 도착한 직접 제어를 한 건씩 넘겨 주는 창구 </summary>
    public interface IBossControlSource
    {
        /// <summary> 대기 중인 직접 제어 한 건을 꺼낸다, 허용 구간이 아닌 제어는 보스가 버린다 </summary>
        bool TryConsumeDirectControl();
    }
}
