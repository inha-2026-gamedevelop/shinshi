// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 기본 사건(IBossEventListener)과 따로 둔 선택 계약이다. 서버 몸체는 수신처가 이 계약을 구현할 때만 보내고,
    // 복제 상태(BossState)는 클라이언트 수신부가 IBossMucheolStateListener를 구현할 때만 넘긴다.
    // 시각은 모두 서버 시각이다. 시전 번호가 키이고 연속 돌진은 그 안에서 돌진 번호로 나눈다.
    // 시전의 정상 종료는 기본 사건의 OnFinished(castId)로 오며 복원 기록은 그때 지운다
    /// <summary> 무철 전용 공격 표현에 필요한 서버 확정 사건, 콤보와 순차 타격, 연속 돌진, 전장 공격, 돌진 충돌 결과 </summary>
    public interface IBossMucheolEventListener
    {
        /// <summary> 콤보(P1 P05)나 순차 타격(P2 P01, P02, P04, P05)의 예고 시작, 타 목록은 시전이 끝날 때까지 바뀌지 않는다 </summary>
        void OnStrikeChainStarted(in BossStrikeChainStart start);

        /// <summary> 추적 중 조준 갱신, 타별 방향은 이 방향에 타의 오프셋을 더한 것이다 </summary>
        void OnStrikeChainAimChanged(EBossStrikeChainKind kind, int castId, Vector3 direction, double serverTime);

        /// <summary> 방향 고정, 이후 모든 타가 이 방향을 쓴다 </summary>
        void OnStrikeChainDirectionLocked(EBossStrikeChainKind kind, int castId, Vector3 direction);

        /// <summary> stepIndex번째 타 확정, 적중 여부와 상관없이 온다 </summary>
        void OnStrikeChainHit(EBossStrikeChainKind kind, int castId, int stepIndex, double serverTime);

        /// <summary> 중단, 남은 타는 나오지 않는다 </summary>
        void OnStrikeChainCancelled(EBossStrikeChainKind kind, int castId);

        /// <summary> 연속 돌진(P2 P03, P06)의 첫 돌진 예고 시작 </summary>
        void OnMultiDashStarted(in BossMultiDashStart start);

        /// <summary> dashIndex번째 돌진의 예고나 재조준 중 조준 갱신, length는 벽 앞까지의 길이다 </summary>
        void OnMultiDashAimChanged(int castId, int dashIndex, Vector3 direction, float length, double serverTime);

        /// <summary> dashIndex번째 돌진의 방향 고정 </summary>
        void OnMultiDashDirectionLocked(int castId, int dashIndex, Vector3 direction, float length);

        /// <summary> 앞 돌진이 끝나고 다음 돌진의 재조준 시작 </summary>
        void OnMultiDashReaimStarted(in BossMultiDashReaim reaim);

        /// <summary> dashIndex번째 돌진 시작, startServerTime부터 speed로 가고 최대 distance에서 멈춘다 </summary>
        void OnMultiDashDashStarted(int castId, int dashIndex, float distance, float seconds, float speed, double startServerTime);

        /// <summary> dashIndex번째 돌진 정지, stopServerTime은 서버가 정지를 판정한 시각이다 </summary>
        void OnMultiDashDashStopped(int castId, int dashIndex, Vector3 position, bool stoppedByWall, double stopServerTime);

        /// <summary> 실행 구간이 짧아 계획한 돌진을 다 달리지 못했다, 남은 돌진은 나오지 않는다 </summary>
        void OnMultiDashTruncated(int castId, int plannedDashCount, int completedDashCount);

        /// <summary> 연속 돌진 중단 </summary>
        void OnMultiDashCancelled(int castId);

        /// <summary> 하드 전장 공격(P2 P07)의 예고 시작, 위치와 방향은 끝까지 고정이다 </summary>
        void OnBattlefieldStarted(in BossBattlefieldStart start);

        /// <summary> 전장 공격 타격 확정 </summary>
        void OnBattlefieldHit(int castId, double serverTime);

        /// <summary> 전장 공격 중단 </summary>
        void OnBattlefieldCancelled(int castId);

        /// <summary> 뿔 들이받기(P1 P06) 돌진의 끝, 예고와 돌진 자체는 기본 돌진 사건으로 온다. 한 시전에 한 번이다 </summary>
        void OnChargeEnded(in BossChargeOutcome outcome);

        /// <summary> 기믹 1의 마지막 구조물 파괴가 확인됐다, 같은 시전의 OnChargeEnded 뒤에 온다. 그로기 시작과 끝은 아직 모르면 NaN이다 </summary>
        void OnChargeGroggyPending(in BossChargeGroggy groggy);

        /// <summary> 기믹 1의 그로기가 시작됐다, 시작과 끝 서버 시각이 채워져 있고 끝 시각에 스스로 끝난다 </summary>
        void OnChargeGroggyStarted(in BossChargeGroggy groggy);

        /// <summary> 대기 중이거나 진행 중인 그로기를 사망과 전투 종료가 아닌 이유로 접었다 </summary>
        void OnChargeGroggyCancelled(int castId);
    }
}
