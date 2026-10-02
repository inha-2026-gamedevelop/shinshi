// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 복구 대상 목록 한 줄. quarantined는 자동 복구 실패 한도에 닿아 운영자 확인을 기다리는 세션,
    /// settlingStale은 종료 이벤트 저장 뒤 정산 대기 시간이 지난 Settling, instanceLost는 배정 boot의 생존 증거가 없는 Running이다.
    /// 페이지의 livenessDeferred가 true인 동안 instanceLost는 항상 false다 </summary>
    [DataContract]
    public sealed class AdminRecoverySession
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "session")]       private AdminSessionSummary      _session;
        [DataMember(Name = "instance")]      private AdminServerInstanceState _instance;
        [DataMember(Name = "quarantined")]   private bool                     _quarantined;
        [DataMember(Name = "settlingStale")] private bool                     _settlingStale;
        [DataMember(Name = "instanceLost")]  private bool                     _instanceLost;

        /****************************************
        *              Properties
        ****************************************/

        public AdminSessionSummary      Session       => _session;
        public AdminServerInstanceState Instance      => _instance;
        public bool                     Quarantined   => _quarantined;
        public bool                     SettlingStale => _settlingStale;
        public bool                     InstanceLost  => _instanceLost;

        /****************************************
        *                Methods
        ****************************************/

        public AdminRecoverySession(AdminSessionSummary session, AdminServerInstanceState instance, bool quarantined,
            bool settlingStale, bool instanceLost)
        {
            _session       = session;
            _instance      = instance;
            _quarantined   = quarantined;
            _settlingStale = settlingStale;
            _instanceLost  = instanceLost;
        }

        private AdminRecoverySession() { }
    }
}
