// System
using System;
using System.Collections.Generic;

namespace Shinshi.Data.Auctions
{
    /// <summary> 품목 개시 입력. 형식 검사는 Controller가 끝낸 값이다 </summary>
    public sealed class AuctionOpenInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid                SessionId              { get; set; }
        public Guid                OperationId            { get; set; }
        public Guid                AuctionId              { get; set; }
        public string              DefinitionId           { get; set; } = string.Empty;
        public IReadOnlyList<Guid> EligibleParticipantIds { get; set; } = Array.Empty<Guid>();
    }

    /// <summary> 입찰 입력. AcceptedAtUnixMs는 던전 서버가 접수한 시각이다 </summary>
    public sealed class AuctionBidInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid SessionId        { get; set; }
        public Guid AuctionId        { get; set; }
        public Guid OperationId      { get; set; }
        public Guid ParticipantId    { get; set; }
        public long Amount           { get; set; }
        public long ExpectedBidSeq   { get; set; }
        public long AcceptedAtUnixMs { get; set; }
    }

    /// <summary> 마감 입력. 접속 집합과 종료 시각은 던전 서버가 확정한 값이다 </summary>
    public sealed class AuctionCloseInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid                SessionId               { get; set; }
        public Guid                AuctionId               { get; set; }
        public Guid                OperationId             { get; set; }
        public IReadOnlyList<Guid> ConnectedParticipantIds { get; set; } = Array.Empty<Guid>();
        public long                EndedAtUnixMs           { get; set; }
    }
}
