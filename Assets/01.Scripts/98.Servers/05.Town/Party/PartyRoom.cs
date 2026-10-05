// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 방 상태는 TownPartyDirector 하나만 바꾼다(메인 틱). 무엇이든 바뀌면 Touch로 revision을 올리고, 비동기 응답은
    // 보낼 때의 revision과 비교해 그 사이 방이 바뀌었으면 결과를 쓰지 않는다. 참가자는 입장 순서를 기억해 방장이 나가면
    // 가장 먼저 들어온 접속 참가자에게 넘긴다
    /// <summary> 마을 파티 방 하나, 참가자 최대 4명 </summary>
    public sealed class PartyRoom
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int CAPACITY = Constants.Session.MAX_PARTY_SIZE;

        private readonly int        _roomId;
        private readonly string     _name;
        private readonly EPartyMode _mode;
        private readonly string     _contentId;
        private readonly sbyte      _difficulty;
        private readonly string     _bossId;
        private readonly int        _ticketCost;
        private readonly string     _password;
        private readonly Slot[]     _slots = new Slot[CAPACITY];

        private int  _count         = 0;
        private long _revision      = 1;
        private long _nextJoinOrder = 0;
        private int  _leaderId      = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int         RoomId      => _roomId;
        public string      Name        => _name;
        public EPartyMode  Mode        => _mode;

        // 일반전은 지역 ID, 보스전은 보스전 정의 ID
        public string      ContentId   => _contentId;

        // GameSessionCodes.Difficulty, 일반전은 NONE
        public sbyte       Difficulty  => _difficulty;

        // 보스전의 보스 ID, 일반전은 null
        public string      BossId      => _bossId;

        // 보스전에서 참가자마다 쓰는 티켓 수, 일반전은 0
        public int         TicketCost  => _ticketCost;
        public bool        HasPassword => !string.IsNullOrEmpty(_password);
        public long        Revision    => _revision;
        public int         Count       => _count;
        public int         LeaderId    => _leaderId;
        public bool        IsFull      => _count >= CAPACITY;
        public bool        IsEmpty     => _count == 0;

        public EPartyPhase Phase       { get; set; } = EPartyPhase.Open;

        // 카운트다운 끝 시각(단조 초), Countdown 단계에서만 쓴다
        public double      CountdownEndsAt { get; set; }

        // 진행 중인 예약 작업. 대기열 재예약은 새 operationId, 응답 유실 재전송은 같은 operationId를 쓴다
        public string      ReservationOperationId { get; set; }
        public int         PendingRequestId       { get; set; }
        public int         Retries                { get; set; }
        public double      NextAttemptAt          { get; set; }

        // 예약이 확정된 출정 ID, 입장권 발급과 취소에 쓴다
        public string      SessionId              { get; set; }

        /// <summary> 참가자 전원이 Ready이고 한 명 이상인지 </summary>
        public bool AllReady
        {
            get
            {
                if (_count == 0)
                {
                    return false;
                }

                for (int i = 0; i < _count; ++i)
                {
                    if (_slots[i].Ready != EPartyReadyState.Ready)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public PartyRoom(int roomId, string name, EPartyMode mode, string contentId, sbyte difficulty, string bossId,
            int ticketCost, string password)
        {
            _roomId     = roomId;
            _name       = name;
            _mode       = mode;
            _contentId  = contentId;
            _difficulty = difficulty;
            _bossId     = bossId;
            _ticketCost = ticketCost;
            _password   = string.IsNullOrEmpty(password) ? null : password;
        }

        /// <summary> 방이 바뀌었다, 진행 중인 비동기 결과는 이 뒤로 쓰지 않는다 </summary>
        public void Touch()
        {
            ++_revision;
        }

        public bool PasswordMatches(string password)
        {
            if (_password == null)
            {
                return true;
            }

            return string.Equals(_password, password, StringComparison.Ordinal);
        }

        /// <summary> 참가자를 넣는다, 첫 참가자가 방장이다. 모집 단계가 아니거나 가득 찼거나 같은 계정이 있으면 거절 </summary>
        public ERejectReason TryAdd(TownMember member)
        {
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            if (Phase != EPartyPhase.Open)
            {
                return ERejectReason.WrongPhase;
            }

            if (IndexOf(member.ParticipantId) >= 0)
            {
                return ERejectReason.WrongPhase;
            }

            if (IsFull)
            {
                return ERejectReason.NoCapacity;
            }

            // 같은 계정의 두 캐릭터는 한 출정에 들어갈 수 없다(API 예약도 계정 중복을 형식 오류로 막는다)
            if ((!string.IsNullOrEmpty(member.AccountId)) && (IndexOfAccount(member.AccountId) >= 0))
            {
                return ERejectReason.AlreadyConnected;
            }

            _slots[_count] = new Slot(member, _nextJoinOrder);
            ++_nextJoinOrder;
            ++_count;
            if (_leaderId == 0)
            {
                _leaderId = member.ParticipantId;
            }

            Touch();
            return ERejectReason.None;
        }

        /// <summary> 참가자를 뺀다, 방장이었으면 가장 먼저 들어온 접속 참가자에게 넘긴다. 없던 참가자면 거짓 </summary>
        public bool Remove(int participantId)
        {
            int index = IndexOf(participantId);
            if (index < 0)
            {
                return false;
            }

            // 입장 순서를 지키도록 뒤를 당긴다
            for (int i = index; i < (_count - 1); ++i)
            {
                _slots[i] = _slots[i + 1];
            }

            --_count;
            _slots[_count] = default;
            if (_leaderId == participantId)
            {
                _leaderId = SelectSuccessor();
            }

            Touch();
            return true;
        }

        /// <summary> 가장 먼저 들어온 접속 참가자, 접속자가 없으면 가장 먼저 들어온 참가자, 비었으면 0 </summary>
        public int SelectSuccessor()
        {
            int  bestId        = 0;
            long bestOrder     = long.MaxValue;
            bool bestConnected = false;
            for (int i = 0; i < _count; ++i)
            {
                ref Slot slot = ref _slots[i];
                bool better = ((slot.Connected) && (!bestConnected))
                    || ((slot.Connected == bestConnected) && (slot.JoinOrder < bestOrder));
                if (better)
                {
                    bestId        = slot.Member.ParticipantId;
                    bestOrder     = slot.JoinOrder;
                    bestConnected = slot.Connected;
                }
            }

            return bestId;
        }

        /// <summary> 접속 표시를 바꾼다. 방장이 끊기면 접속 참가자에게 넘긴다 </summary>
        public bool SetConnected(int participantId, bool connected)
        {
            int index = IndexOf(participantId);
            if ((index < 0) || (_slots[index].Connected == connected))
            {
                return false;
            }

            _slots[index].Connected = connected;
            if ((!connected) && (_leaderId == participantId))
            {
                _leaderId = SelectSuccessor();
            }

            Touch();
            return true;
        }

        public int IndexOf(int participantId)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (_slots[i].Member.ParticipantId == participantId)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool Contains(int participantId)
        {
            return IndexOf(participantId) >= 0;
        }

        public TownMember MemberAt(int index)                 { return _slots[index].Member; }
        public bool IsConnectedAt(int index)                  { return _slots[index].Connected; }
        public EPartyReadyState ReadyAt(int index)            { return _slots[index].Ready; }
        public int SmallPotionsAt(int index)                  { return _slots[index].SmallPotions; }
        public int LargePotionsAt(int index)                  { return _slots[index].LargePotions; }
        public long CharacterRevisionAt(int index)            { return _slots[index].CharacterRevision; }
        public ReservationPotion[] ReservedPotionsAt(int index) { return _slots[index].Potions; }

        /// <summary> 준비 확인 요청을 보냈다, 응답은 같은 요청 번호일 때만 받는다 </summary>
        public void BeginCheck(int participantId, int requestId, int smallPotions, int largePotions)
        {
            int index = IndexOf(participantId);
            if (index < 0)
            {
                return;
            }

            ref Slot slot = ref _slots[index];
            slot.Ready          = EPartyReadyState.Checking;
            slot.CheckRequestId = requestId;
            slot.SmallPotions   = smallPotions;
            slot.LargePotions   = largePotions;
            slot.Potions        = null;
            Touch();
        }

        /// <summary> 이 참가자가 requestId의 준비 확인을 기다리는 중인지 </summary>
        public bool IsChecking(int participantId, int requestId)
        {
            int index = IndexOf(participantId);
            return (index >= 0) && (_slots[index].Ready == EPartyReadyState.Checking) && (_slots[index].CheckRequestId == requestId);
        }

        /// <summary> 준비 확인을 통과했다, 예약에 그대로 넣을 revision과 물약 배분을 남긴다 </summary>
        public void MarkReady(int participantId, long characterRevision, ReservationPotion[] potions)
        {
            int index = IndexOf(participantId);
            if (index < 0)
            {
                return;
            }

            ref Slot slot = ref _slots[index];
            slot.Ready             = EPartyReadyState.Ready;
            slot.CheckRequestId    = 0;
            slot.CharacterRevision = characterRevision;
            slot.Potions           = potions;
            Touch();
        }

        /// <summary> 준비를 푼다, 바뀌었으면 참 </summary>
        public bool ClearReady(int participantId)
        {
            int index = IndexOf(participantId);
            if ((index < 0) || (_slots[index].Ready == EPartyReadyState.NotReady))
            {
                return false;
            }

            ResetReady(ref _slots[index]);
            Touch();
            return true;
        }

        /// <summary> 전원 준비를 푼다, 예약이 확정 거절되면 준비 확인 값이 낡았으므로 부른다 </summary>
        public void ClearAllReady()
        {
            for (int i = 0; i < _count; ++i)
            {
                ResetReady(ref _slots[i]);
            }

            Touch();
        }

        /// <summary> 진행 중인 예약 작업 표시를 지운다 </summary>
        public void ClearPendingWork()
        {
            ReservationOperationId = null;
            PendingRequestId       = 0;
            Retries                = 0;
            NextAttemptAt          = 0.0;
        }

        public int CountReady()
        {
            int ready = 0;
            for (int i = 0; i < _count; ++i)
            {
                if (_slots[i].Ready == EPartyReadyState.Ready)
                {
                    ++ready;
                }
            }

            return ready;
        }

        public int CountClass(EPlayerClass playerClass)
        {
            int count = 0;
            for (int i = 0; i < _count; ++i)
            {
                if (_slots[i].Member.PlayerClass == playerClass)
                {
                    ++count;
                }
            }

            return count;
        }

        private int IndexOfAccount(string accountId)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (string.Equals(_slots[i].Member.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void ResetReady(ref Slot slot)
        {
            slot.Ready             = EPartyReadyState.NotReady;
            slot.CheckRequestId    = 0;
            slot.SmallPotions      = 0;
            slot.LargePotions      = 0;
            slot.CharacterRevision = 0;
            slot.Potions           = null;
        }

        /****************************************
        *               Records
        ****************************************/

        // 배열 안에서 ref로 제자리 수정하는 값 묶음이라 클래스 밖으로 내보내지 않는다
        private struct Slot
        {
            public TownMember          Member;
            public long                JoinOrder;
            public bool                Connected;
            public EPartyReadyState    Ready;
            public int                 CheckRequestId;
            public int                 SmallPotions;
            public int                 LargePotions;
            public long                CharacterRevision;
            public ReservationPotion[] Potions;

            public Slot(TownMember member, long joinOrder)
            {
                Member            = member;
                JoinOrder         = joinOrder;
                Connected         = true;
                Ready             = EPartyReadyState.NotReady;
                CheckRequestId    = 0;
                SmallPotions      = 0;
                LargePotions      = 0;
                CharacterRevision = 0;
                Potions           = null;
            }
        }
    }
}
