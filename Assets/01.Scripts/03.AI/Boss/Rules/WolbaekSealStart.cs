// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 값만 담는다. 클라이언트는 이 값으로 표시를 재현하고 판정은 하지 않는다
    /// <summary> 여우불 유도와 봉인 핵 기믹의 시작 입력, 차례 시간과 반경과 제한 시간 </summary>
    public readonly struct WolbaekSealStart
    {
        /****************************************
        *              Properties
        ****************************************/

        public int BossCombatId { get; }

        /// <summary> 서버가 발급한 시전 번호, 이 기믹의 모든 알림이 같은 번호를 싣는다 </summary>
        public int CastInstanceId { get; }

        /// <summary> 기믹에 진입한 서버 시각 </summary>
        public double StartServerTime { get; }

        /// <summary> 전체 제한 시간이 끝나는 서버 시각, 차례 사이 정리 시간도 포함한 제한이다 </summary>
        public double LimitEndServerTime { get; }

        /// <summary> 기믹을 통과하는 데 필요한 누적 성공 횟수 </summary>
        public int RequiredSuccesses { get; }

        /// <summary> 초, 핵과 여우불이 나타난 뒤 추적이 시작되기까지 </summary>
        public float PrepareSeconds { get; }

        /// <summary> 초, 낙하 위치가 대상을 따라가는 시간 </summary>
        public float TrackSeconds { get; }

        /// <summary> 초, 낙하 위치가 고정된 뒤 폭발까지 빠져나갈 시간 </summary>
        public float EscapeSeconds { get; }

        /// <summary> 초, 판정 뒤 다음 차례까지의 정리 시간 </summary>
        public float CleanupSeconds { get; }

        /// <summary> m, 여우불 폭발 반경 </summary>
        public float BlastRadius { get; }

        /// <summary> m, 낙하 중심에서 핵 중심까지 이 거리 이내면 파괴로 인정한다 </summary>
        public float CoreHitRadius { get; }

        /// <summary> 전장 중심 </summary>
        public Vector3 ArenaCenter { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekSealStart(int bossCombatId, int castInstanceId, double startServerTime, double limitEndServerTime,
            int requiredSuccesses, float prepareSeconds, float trackSeconds, float escapeSeconds, float cleanupSeconds,
            float blastRadius, float coreHitRadius, Vector3 arenaCenter)
        {
            BossCombatId       = bossCombatId;
            CastInstanceId     = castInstanceId;
            StartServerTime    = startServerTime;
            LimitEndServerTime = limitEndServerTime;
            RequiredSuccesses  = requiredSuccesses;
            PrepareSeconds     = prepareSeconds;
            TrackSeconds       = trackSeconds;
            EscapeSeconds      = escapeSeconds;
            CleanupSeconds     = cleanupSeconds;
            BlastRadius        = blastRadius;
            CoreHitRadius      = coreHitRadius;
            ArenaCenter        = arenaCenter;
        }
    }
}
