namespace Shinshi.AI
{
    // 서버 몸체(BossServer)가 무철 방향 봉인의 진행을 복제 상태로 넘기는 선택 계약이다. 클라이언트 값(EMucheolSealSide)만 실어
    // 표현이 서버 어셈블리를 참조하지 않게 한다. 요구 타수는 출정 시작 인원으로 한 번 정해져 모든 사건에서 같다
    /// <summary> 무철 방향 봉인 사건 </summary>
    public interface IBossMucheolSealEventListener
    {
        /// <summary> 봉인이 발동하고 진행 중이던 패턴이 끝나 첫 표식(오른쪽)이 나왔다 </summary>
        void OnSealRevealed(int requiredHits);

        /// <summary> 활성 방향의 유효타 수가 올랐다 </summary>
        void OnSealHitCounted(EMucheolSealSide side, int hitCount, int requiredHits);

        /// <summary> 방향이 채워져 다음 방향으로 넘어갔다 </summary>
        void OnSealDirectionCleared(EMucheolSealSide side, int requiredHits);

        /// <summary> 마지막 방향이 채워져 봉인이 깨졌다 </summary>
        void OnSealBroken(int requiredHits);

        /// <summary> 사망이나 전투 종료로 표식이 나와 있던 봉인이 멈췄다 </summary>
        void OnSealStopped();
    }
}
