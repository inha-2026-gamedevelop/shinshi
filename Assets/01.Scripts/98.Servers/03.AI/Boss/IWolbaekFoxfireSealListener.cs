// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 여우불 유도와 봉인 핵 기믹의 상태 알림이다. 복제 계층이 클라이언트로 전달하는 구성을 가정하며 구현은 아직 없다
    // 서버 프로세스의 C# 호출은 원격 클라이언트에 닿지 않으므로 실제 연결에서는 ClientRpc와 늦은 접속 복원이 따로 필요하다.
    // 성공, 실패, 정지, 정리 알림은 모두 핵과 여우불과 휩쓸기 표시 정리를 뜻한다
    /// <summary> 여우불 유도와 봉인 핵 기믹의 시작, 차례, 추적, 휩쓸기, 판정, 반격, 성공과 실패와 정리 알림 </summary>
    public interface IWolbaekFoxfireSealListener
    {
        /// <summary> 기믹에 진입했다 </summary>
        void OnSealStarted(in WolbaekSealStart start);

        /// <summary> 새 차례가 시작돼 핵과 추적 여우불이 나타났다 </summary>
        void OnTurnStarted(int castInstanceId, in WolbaekSealTurn turn);

        /// <summary> 준비가 끝나 여우불 낙하 위치가 대상을 따라가기 시작했다 </summary>
        void OnTrackingStarted(int castInstanceId, int turnIndex, double serverTime);

        /// <summary> 대상이 쓰러지거나 접속이 끊겨 여우불이 마지막 서버 위치에 머문다, 다른 사람에게 넘기지 않는다 </summary>
        void OnFoxfireDetached(int castInstanceId, int turnIndex, int foxfireIndex, double serverTime, Vector3 position);

        /// <summary> 하드의 넓은 휩쓸기 예고가 시작됐다 </summary>
        void OnSweepTelegraphed(int castInstanceId, in WolbaekSealSweep sweep);

        /// <summary> 하드의 넓은 휩쓸기 판정이 났다, hitCount는 실제로 피해가 적용된 대상 수다 </summary>
        void OnSweepResolved(int castInstanceId, int turnIndex, int sweepIndex, double serverTime, int hitCount);

        /// <summary> 여우불 낙하 위치가 고정됐다, centers는 차례의 여우불 순서와 같다 </summary>
        void OnFoxfiresLocked(int castInstanceId, int turnIndex, double serverTime, IReadOnlyList<Vector3> centers);

        /// <summary> 낙하와 핵 파괴가 판정됐다, playerHits는 폭발에 실제로 피해가 적용된 횟수다 </summary>
        void OnTurnResolved(int castInstanceId, int turnIndex, double serverTime, int destroyedCores, int totalCores,
            int playerHits, bool succeeded, int successCount);

        /// <summary> 필요한 성공 횟수를 채워 통과했다, 표시와 공격은 정리되고 월백 무적은 풀려 반격 시간이 시작된다 </summary>
        void OnSealSucceeded(int castInstanceId, double serverTime, double counterEndServerTime);

        /// <summary> 반격 시간이 끝났다, reachedNextBoundary가 참이면 다음 경계 기믹의 이어받기를 기다린다 </summary>
        void OnCounterEnded(int castInstanceId, double serverTime, bool reachedNextBoundary);

        /// <summary> 제한 시간이 지났거나 파티가 전멸해 실패했다 </summary>
        void OnSealFailed(int castInstanceId, double serverTime, EWolbaekSealFail reason);

        /// <summary> 진행하지 못하고 멈췄다, 성공도 실패도 아니다. 표시를 정리하고 원인을 알린다 </summary>
        void OnSealHalted(int castInstanceId, double serverTime, EWolbaekSealHaltReason reason);

        /// <summary> 사망이나 전투 종료로 표시를 정리한다 </summary>
        void OnSealCleared(int castInstanceId, EBossInterruptReason reason);
    }
}
