// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 게이트웨이에 넘기는 요청 하나, 본문은 만들 때 JSON으로 고정해 재시도해도 같은 바이트를 보낸다 </summary>
    public sealed class BackendRequest
    {
        // 본문에 토큰이 들어갈 수 있으므로 BodyJson을 로그에 남기지 않고 ToString도 만들지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly EBackendMethod _method;
        private readonly string         _path;
        private readonly string         _bodyJson;
        private readonly Type           _responseType;
        private readonly string         _operationId;
        private readonly float          _timeoutSeconds;
        private readonly bool           _bypassesCircuit;

        /****************************************
        *              Properties
        ****************************************/

        public EBackendMethod Method         => _method;
        public string         Path           => _path;
        public string         BodyJson       => _bodyJson;
        public Type           ResponseType   => _responseType;
        public string         OperationId    => _operationId;
        public float          TimeoutSeconds => _timeoutSeconds;

        // 서버 생존 신호(임대, heartbeat, 드레인)만 참이다. 데이터 요청의 장애로 열린 회로가 생존 신호까지 막으면 API가
        // 살아 있는 프로세스를 소실로 보고 출정을 취소한다. 그래서 회로와 응답 대기 상한을 거치지 않고, 결과도 회로에 세지 않는다
        public bool           BypassesCircuit => _bypassesCircuit;

        // 같은 operationId면 API가 최초 확정 결과를 돌려주므로 다시 보내도 효과가 한 번이다. 없으면 한 번만 보낸다
        public bool IsRetryable => !string.IsNullOrEmpty(_operationId);

        /****************************************
        *                Methods
        ****************************************/

        private BackendRequest(EBackendMethod method, string path, string bodyJson, Type responseType, string operationId,
            float timeoutSeconds, bool bypassesCircuit)
        {
            _method         = method;
            _path           = path;
            _bodyJson       = bodyJson;
            _responseType   = responseType;
            _operationId    = operationId;
            _timeoutSeconds = timeoutSeconds;

            _bypassesCircuit = bypassesCircuit;
        }

        /// <summary> 조회 요청, responseType이 null이면 2xx 본문을 읽지 않는다 </summary>
        public static BackendRequest Get(string path, Type responseType, string operationId, float timeoutSeconds)
        {
            Validate(path, timeoutSeconds);
            return new BackendRequest(EBackendMethod.Get, path, null, responseType, operationId, timeoutSeconds, false);
        }

        /// <summary> 본문을 JSON으로 보내는 요청, 경제 작업은 operationId를 반드시 넣는다 </summary>
        public static BackendRequest Post(string path, object body, Type responseType, string operationId, float timeoutSeconds)
        {
            return Post(path, body, responseType, operationId, timeoutSeconds, false);
        }

        /// <summary> bypassesCircuit는 서버 생존 신호에만 참으로 준다, 데이터 요청은 회로를 거친다 </summary>
        public static BackendRequest Post(string path, object body, Type responseType, string operationId, float timeoutSeconds,
            bool bypassesCircuit)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            Validate(path, timeoutSeconds);
            return new BackendRequest(EBackendMethod.Post, path, BackendJson.Serialize(body), responseType, operationId,
                timeoutSeconds, bypassesCircuit);
        }

        /// <summary> 본문 없는 POST, 경로만으로 뜻이 정해지는 멱등 요청(계정 접속 임대 해제)에 쓴다 </summary>
        public static BackendRequest PostWithoutBody(string path, Type responseType, string operationId, float timeoutSeconds)
        {
            Validate(path, timeoutSeconds);
            return new BackendRequest(EBackendMethod.Post, path, null, responseType, operationId, timeoutSeconds, false);
        }

        // 기준 URL 아래 상대 경로만 받는다. 절대 URL이나 상위 경로로 서비스 자격을 다른 곳에 보내지 못하게 한다
        private static void Validate(string path, float timeoutSeconds)
        {
            if ((string.IsNullOrEmpty(path)) || (path[0] != '/') || (path.StartsWith("//", StringComparison.Ordinal))
                || (path.IndexOf("..", StringComparison.Ordinal) >= 0) || (path.IndexOf('\\') >= 0)
                || (path.IndexOf(':') >= 0))
            {
                throw new ArgumentException("슬래시로 시작하는 상대 경로가 필요하다", nameof(path));
            }

            if ((timeoutSeconds <= 0f) || (timeoutSeconds > Constants.Network.MAX_BACKEND_TIMEOUT_SECONDS))
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            }
        }
    }
}
