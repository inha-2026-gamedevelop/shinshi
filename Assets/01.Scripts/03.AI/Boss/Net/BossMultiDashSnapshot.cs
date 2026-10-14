// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 이미 끝난 앞 돌진은 담지 않는다. 표현의 BossMucheolMultiDashView.RestoreDash 입력과 같은 값이다
    /// <summary> 진행 중인 연속 돌진의 현재 돌진 하나 </summary>
    public struct BossMultiDashSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시전의 보스 번호, 시전 번호, 패턴 ID, 돌진 횟수는 여기서 읽는다 </summary>
        public BossMultiDashStart Start { get; set; }

        /// <summary> 지금 돌진 번호 </summary>
        public int DashIndex { get; set; }

        /// <summary> 지금 돌진의 시작 위치 </summary>
        public Vector3 Origin { get; set; }

        public Vector3 Direction { get; set; }

        public float Length { get; set; }

        public float Width { get; set; }

        public bool IsDirectionLocked { get; set; }

        /// <summary> 지금 돌진의 예고나 재조준 시작 서버 시각 </summary>
        public double TelegraphStartServerTime { get; set; }

        /// <summary> 지금 돌진의 예고나 재조준 시간(초) </summary>
        public float TelegraphSeconds { get; set; }

        public bool HasDashStarted { get; set; }

        public double DashStartServerTime { get; set; }

        public float DashDistance { get; set; }

        public float DashSeconds { get; set; }

        public float DashSpeed { get; set; }

        /// <summary> 돌진 시작 위치에서 멈춘 곳까지의 거리(m), 아직 가는 중이면 음수다 </summary>
        public float StoppedDistance { get; set; }

        public bool StoppedByWall { get; set; }

        /// <summary> 실행 구간이 짧아 남은 돌진이 잘렸다 </summary>
        public bool IsTruncated { get; set; }
    }
}
