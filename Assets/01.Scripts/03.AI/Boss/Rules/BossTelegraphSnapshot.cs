// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 늦게 접속하거나 다시 관찰을 시작한 클라이언트가 지금 진행 중인 공격을 복원하는 데 필요한 값 묶음이다
    // 최종 전송 형식은 네트워크 담당이 정한다. 표현은 이 값이 있으면 처음부터 다시 재생하지 않고 서버 시각 기준으로 이어서 그린다
    /// <summary> 진행 중인 예고 한 건의 현재 상태, Start는 시작 입력이고 나머지는 시작 뒤에 바뀐 값이다 </summary>
    public struct BossTelegraphSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public BossTelegraphStart Start { get; set; }

        /// <summary> 지금의 조준 방향, 수평 단위 벡터 </summary>
        public Vector3 Direction { get; set; }

        /// <summary> 지금 보여 주는 길이, 돌진은 방향이 바뀌면 벽 때문에 달라진다 </summary>
        public float Length { get; set; }

        /// <summary> 방향이 고정됐다 </summary>
        public bool IsDirectionLocked { get; set; }

        public EBossTelegraphPhase Phase { get; set; }

        /// <summary> 발동 서버 시각, Phase가 Execute일 때만 뜻이 있다. 돌진은 이동을 시작한 시각이다 </summary>
        public double ExecuteServerTime { get; set; }

        /// <summary> 돌진 최대 거리(벽 앞까지), 돌진이 아니면 0 </summary>
        public float DashDistance { get; set; }

        /// <summary> 돌진 실행 시간(초) </summary>
        public float DashSeconds { get; set; }

        /// <summary> 돌진 속도(m/초), 서버가 정의의 거리 / 실행 시간으로 정한 값이다 </summary>
        public float DashSpeed { get; set; }

        /// <summary> 돌진이 멈춘 거리, 아직 가는 중이면 음수 </summary>
        public float DashStoppedDistance { get; set; }

        /// <summary> 정해진 거리가 아니라 벽에서 멈췄다 </summary>
        public bool DashStoppedByWall { get; set; }

        /// <summary> 서버가 돌진 정지를 판정한 서버 시각, 아직 가는 중이거나 모르면 NaN이다 </summary>
        public double DashStoppedServerTime { get; set; }
    }
}
