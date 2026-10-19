// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 일반 적이 나무와 바위를 돌아가는 길을 서버에서 묻는 창구다. 구현은 NavMesh 경로(운영)나 시험용 가짜다
    /// <summary> 일반 적의 도달 가능 판정과 경로 길이, 다음 이동 지점 조회 </summary>
    public interface IEnemyNavigation
    {
        /// <summary> from에서 to까지 걸어서 닿는 경로의 길이(m), 닿을 수 없으면 거짓 </summary>
        bool TryMeasurePath(Vector3 from, Vector3 to, out float length);

        /// <summary> from에서 to로 가는 경로의 다음 꺾임 지점, 닿을 수 없으면 거짓 </summary>
        bool TryGetSteerPoint(Vector3 from, Vector3 to, out Vector3 point);

        /// <summary> point가 걸어 설 수 있는 지면인가 </summary>
        bool IsWalkable(Vector3 point);
    }
}
