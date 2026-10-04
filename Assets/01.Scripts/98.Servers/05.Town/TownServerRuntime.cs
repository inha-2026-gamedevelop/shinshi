// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 마을 역할의 파티, 성장 중계, 생활 서버를 묶고 접속자 목록과 요청 속도 제한을 맡는다, 메인 스레드에서만 부른다 </summary>
    public sealed class TownServerRuntime : ITownMemberDirectory
    {
        // Mirror 연결과 메시지 등록은 TownServerHost가 하고, 여기는 인증된 참가자(TownMember)와 메시지만 받는다.
        // 그래서 시험은 가짜 창구와 가짜 전송기로 Mirror 없이 전체 흐름을 돌릴 수 있다

        /****************************************
        *                Fields
        ****************************************/

        private readonly Func<double>                    _clock;
        private readonly ITownAudience                   _audience;
        private readonly Dictionary<int, TownMember>     _members;
        private readonly Dictionary<string, TownMember>  _membersByName;

        /****************************************
        *              Properties
        ****************************************/

        public TownPartyDirector  Party         { get; }
        public GrowthRelayServer  Growth        { get; }
        public GatherZoneServer   Gather        { get; }
        public PotionCraftServer  Potions       { get; }
        public LifeSnapshotRelay  LifeSnapshot  { get; }
        public IntroProgressRelay IntroProgress { get; }
        public int                MemberCount   => _members.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> gateway가 null이면 API가 필요한 요청을 BackendUnavailable로 거절한다. roll은 0 이상 BASIS_POINTS 미만 </summary>
        public TownServerRuntime(BackendGateway gateway, ITownAudience audience, IGatherNodeLayout layout, SessionConfigSO session,
            LifeConfigSO life, ProgressionCatalogSO progression, int trainingTotalCap, string contentVersion, bool allowUnverifiedProgress, Func<int> roll,
            Func<double> clock, Action<string> warn)
        {
            if ((audience == null) || (session == null) || (life == null) || (progression == null))
            {
                throw new ArgumentNullException(nameof(audience), "마을 서버에 필요한 참조가 없다");
            }

            _clock         = clock ?? BackendGateway.MonotonicSeconds;
            _audience      = audience;
            _members       = new Dictionary<int, TownMember>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _membersByName = new Dictionary<string, TownMember>(Constants.Session.MAX_SESSION_PARTICIPANTS, StringComparer.Ordinal);

            Party         = new TownPartyDirector(gateway, audience, this, progression, life, session.ReadyCountdownSeconds,
                contentVersion, allowUnverifiedProgress, _clock, warn);
            Growth        = new GrowthRelayServer(gateway, audience, roll, _clock, Party.OnCharacterChanged, trainingTotalCap);
            LifeSnapshot  = new LifeSnapshotRelay(gateway, audience);
            Gather        = new GatherZoneServer(gateway, audience, layout, GatherSettings.FromConfig(life), _clock, RefreshLife);
            Potions       = new PotionCraftServer(gateway, audience, life, RefreshLife);
            IntroProgress = new IntroProgressRelay(gateway, audience);
        }

        public bool TryGetMember(int participantId, out TownMember member)
        {
            return _members.TryGetValue(participantId, out member);
        }

        public bool TryFindByName(string characterName, out TownMember member)
        {
            member = null;
            return (!string.IsNullOrEmpty(characterName)) && (_membersByName.TryGetValue(characterName, out member));
        }

        /// <summary> 인증된 연결의 첫 메시지 때 넣는다. 같은 계정의 남은 채집 선점을 새 연결에 잇는다 </summary>
        public void AddMember(TownMember member)
        {
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            _members[member.ParticipantId] = member;
            if (!string.IsNullOrEmpty(member.CharacterName))
            {
                _membersByName[member.CharacterName] = member;
            }

            Gather.OnMemberConnected(member);
        }

        /// <summary> 연결이 끊겼다. 방에서 빼고, 성장, 생활 대기를 지우고, 채집 선점은 해제 시간을 재기 시작한다 </summary>
        public void RemoveMember(int participantId)
        {
            if (!_members.TryGetValue(participantId, out TownMember member))
            {
                return;
            }

            Party.OnMemberDisconnected(participantId);
            Growth.RemoveMember(participantId);
            Gather.OnMemberDisconnected(member);
            Potions.RemoveMember(participantId);
            LifeSnapshot.RemoveMember(participantId);
            IntroProgress.RemoveMember(participantId);
            _members.Remove(participantId);
            if ((!string.IsNullOrEmpty(member.CharacterName))
                && (_membersByName.TryGetValue(member.CharacterName, out TownMember named)) && (named == member))
            {
                _membersByName.Remove(member.CharacterName);
            }
        }

        public void HandleParty(TownMember member, in PartyRequestMessage message)
        {
            if (!member.TryAcceptRequest(ETownChannel.Party, _clock()))
            {
                _audience.Send(member.ParticipantId, new PartyResultMessage(message.Action, ERejectReason.RateLimited));
                return;
            }

            Party.Handle(member, in message);
        }

        public void HandleGrowth(TownMember member, in GrowthRequestMessage message)
        {
            Growth.Handle(member, in message);
        }

        public void HandleLife(TownMember member, in LifeRequestMessage message)
        {
            if (!member.TryAcceptRequest(ETownChannel.Life, _clock()))
            {
                Reply(member, in message, ERejectReason.RateLimited);
                return;
            }

            ERejectReason reason;
            switch (message.Action)
            {
                case ELifeAction.Snapshot:
                    reason = LifeSnapshot.Request(member);
                    if (reason != ERejectReason.None)
                    {
                        Reply(member, in message, reason);
                    }
                    return;
                case ELifeAction.GatherNodes:
                    Gather.SendActiveNodes(member.ParticipantId);
                    return;
                case ELifeAction.GatherStart:
                    Reply(member, in message, Gather.Start(member, message.Zone, message.Node));
                    return;
                case ELifeAction.GatherCancel:
                    Reply(member, in message, Gather.Cancel(member));
                    return;
                case ELifeAction.PotionCraft:
                    reason = Potions.Request(member, in message);
                    if (reason != ERejectReason.None)
                    {
                        Reply(member, in message, reason);
                    }
                    return;
                default:
                    Reply(member, in message, ERejectReason.InvalidTarget);
                    return;
            }
        }

        /// <summary> 신시 안내 완료 기록. 성장 요청과 같은 속도 제한 창구를 쓴다 </summary>
        public void HandleIntro(TownMember member, in IntroCompleteRequestMessage message)
        {
            ERejectReason reason = member.TryAcceptRequest(ETownChannel.Growth, _clock())
                ? IntroProgress.Request(member, in message)
                : ERejectReason.RateLimited;
            if (reason != ERejectReason.None)
            {
                _audience.Send(member.ParticipantId, new IntroCompleteResultMessage(message.OperationId, reason));
            }
        }

        /// <summary> 매 프레임 부른다 </summary>
        public void Tick()
        {
            Party.Tick();
            Gather.Tick();
        }

        // 채집, 제작이 확정되면 바뀐 재고를 소유자에게 보낸다
        private void RefreshLife(int participantId)
        {
            if (_members.TryGetValue(participantId, out TownMember member))
            {
                LifeSnapshot.Request(member);
            }
        }

        private void Reply(TownMember member, in LifeRequestMessage message, ERejectReason reason)
        {
            _audience.Send(member.ParticipantId, new LifeResultMessage(message.OperationId, message.Action, reason, message.Zone,
                message.Node));
        }
    }
}
