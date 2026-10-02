// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // ICombatTarget은 읽기 전용이라 패턴이 보스 위치를 바꿀 수 없다. 위치 변경과 벽 검사는 서버 몸체가 맡고 패턴은 이 창구로만 부탁한다
    // 플레이어와 소환물은 이 창구의 검사 대상이 아니다. 몸체가 막히는 것은 정적 지형뿐이다
    /// <summary> 보스 몸체의 서버 이동과 벽 검사, 구현은 서버 몸체가 하고 패턴은 결과만 읽는다. 벽 검사는 IBossWallQuery의 GetClearDistance다 </summary>
    public interface IBossMovement : IBossWallQuery
    {
        /// <summary> from에서 to로 직선 이동을 시도해 보스 위치를 옮기고 실제로 도착한 위치를 돌려준다, 벽이 막으면 닿기 전 위치다 </summary>
        Vector3 Move(Vector3 from, Vector3 to, float bodyRadius);
    }
}
