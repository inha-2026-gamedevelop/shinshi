namespace Shinshi.AI
{
    /// <summary> 일반전 적 출현을 맡는 서버 진행자, 웨이브 세션이 웨이브 번호와 source ID로 출현을 요청한다 </summary>
    public interface ISpawnDirector
    {
        /// <summary> 동시에 살아 있을 수 있는 적 수 </summary>
        int ActiveLimit  { get; }

        // TODO(기웅): 기획 미정 - 출현 예고 1초 동안 슬롯을 차지하는지 정해지지 않았다
        /// <summary> 지금 활성 슬롯을 차지한 적 수, ActiveLimit을 넘지 않는다 </summary>
        int ActiveCount  { get; }

        /// <summary> 수락했지만 아직 활성 슬롯을 받지 못한 출현 요청 수 </summary>
        int PendingCount { get; }

        /// <summary> 진행 통지를 받을 대상을 정한다, 첫 출현 요청 전에 한 번 부른다 </summary>
        void SetListener(ISpawnProgressListener listener);

        /// <summary>
        /// 출현을 요청한다. 상한이나 출현 금지 조건으로 바로 나올 수 없으면 대기열에 넣고
        /// 빈자리가 생길 때 요청 순서대로 내보낸다. 수락한 요청은 버리지 않는다.
        /// source ID가 비었거나 이미 요청한 ID이거나 Halt 뒤이면 false를 돌려주고 아무것도 하지 않는다.
        /// waveIndex는 0부터 센다.
        /// </summary>
        bool TryRequestSpawn(int waveIndex, string sourceId);

        // TODO(기웅): 기획 미정 - 시간 초과로 끝났을 때 남은 활성 적을 지울지 그대로 둘지 정해지지 않았다
        /// <summary> 세션이 끝났을 때 부른다, 대기 중인 출현을 버리고 이후 요청을 거절하며 더는 진행 통지를 보내지 않는다 </summary>
        void Halt();
    }
}
