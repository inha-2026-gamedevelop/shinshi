// System
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json.Linq;

// Project
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Contracts.Admin;

namespace Shinshi.Admin.Web.Models
{
    /// <summary> 업무 조회와 변경 흐름. 최종 확인에서 요청을 고정하고 재시도와 화면 이동 이후 응답을 처리한다 </summary>
    public abstract class AdminPageBase : ComponentBase, IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private Func<string, object>? _bodyFactory;
        private PendingAdminWrite? _pending;
        private HttpMethod _method = HttpMethod.Post;
        private string _writePath = string.Empty;
        private int _writeRole;
        private bool _disposed;
        private readonly LatestRequest _readLatest = new();
        private bool _readInFlight;

        protected JToken? _data;
        protected JToken? _preparedBody;
        protected string _message = string.Empty;
        protected string _error = string.Empty;
        protected string _writeTitle = string.Empty;
        protected string _writeTarget = string.Empty;
        protected bool _busy;
        protected bool _writeAttempted;
        protected bool _retryAllowed;
        protected WriteDefaults? _writeDefaults;

        /****************************************
        *              Properties
        ****************************************/

        [Inject] protected AdminWebApiClient Api { get; set; } = default!;
        [Inject] protected AdminWebContext Context { get; set; } = default!;
        protected bool FormLocked => (_busy) || (_bodyFactory != null);
        protected bool HasPrepared => _bodyFactory != null;
        protected string? PendingOperationId => _pending?.OperationId;
        protected bool CanConfirm => (!_busy) && (Context.HasRole(_writeRole)) && ((!_writeAttempted) || (_retryAllowed));

        /****************************************
        *                Methods
        ****************************************/

        protected override void OnInitialized()
        {
            Context.Initialize();
            Context.SessionExpired += ClearForExpiredSession;
        }

        protected async Task ReadAsync(string path)
        {
            if ((_busy) || (!Context.IsAuthenticated))
            {
                return;
            }
            _busy = true;
            _readInFlight = true;
            _error = string.Empty;
            _message = string.Empty;
            int generation = _readLatest.Begin();
            try
            {
                AdminWebApiResult<JToken> result = await Api.GetAsync<JToken>(path);
                // 조회 대상이 바뀌어 InvalidateRead가 불렸다면 이 응답은 버린다
                if ((!_readLatest.IsCurrent(generation)) || (_disposed) || (!Context.IsAuthenticated))
                {
                    return;
                }
                _data = result.IsSuccess ? result.Value : null;
                _error = result.IsSuccess ? string.Empty : result.ErrorMessage;
                if (result.IsSuccess)
                {
                    OnReadResponse(_data);
                }
            }
            finally
            {
                if (_readLatest.IsCurrent(generation))
                {
                    _busy = false;
                    _readInFlight = false;
                }
            }
        }

        /// <summary> 진행 중인 ReadAsync의 응답을 버리고 다시 조회할 수 있게 한다. 대상이 바뀌었을 때 부른다 </summary>
        protected void InvalidateRead()
        {
            if (_readInFlight)
            {
                _readLatest.Cancel();
                _readInFlight = false;
                _busy = false;
            }
        }

        /// <summary> 화면 상태(_data·_message·_error)를 건드리지 않는 조회. 목록 새로 고침처럼 쓰기 결과와 겹치는 읽기에 쓴다 </summary>
        protected async Task<AdminWebApiResult<JToken>?> FetchAsync(string path)
        {
            if ((_disposed) || (!Context.IsAuthenticated))
            {
                return null;
            }
            AdminWebApiResult<JToken> result = await Api.GetAsync<JToken>(path);
            return ((_disposed) || (!Context.IsAuthenticated)) ? null : result;
        }

        protected async Task SearchAsync(object body)
        {
            if ((_busy) || (!Context.HasRole(AdminContractCodes.Role.VIEWER)))
            {
                return;
            }
            _busy = true;
            _error = string.Empty;
            try
            {
                AdminWebApiResult<JToken> result = await Api.SendAsync<JToken>(HttpMethod.Post,
                    AdminApiRoutes.ACCOUNTS_SEARCH, body);
                if ((_disposed) || (!Context.IsAuthenticated))
                {
                    return;
                }
                _data = result.IsSuccess ? result.Value : null;
                _error = result.IsSuccess ? string.Empty : result.ErrorMessage;
            }
            finally
            {
                _busy = false;
            }
        }

