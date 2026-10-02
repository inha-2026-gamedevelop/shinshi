// System
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.Extensions.Options;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;

namespace Shinshi.Backend
{
    /// <summary> 프로세스 서비스 자격을 검증해 설정에 고정된 역할과 인스턴스의 임대만 발급한다 </summary>
    public sealed class ServerCredentialIssuer
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int HASH_BYTES = 32;
        private const int MAXIMUM_LIFETIME_SECONDS = 86400;
        private readonly PostgresAuthRepository _repository;
        private readonly IOptions<ServiceCredentialOptions> _options;

        /****************************************
        *                Methods
        ****************************************/

        public ServerCredentialIssuer(PostgresAuthRepository repository, IOptions<ServiceCredentialOptions> options)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(options);
            _repository = repository;
            _options    = options;
        }

        /// <summary> 서비스 자격이 맞지 않으면 null이다. 역할은 설정 값과 같아야 하고, 첫 등록 전 인스턴스면 행을 만든다 </summary>
        public async Task<AuthValidationOutcome<IssuedAuthToken>?> IssueAsync(Guid instanceId, int role, Guid bootId,
            string credential, CancellationToken cancellationToken = default)
        {
            ServiceCredentialOptions options = _options.Value;
            if ((options.LeaseLifetimeSeconds <= 0) || (options.LeaseLifetimeSeconds > MAXIMUM_LIFETIME_SECONDS))
            {
                throw new InvalidOperationException("Server credential lease lifetime is invalid.");
            }
            ServiceCredentialRegistration? registration = Match(options, instanceId, credential);
            if (registration == null)
            {
                return null;
            }
            if ((registration.Role != role) || (bootId == Guid.Empty))
            {
                return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            return await _repository.IssueServerLeaseAsync(registration.ServiceId, registration.InstanceId,
                registration.Role, bootId, TimeSpan.FromSeconds(options.LeaseLifetimeSeconds), cancellationToken);
        }

        /// <summary> 입력 ID는 허용 목록 선택에만 사용하며 반환 신원과 역할은 설정에서 결정한다 </summary>
        public static ServiceCredentialRegistration? Match(ServiceCredentialOptions options,
            Guid instanceId, string credential)
        {
            ArgumentNullException.ThrowIfNull(options);
            if ((instanceId == Guid.Empty) || !AuthTokenHasher.IsWellFormed(credential))
            {
                return null;
            }

            ServiceCredentialRegistration? matched = null;
            bool seen = false;
            byte[] candidate = SHA256.HashData(Encoding.UTF8.GetBytes(credential));
            foreach (ServiceCredentialRegistration registration in options.Registrations ?? Array.Empty<ServiceCredentialRegistration>())
            {
                if (registration.InstanceId != instanceId)
                {
                    continue;
                }
                if (seen)
                {
                    throw new InvalidOperationException("Multiple service credentials target the same server instance.");
                }
                seen = true;
                if ((registration.ServiceId == Guid.Empty)
                    || ((registration.Role != ContractCodes.ServerRole.TOWN) && (registration.Role != ContractCodes.ServerRole.DUNGEON)))
                {
                    throw new InvalidOperationException("Server credential binding is invalid.");
                }
                byte[] expected;
                try
                {
                    expected = Convert.FromBase64String(registration.CredentialHashBase64);
                }
                catch (FormatException)
                {
                    throw new InvalidOperationException("Server credential hash must be base64 SHA256.");
                }
                if (expected.Length != HASH_BYTES)
                {
                    throw new InvalidOperationException("Server credential hash must contain 32 bytes.");
                }
                if (CryptographicOperations.FixedTimeEquals(candidate, expected))
                {
                    matched = registration;
                }
            }

            return matched;
        }
    }
}

