// System
using System;

namespace Shinshi.Backend
{
    /// <summary> 서버 프로세스별 서비스 자격과 허용 인스턴스를 외부 비밀 설정으로 고정한다 </summary>
    public sealed class ServiceCredentialOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SECTION_NAME = "ServerCredentials";
        private const int DEFAULT_LEASE_SECONDS = 900;

        /****************************************
        *              Properties
        ****************************************/

        public int LeaseLifetimeSeconds { get; set; } = DEFAULT_LEASE_SECONDS;
        public ServiceCredentialRegistration[] Registrations { get; set; } = Array.Empty<ServiceCredentialRegistration>();
    }

    /// <summary> CredentialHashBase64는 256비트 무작위 서비스 자격 원문의 SHA256이며 임대 bearer와 다르다 </summary>
    public sealed class ServiceCredentialRegistration
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid ServiceId { get; set; }
        public Guid InstanceId { get; set; }
        public int Role { get; set; }
        public string CredentialHashBase64 { get; set; } = string.Empty;
    }
}
