// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 돌진 알림(IBossDashTelegraphListener)은 시전 하나에 돌진 하나라 돌진마다 방향과 길이가 달라지는 연속 돌진을 담지 못한다.
    // 돌진 번호를 함께 실어 같은 시전 번호 안에서 돌진 단위로 구분한다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    /// <summary> 연속 돌진 패턴의 예고 시작, 추적과 재조준, 돌진 시작과 정지, 타격, 잘림, 취소 시점 알림 </summary>
    public interface IMucheolMultiDashListener
    {
        /// <summary> 첫 돌진의 예고 시작, length는 몸체가 벽 앞에서 실제로 갈 수 있는 거리다. dashCount는 이 시전이 달릴 돌진 횟수로 시전 중 바뀌지 않는다 </summary>
        void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width,
            float telegraphSeconds, int dashCount);

        /// <summary> 조준 방향이 실제로 바뀌었다, dashIndex번째 돌진의 예고나 재조준 중이며 방향이 바뀌면 벽까지의 거리도 달라져 length를 함께 보낸다 </summary>
        void OnAimChanged(int castInstanceId, int dashIndex, Vector3 direction, float length);

        /// <summary> dashIndex번째 돌진의 방향이 고정됐다, 그 돌진이 끝날 때까지 방향과 길이가 바뀌지 않는다 </summary>
        void OnDirectionLocked(int castInstanceId, int dashIndex, Vector3 direction, float length);

        /// <summary> 돌진이 끝나고 dashIndex번째 돌진의 재조준이 시작됐다, origin은 앞 돌진이 선 위치이며 reaimSeconds 뒤에 돌진이 시작된다 </summary>
        void OnReaimStarted(int castInstanceId, int dashIndex, Vector3 origin, Vector3 direction, float length, float width,
            float reaimSeconds);

        /// <summary> dashIndex번째 돌진 시작, startServerTime부터 초당 speed(m)로 일정하게 가고 최대 distance에서 멈춘다. startServerTime은 틱 시각이 아니라 계산한 경계다 </summary>
        void OnDashStarted(int castInstanceId, int dashIndex, Vector3 origin, Vector3 direction, float distance, float seconds,
            float speed, double startServerTime);

        /// <summary> dashIndex번째 돌진이 더 이상 가지 않는다, 정해진 거리에 닿았거나 벽에 막혔거나 실행이 끝나 잘렸다. stopServerTime은 정지를 판정한 시각이다 </summary>
        void OnDashStopped(int castInstanceId, int dashIndex, Vector3 position, bool stoppedByWall, double stopServerTime);

        /// <summary> dashIndex번째 돌진의 한 틱에 피해 요청이 실제로 나간 대상 수, 나간 것이 없으면 부르지 않는다 </summary>
        void OnHit(int castInstanceId, int dashIndex, int hitCount);

        /// <summary> 실행 구간이 짧아 계획한 돌진을 다 달리지 못하고 잘렸다, 남은 돌진은 나오지 않는다 </summary>
        void OnTruncated(int castInstanceId, int plannedDashCount, int completedDashCount);

        /// <summary> 도중에 중단됐다, 이후 이동과 타격은 나오지 않으며 이미 적용된 피해는 되돌리지 않는다 </summary>
        void OnCancelled(int castInstanceId, EBossInterruptReason reason);
    }
}
