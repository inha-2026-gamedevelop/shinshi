// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 장판 한 개의 활성과 타격과 소멸 알림이다. 복제 계층이 클라이언트로 전달하는 구성을 가정하며 구현은 아직 없다
    // 장판 한 개는 장판 번호가 가리키는 키다. 시전 번호는 같은 시전의 장판들이 함께 쓰는 묶음이라 한 장판의 종료가 다른 장판을 지우지 않는다
    // 클라이언트는 피해와 대상 조회를 다시 계산하지 않는다
    /// <summary> 장판의 활성, 타격 회차, 소멸 시점 알림 </summary>
    public interface IBossAreaListener
    {
        /// <summary> 활성, 이 서버 시각부터 duration 동안 유지하고 tickInterval마다 타격한다. 활성 순간이 첫 타격이다 </summary>
        void OnAreaActivated(int castInstanceId, int areaId, Vector3 center, float radius, float durationSeconds, float tickInterval,
            double activateServerTime);

        /// <summary> 타격 회차 하나가 끝났다, hitIndex는 0부터이고 roundServerTime은 그 회차가 도래한 시각이다. hitCount는 피해 요청이 실제로 나간 대상 수다 </summary>
        void OnAreaTick(int castInstanceId, int areaId, int hitIndex, double roundServerTime, int hitCount);

        /// <summary> 소멸, Expired는 지속 시간 종료(추가 타격 없음), Cancelled는 즉시 제거다. endServerTime은 소멸 시각이다 </summary>
        void OnAreaEnded(int castInstanceId, int areaId, EBossAreaEnd end, double endServerTime);
    }
}
