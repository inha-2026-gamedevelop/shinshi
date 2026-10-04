// System
using System;
using System.Collections;
using System.Text;

// Unity
using UnityEngine.Networking;

// 서드파티
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 로비 화면이 API를 부른다. 화면 하나가 하나씩 들고 한 번에 요청 하나만 보낸다 </summary>
    public sealed class LobbyApiClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    REQUEST_TIMEOUT_SECONDS = 15;
        private const string JSON_TYPE               = "application/json";
        private const string CONTENT_TYPE_HEADER     = "Content-Type";
        private const string REASON_CODE_KEY         = "reasonCode";
        private const string RESULT_KEY              = "result";

        private static readonly JsonSerializerSettings G_Json = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None };

        private UnityWebRequest _request = null;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsBusy => _request != null;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 받은 게임 세션으로 부른다. 세션이 없거나 만료됐으면 보내지 않고 TOKEN_EXPIRED로 끝낸다 </summary>
        public IEnumerator SendGame<T>(string path, object body, Action<LobbyApiReply<T>> done) where T : class
        {
            string token = LauncherSession.GetGameSessionToken();
            if (string.IsNullOrEmpty(token))
            {
                done(new LobbyApiReply<T>(null, 0, ContractCodes.RejectReason.TOKEN_EXPIRED));
                yield break;
            }

            yield return Send(LauncherSession.ApiBaseUrl, token, path, body, done);
        }

        /// <summary> body가 없으면 GET, 있으면 POST다. bearer가 없으면 인증 헤더를 싣지 않는다 </summary>
        public IEnumerator Send<T>(string baseUrl, string bearer, string path, object body, Action<LobbyApiReply<T>> done)
            where T : class
        {
            return SendCore(baseUrl, bearer, path, body, body != null, done);
        }

        /// <summary> 본문 없는 POST다. 게임 세션 갱신처럼 bearer만으로 동작하는 경로에 쓴다 </summary>
        public IEnumerator SendEmptyPost<T>(string baseUrl, string bearer, string path, Action<LobbyApiReply<T>> done)
            where T : class
        {
            return SendCore(baseUrl, bearer, path, null, true, done);
        }

        private IEnumerator SendCore<T>(string baseUrl, string bearer, string path, object body, bool post,
            Action<LobbyApiReply<T>> done) where T : class
        {
            if (done == null)
            {
                throw new ArgumentNullException(nameof(done));
            }

            if (IsBusy)
            {
                throw new InvalidOperationException("이전 요청이 끝나기 전에 새 요청을 보냈다");
            }

            if (string.IsNullOrEmpty(baseUrl))
            {
                done(new LobbyApiReply<T>(null, 0, ContractCodes.RejectReason.NONE));
                yield break;
            }

            string address = baseUrl.TrimEnd('/') + path;
            using (var request = new UnityWebRequest(address, post ? UnityWebRequest.kHttpVerbPOST : UnityWebRequest.kHttpVerbGET))
            {
                _request                = request;
                request.timeout         = REQUEST_TIMEOUT_SECONDS;
                request.redirectLimit   = 0;
                request.downloadHandler = new DownloadHandlerBuffer();
                if (!string.IsNullOrEmpty(bearer))
                {
                    request.SetRequestHeader(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + bearer);
                }

                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, G_Json)));
                    request.SetRequestHeader(CONTENT_TYPE_HEADER, JSON_TYPE);
                }

                yield return request.SendWebRequest();

                // 화면이 닫히며 Abort한 요청은 결과를 알리지 않는다
                if (_request != request)
                {
                    yield break;
                }

                _request = null;
                string text = request.downloadHandler?.text;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    done(new LobbyApiReply<T>(null, request.responseCode, ReadReason(text)));
                    yield break;
                }

                T value = null;
                try
                {
                    value = JsonConvert.DeserializeObject<T>(text, G_Json);
                }
                catch (JsonException)
                {
                    value = null;
                }

                done(new LobbyApiReply<T>(value, request.responseCode, ContractCodes.RejectReason.NONE));
            }
        }

        /// <summary> 보내는 중인 요청을 버린다. 화면이 닫힐 때 부른다 </summary>
        public void Abort()
        {
            if (_request == null)
            {
                return;
            }

            UnityWebRequest request = _request;
            _request = null;
            request.Abort();
        }

        // 조회, 인증 거절은 ApiRejection을, 작업 거절(캐릭터 생성 409)은 result에 OperationResult를 싣는다.
        // 둘 다 아니면 NONE으로 두고 HTTP 상태로 안내한다
        private static int ReadReason(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return ContractCodes.RejectReason.NONE;
            }

            try
            {
                JObject root = JObject.Parse(text);
                JToken reason = root[REASON_CODE_KEY] ?? root[RESULT_KEY]?[REASON_CODE_KEY];
                return ((reason != null) && (reason.Type == JTokenType.Integer)) ? reason.Value<int>() : ContractCodes.RejectReason.NONE;
            }
            catch (JsonException)
            {
                return ContractCodes.RejectReason.NONE;
            }
        }
    }
}
