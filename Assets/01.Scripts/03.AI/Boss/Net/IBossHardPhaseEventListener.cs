// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 기본 사건(IBossEventListener)과 따로 둔 선택 계약이다. 서버 몸체는 수신처가 이 계약을 구현할 때만 보낸다.
    // 배치와 시각은 서버가 시전 시작에 고정한 값이고 서버 전용 배치 타입 대신 풀어 쓴 값으로 싣는다.
    // 예정 시각(scheduled)은 계약대로의 시각이라 서버가 늦게 처리해도 바뀌지 않는다. 처리 시각과 적중 수는 표현에 쓰지 않아 싣지 않는다.
    // P04 장판의 활성 뒤와 P05 투사체의 비행은 기본 사건(OnAreaActivated, OnProjectileLaunched)으로 온다
    /// <summary> 월백 하드 2페이즈 P01~P06 표현에 필요한 서버 확정 사건 </summary>
    public interface IBossHardPhaseEventListener
    {
        /// <summary> P01 단계형 별 빔 시작 </summary>
        void OnStarStageStarted(in BossStarStageStart start);

        /// <summary> P01 단계 하나의 타격 확정 </summary>
        void OnStarStageStruck(int castId, int stageIndex, float rotationDegrees, double scheduledServerTime);

        /// <summary> P01 중단, 남은 단계는 타격하지 않는다 </summary>
        void OnStarStageCancelled(int castId);

        /// <summary> P02 격자 순차 빔 시작 </summary>
        void OnGridBeamStarted(in BossGridBeamStart start);

        /// <summary> P02 줄 하나의 강조 예고 시작 </summary>
        void OnGridBeamLineTelegraphed(int castId, int lineId, bool isHorizontal, int lineIndex, double telegraphServerTime,
            double strikeServerTime);

        /// <summary> P02 줄 하나의 타격 확정 </summary>
        void OnGridBeamLineStruck(int castId, int lineId, bool isHorizontal, int lineIndex, double scheduledServerTime);

        /// <summary> P02 중단, 타격하지 않은 줄은 정리한다 </summary>
        void OnGridBeamCancelled(int castId);

        /// <summary> P03 잔류 부채꼴 연쇄 시작 </summary>
        void OnChainFanStarted(in BossChainFanStart start);

        /// <summary> P03 구역 하나의 폭발, 폭발한 구역은 위험 종료까지 남는다 </summary>
        void OnChainFanBlasted(int castId, int zoneIndex, double scheduledServerTime);

        /// <summary> P03 잔류 위험 종료, 모든 구역이 같은 시각에 사라진다 </summary>
        void OnChainFanDangerEnded(int castId, double scheduledServerTime);

        /// <summary> P03 중단, 남은 구역과 잔류 위험을 정리한다 </summary>
        void OnChainFanCancelled(int castId);

        /// <summary> P04 추적 장판의 이동 예고 시작, 장판 번호가 키다 </summary>
        void OnTrackingAreaStarted(in BossTrackingAreaStart start);

        /// <summary> P04 이동 예고의 중심 갱신 </summary>
        void OnTrackingAreaMoved(int areaId, Vector3 center, double serverTime);

        /// <summary> P04 중심 고정, 이후 활성까지 움직이지 않는다 </summary>
        void OnTrackingAreaFixed(int areaId, Vector3 center, double fixedServerTime);

        /// <summary> P04 활성 전 취소 </summary>
        void OnTrackingAreaCancelled(int areaId);

        /// <summary> P05 전방위 난사 시작, 방향은 발별 예고에서 처음 알린다 </summary>
        void OnBarrageStarted(in BossBarrageStart start);

        /// <summary> P05 발 하나의 예고 시작 </summary>
        void OnBarrageShotTelegraphed(in BossBarrageShot shot);

        /// <summary> P05 발 하나의 발사 처리, isLaunched가 참이면 projectileId의 투사체 사건이 따로 온다 </summary>
        void OnBarrageShotFired(int castId, int shotIndex, int projectileId, bool isLaunched, double scheduledServerTime);

        /// <summary> P05 중단, 나가지 않은 발의 예고를 정리한다 </summary>
        void OnBarrageCancelled(int castId);

        /// <summary> P06 동시 원형 폭발 시작, 원의 중심과 반경은 서버 확정값이다 </summary>
        void OnCircleBurstStarted(in BossCircleBurstStart start);

        /// <summary> P06 폭발 확정 </summary>
        void OnCircleBurstStruck(int castId, double scheduledServerTime);

        /// <summary> P06 중단 </summary>
        void OnCircleBurstCancelled(int castId);
    }
}
