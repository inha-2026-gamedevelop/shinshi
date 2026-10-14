// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 최후의 영역의 상태 알림이다. 복제 계층이 클라이언트로 전달하는 구성을 가정하며 구현은 아직 없다.
    // 서버 프로세스의 C# 호출은 원격 클라이언트에 닿지 않으므로 실제 연결에서는 ClientRpc와 늦은 접속 복원이 따로 필요하다.
    // 성공, 실패, 정지, 정리 알림은 모두 노란 여우불과 빨간 여우불과 바닥 표시 정리를 뜻한다
    /// <summary> 최후의 영역의 시작, 바닥 제거, 추락, 노란 여우불, 빨간 묶음, 판정, 성공과 실패와 정리 알림 </summary>
    public interface IWolbaekFinalAreaListener
    {
        /// <summary> 기믹에 진입해 중앙만 남는다는 예고가 시작됐다 </summary>
        void OnFinalStarted(in WolbaekFinalStart start);

        /// <summary> 외곽 바닥이 사라졌다, 제거 순간에는 아무도 죽이지 않으므로 fallen은 항상 비어 있다. 추락 사망은 OnParticipantsFell로 온다 </summary>
        void OnOuterFloorRemoved(int castInstanceId, double serverTime, IReadOnlyList<int> fallen);

        /// <summary> 외곽 제거 뒤 서버가 낙하를 확정해 추락 사망으로 처리한 참가자다. fallen은 호출 뒤 재사용되는 버퍼라 보관하려면 복사해야 한다 </summary>
        void OnParticipantsFell(int castInstanceId, double serverTime, IReadOnlyList<int> fallen);

        /// <summary> 노란 여우불이 중앙 바닥에 생겼다, 수는 진입 순간에 고정된 값이다 </summary>
        void OnYellowsSpawned(int castInstanceId, double serverTime, IReadOnlyList<WolbaekFinalYellow> yellows);

        /// <summary> 노란 여우불 하나가 부서졌다, remaining은 남은 수다 </summary>
        void OnYellowDestroyed(int castInstanceId, int yellowIndex, double serverTime, int remaining);

        /// <summary> 빨간 여우불 묶음이 나타나 추적을 시작했다 </summary>
        void OnBundleStarted(int castInstanceId, in WolbaekFinalBundle bundle);

        /// <summary> 대상이 쓰러지거나 접속이 끊겨 빨간 여우불이 마지막 서버 위치에 머문다, 다른 사람에게 넘기지 않는다 </summary>
        void OnRedDetached(int castInstanceId, int bundleIndex, int redIndex, double serverTime, Vector3 position);

        /// <summary> 빨간 여우불 낙하 위치가 고정됐다, centers는 묶음의 여우불 순서와 같다 </summary>
        void OnRedsLocked(int castInstanceId, int bundleIndex, double serverTime, IReadOnlyList<Vector3> centers);

        /// <summary> 빨간 폭발이 판정됐다, singleHits는 피해가 실제로 적용된 횟수이고 doubleKills는 중복 적중으로 사망한 수다 </summary>
        void OnBundleResolved(int castInstanceId, int bundleIndex, double serverTime, int singleHits, int doubleKills);

        /// <summary> 마지막 노란 여우불이 부서져 최종 성공이 확정됐다, 남은 빨간 폭발은 취소됐다 </summary>
        void OnFinalSucceeded(int castInstanceId, double serverTime);

        /// <summary> 파티가 전멸해 실패했다 </summary>
        void OnFinalFailed(int castInstanceId, double serverTime, EWolbaekFinalFail reason);

        /// <summary> 진행하지 못하고 멈췄다, 성공도 실패도 아니다. 표시를 정리하고 원인을 알린다 </summary>
        void OnFinalHalted(int castInstanceId, double serverTime, EWolbaekFinalHaltReason reason);

        /// <summary> 사망이나 전투 종료로 표시를 정리한다 </summary>
        void OnFinalCleared(int castInstanceId, EBossInterruptReason reason);
    }
}
