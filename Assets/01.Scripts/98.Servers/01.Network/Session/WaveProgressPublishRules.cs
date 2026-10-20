// System
using System;

// 프로젝트 내부
using Shinshi.Data;
using Shinshi.Systems;

namespace Shinshi.Network
{
    /// <summary> 서버 웨이브 진행 값을 클라이언트 진행 메시지로 바꾸는 규칙, 범위를 넘는 값은 전송 형식에 맞게 자른다 </summary>
    public static class WaveProgressPublishRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 웨이브마다 계획 적 수를 센다, 원본이 없으면 빈 배열 </summary>
        public static ushort[] BuildPlanned(WaveConfigSO waves)
        {
            if ((waves == null) || (waves.Waves == null))
            {
                return Array.Empty<ushort>();
            }

            int      count   = Math.Min(waves.Waves.Count, byte.MaxValue);
            ushort[] planned = new ushort[count];
            for (int i = 0; i < count; ++i)
            {
                planned[i] = ToUShort((waves.Waves[i] != null) ? waves.Waves[i].CountEnemies() : 0);
            }

            return planned;
        }

        public static WaveProgressMessage Build(in WaveProgressSnapshot progress, bool isRunning, float timeLimitSeconds,
            double serverTime, ushort[] planned)
        {
            int last  = Math.Max(0, ((planned != null) ? planned.Length : 0) - 1);
            int index = Math.Min(Math.Max(0, progress.WaveIndex), last);

            float elapsed = (progress.ElapsedSeconds > 0.0) ? (float)progress.ElapsedSeconds : 0f;
            return new WaveProgressMessage((byte)Math.Min(index, byte.MaxValue), ToUShort(progress.RemainingInWave), isRunning,
                elapsed, Math.Max(0f, timeLimitSeconds), serverTime, planned);
        }

        private static ushort ToUShort(int value)
        {
            return (ushort)Math.Min(Math.Max(0, value), ushort.MaxValue);
        }
    }
}
