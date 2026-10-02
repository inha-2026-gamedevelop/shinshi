// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 서비스 자격과 서버 임대 토큰으로 호출 서버를 확인한다. 구현은 인증 작업이 제공한다 </summary>
    public interface IServerCallerAuthenticator
    {
        /****************************************
        *                Methods
        ****************************************/

        // 플레이어 로그인 토큰, 어드민 토큰, 자격 없음은 모두 null이다. 본문의 instanceId를 믿지 않고 자격에 묶인 값을 돌려준다
        Task<ServerCaller?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken);
    }

    /// <summary> 인증된 게임 서버 인스턴스. 멱등 작업의 actor가 된다 </summary>
    public sealed class ServerCaller
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid  InstanceId { get; }
        public int   Role       { get; }

        // 임대가 묶인 프로세스. 0005 이전 임대는 값이 없다
        public Guid? BootId     { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ServerCaller(Guid instanceId, int role, Guid? bootId = null)
        {
            if (instanceId == Guid.Empty)
            {
                throw new ArgumentException("Instance ID is required.", nameof(instanceId));
            }

            InstanceId = instanceId;
            Role       = role;
            BootId     = bootId;
        }
    }
}
