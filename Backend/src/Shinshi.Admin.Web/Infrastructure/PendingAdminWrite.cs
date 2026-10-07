// System
using System;
using System.Net.Http;
using System.Text;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> 최종 확인 뒤 확정한 요청. 재시도는 같은 객체의 바이트를 쓰며 본문은 공개하지 않는다 </summary>
    public sealed class PendingAdminWrite
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly byte[] _body;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId { get; }
        public HttpMethod Method { get; }
        public string PathAndQuery { get; }

        /****************************************
        *                Methods
        ****************************************/

        private PendingAdminWrite(HttpMethod method, string pathAndQuery, string operationId, byte[] body)
        {
            Method       = method;
            PathAndQuery = pathAndQuery;
            OperationId  = operationId;
            _body        = body;
        }

        public static PendingAdminWrite Create(HttpMethod method, string pathAndQuery,
            Func<string, object> bodyFactory)
        {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(bodyFactory);
            AdminWebApiClient.ValidatePath(pathAndQuery);
            if ((method != HttpMethod.Post) && (method != HttpMethod.Put) && (method != HttpMethod.Delete))
            {
                throw new ArgumentException("쓰기 요청의 HTTP 메서드가 올바르지 않습니다.", nameof(method));
            }

            string operationId = Guid.NewGuid().ToString("D");
            object body = bodyFactory(operationId);
            string serialized = JsonConvert.SerializeObject(body, ContractJson.CreateSettings());
            JObject request = JObject.Parse(serialized);
            string? reason = request.Value<string>("reason");
            if (!string.Equals(request.Value<string>("operationId"), operationId, StringComparison.Ordinal)
                || (string.IsNullOrWhiteSpace(reason)) || (reason.Length > AdminContractCodes.MAX_REASON_LENGTH))
            {
                throw new ArgumentException("쓰기 요청에는 동일한 operationId와 올바른 사유가 필요합니다.", nameof(bodyFactory));
            }

            return new PendingAdminWrite(method, pathAndQuery, operationId, Encoding.UTF8.GetBytes(serialized));
        }

        internal HttpContent CreateContent()
        {
            ByteArrayContent content = new(_body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };
            return content;
        }
    }
}
