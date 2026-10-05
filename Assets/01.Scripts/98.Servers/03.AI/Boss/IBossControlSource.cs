namespace Shinshi.AI
{
    // BossServer가 기술 속박(ISkillControlTarget.ApplyRoot)을 받아 두었다가 이 창구로 한 건씩 넘긴다
    // 허용 구간, 한 패턴에 최초 1회, 적용 뒤 면역은 보스 쪽이 판정한다
    // 속박 등 취소형이 아닌 제어가 보스를 어떻게 움직이는지는 협의 전이라 구현하지 않았다
    // 제어 종류를 일괄적으로 이동, 회전, 공격 정지로 해석하지 않는다. 그 가능성을 열어 두려고 제어를 값 묶음(BossDirectControl)으로 받는다
    /// <summary> 보스에 도착한 직접 제어를 한 건씩 넘겨 주는 창구 </summary>
    public interface IBossControlSource
    {
        /// <summary> 대기 중인 직접 제어 한 건을 꺼낸다, 허용 구간이 아닌 제어는 보스가 버린다 </summary>
        bool TryConsumeDirectControl(out BossDirectControl control);
    }
}
