namespace Shinshi.Systems
{
    /// <summary> 일반전 진행의 한 시점 값, 웨이브 번호는 0부터 세고 남은 적에는 아직 나오지 않은 예정 적도 들어간다 </summary>
    public readonly struct WaveProgressSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public int    WaveIndex       { get; }
        public int    WaveCount       { get; }
        public int    RemainingInWave { get; }
        public double ElapsedSeconds  { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WaveProgressSnapshot(int waveIndex, int waveCount, int remainingInWave, double elapsedSeconds)
        {
            WaveIndex       = waveIndex;
            WaveCount       = waveCount;
            RemainingInWave = remainingInWave;
            ElapsedSeconds  = elapsedSeconds;
        }
    }
}
