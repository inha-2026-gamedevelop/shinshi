// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    // IBossWallQuery는 이동 경로의 스윕이라 시작부터 겹쳐 있는 벽을 못 잡는다. 순간 이동은 도착 지점이 벽 안인지를 따로 물어야 하므로
    // 겹침 검사만 하는 선택 인터페이스로 분리했다. IBossWallQuery 구현체가 함께 구현하면 BossServer가 착지 검사에 쓴다
    /// <summary> 보스 몸체가 그 지점에 정적 지형과 겹치지 않고 설 수 있는가 </summary>
    public interface IBossStandQuery
    {
        /// <summary> point는 발 위치다. 그 위에 놓은 몸체 반경의 구가 정적 지형과 겹치지 않으면 참이다, 할당하지 않는다 </summary>
        bool CanStand(Vector3 point, float bodyRadius);
    }
}
