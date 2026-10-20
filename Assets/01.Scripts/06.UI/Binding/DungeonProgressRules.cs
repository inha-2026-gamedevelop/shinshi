// System
using System;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    // 서버가 보낸 웨이브 진행을 진행 패널 칸 상태로 바꾼다. 한 칸은 한 웨이브(구역)다.
    // 현재 웨이브를 모두 처치해 다음 구역 도착을 기다리는 동안은 그 칸을 완료로, 다음 칸을 현재(처치 0)로 본다
    /// <summary> 던전 진행 패널 표시 규칙 </summary>
    public static class DungeonProgressRules
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int ALL_CLEARED = -1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 현재 칸 번호(0부터), 모든 웨이브를 처치했으면 ALL_CLEARED </summary>
        public static int CurrentNode(in WaveProgressMessage progress)
        {
            int count = progress.WaveCount;
            if (count == 0)
            {
                return ALL_CLEARED;
            }

            int index = progress.WaveIndex;
            if (progress.RemainingInWave == 0)
            {
                ++index;
            }

            return (index >= count) ? ALL_CLEARED : index;
        }

        public static EWaveNodeState NodeState(in WaveProgressMessage progress, int node)
        {
            int current = CurrentNode(in progress);
            if ((current == ALL_CLEARED) || (node < current))
            {
                return EWaveNodeState.Cleared;
            }

            return (node == current) ? EWaveNodeState.Current : EWaveNodeState.Pending;
        }

        /// <summary> 칸의 처치 수, 완료 칸은 계획 수와 같고 대기 칸은 0 </summary>
        public static int NodeKilled(in WaveProgressMessage progress, int node)
        {
            int planned = progress.GetPlanned(node);
            switch (NodeState(in progress, node))
            {
                case EWaveNodeState.Cleared:
                    return planned;
                case EWaveNodeState.Current:
                    return (node == progress.WaveIndex) ? Math.Max(0, planned - progress.RemainingInWave) : 0;
                default:
                    return 0;
            }
        }

        /// <summary> "현재 / 전체"에 쓸 1부터 센 현재 웨이브, 모두 처치했으면 전체 수 </summary>
        public static int CurrentNumber(in WaveProgressMessage progress)
        {
            int current = CurrentNode(in progress);
            return (current == ALL_CLEARED) ? progress.WaveCount : (current + 1);
        }

        /// <summary> 전체 계획 적 중 처치한 비율, 계획이 없으면 0 </summary>
        public static float OverallRatio(in WaveProgressMessage progress)
        {
            int total  = 0;
            int killed = 0;
            for (int i = 0; i < progress.WaveCount; ++i)
            {
                total  += progress.GetPlanned(i);
                killed += NodeKilled(in progress, i);
            }

            return (total > 0) ? Math.Min(1f, (float)killed / total) : 0f;
        }

        // 시간이 흐르는 동안은 메시지를 잰 서버 시각 뒤로 지난 만큼 더한다. 멈췄으면 받은 값 그대로다
        /// <summary> 서버 시각(NetworkTime) 기준 남은 제한 시간(초) </summary>
        public static float RemainingSeconds(in WaveProgressMessage progress, double serverNow)
        {
            double elapsed = progress.ElapsedSeconds;
            if (progress.IsRunning)
            {
                elapsed += Math.Max(0.0, serverNow - progress.ServerTime);
            }

            return (float)Math.Max(0.0, progress.TimeLimitSeconds - elapsed);
        }
    }
}