        protected void Prepare(string title, string target, string reason, int role, string path,
            Func<string, object> bodyFactory, HttpMethod? method = null, WriteDefaults? defaults = null)
        {
            if (FormLocked)
            {
                return;
            }
            RequireReason(reason);
            if (!Context.HasRole(role))
            {
                throw new InvalidOperationException("이 작업에 필요한 권한이 없습니다.");
            }
            _bodyFactory = bodyFactory;
            _pending = null;
            _writePath = path;
            _method = method ?? HttpMethod.Post;
            _writeRole = role;
            _writeTitle = title;
            _writeTarget = target;
            _writeDefaults = defaults;
            _preparedBody = JObject.FromObject(bodyFactory("확인 후 생성"));
            _writeAttempted = false;
            _retryAllowed = false;
            _error = string.Empty;
            _message = string.Empty;
        }

        protected void Validate(Action action)
        {
            _error = string.Empty;
            try
            {
                action();
            }
            catch (ArgumentException exception)
            {
                _error = exception.Message;
            }
            catch (InvalidOperationException exception)
            {
                _error = exception.Message;
            }
        }

        protected async Task ConfirmAsync()
        {
            if ((!CanConfirm) || (_bodyFactory == null))
            {
                return;
            }
            _pending ??= PendingAdminWrite.Create(_method, _writePath, _bodyFactory);
            if (_preparedBody is JObject preview)
            {
                preview["operationId"] = _pending.OperationId;
            }
            PendingAdminWrite pending = _pending;
            _busy = true;
            _writeAttempted = true;
            _error = string.Empty;
            try
            {
                AdminWebApiResult<JToken> result = await Api.SendAsync<JToken>(pending);
                if ((_disposed) || (!Context.IsAuthenticated))
                {
                    return;
                }
                _retryAllowed = result.CanRetry;
                if (result.IsSuccess)
                {
                    JToken? value = result.Value;
                    OnWriteResponse(value);
                    _data = value;
                    _message = "작업이 확정되었습니다. 작업 ID: " + pending.OperationId;
                    CancelPrepared();
                }
                else
                {
                    _data = null;
                    _error = WriteFailureMessage(result);
                    if (result.ResponseLost)
                    {
                        _error += " 결과를 받지 못했으므로 같은 요청으로 재시도하세요. 이미 처리되었을 수 있습니다.";
                    }
                }
            }
            finally
            {
                _busy = false;
            }
        }

        protected void CancelPrepared()
        {
            _bodyFactory = null;
            _pending = null;
            _preparedBody = null;
            _writeAttempted = false;
            _retryAllowed = false;
            _writeDefaults = null;
        }

        protected virtual string WriteFailureMessage(AdminWebApiResult<JToken> result) => result.ErrorMessage;

        protected virtual void OnReadResponse(JToken? value) { }
        protected virtual void OnWriteResponse(JToken? value) { }
        protected virtual void ClearSensitiveState() { }

        protected static string Id(string value, string label)
        {
            if ((!Guid.TryParse(value, out Guid id)) || (id == Guid.Empty))
            {
                throw new ArgumentException(label + "에 유효한 UUID를 입력하세요.");
            }
            return id.ToString("D");
        }

        protected static string? OptionalId(string value, string label)
        {
            return string.IsNullOrWhiteSpace(value) ? null : Id(value, label);
        }

        protected static string? Optional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        protected static string RequireReason(string value)
        {
            if ((string.IsNullOrWhiteSpace(value)) || (value.Length > AdminContractCodes.MAX_REASON_LENGTH))
            {
                throw new ArgumentException("사유는 1~500자로 입력하세요.");
            }
            return value.Trim();
        }

        protected static string Query(string path, params (string Key, string? Value)[] fields)
        {
            string query = string.Join("&", fields.Where(field => !string.IsNullOrEmpty(field.Value))
                .Select(field => Uri.EscapeDataString(field.Key) + "=" + Uri.EscapeDataString(field.Value!)));
            return query.Length == 0 ? path : path + "?" + query;
        }

        protected static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        private void ClearForExpiredSession()
        {
            _data = null;
            ClearSensitiveState();
            CancelPrepared();
            _ = InvokeAsync(StateHasChanged);
        }

        public virtual void Dispose()
        {
            _disposed = true;
            Context.SessionExpired -= ClearForExpiredSession;
            _data = null;
            ClearSensitiveState();
            CancelPrepared();
        }
    }
}