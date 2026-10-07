// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 1:1 거래 한 건의 상태 기계, 시간과 위치는 호출자가 넣는다. 메인 스레드에서만 부른다 </summary>
    public sealed class TradeSession
    {
        // 상태: Editing -> Locked(한쪽 확정) -> Countdown(양쪽 확정, 대기) -> Accepting(수락 받음) -> Committing -> Done / Failed / Cancelled.
        // 제안이 바뀌면 offerRevision과 confirmGeneration이 오르고 양쪽 확정과 수락이 풀린다. 확정 풀기도 confirmGeneration을 올린다.
        // Lock과 Accept는 호출자가 본 두 값이 지금과 다르면 StaleRevision으로 무시한다(늦은 수락이 바뀐 제안을 승인하지 못하게).
        // Committing부터는 제안 변경, 확정 풀기, 취소를 막는다. 실행이 확정 거절이면 Editing으로 돌아가고, 다음 실행은 새 executionId를 쓴다.
        // 쪽 번호: 0이 요청한 사람, 1이 받은 사람

        /****************************************
        *                Fields
        ****************************************/

        public const int SIDE_COUNT = 2;

        private static readonly TradeOfferLine[] G_NoLines = new TradeOfferLine[0];

        private readonly TownMember[]       _members = new TownMember[SIDE_COUNT];
        private readonly TradeOfferLine[][] _lines   = { G_NoLines, G_NoLines };
        private readonly long[]             _currency = new long[SIDE_COUNT];
        private readonly bool[]             _locked   = new bool[SIDE_COUNT];
        private readonly bool[]             _accepted = new bool[SIDE_COUNT];
        private readonly bool[]             _connected = { true, true };

        private ETradeState _state;
        private double      _countdownEndsAt;
        private double      _lastActivityAt;

        /****************************************
        *              Properties
        ****************************************/

        public Guid         TradeId           { get; }
        public ETradeState  State             => _state;
        public int          OfferRevision     { get; private set; }
        public int          ConfirmGeneration { get; private set; }

        // 이 거래의 현재 실행 시도. 양쪽 수락마다 새로 만들고 확정 거절 뒤에는 새로 만든다
        public Guid         ExecutionId       { get; private set; }

        // commit 호출의 operationId, 실행 시도와 함께 만든다
        public Guid         CommitOperationId { get; private set; }

        // 상태가 이렇게 된 이유(취소 사유, 확정 거절로 편집에 돌아온 사유), 평소에는 None
        public ERejectReason LastReason       { get; private set; }

        // 실행(prepare, commit, resolve) 진행 표. 상태 기계는 건드리지 않고 TownTradeDirector가 쓴다
        public ETradeCommitPhase CommitPhase  { get; set; }
        public int               PendingRequestId { get; set; }
        public double            NextActionAt { get; set; }
        public double            CommitStartedAt { get; set; }
        public int               ResolveAttempts { get; set; }

        // resolve가 연달아 실패한 횟수와, 결과를 모르는 prepare를 포기하고 한 번 물어보는 중인지
        public int               ResolveFailures { get; set; }
        public bool              PrepareGaveUp { get; set; }

        public bool IsFinished => (_state == ETradeState.Done) || (_state == ETradeState.Failed) || (_state == ETradeState.Cancelled);
        public bool IsCommitting => _state == ETradeState.Committing;

        /****************************************
        *                Methods
        ****************************************/

        public TradeSession(Guid tradeId, TownMember requester, TownMember target, double now)
        {
            if ((requester == null) || (target == null))
            {
                throw new ArgumentNullException(nameof(requester));
            }

            TradeId         = tradeId;
            _members[0]     = requester;
            _members[1]     = target;
            _state          = ETradeState.Editing;
            _lastActivityAt = now;
        }

        public TownMember MemberAt(int side)
        {
            return _members[side];
        }

        /// <summary> 참가자 번호의 쪽, 이 거래의 참가자가 아니면 -1 </summary>
        public int SideOf(int participantId)
        {
            if (_members[0].ParticipantId == participantId)
            {
                return 0;
            }

            return (_members[1].ParticipantId == participantId) ? 1 : -1;
        }

        public static int Other(int side)
        {
            return 1 - side;
        }

        public TradeOfferLine[] LinesOf(int side)    { return _lines[side]; }
        public long             CurrencyOf(int side) { return _currency[side]; }
        public bool             IsLocked(int side)   { return _locked[side]; }
        public bool             IsAccepted(int side) { return _accepted[side]; }
        public bool             IsConnected(int side) { return _connected[side]; }
        public void             SetConnected(int side, bool connected) { _connected[side] = connected; }

        /// <summary> 내 제안을 통째로 바꾼다. 양쪽 확정과 수락이 풀리고 두 세대 값이 오른다 </summary>
        public ERejectReason SetOffer(int side, TradeOfferLine[] lines, long currency, double now)
        {
            ERejectReason phase = CheckEditable();
            if (phase != ERejectReason.None)
            {
                return phase;
            }

            _lines[side]    = (lines == null) ? G_NoLines : (TradeOfferLine[])lines.Clone();
            _currency[side] = currency;
            ++OfferRevision;
            ResetConfirmation();
            LastReason      = ERejectReason.None;
            _lastActivityAt = now;
            return ERejectReason.None;
        }

        /// <summary> 내 제안을 확정한다. 두 값이 지금과 다르면 StaleRevision, 양쪽이 확정하면 대기가 시작된다 </summary>
        public ERejectReason Lock(int side, int offerRevision, int confirmGeneration, double now)
        {
            ERejectReason phase = CheckEditable();
            if (phase != ERejectReason.None)
            {
                return phase;
            }

            if ((offerRevision != OfferRevision) || (confirmGeneration != ConfirmGeneration))
            {
                return ERejectReason.StaleRevision;
            }

            // 이미 확정했으면 그대로 둔다. 다시 눌러도 대기 시간이 새로 시작되지 않는다
            if (_locked[side])
            {
                return ERejectReason.None;
            }

            _locked[side]   = true;
            LastReason      = ERejectReason.None;
            _lastActivityAt = now;
            if ((_locked[0]) && (_locked[1]))
            {
                _state           = ETradeState.Countdown;
                _countdownEndsAt = now + TradeSettings.COUNTDOWN_SECONDS;
            }
            else
            {
                _state = ETradeState.Locked;
            }

            return ERejectReason.None;
        }

        /// <summary> 내 확정을 푼다. 양쪽 수락이 풀리고 확정 세대 값이 오른다. 확정하지 않았으면 아무 일도 없다 </summary>
        public ERejectReason Unlock(int side, double now)
        {
            ERejectReason phase = CheckEditable();
            if (phase != ERejectReason.None)
            {
                return phase;
            }

            if (!_locked[side])
            {
                return ERejectReason.None;
            }

            bool otherLocked = _locked[Other(side)];
            ResetConfirmation();
            if (otherLocked)
            {
                _locked[Other(side)] = true;
                _state               = ETradeState.Locked;
            }

            _lastActivityAt = now;
            return ERejectReason.None;
        }

        /// <summary> 수락한다. 대기가 끝나 수락을 받는 중이고 두 값이 지금과 같을 때만 받는다. 양쪽이 수락하면 Committing이 되고 참 </summary>
        public ERejectReason Accept(int side, int offerRevision, int confirmGeneration, double now, out bool readyToCommit)
        {
            readyToCommit = false;
            ERejectReason phase = CheckEditable();
            if (phase != ERejectReason.None)
            {
                return phase;
            }

            if ((offerRevision != OfferRevision) || (confirmGeneration != ConfirmGeneration))
            {
                return ERejectReason.StaleRevision;
            }

            if (_state != ETradeState.Accepting)
            {
                return ERejectReason.TooEarly;
            }

            _accepted[side] = true;
            _lastActivityAt = now;
            if ((_accepted[0]) && (_accepted[1]))
            {
                BeginCommit();
                readyToCommit = true;
            }

            return ERejectReason.None;
        }

        /// <summary> 시간이 지나 대기가 끝났으면 수락을 받기 시작한다, 상태가 바뀌었으면 참 </summary>
        public bool Tick(double now)
        {
            if ((_state == ETradeState.Countdown) && (now >= _countdownEndsAt))
            {
                _state = ETradeState.Accepting;
                return true;
            }

            return false;
        }

        /// <summary> 편집 중 마지막 변화로부터 5분이 지났는지, Committing 이후와 끝난 거래는 지나지 않은 것으로 본다 </summary>
        public bool IsIdleExpired(double now)
        {
            if ((_state == ETradeState.Committing) || (IsFinished))
            {
                return false;
            }

            return (now - _lastActivityAt) >= TradeSettings.IDLE_CANCEL_SECONDS;
        }

        /// <summary> 거래를 취소한다. Committing 중에는 거짓이고 아무것도 바꾸지 않는다 </summary>
        public bool TryCancel(ERejectReason reason)
        {
            if ((_state == ETradeState.Committing) || (IsFinished))
            {
                return false;
            }

            _state     = ETradeState.Cancelled;
            LastReason = reason;
            ResetConfirmation();
            return true;
        }

        /// <summary> 실행이 확정 거절이다. 편집으로 돌아가고 양쪽 확정과 수락이 풀린다. 다음 실행은 새 executionId를 쓴다 </summary>
        public void CompleteRejected(ERejectReason reason, double now)
        {
            if (_state != ETradeState.Committing)
            {
                return;
            }

            ResetConfirmation();
            ExecutionId       = Guid.Empty;
            CommitOperationId = Guid.Empty;
            LastReason        = reason;
            _lastActivityAt   = now;
            ClearCommitProgress();
        }

        /// <summary> 실행이 확정됐다 </summary>
        public void CompleteDone()
        {
            if (_state != ETradeState.Committing)
            {
                return;
            }

            _state = ETradeState.Done;
            ClearCommitProgress();
        }

        /// <summary> 실행이 방치로 끝났거나 결과를 끝내 얻지 못해 거래를 마친다 </summary>
        public void CompleteFailed(ERejectReason reason)
        {
            if (_state != ETradeState.Committing)
            {
                return;
            }

            _state     = ETradeState.Failed;
            LastReason = reason;
            ClearCommitProgress();
        }

        /// <summary> side 기준(나, 상대)으로 상태 메시지를 만든다 </summary>
        public TradeStateMessage ToMessage(int side, double now)
        {
            int   other     = Other(side);
            float countdown = 0f;
            if (_state == ETradeState.Countdown)
            {
                countdown = (float)Math.Max(0.0, _countdownEndsAt - now);
            }

            return new TradeStateMessage(TradeId, _state, LastReason, _members[other].CharacterName, _lines[side], _lines[other],
                _currency[side], _currency[other], _locked[side], _locked[other], _accepted[side], _accepted[other], OfferRevision,
                ConfirmGeneration, countdown);
        }

        private ERejectReason CheckEditable()
        {
            if (_state == ETradeState.Committing)
            {
                return ERejectReason.PendingCommit;
            }

            if (IsFinished)
            {
                return ERejectReason.WrongPhase;
            }

            return ERejectReason.None;
        }

        private void ResetConfirmation()
        {
            _locked[0]   = false;
            _locked[1]   = false;
            _accepted[0] = false;
            _accepted[1] = false;
            ++ConfirmGeneration;
            _countdownEndsAt = 0.0;
            if (!IsFinished)
            {
                _state = ETradeState.Editing;
            }
        }

        private void BeginCommit()
        {
            _state            = ETradeState.Committing;
            ExecutionId       = Guid.NewGuid();
            CommitOperationId = Guid.NewGuid();
            ClearCommitProgress();
        }

        private void ClearCommitProgress()
        {
            CommitPhase      = ETradeCommitPhase.None;
            PendingRequestId = 0;
            NextActionAt     = 0.0;
            ResolveAttempts  = 0;
            ResolveFailures  = 0;
            PrepareGaveUp    = false;
        }
    }
}
