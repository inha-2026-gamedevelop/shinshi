// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 참가자 한 명의 입력 헤더 검증, 프로토콜과 세션과 참가자, 주기, 순번, 시각 창 순서로 본다 </summary>
    public sealed class PlayerInputGate
    {
        /// <summary> 재접속 때 남은 토큰만 이어 받는다. 기본값은 이전 형식 스냅샷처럼 토큰 정보가 없는 상태다 </summary>
        public readonly struct TokenBucketSnapshot
        {
            /****************************************
            *                Fields
            ****************************************/

            private readonly bool   _hasValue;
            private readonly double _tokens;
            private readonly double _lastRefillTime;
            private readonly double _lastServerTime;

            /****************************************
            *              Properties
            ****************************************/

            public bool   HasValue       => _hasValue;
            public double Tokens         => _tokens;
            public double LastRefillTime => _lastRefillTime;
            public double LastServerTime => _lastServerTime;

            /****************************************
            *                Methods
            ****************************************/

            internal TokenBucketSnapshot(double tokens, double lastRefillTime, double lastServerTime)
            {
                _hasValue       = true;
                _tokens         = tokens;
                _lastRefillTime = lastRefillTime;
                _lastServerTime = lastServerTime;
            }
        }

        /// <summary> 재접속에 보존할 주기 초과 구간. 값으로 복사해 이전 게이트의 버퍼와 공유하지 않는다 </summary>
        public readonly struct RateViolationSnapshot
        {
            /****************************************
            *                Fields
            ****************************************/

            private readonly int    _count;
            private readonly double _firstTime;
            private readonly double _secondTime;
            private readonly double _thirdTime;
            private readonly double _fourthTime;
            private readonly double _fifthTime;

            /****************************************
            *              Properties
            ****************************************/

            public int Count => _count;

            /****************************************
            *                Methods
            ****************************************/

            internal RateViolationSnapshot(int count, double firstTime, double secondTime, double thirdTime,
                double fourthTime, double fifthTime)
            {
                _count      = count;
                _firstTime  = firstTime;
                _secondTime = secondTime;
                _thirdTime  = thirdTime;
                _fourthTime = fourthTime;
                _fifthTime  = fifthTime;
            }

            public double GetTime(int index)
            {
                if ((index < 0) || (index >= _count))
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                switch (index)
                {
                    case 0: return _firstTime;
                    case 1: return _secondTime;
                    case 2: return _thirdTime;
                    case 3: return _fourthTime;
                    default: return _fifthTime;
                }
            }
        }

        // 참가자 서버 개체가 들고 있다. 연결이 바뀌어도 같은 참가자면 Restore로 마지막 순번을 이어 받는다

        /****************************************
        *                Fields
        ****************************************/

        private const int RATE_SNAPSHOT_CAPACITY = 5;

        private readonly int      _protocolVersion;
        private readonly double   _refillPerSecond;
        private readonly int      _burst;
        private readonly double   _pastTolerance;
        private readonly double   _futureTolerance;
        private readonly double   _violationWindow;
        private readonly int      _violationThreshold;
        private readonly double[] _rateViolationTimes = new double[RATE_SNAPSHOT_CAPACITY];

        private int    _sessionEpoch       = 0;
        private int    _participantId      = 0;
        private uint   _lastSequence       = 0;
        private double _tokens             = 0.0;
        private double _lastRefillTime     = 0.0;
        private double _lastServerTime     = 0.0;
        private double _violationStart     = 0.0;
        private int    _violationCount     = 0;
        private int    _rateViolationCount = 0;
        private bool   _shouldDisconnect   = false;

        /****************************************
        *              Properties
        ****************************************/

        public uint   LastAcceptedSequence => _lastSequence;
        public int    ViolationCount       => _violationCount;
        public double ViolationWindowStart => _violationStart;
        public int    RateViolationCount   => _rateViolationCount;

        // 참이면 처리기가 연결을 끊는다. 같은 연결 안에서는 되돌리지 않고, 새 연결이 캐릭터를 넘겨받을 때만 ClearDisconnect로 내린다
        public bool ShouldDisconnect => _shouldDisconnect;

        /****************************************
        *                Methods
        ****************************************/

        public PlayerInputGate(int protocolVersion, int ratePerSecond, int burst, double pastTolerance, double futureTolerance,
            double violationWindow, int violationThreshold, double serverTime)
        {
            if (protocolVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(protocolVersion));
            }

            if (ratePerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ratePerSecond));
            }

            if (burst <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(burst));
            }

            if (violationThreshold <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(violationThreshold));
            }

            if ((!IsFinite(pastTolerance)) || (pastTolerance < 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(pastTolerance));
            }

            if ((!IsFinite(futureTolerance)) || (futureTolerance < 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(futureTolerance));
            }

            if ((!IsFinite(violationWindow)) || (violationWindow <= 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(violationWindow));
            }

            if ((!IsFinite(serverTime)) || (serverTime < 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(serverTime));
            }
            if (Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD != _rateViolationTimes.Length)
            {
                throw new InvalidOperationException("주기 초과 스냅샷의 다섯 칸과 임계값이 일치해야 한다");
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
            _lastServerTime     = serverTime;
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
            _violationStart = IsFinite(windowStart) ? Math.Min(_lastServerTime, Math.Max(0.0, windowStart)) : _lastServerTime;
            _violationCount = Math.Max(0, count);
        }

        public TokenBucketSnapshot CaptureTokenBucket()
        {
            return new TokenBucketSnapshot(_tokens, _lastRefillTime, _lastServerTime);
        }

        /// <summary> 연결을 다시 만들었다고 버스트를 주지 않는다. 끊긴 뒤 실제로 지난 시간만 충전한다 </summary>
        public void RestoreTokenBucket(in TokenBucketSnapshot snapshot, double serverTime)
        {
            if (!snapshot.HasValue)
            {
                NormalizeServerTime(serverTime);
                return;
            }

            if ((IsFinite(snapshot.LastServerTime)) && (snapshot.LastServerTime > _lastServerTime))
            {
                _lastServerTime = snapshot.LastServerTime;
            }

            serverTime = NormalizeServerTime(serverTime);
            _tokens = IsFinite(snapshot.Tokens) ? Math.Max(0.0, Math.Min(_burst, snapshot.Tokens)) : 0.0;
            _lastRefillTime = IsFinite(snapshot.LastRefillTime)
                ? Math.Min(serverTime, Math.Max(0.0, snapshot.LastRefillTime)) : serverTime;
            RefillTokens(serverTime);
        }

        public RateViolationSnapshot CaptureRateViolations()
        {
            return new RateViolationSnapshot(_rateViolationCount, _rateViolationTimes[0], _rateViolationTimes[1],
                _rateViolationTimes[2], _rateViolationTimes[3], _rateViolationTimes[4]);
        }

        /// <summary> 새 연결을 만든 시각에 이미 만료된 구간은 버린다. 같은 1초 구간의 재초과도 중복 집계하지 않는다 </summary>
        public void RestoreRateViolations(in RateViolationSnapshot snapshot, double serverTime)
        {
            serverTime = NormalizeServerTime(serverTime);
            _rateViolationCount = 0;
            int count = Math.Min(snapshot.Count, _rateViolationTimes.Length);
            for (int i = 0; i < count; ++i)
            {
                double time = snapshot.GetTime(i);
                if ((!IsFinite(time)) || (time < 0.0))
                {
                    continue;
                }

                time = Math.Min(time, serverTime);
                if ((serverTime - time) >= Constants.Player.INPUT_RATE_VIOLATION_WINDOW_SECONDS)
                {
                    continue;
                }

                int last = _rateViolationCount - 1;
                if ((_rateViolationCount > 0) && (time < _rateViolationTimes[last]))
                {
                    continue;
                }

                if ((_rateViolationCount > 0) && (Math.Floor(time) == Math.Floor(_rateViolationTimes[last])))
                {
                    _rateViolationTimes[last] = time;
                    continue;
                }

                _rateViolationTimes[_rateViolationCount] = time;
                ++_rateViolationCount;
            }
        }

        /// <summary> 새 연결이 캐릭터를 넘겨받을 때 부른다, 끊기 표시만 내리고 위반 누적과 순번은 그대로 둔다 </summary>
        public void ClearDisconnect()
        {
            _shouldDisconnect = false;
        }

        /// <summary> 헤더를 검증한다, 통과하면 순번을 승인하고 허용 창으로 자른 요청 시각을 돌려준다 </summary>
        public EInputVerdict Check(in PlayerInputHeader header, double serverTime, out double requestTime)
        {
            serverTime = NormalizeServerTime(serverTime);
            requestTime = serverTime;
            PruneRateViolations(serverTime);

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
                AddRateViolation(serverTime);
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
            RefillTokens(serverTime);

            // 정확한 30Hz 간격도 이진 소수 오차로 1보다 조금 작아진다. 오차 범위만 허용하고 음수 잔량은 남기지 않는다
            if (_tokens < (1.0 - Constants.Player.INPUT_RATE_TOKEN_EPSILON))
            {
                return false;
            }

            _tokens = Math.Max(0.0, _tokens - 1.0);
            return true;
        }

        private void RefillTokens(double serverTime)
        {
            double elapsed = serverTime - _lastRefillTime;
            if (elapsed > 0.0)
            {
                _tokens         = Math.Min(_burst, _tokens + (elapsed * _refillPerSecond));
                _lastRefillTime = serverTime;
            }

        }

        // 서버 시계가 비정상이거나 역행해도 구간 정렬과 만료 창을 되돌리지 않는다. 헤더 불일치로 일찍 반환해도 갱신한다
        private double NormalizeServerTime(double serverTime)
        {
            if ((!IsFinite(serverTime)) || (serverTime < _lastServerTime))
            {
                return _lastServerTime;
            }

            _lastServerTime = serverTime;
            return serverTime;
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }

        // 구간은 절대 서버 시각을 내림한 1초 단위다. 같은 구간의 마지막 초과 시각을 보존해 최근 10초에 남은 구간만 센다
        private void AddRateViolation(double serverTime)
        {
            PruneRateViolations(serverTime);
            int last = _rateViolationCount - 1;
            if ((_rateViolationCount > 0) && (Math.Floor(_rateViolationTimes[last]) == Math.Floor(serverTime)))
            {
                _rateViolationTimes[last] = Math.Max(_rateViolationTimes[last], serverTime);
            }
            else
            {
                // 끊기 표시를 내린 새 연결도 최대 다섯 구간만 가진다. 임계값 뒤 입력이 들어와도 버퍼를 늘리지 않는다
                if (_rateViolationCount == _rateViolationTimes.Length)
                {
                    RemoveRateViolations(1);
                }

                _rateViolationTimes[_rateViolationCount] = serverTime;
                ++_rateViolationCount;
            }

            if (_rateViolationCount >= Constants.Player.INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD)
            {
                _shouldDisconnect = true;
            }
        }

        // 포함 범위는 (현재 시각 - 10초, 현재 시각]이다. 정확히 10초 지난 기록은 만료한다
        private void PruneRateViolations(double serverTime)
        {
            int expired = 0;
            while ((expired < _rateViolationCount)
                && ((serverTime - _rateViolationTimes[expired]) >= Constants.Player.INPUT_RATE_VIOLATION_WINDOW_SECONDS))
            {
                ++expired;
            }

            if (expired > 0)
            {
                RemoveRateViolations(expired);
            }
        }

        private void RemoveRateViolations(int count)
        {
            _rateViolationCount -= count;
            for (int i = 0; i < _rateViolationCount; ++i)
            {
                _rateViolationTimes[i] = _rateViolationTimes[i + count];
            }
        }

        // 순번 역행과 시각 이상은 기존 창 단위 집계를 유지한다
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
