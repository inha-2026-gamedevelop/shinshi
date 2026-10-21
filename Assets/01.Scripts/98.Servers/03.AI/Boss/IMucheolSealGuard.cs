namespace Shinshi.AI
{
    // 서버 피해 판정기가 보스 체력을 깎기 전에 부르는 창구다. 판정기는 정상 적중과 중복을 가른 뒤 보스에 들어오는 모든 타격을 여기로 보고하고,
    // 돌려받은 체력 하한 아래로는 체력을 깎지 않는다. 하한은 발동 전에는 0이라 정상 피해로 체력이 0이 되면 기존 처치 경로를 그대로 따른다.
    // 하한에서 막힌 피해는 치명, 사망, 과잉 피해로 알리지 않고 저장하지도 않는다
    /// <summary> 보스 체력 하한과 봉인 타수 판정을 판정기에 내주는 계약 </summary>
    public interface IMucheolSealGuard
    {
        /// <summary> 타격 한 건을 보고한다, 결과의 체력 하한은 이 타격이 처리되기 전의 값이라 봉인을 깨는 타격에도 하한이 적용된다 </summary>
        MucheolSealVerdict ReportHit(in MucheolSealHit hit);
    }

    /// <summary> 봉인이 타격 한 건에 내린 판정, 처리 결과와 이 타격에 적용할 체력 하한 </summary>
    public readonly struct MucheolSealVerdict
    {
        private readonly EMucheolSealHitResult _result;
        private readonly float                 _healthFloorRate;

        public EMucheolSealHitResult Result => _result;

        /// <summary> 이 타격으로 체력이 내려갈 수 있는 최저 비율, 0이면 하한이 없다. 봉인 중에는 발동 비율에 도착한 순간 남은 실제 체력이다 </summary>
        public float HealthFloorRate => _healthFloorRate;

        /// <summary> 봉인 타격으로 소비됐다, 판정기는 이 타격을 일반 피해나 처치로 보고하지 않고 생명력 흡수와 피해량 기반 공명도 주지 않는다 </summary>
        public bool ConsumesHit
        {
            get
            {
                return (_result == EMucheolSealHitResult.Counted)
                    || (_result == EMucheolSealHitResult.DirectionCleared)
                    || (_result == EMucheolSealHitResult.Broken);
            }
        }

        public MucheolSealVerdict(EMucheolSealHitResult result, float healthFloorRate)
        {
            _result          = result;
            _healthFloorRate = healthFloorRate;
        }
    }
}
