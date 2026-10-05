// System
using System;

namespace Shinshi.Combat
{
    /// <summary> 참가자 한 명의 입력 헤더 검증, 프로토콜과 세션과 참가자, 주기, 순번, 시각 창 순서로 본다 </summary>
    public sealed class PlayerInputGate
    {
        // 참가자 서버 개체가 들고 있다. 연결이 바뀌어도 같은 참가자면 Restore로 마지막 순번을 이어 받는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly int    _protocolVersion;
        private readonly double _refillPerSecond;
        private readonly int    _burst;
        private readonly double _pastTolerance;
        private readonly double _futureTolerance;
        private readonly double _violationWindow;
        private readonly int    _violationThreshold;

        private int    _sessionEpoch     = 0;
        private int    _participantId    = 0;
        private uint   _lastSequence     = 0;
        private double _tokens           = 0.0;
        private double _lastRefillTime   = 0.0;
        private double _violationStart   = 0.0;
        private int    _violationCount   = 0;
        private double _lastRateViolationTime = double.NegativeInfinity;
        private bool   _shouldDisconnect = false;

        /****************************************
        *              Properties
        ****************************************/

        public uint   LastAcceptedSequence => _lastSequence;
        public int    ViolationCount       => _violationCount;
        public double ViolationWindowStart => _violationStart;

        // 참이면 처리기가 연결을 끊는다. 같은 연결 안에서는 되돌리지 않고, 새 연결이 캐릭터를 넘겨받을 때만 ClearDisconnect로 내린다
        public bool ShouldDisconnect => _shouldDisconnect;

        /****************************************
        *                Methods
        ****************************************/

        public PlayerInputGate(int protocolVersion, int ratePerSecond, int burst, double pastTolerance, double futureTolerance,
            double violationWindow, int violationThreshold, double serverTime)
        {
            if ((ratePerSecond <= 0) || (burst <= 0) || (violationThreshold <= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(ratePerSecond), "주기, 버스트, 위반 임계값은 1 이상이다");
            }

            _protocolVersion    = protocolVersion;
            _refillPerSecond    = ratePerSecond;
            _burst              = burst;
            _pastTolerance      = pastTolerance;
            _futureTolerance    = futureTolerance;
            _violationWindow    = violationWindow;
            _violationThreshold = violationThreshold;
            _tokens             = burst;
            _lastRefillTime     = serverTime;
            _violationStart     = serverTime;
        }

        /// <summary> 세션 바인딩이 출정 번호와 참가자 번호를 정한다 </summary>
        public void Bind(int sessionEpoch, int participantId)
        {
            _sessionEpoch  = sessionEpoch;
            _participantId = participantId;
        }

        /// <summary> 재접속한 참가자의 마지막 승인 순번을 이어 받는다 </summary>
        public void Restore(uint lastAcceptedSequence)
        {
            _lastSequence = lastAcceptedSequence;
        }

        /// <summary> 재접속한 참가자의 위반 누적을 이어 받는다, 끊고 다시 붙어 위반 기록을 지우지 못하게 한다 </summary>
        public void RestoreViolations(double windowStart, int count)
        {
            _violationStart = windowStart;
            _violationCount = Math.Max(0, count);
        }

        /// <summary> 새 연결이 캐릭터를 넘겨받을 때 부른다, 끊기 표시만 내리고 위반 누적과 순번은 그대로 둔다 </summary>
        public void ClearDisconnect()
        {
            _shouldDisconnect = false;
        }

        /// <summary> 헤더를 검증한다, 통과하면 순번을 승인하고 허용 창으로 자른 요청 시각을 돌려준다 </summary>
        public EInputVerdict Check(in PlayerInputHeader header, double serverTime, out double requestTime)
        {
            requestTime = serverTime;

            if (header.ProtocolVersion != _protocolVersion)
            {
                _shouldDisconnect = true;
                return EInputVerdict.ProtocolMismatch;
            }

            if (header.SessionEpoch != _sessionEpoch)
            {
                _shouldDisconnect = true;
                return EInputVerdict.SessionMismatch;
            }

            if (header.ParticipantId != _participantId)
            {
                _shouldDisconnect = true;
                return EInputVerdict.ParticipantMismatch;
            }

            // 주기 초과분은 순번을 보기 전에 버린다. 버린 입력의 순번은 승인하지 않으므로 다음 입력이 이어 받는다
            if (!TryTakeToken(serverTime))
            {
                // 끊겼던 연결이 다시 이어지면 쌓인 입력이 한 틱에 몰려 온다. 같은 서버 시각의 초과는 한 번만 세어 정상 사용자가
                // 지연 한 번으로 끊기지 않게 한다. 계속 몰아 보내는 클라이언트는 틱마다 한 번씩 쌓여 곧 임계값에 닿는다
                if (serverTime > _lastRateViolationTime)
                {
                    _lastRateViolationTime = serverTime;
                    AddViolation(serverTime);
                }

                return EInputVerdict.RateLimited;
            }

            // 신뢰 순서 채널이라 정상 클라이언트는 역행, 중복 순번을 보내지 않는다
            if (header.Sequence <= _lastSequence)
            {
                AddViolation(serverTime);
                return EInputVerdict.StaleSequence;
            }

            double estimated = header.EstimatedServerTime;
            if ((double.IsNaN(estimated)) || (double.IsInfinity(estimated)))
            {
                AddViolation(serverTime);
                return EInputVerdict.InvalidTime;
            }

            _lastSequence = header.Sequence;
            requestTime   = ClampToWindow(estimated, serverTime, _pastTolerance, _futureTolerance);
            return EInputVerdict.Accepted;
        }

        /// <summary> 클라이언트 추정 시각을 서버 시각 기준 과거, 미래 허용 창으로 자른다 </summary>
        public static double ClampToWindow(double estimatedServerTime, double serverTime, double pastTolerance, double futureTolerance)
        {
            double earliest = serverTime - pastTolerance;
            double latest   = serverTime + futureTolerance;

            if (estimatedServerTime < earliest)
            {
                return earliest;
            }

            if (estimatedServerTime > latest)
            {
                return latest;
            }

            return estimatedServerTime;
        }

        private bool TryTakeToken(double serverTime)
        {
            double elapsed = serverTime - _lastRefillTime;
            if (elapsed > 0.0)
            {
                _tokens         = Math.Min(_burst, _tokens + (elapsed * _refillPerSecond));
                _lastRefillTime = serverTime;
            }

            if (_tokens < 1.0)
            {
                return false;
            }

            _tokens -= 1.0;
            return true;
        }

        // 가끔 생기는 지연 뭉침이 긴 세션 동안 쌓여 끊기지 않도록 창 단위로 센다
        private void AddViolation(double serverTime)
        {
            if ((serverTime - _violationStart) >= _violationWindow)
            {
                _violationStart = serverTime;
                _violationCount = 0;
            }

            ++_violationCount;
            if (_violationCount >= _violationThreshold)
            {
                _shouldDisconnect = true;
            }
        }
    }
}
