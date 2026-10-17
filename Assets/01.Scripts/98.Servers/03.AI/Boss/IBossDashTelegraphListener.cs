// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 패턴과 돌진 예고 VFX 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    // 직사각형 알림과 나눈 이유는 벽 때문에 예고 길이가 조준마다 달라져 방향 알림이 길이를 함께 실어야 하기 때문이다
    /// <summary> 돌진 패턴의 예고 시작, 추적 방향 변경, 방향 고정, 돌진 시작과 정지, 타격, 취소 시점 알림 </summary>
    public interface IBossDashTelegraphListener
    {
        /// <summary> 예고 시작, length는 몸체가 벽 앞에서 실제로 갈 수 있는 거리로 정의의 최대 거리 이하다. 피해 판정도 이 길이와 width를 쓴다 </summary>
        void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width, float telegraphSeconds);

        /// <summary> 추적 중 조준 방향이 실제로 바뀌었다, 방향이 바뀌면 벽까지의 거리도 달라지므로 length를 함께 보낸다 </summary>
        void OnAimChanged(int castInstanceId, Vector3 direction, float length);

        /// <summary> 방향이 고정됐다, 이후 같은 시전에서는 방향과 길이가 바뀌지 않는다 </summary>
        void OnDirectionLocked(int castInstanceId, Vector3 direction, float length);

        /// <summary> 돌진 시작, startServerTime부터 초당 speed(m)로 일정하게 가고 최대 distance(벽 앞 거리)에서 멈춘다. startServerTime은 틱 시각이 아니라 FSM의 단계 경계라 틱이 거칠어도 정확하다. seconds는 실행 시간이며 벽에서 일찍 멈춰도 그 끝까지 실행이다 </summary>
        void OnDashStarted(int castInstanceId, Vector3 origin, Vector3 direction, float distance, float seconds, float speed,
            double startServerTime);

        /// <summary> 더 이상 가지 않는다, 정해진 거리에 닿았거나 벽 앞에서 멈췄다. 정지 위치는 position이고 이후 피해는 나오지 않는다. stopServerTime은 정지를 판정한 틱 시각이나 실행 끝 경계다 </summary>
        void OnDashStopped(int castInstanceId, Vector3 position, bool stoppedByWall, double stopServerTime);

        /// <summary> 한 틱에 피해 요청이 실제로 나간 대상 수, 나간 것이 없으면 부르지 않는다 </summary>
        void OnHit(int castInstanceId, int hitCount);

        /// <summary> 도중에 중단됐다, 이후 이동과 타격은 나오지 않으며 이미 적용된 피해는 되돌리지 않는다 </summary>
        void OnCancelled(int castInstanceId, EBossInterruptReason reason);
    }
}
