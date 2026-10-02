// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 새 운영자 행과 한 번만 보여 줄 원문 비밀. ToString을 만들지 않아 실수로 로그에 찍히지 않게 한다 </summary>
    public sealed class AdminIssuedCredentials
    {
        /****************************************
        *              Properties
        ****************************************/

        public AdminNewOperator Operator { get; }
        public string Password { get; }
        public string TotpSecretBase32 { get; }
        public string TotpUri { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminIssuedCredentials(AdminNewOperator newOperator, string password, string totpSecretBase32, string totpUri)
        {
            ArgumentNullException.ThrowIfNull(newOperator);
            ArgumentException.ThrowIfNullOrWhiteSpace(password);
            ArgumentException.ThrowIfNullOrWhiteSpace(totpSecretBase32);
            ArgumentException.ThrowIfNullOrWhiteSpace(totpUri);
            Operator         = newOperator;
            Password         = password;
            TotpSecretBase32 = totpSecretBase32;
            TotpUri          = totpUri;
        }
    }
}
