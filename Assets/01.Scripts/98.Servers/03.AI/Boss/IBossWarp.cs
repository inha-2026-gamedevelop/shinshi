// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // ICombatTarget은 읽기 전용이라 패턴이 보스 위치를 바꿀 수 없고, IBossMovement.Move는 벽 앞까지 직선으로 미는 이동이라 순간 이동을 대신할 수 없다.
    // Move를 순간 이동처럼 쓰면 착지 지점 검사와 서버 권한 판단을 패턴이 우회하게 되므로 별도 창구를 둔다.
    // 전장 경계 검사는 IBossArenaSource와 몸체 반경으로 패턴이 하고, 이 창구는 서버 월드를 알아야 답할 수 있는 충돌, 경로, 위치 변경만 맡는다.
    // 구현이 위치 변경의 최종 권한이다. TryWarp는 패턴의 사전 검사를 믿지 않고 전장 경계와 충돌을 다시 확인해야 한다.
    // 운영 서버의 구현은 아직 없다. 구현이 없으면 이 창구가 필요한 패턴은 팩토리가 만들지 않는다
    /// <summary> 보스 몸체의 서버 순간 이동과 착지 검사 창구, 구현은 서버 몸체가 한다 </summary>
    public interface IBossWarp
    {
        /// <summary> 충돌 검사, point에 보스 몸체가 정적 지형과 구조물과 다른 몸체에 걸리지 않고 설 수 있는가 </summary>
        bool CanStandAt(Vector3 point);

        /// <summary> 경로 검사, from에서 to까지 보스가 걸어서 이어지는가. 한 번의 호출이 서버 틱 경로 계산 예산 안이어야 한다 </summary>
        bool HasPath(Vector3 from, Vector3 to);

        /// <summary> 보스 위치를 point로 옮긴다, 성공하면 실제로 선 위치를 돌려주고 실패하면 위치는 그대로다 </summary>
        bool TryWarp(Vector3 point, out Vector3 landed);
    }
}
