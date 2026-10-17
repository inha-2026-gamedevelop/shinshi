// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 표현 연결부가 IBossStateListener와 함께 구현하면 BossState가 월백 기믹 사건과 복원을 넘긴다. 구현하지 않으면 버려진다.
    // 복원은 기본 복원과 전용 복원 뒤에 오고, 끝난 기믹과 지난 일회성 효과(타격, 판정, 폭발)는 오지 않는다
    /// <summary> 클라이언트가 받는 월백 체력 기믹 사건과 늦은 접속, 재접속 복원 </summary>
    public interface IBossWolbaekGimmickStateListener : IBossWolbaekGimmickEventListener
    {
        /// <summary> 진행 중이거나 반격 중인 부적진 </summary>
        void OnTalismanRestored(in BossWolbaekTalismanRestore restore);

        /// <summary> 진행 중인 색 결계 </summary>
        void OnWardRestored(in BossWolbaekWardRestore restore);

        /// <summary> 진행 중이거나 반격 중인 여우불 봉인 </summary>
        void OnSealRestored(in BossWolbaekSealRestore restore);

        /// <summary> 진행 중인 최후의 영역 </summary>
        void OnFinalRestored(in BossWolbaekFinalRestore restore);

        /// <summary> 아직 끝나지 않은 워프 시전의 출발 위치 </summary>
        void OnWarpRestored(int castId, Vector3 departure, double serverTime);
    }
}
