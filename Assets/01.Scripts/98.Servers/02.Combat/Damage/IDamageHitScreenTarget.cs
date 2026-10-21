namespace Shinshi.Combat
{
    // IDamageCapTarget처럼 선택 계약이다. 판정기가 중복과 방어 확인을 마친 정상 적중마다, 치명 판정과 상한 적용 전에 한 번 부른다.
    // 무철 방향 봉인처럼 타격 자체를 소비하거나 체력 하한을 거는 대상만 구현한다
    /// <summary> 정상 적중 한 건을 먼저 살펴 소비하거나 체력 하한을 정하는 전투 대상의 선택 계약 </summary>
    public interface IDamageHitScreenTarget
    {
        // 참이면 대상이 타격을 소비했다. 판정기는 체력 피해, 치명, 처치, 피해 숫자, 생명력 흡수, 공명을 모두 만들지 않는다.
        // healthFloor는 이 타격으로 내려갈 수 있는 최저 체력(절대값)이고 0이면 하한이 없다. 하한에서 막힌 피해는 치명으로 알리지 않는다
        /// <summary> 정상 적중 한 건을 살핀다, 참이면 없던 피해이고 healthFloor는 체력 하한이다 </summary>
        bool ScreenHit(in DamageRequest request, bool isSummonAttack, out float healthFloor);
    }
}
