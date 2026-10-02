// System
using System;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 출정 참가 집합과 재접속 자격을 관리한다, 생성과 모든 호출은 서버 메인 스레드에서 한다 </summary>
    public sealed class ParticipantRoster : IParticipantRoster
    {
        /****************************************
        *                Fields
        ****************************************/

        private struct Entry
        {
            public int                         ParticipantId;
            public string                      ParticipantKey;
            public EParticipantConnectionState ConnectionState;
            public EParticipantLifeState       LifeState;
            public double                      GraceUntil;
            public ESessionEndReason           CloseReason;
        }

        // 출정 인원 상한이 작고 닫힌 기록도 남겨야 하므로 고정 배열을 훑는다
        private readonly Entry[] _entries;
        private readonly double  _reconnectGraceSeconds;

        private int    _count          = 0;
        private int    _activeCount    = 0;
        private int    _startingCount  = 0;
        private bool   _isSealed       = false;
        private double _lastServerTime = 0.0;

        /****************************************
        *              Properties
        ****************************************/

        public int    Count          => _count;
        public int    ActiveCount    => _activeCount;
        public int    StartingCount  => _startingCount;
        public bool   IsSealed       => _isSealed;
        public double LastServerTime => _lastServerTime;

        /// <summary> 닫힘을 먼저 반영한 뒤 한 번 통지한다, 수신자는 저장 큐에 넣고 틱에서 HTTP 응답을 기다리지 않는다 </summary>
        public event Action<int, ESessionEndReason, double> ParticipantClosed;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> GameDB 초기화 뒤 Session 설정을 전달한다, 재접속 유예는 생성 시 한 번 캐싱한다 </summary>
        public ParticipantRoster(SessionConfigSO sessionConfig, int capacity = Constants.Session.MAX_PARTY_SIZE)
        {
            if (sessionConfig == null)
            {
                throw new ArgumentNullException(nameof(sessionConfig));
            }

            if ((capacity < Constants.Session.MIN_PARTY_SIZE) || (capacity > Constants.Session.MAX_PARTY_SIZE))
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "출정 참가자 상한이 허용 범위를 벗어났다");
            }

            double reconnectGraceSeconds = sessionConfig.ReconnectGraceSeconds;
            if ((!IsFinite(reconnectGraceSeconds)) || (reconnectGraceSeconds <= 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(sessionConfig), "재접속 유예는 유한한 양수여야 한다");
            }

            _entries               = new Entry[capacity];
            _reconnectGraceSeconds = reconnectGraceSeconds;
        }

        /// <summary> 시작 전에 인증된 참가자를 Connected로 등록한다, 번호와 영구 키는 출정 안에서 각각 유일해야 한다 </summary>
        public bool TryAdd(int participantId, string participantKey, EParticipantLifeState lifeState = EParticipantLifeState.Alive)
        {
            if ((_isSealed) || (_count >= _entries.Length) || (participantId <= 0)
                || (string.IsNullOrWhiteSpace(participantKey)) || (!IsValidLifeState(lifeState)))
            {
                return false;
            }

            for (int i = 0; i < _count; ++i)
            {
                ref Entry existing = ref _entries[i];
                if ((existing.ParticipantId == participantId)
                    || (string.Equals(existing.ParticipantKey, participantKey, StringComparison.Ordinal)))
                {
                    return false;
                }
            }

            ref Entry entry = ref _entries[_count];
            entry.ParticipantId   = participantId;
            entry.ParticipantKey  = participantKey;
            entry.ConnectionState = EParticipantConnectionState.Connected;
            entry.LifeState       = lifeState;
            ++_count;
            ++_activeCount;
            return true;
        }

        /// <summary> Running 진입 시 한 번 호출한다, 이후 시작 인원과 참가 집합은 이탈해도 바뀌지 않는다 </summary>
        public void Seal()
        {
            if (_isSealed)
            {
                return;
            }

            _startingCount = _count;
            _isSealed      = true;
        }

        public bool TryGet(int participantId, out ParticipantSnapshot snapshot)
        {
            int index = IndexOf(participantId);
            if (index < 0)
            {
                snapshot = default;
                return false;
            }

            snapshot = Capture(in _entries[index]);
            return true;
        }

        /// <summary> 등록 순서로 읽는다, 닫힌 참가자도 같은 인덱스에 남는다 </summary>
        public ParticipantSnapshot GetAt(int index)
        {
            if ((index < 0) || (index >= _count))
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Capture(in _entries[index]);
        }

        /// <summary> 인증된 같은 참가자를 재연결한다, 만료 시각과 같거나 늦으면 닫고 복귀를 거절한다 </summary>
        public bool TryConnect(int participantId, double serverTime)
        {
            if (!TryObserveServerTime(serverTime))
            {
                return false;
            }

            int index = IndexOf(participantId);
            if ((index < 0) || (_entries[index].ConnectionState == EParticipantConnectionState.Departed))
            {
                return false;
            }

            ref Entry entry = ref _entries[index];
            if ((entry.ConnectionState == EParticipantConnectionState.ReconnectGrace) && (serverTime >= entry.GraceUntil))
            {
                CloseEntry(ref entry, ESessionEndReason.DisconnectExpired, serverTime);
                return false;
            }

            entry.ConnectionState = EParticipantConnectionState.Connected;
            entry.GraceUntil      = 0.0;
            return true;
        }

        /// <summary> 서버가 감지한 연결 종료부터 유예를 시작한다, 같은 종료 통지는 만료시각을 늘리지 않는다 </summary>
        public bool TryDisconnect(int participantId, double serverTime)
        {
            if (!TryObserveServerTime(serverTime))
            {
                return false;
            }

            int index = IndexOf(participantId);
            if ((index < 0) || (_entries[index].ConnectionState == EParticipantConnectionState.Departed))
            {
                return false;
            }

            ref Entry entry = ref _entries[index];
            if (entry.ConnectionState == EParticipantConnectionState.ReconnectGrace)
            {
                if (serverTime >= entry.GraceUntil)
                {
                    CloseEntry(ref entry, ESessionEndReason.DisconnectExpired, serverTime);
                    return false;
                }

                return true;
            }

            double graceUntil = serverTime + _reconnectGraceSeconds;
            if ((!IsFinite(graceUntil)) || (graceUntil <= serverTime))
            {
                return false;
            }

            entry.ConnectionState = EParticipantConnectionState.ReconnectGrace;
            entry.GraceUntil      = graceUntil;
            return true;
        }

        /// <summary> 생사 판정자가 확정한 상태만 기록한다, 연결 유예 중 피격과 사망도 이 축에 반영한다 </summary>
        public bool TrySetLifeState(int participantId, EParticipantLifeState state)
        {
            if (!IsValidLifeState(state))
            {
                return false;
            }

            int index = IndexOf(participantId);
            if ((index < 0) || (_entries[index].ConnectionState == EParticipantConnectionState.Departed))
            {
                return false;
            }

            _entries[index].LifeState = state;
            return true;
        }

        /// <summary> 생사와 무관하게 접속 중 또는 유예 안인 참가자만 보상을 받는다, 만료 판정에는 서버 시각을 쓴다 </summary>
        public bool IsRewardEligible(int participantId, double serverTime)
        {
            if (!TryObserveServerTime(serverTime))
            {
                return false;
            }

            int index = IndexOf(participantId);
            if (index < 0)
            {
                return false;
            }

            ref Entry entry = ref _entries[index];
            return (entry.ConnectionState == EParticipantConnectionState.Connected)
                || ((entry.ConnectionState == EParticipantConnectionState.ReconnectGrace) && (serverTime < entry.GraceUntil));
        }

        /// <summary> 서버 틱에서 유예 만료를 닫는다, 시각은 모든 시각 인자 호출을 통틀어 유한하고 역행하지 않아야 한다 </summary>
        public void Tick(double serverTime)
        {
            if (!TryObserveServerTime(serverTime))
            {
                return;
            }

            for (int i = 0; i < _count; ++i)
            {
                ref Entry entry = ref _entries[i];
                if ((entry.ConnectionState != EParticipantConnectionState.ReconnectGrace) || (serverTime < entry.GraceUntil))
                {
                    continue;
                }

                CloseEntry(ref entry, ESessionEndReason.DisconnectExpired, serverTime);
            }
        }

        /// <summary> 개인 포기나 세션 종료 원인으로 닫는다, 이미 닫힌 참가자의 원인과 통지는 덮어쓰지 않는다 </summary>
        public bool TryClose(int participantId, ESessionEndReason reason, double serverTime)
        {
            if ((!IsValidCloseReason(reason)) || (!TryObserveServerTime(serverTime)))
            {
                return false;
            }

            int index = IndexOf(participantId);
            if ((index < 0) || (_entries[index].ConnectionState == EParticipantConnectionState.Departed))
            {
                return false;
            }

            CloseEntry(ref _entries[index], reason, serverTime);
            return true;
        }

        /// <summary> 이탈하지 않은 참가자 중 살아 있는 수, 유예 중인 캐릭터도 서버에 남아 있으므로 센다 </summary>
        public int CountLiving()
        {
            int living = 0;
            for (int i = 0; i < _count; ++i)
            {
                ref Entry entry = ref _entries[i];
                if ((entry.ConnectionState != EParticipantConnectionState.Departed)
                    && (entry.LifeState == EParticipantLifeState.Alive))
                {
                    ++living;
                }
            }

            return living;
        }

        /// <summary> 이탈(Departed)한 참가자가 있는지, 시작 전 이탈은 start를 막는다 </summary>
        public bool HasDeparted => _activeCount != _count;

        // 미등록 참가자도 닫힘으로 취급해 전투 쪽이 새 참가자를 우회 등록하지 못하게 한다
        public bool IsClosed(int participantId)
        {
            int index = IndexOf(participantId);
            return (index < 0) || (_entries[index].ConnectionState == EParticipantConnectionState.Departed);
        }

        /// <summary> 전투 상태 보존 만료나 퇴거 통지에 쓴다, 마지막 관측 서버 시각으로 DisconnectExpired를 기록한다 </summary>
        public void Close(int participantId)
        {
            TryClose(participantId, ESessionEndReason.DisconnectExpired, _lastServerTime);
        }

        private void CloseEntry(ref Entry entry, ESessionEndReason reason, double serverTime)
        {
            entry.ConnectionState = EParticipantConnectionState.Departed;
            entry.CloseReason     = reason;
            --_activeCount;
            ParticipantClosed?.Invoke(entry.ParticipantId, reason, serverTime);
        }

        private int IndexOf(int participantId)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (_entries[i].ParticipantId == participantId)
                {
                    return i;
                }
            }

            return -1;
        }

        // 보상 조회와 거절된 호출도 서버 시각을 관측하므로 이후 호출이 과거 자격을 되살릴 수 없다
        private bool TryObserveServerTime(double serverTime)
        {
            if ((!IsFinite(serverTime)) || (serverTime < _lastServerTime))
            {
                return false;
            }

            _lastServerTime = serverTime;
            return true;
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }

        private static bool IsValidLifeState(EParticipantLifeState state)
        {
            switch (state)
            {
                case EParticipantLifeState.Alive:
                case EParticipantLifeState.Dead:
                case EParticipantLifeState.ReviveWaiting:
                case EParticipantLifeState.Spectating:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsValidCloseReason(ESessionEndReason reason)
        {
            switch (reason)
            {
                case ESessionEndReason.Success:
                case ESessionEndReason.Timeout:
                case ESessionEndReason.AllDead:
                case ESessionEndReason.Abandon:
                case ESessionEndReason.DisconnectExpired:
                case ESessionEndReason.ServiceCancelled:
                    return true;
                default:
                    return false;
            }
        }

        private static ParticipantSnapshot Capture(in Entry entry)
        {
            return new ParticipantSnapshot(entry.ParticipantId, entry.ParticipantKey, entry.ConnectionState,
                entry.LifeState, entry.GraceUntil, entry.CloseReason);
        }
    }
}
