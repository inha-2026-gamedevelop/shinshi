// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> game_sessions 한 행. 상태와 결과는 DB 문자열(Running, Settling, Closed, ServiceCancelled 등), endReason은
    /// GameSessionCodes.EndReason이다. 값이 없는 시각과 ID는 null이다 </summary>
    [DataContract]
    public sealed class AdminSessionSummary
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "sessionId")]             private string _sessionId;
        [DataMember(Name = "state")]                 private string _state;
        [DataMember(Name = "result")]                private string _result;
        [DataMember(Name = "mode")]                  private string _mode;
        [DataMember(Name = "mapId")]                 private string _mapId;
        [DataMember(Name = "startingCount")]         private int    _startingCount;
        [DataMember(Name = "contentVersion")]        private string _contentVersion;
        [DataMember(Name = "instanceId")]            private string _instanceId;
        [DataMember(Name = "instanceBootId")]        private string _instanceBootId;
        [DataMember(Name = "endReason")]             private int?   _endReason;
        [DataMember(Name = "lastEventSeq")]          private long   _lastEventSeq;
        [DataMember(Name = "endedEventSeq")]         private long?  _endedEventSeq;
        [DataMember(Name = "settlementOperationId")] private string _settlementOperationId;

        // 자동 복구가 예외로 실패한 횟수와 다음 시도 시각. 한도에 닿은 세션은 자동 복구가 다시 고르지 않는다
        [DataMember(Name = "recoveryFailures")]      private int    _recoveryFailures;
        [DataMember(Name = "nextRecoveryAtUnixMs")]  private long?  _nextRecoveryAtUnixMs;

        [DataMember(Name = "createdAtUnixMs")]       private long   _createdAtUnixMs;
        [DataMember(Name = "startedAtUnixMs")]       private long?  _startedAtUnixMs;
        [DataMember(Name = "settlingAtUnixMs")]      private long?  _settlingAtUnixMs;
        [DataMember(Name = "closedAtUnixMs")]        private long?  _closedAtUnixMs;
        [DataMember(Name = "revision")]              private long   _revision;

        /****************************************
        *              Properties
        ****************************************/

        public string SessionId             => _sessionId;
        public string State                 => _state;
        public string Result                => _result;
        public string Mode                  => _mode;
        public string MapId                 => _mapId;
        public int    StartingCount         => _startingCount;
        public string ContentVersion        => _contentVersion;
        public string InstanceId            => _instanceId;
        public string InstanceBootId        => _instanceBootId;
        public int?   EndReason             => _endReason;
        public long   LastEventSeq          => _lastEventSeq;
        public long?  EndedEventSeq         => _endedEventSeq;
        public string SettlementOperationId => _settlementOperationId;
        public int    RecoveryFailures      => _recoveryFailures;
        public long?  NextRecoveryAtUnixMs  => _nextRecoveryAtUnixMs;
        public long   CreatedAtUnixMs       => _createdAtUnixMs;
        public long?  StartedAtUnixMs       => _startedAtUnixMs;
        public long?  SettlingAtUnixMs      => _settlingAtUnixMs;
        public long?  ClosedAtUnixMs        => _closedAtUnixMs;
        public long   Revision              => _revision;

        /****************************************
        *                Methods
        ****************************************/

        public AdminSessionSummary(string sessionId, string state, string result, string mode, string mapId,
            int startingCount, string contentVersion, string instanceId, string instanceBootId, int? endReason,
            long lastEventSeq, long? endedEventSeq, string settlementOperationId, int recoveryFailures,
            long? nextRecoveryAtUnixMs, long createdAtUnixMs, long? startedAtUnixMs, long? settlingAtUnixMs,
            long? closedAtUnixMs, long revision)
        {
            _sessionId             = sessionId;
            _state                 = state;
            _result                = result;
            _mode                  = mode;
            _mapId                 = mapId;
            _startingCount         = startingCount;
            _contentVersion        = contentVersion;
            _instanceId            = instanceId;
            _instanceBootId        = instanceBootId;
            _endReason             = endReason;
            _lastEventSeq          = lastEventSeq;
            _endedEventSeq         = endedEventSeq;
            _settlementOperationId = settlementOperationId;
            _recoveryFailures      = recoveryFailures;
            _nextRecoveryAtUnixMs  = nextRecoveryAtUnixMs;
            _createdAtUnixMs       = createdAtUnixMs;
            _startedAtUnixMs       = startedAtUnixMs;
            _settlingAtUnixMs      = settlingAtUnixMs;
            _closedAtUnixMs        = closedAtUnixMs;
            _revision              = revision;
        }

        private AdminSessionSummary() { }
    }
}
