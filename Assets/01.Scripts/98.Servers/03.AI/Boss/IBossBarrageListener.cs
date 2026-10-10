// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 패턴과 전방위 투사체 난사 표현 사이의 연결 자리다. 복제 계층이 이 알림을 클라이언트로 전달하는 구성을 가정한다. 구현은 아직 없다
    // 각 발의 방향은 그 발의 예고 알림에서 처음 알린다. 시작 알림에는 방향이 없어 클라이언트가 예고 전에 다음 발의 방향을 알 수 없다
    // 발이 나간 뒤의 비행과 소멸은 IBossProjectileListener가 알린다. 패턴이 끝나도 투사체는 남기 때문에 알림을 나눴다
    //
    // 시각은 모두 서버 시각이다. 예정 시각은 계약대로의 시각이고 처리 시각은 서버가 그 일을 처리한 틱 시각이다. 큰 틱으로 늦게 처리돼도 예정 시각은 바뀌지 않는다
    //   발 k의 예고 예정 시각 = castStartServerTime + k x shotIntervalSeconds, 예고는 발사 telegraphSeconds 전에 시작해 발사 때까지 이어진다
    //   발 k의 발사 예정 시각 = castStartServerTime + telegraphSeconds + k x shotIntervalSeconds
    // 늦게 접속한 클라이언트가 복원하려면 복제 계층이 시작 알림의 값과 지금까지 알린 예고(구역, 방향, 예정 시각), 발사 수, 취소 여부를 상태로 들고 있어야 한다.
    // 이 알림만으로는 복원되지 않는다
    /// <summary> 전방위 투사체 난사의 시작, 발별 예고, 발사 처리, 발사 전 취소 알림 </summary>
    public interface IBossBarrageListener
    {
        /// <summary> 시작, 원점은 이 시점에 고정되며 시전이 끝날 때까지 바뀌지 않는다. 방향은 알리지 않는다 </summary>
        void OnBarrageStarted(int castInstanceId, Vector3 origin, double castStartServerTime, int shotCount, float telegraphSeconds,
            float shotIntervalSeconds, float width);

        /// <summary> 발 하나의 예고 시작, 방향과 벽 앞까지의 경로 길이(최대 사거리 이하)와 구역 번호를 처음 알린다. 발마다 한 번이고 발 번호 순서대로 온다 </summary>
        void OnBarrageShotTelegraphed(int castInstanceId, int shotIndex, int sectorIndex, Vector3 direction, float pathLength,
            double scheduledServerTime, double processedServerTime);

        /// <summary> 발 하나의 발사 처리, 발사 창구가 받아들였는지(isLaunched)를 알린다. 받아들이지 않은 발은 다시 쏘지 않는다. 발마다 한 번이고 발 번호 순서대로 온다 </summary>
        void OnBarrageShotFired(int castInstanceId, int shotIndex, int projectileId, bool isLaunched, double scheduledServerTime,
            double processedServerTime);

        /// <summary> 중단됐다, 아직 나가지 않은 발과 예고는 정리한다. 이미 나간 투사체는 IBossProjectileListener가 따로 알린다. 이미 적용된 피해는 되돌리지 않는다 </summary>
        void OnBarrageCancelled(int castInstanceId, int telegraphedCount, int firedCount, EBossInterruptReason reason);
    }
}
