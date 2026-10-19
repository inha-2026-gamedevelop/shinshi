// Unity
using UnityEngine;

namespace Shinshi.Systems
{
    // 지형 높이를 0으로 가정하지 않는다. 지점은 맵 쪽이 NavMesh나 지면 검사로 확인한 값만 넘기고, 웨이브는 받은 y를 그대로 쓴다.
    // 웨이브 번호는 WaveConfigSO 정본 순서(0부터)다. 구역 대응은 구현이 정하고 웨이브는 구역 번호를 모른다.
    // 웨이브는 배치 순번 순서로 지점을 돌려 쓰고, 플레이어가 가까우면 다음 지점을 본다
    /// <summary> 웨이브가 적을 내보낼 수 있는 지면 유효 지점 목록, 맵 쪽이 구현해 웨이브 팩토리 생성자에 넘긴다 </summary>
    public interface IWaveSpawnPointSource
    {
        /// <summary> 웨이브가 쓸 지점 수, 0이면 그 웨이브를 열 수 없다. 웨이브가 시작될 때 한 번 부른다 </summary>
        int GetPointCount(int waveIndex);

        /// <summary> 웨이브의 index(0부터 GetPointCount 미만) 지점, 지면 위의 서 있을 수 있는 위치다 </summary>
        bool TryGetPoint(int waveIndex, int index, out Vector3 position);

        // 적이 매 틱 수평으로만 움직이므로 높이를 지면에 맞추는 일은 이 창구가 맡는다. 구현이 비용을 정한다.
        // 맞출 지면이 없으면 false를 돌려주고, 웨이브는 그 틱의 높이를 바꾸지 않는다
        /// <summary> position의 수평 좌표에서 지면 높이를 맞춘 위치, 매 틱 살아 있는 적마다 부른다 </summary>
        bool TrySnapToGround(int waveIndex, Vector3 position, out Vector3 grounded);

        // 웨이브 waveIndex를 모두 처치한 뒤 참가자가 도착해야 다음 웨이브가 시작되는 지점이다(다음 구역 입구 안쪽).
        // 마지막 웨이브나 지점이 없으면 false다. 지점이 없는 채로 웨이브가 끝나면 다음 웨이브는 열리지 않는다
        /// <summary> 웨이브 waveIndex를 마친 뒤 향할 다음 웨이브 시작 지점 </summary>
        bool TryGetNextWavePoint(int waveIndex, out Vector3 position);
    }
}
