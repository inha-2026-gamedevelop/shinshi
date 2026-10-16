// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Sessions;
using Shinshi.Data.Social;

namespace Shinshi.Backend.Social
{
    /// <summary> 마을과 던전 서버의 귓속말, 길드 채팅, 전달함 꺼내기. 채팅은 기능 키 social이 꺼지면 409 FEATURE_DISABLED이고
    /// 꺼내기는 막지 않는다(이미 쌓인 전달). 채팅 본문은 게임 서버가 정리한 1~100자만 받는다 </summary>
    [ApiController]
    public sealed class SocialChatController : SocialControllerBase
    {
        /****************************************
        *                Methods
        ****************************************/

        public SocialChatController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresSocialRepository social, SocialRequestLimiter limiter, TimeProvider timeProvider,
            ServiceFeatureGate? features = null)
            : base(callers, pool, social, limiter, timeProvider, features)
        {
        }

        /// <summary> 200 성공(localDeliveries), 409 SOCIAL_TARGET_NOT_FOUND, SOCIAL_TARGET_SELF, WHISPER_TARGET_OFFLINE, SOCIAL_NOT_CONNECTED </summary>
        [HttpPost(SocialRouteTemplates.WHISPER)]
        public async Task<IActionResult> Whisper(string characterId, [FromBody] SocialWhisperRequest? request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, Guid actor, IActionResult? rejection) = await PrepareChatAsync(characterId,
                request?.Text, request?.TargetName != null, true, cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }

            string? nameKey = CharacterNameRules.TryNormalize(request!.TargetName, out _, out string key) ? key : null;
            SocialChatOutcome outcome = await Social.WhisperAsync(ToCaller(caller), actor, nameKey, request.Text,
                cancellationToken);
            return Respond(new SocialWhisperResponse(ToResponse(outcome.ReasonCode), outcome.Name,
                ToRows(outcome.LocalDeliveries)), outcome.ReasonCode);
        }

        /// <summary> 200 성공(localDeliveries), 409 GUILD_NOT_MEMBER, SOCIAL_NOT_CONNECTED </summary>
        [HttpPost(SocialRouteTemplates.GUILD_CHAT)]
        public async Task<IActionResult> GuildChat(string characterId, [FromBody] SocialGuildChatRequest? request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, Guid actor, IActionResult? rejection) = await PrepareChatAsync(characterId,
                request?.Text, true, false, cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }

            SocialChatOutcome outcome = await Social.GuildChatAsync(ToCaller(caller), actor, request!.Text, cancellationToken);
            return Respond(new SocialGuildChatResponse(ToResponse(outcome.ReasonCode), outcome.Name,
                ToRows(outcome.LocalDeliveries)), outcome.ReasonCode);
        }

        /// <summary> 200 꺼낸 행. 경로 인스턴스가 호출 인스턴스와 다르면 403, bootId가 임대 boot와 다르면 409 SESSION_MISMATCH,
        /// 형식 오류(waitMs가 0이 아님, 확인 목록 상한 초과, 잘못된 ID)는 400이다 </summary>
        [HttpPost(SocialRouteTemplates.DELIVERY_PULL)]
        public async Task<IActionResult> Pull(string instanceId, [FromBody] SocialDeliveryPullRequest? request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireGameServerCallerAsync(cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(instanceId, out Guid instance))
            {
                return BadRequest();
            }
            if (instance != caller.InstanceId)
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            SocialDeliveryAck[] acks = request?.AckDeliveries ?? Array.Empty<SocialDeliveryAck>();
            if ((request == null) || (!TryParseId(request.BootId, out Guid bootId))
                || (request.WaitMs != SocialLimits.PULL_WAIT_MS) || (acks.Length > SocialLimits.MAX_ACKS_PER_PULL)
                || (acks.Any(ack => (ack == null) || (ack.DeliveryId <= 0) || (!TryParseId(ack.RecipientCharacterId, out _)))))
            {
                return BadRequest();
            }
            if ((caller.BootId.HasValue) && (caller.BootId.Value != bootId))
            {
                return Rejection(StatusCodes.Status409Conflict, ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            SocialPullOutcome outcome = await Social.PullAsync(ToCaller(caller), acks
                .Select(ack => new SocialDeliveryAckInput(ack.DeliveryId, Guid.Parse(ack.RecipientCharacterId)))
                .Distinct().ToArray(), cancellationToken);
            return Ok(new SocialDeliveryPullResponse(NowUnixMs(), ToRows(outcome.Deliveries)));
        }

        // 서버 자격 -> 형식 -> 기능 차단 -> 한도. 실패면 caller가 null이고 rejection에 응답이 있다
        private async Task<(ServerCaller? Caller, Guid Actor, IActionResult? Rejection)> PrepareChatAsync(string characterId,
            string? text, bool bodyValid, bool whisper, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireGameServerCallerAsync(cancellationToken);
            if (caller == null)
            {
                return (null, Guid.Empty, rejection);
            }
            if ((!bodyValid) || (string.IsNullOrEmpty(text)) || (text.Length > SocialLimits.MAX_CHAT_LENGTH)
                || (!TryParseId(characterId, out Guid actor)))
            {
                return (null, Guid.Empty, BadRequest());
            }
            if (await IsBlockedAsync(cancellationToken))
            {
                SocialActionResponse refused = ToResponse(ContractCodes.RejectReason.FEATURE_DISABLED);
                object body = whisper
                    ? new SocialWhisperResponse(refused, string.Empty, Array.Empty<SocialDeliveryRow>())
                    : new SocialGuildChatResponse(refused, string.Empty, Array.Empty<SocialDeliveryRow>());
                return (null, Guid.Empty, Respond(body, ContractCodes.RejectReason.FEATURE_DISABLED));
            }

            int? retryAfter = Limiter.TryAcquireChat(actor);
            if (retryAfter.HasValue)
            {
                return (null, Guid.Empty, TooMany(retryAfter.Value));
            }

            return (caller, actor, null);
        }
    }
}
