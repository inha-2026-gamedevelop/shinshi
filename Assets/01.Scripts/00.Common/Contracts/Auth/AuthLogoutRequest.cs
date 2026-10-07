// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 런처가 로그아웃할 때 저장된 갱신 토큰으로 로그인 묶음의 폐기를 요청한다 </summary>
    [DataContract]
    public sealed class AuthLogoutRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 폐기할 묶음의 갱신 토큰 원문. 만료됐거나 이미 회전된 토큰이어도 같은 묶음을 찾는다. 로그에 남기지 않는다
        [DataMember(Name = "refreshToken")] private string _refreshToken;

        /****************************************
        *              Properties
        ****************************************/

        public string RefreshToken => _refreshToken;

        /****************************************
        *                Methods
        ****************************************/

        public AuthLogoutRequest(string refreshToken)
        {
            _refreshToken = refreshToken;
        }

        private AuthLogoutRequest() { }
    }
}
