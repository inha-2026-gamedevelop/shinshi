// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 로그인한 런처가 게임 실행 직전에 게임 전용 하위 세션을 요청한다 </summary>
    [DataContract]
    public sealed class GameSessionIssueRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 실행할 설치 빌드. 허용 목록에 없으면 런처가 시작 버튼을 막는다
        // TODO(민성): API도 이 값으로 발급을 거절할지 기획 미정
        [DataMember(Name = "build")] private ClientBuildInfo _build;

        /****************************************
        *              Properties
        ****************************************/

        public ClientBuildInfo Build => _build;

        /****************************************
        *                Methods
        ****************************************/

        public GameSessionIssueRequest(ClientBuildInfo build)
        {
            _build = build;
        }

        private GameSessionIssueRequest() { }
    }
}
