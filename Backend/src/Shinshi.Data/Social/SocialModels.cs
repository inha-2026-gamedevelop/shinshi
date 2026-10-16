// System
using System;
using System.Collections.Generic;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Social
{
    /// <summary> 소셜 경로를 부른 게임 서버. 행위 캐릭터의 임대가 이 인스턴스에 있어야 한다 </summary>
    public sealed record SocialCaller(Guid InstanceId, int Role);

    /// <summary> 관리 쓰기 결과. reasonCode가 NONE이면 확정(이미 목표 상태 포함)이다 </summary>
    public sealed record SocialActionOutcome(int ReasonCode, IReadOnlyList<Guid> ChangedCharacterIds)
    {
        /****************************************
        *              Properties
        ****************************************/

        public bool IsCommitted => ReasonCode == ContractCodes.RejectReason.NONE;

        /****************************************
        *                Methods
        ****************************************/

        public static SocialActionOutcome Committed(IReadOnlyList<Guid>? changed = null)
        {
            return new SocialActionOutcome(ContractCodes.RejectReason.NONE, changed ?? Array.Empty<Guid>());
        }

        public static SocialActionOutcome Rejected(int reasonCode)
        {
            return new SocialActionOutcome(reasonCode, Array.Empty<Guid>());
        }
    }

    /// <summary> 전달함 한 행. 로컬 전달과 꺼내기가 같은 모양이다 </summary>
    public sealed record SocialDeliveryRecord(long DeliveryId, Guid TargetCharacterId, int Kind, int Channel,
        Guid? SenderCharacterId, string SenderName, string Payload, DateTime CreatedAt);

    /// <summary> 귓속말, 길드 채팅 결과. 거절이면 행이 없다 </summary>
    public sealed record SocialChatOutcome(int ReasonCode, string Name, IReadOnlyList<SocialDeliveryRecord> LocalDeliveries)
    {
        /****************************************
        *                Methods
        ****************************************/

        public static SocialChatOutcome Rejected(int reasonCode)
        {
            return new SocialChatOutcome(reasonCode, string.Empty, Array.Empty<SocialDeliveryRecord>());
        }
    }

    /// <summary> 꺼내기 수신 확인 한 줄. 수신 캐릭터는 게임 서버가 보낸 기록에서 정한다 </summary>
    public sealed record SocialDeliveryAckInput(long DeliveryId, Guid RecipientCharacterId);

    /// <summary> 꺼내기 결과 </summary>
    public sealed record SocialPullOutcome(int Acknowledged, IReadOnlyList<SocialDeliveryRecord> Deliveries);

    /// <summary> 만료 정리 결과 </summary>
    public sealed record SocialPurgeResult(int FriendRequests, int GuildRequests, int Deliveries)
    {
        /****************************************
        *              Properties
        ****************************************/

        public int Total => FriendRequests + GuildRequests + Deliveries;
    }

    /// <summary> 접속 위치. 임대가 없거나 서버가 죽었으면 Offline이다 </summary>
    public sealed record SocialPresence(int Presence, bool SameServer);

    /// <summary> 스냅샷 한 사람 </summary>
    public sealed record SocialPersonRecord(Guid CharacterId, string Name, int ClassId, SocialPresence Presence);

    /// <summary> 스냅샷 요청 한 건 </summary>
    public sealed record SocialRequestRecord(Guid CharacterId, string Name, int ClassId, DateTime ExpiresAt);

    /// <summary> 스냅샷 길드원 한 명 </summary>
    public sealed record SocialMemberRecord(Guid CharacterId, Guid MembershipId, string Name, int ClassId, int Role,
        SocialPresence Presence);

    /// <summary> 스냅샷 받은 길드 초대 한 건 </summary>
    public sealed record SocialInviteRecord(Guid GuildId, string GuildName, string InviterName, DateTime ExpiresAt);

    /// <summary> 스냅샷 내 길드 </summary>
    public sealed record SocialGuildRecord(Guid GuildId, string Name, int MemberCount, int MyRole, Guid MyMembershipId,
        long LeaderVersion);

    /// <summary> 소유 캐릭터 한 명의 소셜 상태. 행위 캐릭터가 호출 인스턴스에 없으면 null이다 </summary>
    public sealed record SocialSnapshotRecord(DateTime ServerTime, IReadOnlyList<SocialPersonRecord> Friends,
        IReadOnlyList<SocialRequestRecord> Incoming, IReadOnlyList<SocialRequestRecord> Outgoing, SocialGuildRecord? Guild,
        IReadOnlyList<SocialMemberRecord> Members, IReadOnlyList<SocialRequestRecord> GuildApplications,
        IReadOnlyList<SocialInviteRecord> GuildInvites);
}
