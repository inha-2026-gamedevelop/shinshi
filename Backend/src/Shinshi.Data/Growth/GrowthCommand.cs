// System
using System;

namespace Shinshi.Data.Growth
{
    /// <summary> 마을 서버가 보낸 성장 쓰기 하나. 멱등 actor는 캐릭터의 계정이라 재접속이나 다른 마을에서 같은 operationId를 다시 보내도
    /// 같은 결과를 받는다. 확률 입력과 기대 revision은 판정 입력일 뿐 해시 본문에 넣지 않는다 </summary>
    public sealed class GrowthCommand
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid   TownInstanceId   { get; }
        public Guid   OperationId      { get; }
        public Guid   CharacterId      { get; }
        public long   ExpectedRevision { get; }
        public string OperationKind    { get; }

        // ContractCodes.EconomyReason, 이 작업이 쓰는 원장 줄의 사유
        public int    ReasonCode       { get; }

        // 마을 서버가 뽑은 확률 입력(0~9999). 확률 판정이 없는 작업은 null이다. 영수증에 기록한다
        public int?   RollBasisPoints  { get; }

        // 요청 해시 본문. 같은 operationId 재전송이 확률 입력이나 마을 서버가 다시 읽은 revision만 달라도 처음 결과를 받도록 둘을 뺀다
        public string RequestJson      { get; }

        /****************************************
        *                Methods
        ****************************************/

        public GrowthCommand(Guid townInstanceId, Guid operationId, Guid characterId, long expectedRevision,
            string operationKind, int reasonCode, object requestBody, int? rollBasisPoints = null)
        {
            if ((townInstanceId == Guid.Empty) || (operationId == Guid.Empty) || (characterId == Guid.Empty))
            {
                throw new ArgumentException("Town, operation and character IDs are required.");
            }
            if (expectedRevision < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedRevision));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
            ArgumentNullException.ThrowIfNull(requestBody);

            TownInstanceId   = townInstanceId;
            OperationId      = operationId;
            CharacterId      = characterId;
            ExpectedRevision = expectedRevision;
            OperationKind    = operationKind;
            ReasonCode       = reasonCode;
            RollBasisPoints  = rollBasisPoints;
            RequestJson      = Sessions.SessionDb.ToRequestJson(new
            {
                characterId = characterId.ToString("D"),
                body = requestBody
            });
        }

        /// <summary> actor는 서버가 캐릭터에서 읽은 계정이다. 요청 본문의 값으로 정하지 않는다 </summary>
        public OperationRequest ToOperationRequest(Guid accountId)
        {
            return new OperationRequest(accountId, OperationId, OperationKind, RequestJson);
        }
    }
}
