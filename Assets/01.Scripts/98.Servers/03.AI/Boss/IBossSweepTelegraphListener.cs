// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 패턴과 반월 연쇄 폭발 표현 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    // 좌우와 중심과 기준 방향과 구역과 시각은 모두 서버가 확정한 값이고 클라이언트는 추첨, 추적, 판정을 하지 않고 그대로 그린다
    // 구역 하나는 구역 번호(그 회차의 동작 번호)가 가리키는 키다. 시전 번호는 한 시전의 구역들이 함께 쓰는 묶음이다
    /// <summary> 반월 연쇄 폭발의 시작, 구역 예약, 구역 폭발, 취소 알림 </summary>
    public interface IBossSweepTelegraphListener
    {
        /// <summary> 시작, 중심과 기준 방향은 이 시점에 고정되며 시전이 끝날 때까지 바뀌지 않는다. isRight는 서버가 추첨한 쪽이고 blastSpanSeconds는 첫 구역부터 마지막 구역 폭발까지의 길이다 </summary>
        void OnSweepStarted(int castInstanceId, Vector3 origin, Vector3 baseDirection, bool isRight, float radius,
            float totalArcAngle, int zoneCount, float telegraphSeconds, float blastSpanSeconds);

        /// <summary> 구역 하나의 예약, centerDirection은 그 구역 부채꼴의 가운데 방향이고 폭발은 시작 시각에서 secondsUntilBlast 뒤다. 시작 직후 구역 수만큼 순서대로 온다 </summary>
        void OnZoneScheduled(int castInstanceId, int zoneId, int zoneIndex, Vector3 centerDirection, float zoneArcAngle,
            float secondsUntilBlast);

        /// <summary> 구역 폭발이 확정됐다, blastServerTime은 예약 시각이고 틱 시각이 아니다. hitCount는 이 구역에서 피해 요청이 실제로 나간 대상 수다 </summary>
        void OnZoneBlasted(int castInstanceId, int zoneId, int zoneIndex, int hitCount, double blastServerTime);

        /// <summary> 중단됐다, 아직 폭발하지 않은 구역은 나오지 않는다. 이미 폭발한 구역의 표현은 이 알림과 상관없이 끝까지 간다 </summary>
        void OnCancelled(int castInstanceId, EBossInterruptReason reason);
    }
}
