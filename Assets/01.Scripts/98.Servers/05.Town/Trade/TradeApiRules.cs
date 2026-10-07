// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 거래 세션을 prepare, commit, resolve API 호출로 옮기고 응답을 읽는 순수 계산 </summary>
    public static class TradeApiRules
    {
        // prepare의 operationId는 executionId이고, commit의 operationId는 prepare 본문에 실은 commitOperationId다. 둘 다 세션이
        // 양쪽 수락 때 새로 만든 값이라, 확정 거절 뒤 다시 실행하면 새 값을 쓰고 결과를 모르는 재시도에서만 같은 값을 다시 쓴다.
        // resolve는 본문이 없는 POST이고 operationId가 없어 게이트웨이가 다시 보내지 않는다(마을 서버가 스스로 간격을 두고 부른다)

        /****************************************
        *                Fields
        ****************************************/

        private const string GUID_FORMAT = "D";

        /****************************************
        *                Methods
        ****************************************/

        public static BackendRequest BuildPrepare(TradeSession session, string townInstanceId)
        {
            TownMember sideA = session.MemberAt(0);
            TownMember sideB = session.MemberAt(1);
            string executionId = session.ExecutionId.ToString(GUID_FORMAT);
            var body = new TradePrepareRequest(executionId, executionId, session.TradeId.ToString(GUID_FORMAT), townInstanceId,
                ToSide(sideA, session.LinesOf(0), session.CurrencyOf(0)), ToSide(sideB, session.LinesOf(1), session.CurrencyOf(1)),
                session.CommitOperationId.ToString(GUID_FORMAT));
            return BackendRequest.Post(TradeRoutes.PREPARE, body, typeof(TradeExecutionResponse), executionId,
                Constants.Network.API_TIMEOUT_SECONDS);
        }

        public static BackendRequest BuildCommit(TradeSession session)
        {
            string commitOperationId = session.CommitOperationId.ToString(GUID_FORMAT);
            return BackendRequest.Post(Format(TradeRoutes.COMMIT_FORMAT, session.ExecutionId), new TradeCommitRequest(commitOperationId),
                typeof(TradeExecutionResponse), commitOperationId, Constants.Network.API_TIMEOUT_SECONDS);
        }

        public static BackendRequest BuildResolve(TradeSession session)
        {
            return BackendRequest.PostWithoutBody(Format(TradeRoutes.RESOLVE_FORMAT, session.ExecutionId),
                typeof(TradeExecutionResponse), null, Constants.Network.API_TIMEOUT_SECONDS);
        }

        /// <summary> commit 응답이 없을 때 resolve를 부르는 간격. 1, 2, 4초 뒤이고 그다음부터는 5초마다다 </summary>
        public static double ResolveDelay(int attempt)
        {
            switch (attempt)
            {
                case 0:
                    return TradeSettings.RESOLVE_FIRST_SECONDS;
                case 1:
                    return TradeSettings.RESOLVE_SECOND_SECONDS;
                case 2:
                    return TradeSettings.RESOLVE_THIRD_SECONDS;
                default:
                    return TradeSettings.RESOLVE_REPEAT_SECONDS;
            }
        }

        /// <summary> 실행 상태 문자열을 해석한다, 모르는 값이면 거짓 </summary>
        public static bool TryReadState(TradeExecutionResponse payload, out string state, out int reasonCode)
        {
            state      = null;
            reasonCode = ContractCodes.RejectReason.NONE;
            if (payload?.Execution == null)
            {
                return false;
            }

            state      = payload.Execution.State;
            reasonCode = payload.Execution.ReasonCode;
            return !string.IsNullOrEmpty(state);
        }

        private static TradeSideOffer ToSide(TownMember member, TradeOfferLine[] lines, long currency)
        {
            var items = new TradeItemLine[lines.Length];
            for (int i = 0; i < lines.Length; ++i)
            {
                TradeOfferLine line = lines[i];
                items[i] = line.IsEquipment
                    ? new TradeItemLine(MarketCodes.ASSET_EQUIPMENT, line.DefinitionId, line.ItemId, 1)
                    : new TradeItemLine(MarketCodes.ASSET_ACCOUNT_ITEM, line.DefinitionId, null, line.Quantity);
            }

            return new TradeSideOffer(member.AccountId, member.CharacterId, items, currency);
        }

        private static string Format(string format, Guid executionId)
        {
            return string.Format(CultureInfo.InvariantCulture, format, executionId.ToString(GUID_FORMAT));
        }
    }
}
