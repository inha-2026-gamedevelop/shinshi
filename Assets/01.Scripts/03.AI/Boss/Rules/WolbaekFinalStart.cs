// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 값만 담는다. 클라이언트는 이 값으로 표시를 재현하고 판정은 하지 않는다
    /// <summary> 최후의 영역의 시작 입력, 바닥 제거 시각과 노란 여우불 수와 빨간 여우불 시간표 </summary>
    public readonly struct WolbaekFinalStart
    {
        /****************************************
        *              Properties
        ****************************************/

        public int BossCombatId { get; }

        /// <summary> 서버가 발급한 시전 번호, 이 기믹의 모든 알림이 같은 번호를 싣는다 </summary>
        public int CastInstanceId { get; }

        /// <summary> 기믹에 진입한 서버 시각 </summary>
        public double StartServerTime { get; }

        /// <summary> 외곽 바닥이 사라지는 서버 시각 </summary>
        public double CollapseServerTime { get; }

        /// <summary> m, 바닥 제거 뒤에도 남는 중앙 반경 </summary>
        public float CentralRadius { get; }

        /// <summary> 진입 순간 생존자 수로 고정된 노란 여우불 수, 이후 바뀌지 않는다 </summary>
        public int YellowCount { get; }

        /// <summary> 노란 여우불 한 개의 체력 </summary>
        public float YellowMaxHealth { get; }

        /// <summary> 초, 빨간 여우불 묶음이 시작된 뒤 낙하 위치를 따라가는 시간 </summary>
        public float RedTrackSeconds { get; }

        /// <summary> 초, 낙하 위치가 고정된 뒤 폭발까지의 시간 </summary>
        public float RedFuseSeconds { get; }

        /// <summary> m, 빨간 여우불 폭발 반경 </summary>
        public float RedBlastRadius { get; }

        /// <summary> 전장 중심 </summary>
        public Vector3 ArenaCenter { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekFinalStart(int bossCombatId, int castInstanceId, double startServerTime, double collapseServerTime,
            float centralRadius, int yellowCount, float yellowMaxHealth, float redTrackSeconds, float redFuseSeconds,
            float redBlastRadius, Vector3 arenaCenter)
        {
            BossCombatId       = bossCombatId;
            CastInstanceId     = castInstanceId;
            StartServerTime    = startServerTime;
            CollapseServerTime = collapseServerTime;
            CentralRadius      = centralRadius;
            YellowCount        = yellowCount;
            YellowMaxHealth    = yellowMaxHealth;
            RedTrackSeconds    = redTrackSeconds;
            RedFuseSeconds     = redFuseSeconds;
            RedBlastRadius     = redBlastRadius;
            ArenaCenter        = arenaCenter;
        }
    }
}
