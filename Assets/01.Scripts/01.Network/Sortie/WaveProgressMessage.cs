// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 처치, 웨이브 전환, 시작과 종료처럼 값이 바뀔 때와 (재)접속 때만 보낸다. 매 틱 흐르는 시간은 보내지 않고
    // 보낸 순간의 서버 시각을 함께 실어 클라이언트가 NetworkTime으로 이어 센다
    /// <summary> 일반전 웨이브 진행, 모든 참가자에게 같은 값을 보낸다 </summary>
    public readonly struct WaveProgressMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly byte     _waveIndex;
        private readonly ushort   _remainingInWave;
        private readonly bool     _isRunning;
        private readonly float    _elapsedSeconds;
        private readonly float    _timeLimitSeconds;
        private readonly double   _serverTime;
        private readonly ushort[] _planned;

        /****************************************
        *              Properties
        ****************************************/

        // 0부터 센다. 마지막 웨이브를 넘지 않는다
        public byte   WaveIndex        => _waveIndex;
        public int    WaveCount        => (_planned != null) ? _planned.Length : 0;

        // 현재 웨이브에서 아직 처치하지 않은 적 수, 나오지 않은 예정 적도 들어간다
        public ushort RemainingInWave  => _remainingInWave;

        // 시간이 흐르는 중인지. 시작 전이거나 끝났으면 거짓이고 ElapsedSeconds가 그대로 멈춘다
        public bool   IsRunning        => _isRunning;
        public float  ElapsedSeconds   => _elapsedSeconds;
        public float  TimeLimitSeconds => _timeLimitSeconds;

        // ElapsedSeconds를 잰 서버 시각(NetworkTime 기준 초)
        public double ServerTime       => _serverTime;

        /****************************************
        *                Methods
        ****************************************/

        public WaveProgressMessage(byte waveIndex, ushort remainingInWave, bool isRunning, float elapsedSeconds,
            float timeLimitSeconds, double serverTime, ushort[] planned)
        {
            _waveIndex        = waveIndex;
            _remainingInWave  = remainingInWave;
            _isRunning        = isRunning;
            _elapsedSeconds   = elapsedSeconds;
            _timeLimitSeconds = timeLimitSeconds;
            _serverTime       = serverTime;
            _planned          = planned;
        }

        /// <summary> 웨이브 하나의 계획 적 수, 범위 밖이면 0 </summary>
        public int GetPlanned(int waveIndex)
        {
            return ((_planned != null) && (waveIndex >= 0) && (waveIndex < _planned.Length)) ? _planned[waveIndex] : 0;
        }

        // 시간은 클라이언트가 이어 세므로 비교하지 않는다. 멈추거나 다시 흐를 때는 IsRunning이 바뀌어 보내진다
        /// <summary> 진행 값이 같은지, 바뀐 것만 보내기 위해 쓴다 </summary>
        public bool SameAs(in WaveProgressMessage other)
        {
            if ((_waveIndex != other._waveIndex) || (_remainingInWave != other._remainingInWave)
                || (_isRunning != other._isRunning) || (WaveCount != other.WaveCount)
                || (!_timeLimitSeconds.Equals(other._timeLimitSeconds)))
            {
                return false;
            }

            for (int i = 0; i < WaveCount; ++i)
            {
                if (_planned[i] != other._planned[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
