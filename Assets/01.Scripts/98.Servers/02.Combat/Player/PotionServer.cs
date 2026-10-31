// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 던전 출정의 물약 사용 판정, 저장 확정 뒤에만 회복하고 쿨다운을 시작한다 </summary>
    public sealed class PotionServer
    {
        // 검증 순서: 출정 진행 -> 생존 -> 제어(Disabled) -> 체력 가득(FullHealth) -> 쿨다운 -> 지참 잔량과 보스 횟수(PotionLimit)
        // -> 확정 대기 중(PendingCommit). 모두 통과해야 POTION_USED를 저장 큐에 넣고 그 사용을 대기로 둔다. 거절은 아무것도
        // 소모하지 않는다.
        //
        // 잔량과 보스 횟수는 반드시 API보다 먼저 막는다. /events 저장 거절(POTION_LIMIT)은 저장 큐를 영구 정지시키고 출정을
        // 장애 취소로 끝내므로, API가 거절할 수 있는 사용을 보내면 안 된다. 그래서 되돌림을 넣은 몫은 되돌림이 확정될 때까지
        // 다시 쓸 수 없게 잡아 둔다. 같은 배치에 사용과 되돌림이 섞이면 API가 사용을 먼저 세어 예약 수량을 넘길 수 있다.
        //
        // 저장이 확정되면(큐의 마지막 ACK 순번이 대기 순번 이상) 틱에서 생존과 출정 진행을 다시 보고 회복을 한 번 적용하고 공용
        // 쿨다운을 시작한다. 그 사이 죽었거나 개체가 지워졌으면 POTION_USE_REVERTED로 물약을 돌려준다.
        //
        // 출정이 끝나면 회복을 적용하지 못한 대기 사용(확정 여부와 무관)을 종료 이벤트보다 먼저 모두 되돌린다(RevertUnapplied).
        // 저장 큐는 순번 순서로만 보내므로 되돌림은 원 사용과 같은 배치이거나 그 뒤 배치에 실려 API가 받는다. 원 사용이 저장
        // 거절되면 큐가 멈춰 되돌림도 저장되지 않고 출정은 장애 취소 복구(전액 환급)로 끝난다. 정산은 예약에서 consumed만 소비로
        // 보므로 되돌리지 않으면 회복도 못 한 물약이 사라진다. 종료 이벤트 뒤에는 되돌림을 넣을 수 없어(SESSION_TERMINAL로 큐가
        // 멈춘다) 그때 넣지 못한 대기만 사용으로 남긴다.
        //
        // 상태는 참가자 번호로 프로세스 수명 동안 들고 있다. 던전 프로세스는 출정 하나만 맡으므로 재접속, 개체 재생성, 부활이
        // 있어도 잔량, 사용 횟수, 쿨다운이 그대로 이어진다(재접속 보존소에 따로 넣지 않는다)

        /****************************************
        *                Fields
        ****************************************/

        private const int    NO_PARTICIPANT   = 0;
        private const string PROLOGUE_BINDING = "Prologue";

        private struct Record
        {
            public int         ParticipantId;
            public string      ParticipantKey;
            public string      SmallDefinitionId;
            public string      LargeDefinitionId;
            public string      SmallBinding;
            public string      LargeBinding;
            public int         SmallStock;      // 확정 사용을 뺀 남은 예약분
            public int         LargeStock;
            public int         CommittedUses;   // 확정되고 되돌리지 않은 사용 수, 보스 상한에 센다
            public bool        HasPending;
            public EPotionSize PendingSize;
            public long        PendingSequence;
            public uint        PendingInput;    // 대기 중인 사용을 요청한 입력 순번
            public int         RevertingSmall;  // 되돌림 확정 전까지 다시 쓸 수 없게 잡아 둔 몫
            public int         RevertingLarge;
            public long        RevertSequence;  // 마지막으로 넣은 되돌림의 저장 순번
            public double      CooldownEndsAt;
        }

        private readonly PlayerConfigSO     _player;
        private readonly LifeConfigSO       _life;
        private readonly IPotionEventLedger _ledger;
        private readonly IPotionHost        _host;
        private readonly Record[]           _records;

        /****************************************
        *              Properties
        ****************************************/

        public int Capacity => _records.Length;

        /// <summary> 저장이 확정된 물약으로 체력이 실제 증가한 참가자를 통지한다 </summary>
        public event Action<int> PotionApplied;

        /****************************************
        *                Methods
        ****************************************/

        public PotionServer(PlayerConfigSO player, LifeConfigSO life, IPotionEventLedger ledger, IPotionHost host,
            int capacity)
        {
            if ((player == null) || (life == null) || (ledger == null) || (host == null))
            {
                throw new ArgumentNullException(nameof(player), "물약 판정의 설정과 저장 경로, 참가자 창구가 필요하다");
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "참가자 상한은 1 이상이다");
            }

            _player  = player;
            _life    = life;
            _ledger  = ledger;
            _host    = host;
            _records = new Record[capacity];
        }

        /// <summary> 참가자의 지참 물약을 처음 한 번 기록한다. 이미 있으면 재입장 값으로 덮지 않고 참, 자리가 없으면 거짓 </summary>
        public bool TryRegister(int participantId, string participantKey, ReservationPotion[] reserved)
        {
            if ((participantId == NO_PARTICIPANT) || (string.IsNullOrEmpty(participantKey)))
            {
                return false;
            }

            // 재입장 교환의 남은 수량은 확정된 사용만 뺀 값이다. 이 프로세스가 대기, 되돌림까지 더 정확히 알고 있으므로 첫 값만 쓴다
            if (IndexOf(participantId) >= 0)
            {
                return true;
            }

            int index = IndexOf(NO_PARTICIPANT);
            if (index < 0)
            {
                return false;
            }

            ref Record record = ref _records[index];
            record = new Record
            {
                ParticipantId     = participantId,
                ParticipantKey    = participantKey,
                SmallDefinitionId = _life.GetPotion(EPotionSize.Small).DefinitionId,
                LargeDefinitionId = _life.GetPotion(EPotionSize.Large).DefinitionId
            };

            int count = (reserved == null) ? 0 : reserved.Length;
            for (int i = 0; i < count; ++i)
            {
                ReservationPotion potion = reserved[i];
                if ((potion == null) || (potion.Quantity <= 0) || (string.IsNullOrEmpty(potion.BindingKind)))
                {
                    continue;
                }

                int quantity = (int)Math.Min(potion.Quantity, int.MaxValue);
                if ((record.SmallBinding == null) && (string.Equals(potion.DefinitionId, record.SmallDefinitionId, StringComparison.Ordinal)))
                {
                    record.SmallBinding = potion.BindingKind;
                    record.SmallStock   = quantity;
                }
                else if ((record.LargeBinding == null)
                    && (string.Equals(potion.DefinitionId, record.LargeDefinitionId, StringComparison.Ordinal)))
                {
                    record.LargeBinding = potion.BindingKind;
                    record.LargeStock   = quantity;
                }
                else
                {
                    // 같은 물약을 다른 귀속으로 둘 이상 예약했거나 모르는 정의다. 쓰지 않은 예약분은 정산이 반환한다
                    Debug.LogWarning($"[Potion] 쓰지 않는 지참 예약이다, participantId={participantId}, " +
                        $"definition={potion.DefinitionId}, binding={potion.BindingKind}");
                }
            }

            return true;
        }

        // 프롤로그 임시 물약 전용이다. 출정 예약과 무관한 별도 재고라 저장 이벤트를 만들지 않는 레저(ProloguePotionLedger)와 함께 쓴다.
        // 참가자가 없으면 만들고, 있으면 소형 재고만 이 수량으로 맞춘다. 이미 사용한 횟수와 쿨다운은 그대로 둔다
        /// <summary> 소형 물약 재고를 count로 맞춘다, 0이면 제거다. 자리가 없으면 거짓 </summary>
        public bool SetSmallStock(int participantId, string participantKey, int count)
        {
            if ((participantId == NO_PARTICIPANT) || (string.IsNullOrEmpty(participantKey)) || (count < 0))
            {
                return false;
            }

            int index = IndexOf(participantId);
            if (index < 0)
            {
                if (!TryRegister(participantId, participantKey, null))
                {
                    return false;
                }

                index = IndexOf(participantId);
            }

            ref Record record = ref _records[index];
            record.SmallBinding   = PROLOGUE_BINDING;
            record.SmallStock     = count;
            record.RevertingSmall = 0;
            _host.OnPotionState(participantId, 0, ERejectReason.None);
            return true;
        }

        /// <summary> 사용을 판정한다. None이면 저장 큐에 넣고 확정을 기다리며, 그 밖은 아무것도 바꾸지 않은 거절 사유다 </summary>
        public ERejectReason TryUse(IPotionUser user, EPotionSize size, uint inputSequence, double serverTime)
        {
            if ((user == null) || ((size != EPotionSize.Small) && (size != EPotionSize.Large)))
            {
                return ERejectReason.InvalidTarget;
            }

            if (!_ledger.IsSessionRunning)
            {
                return ERejectReason.WrongPhase;
            }

            if (user.LifeState != EParticipantLifeState.Alive)
            {
                return ERejectReason.Dead;
            }

            if (user.IsControlLocked)
            {
                return ERejectReason.Disabled;
            }

            if (user.Health >= user.MaxHealth)
            {
                return ERejectReason.FullHealth;
            }

            int index = IndexOf(user.ParticipantId);
            if (index < 0)
            {
                return ERejectReason.PotionLimit;
            }

            ref Record record = ref _records[index];
            if (serverTime < record.CooldownEndsAt)
            {
                return ERejectReason.Cooldown;
            }

            if ((Available(in record, size) <= 0) || (IsBossLimitReached(in record)))
            {
                return ERejectReason.PotionLimit;
            }

            if (record.HasPending)
            {
                return ERejectReason.PendingCommit;
            }

            string binding = (size == EPotionSize.Small) ? record.SmallBinding : record.LargeBinding;
            string definition = (size == EPotionSize.Small) ? record.SmallDefinitionId : record.LargeDefinitionId;
            if (!_ledger.TryRecordUse(record.ParticipantKey, definition, binding, serverTime, out long sequence))
            {
                return ERejectReason.BackendUnavailable;
            }

            record.HasPending      = true;
            record.PendingSize     = size;
            record.PendingSequence = sequence;
            record.PendingInput    = inputSequence;
            return ERejectReason.None;
        }

        /// <summary> 서버 틱에서 부른다, 저장이 확정된 사용을 회복으로 적용하거나 되돌리고 되돌림 확정을 반영한다 </summary>
        public void Tick(double serverTime)
        {
            for (int i = 0; i < _records.Length; ++i)
            {
                ref Record record = ref _records[i];
                if (record.ParticipantId == NO_PARTICIPANT)
                {
                    continue;
                }

                if ((record.HasPending) && (_ledger.IsCommitted(record.PendingSequence)))
                {
                    ResolvePending(ref record, serverTime);
                }

                if (((record.RevertingSmall + record.RevertingLarge) > 0) && (_ledger.IsCommitted(record.RevertSequence)))
                {
                    record.RevertingSmall = 0;
                    record.RevertingLarge = 0;
                    _host.OnPotionState(record.ParticipantId, 0, ERejectReason.None);
                }
            }
        }

        /// <summary> 출정 종료 판정 직후, 종료 이벤트를 넣기 전에 부른다. 회복을 적용하지 못한 대기 사용을 모두 되돌림으로 넣는다 </summary>
        public void RevertUnapplied(double serverTime)
        {
            for (int i = 0; i < _records.Length; ++i)
            {
                ref Record record = ref _records[i];
                if ((record.ParticipantId == NO_PARTICIPANT) || (!record.HasPending))
                {
                    continue;
                }

                if (!_ledger.TryRecordRevert(record.ParticipantKey, record.PendingSequence, serverTime, out long revertSequence))
                {
                    // 대기는 그대로 두고 확정되면 ResolvePending이 사용으로 남긴다. 종료 때 한 번이라 로그 문자열을 만든다
                    Debug.LogWarning($"[Potion] 출정 종료 전에 미적용 물약을 되돌리지 못했다, participantId={record.ParticipantId}");
                    continue;
                }

                record.HasPending = false;
                if (record.PendingSize == EPotionSize.Small)
                {
                    ++record.RevertingSmall;
                }
                else
                {
                    ++record.RevertingLarge;
                }

                record.RevertSequence = revertSequence;
                _host.OnPotionState(record.ParticipantId, record.PendingInput, ERejectReason.SessionTerminal);
            }
        }

        /// <summary> 소유자에게 보낼 상태, 등록되지 않은 참가자는 잔량 0이다 </summary>
        public PotionStateMessage CreateState(int participantId, uint sequence, ERejectReason reason)
        {
            int  index = IndexOf(participantId);
            byte limit = _ledger.IsBossSession ? ToByte(GameSessionCodes.Potion.BOSS_USE_LIMIT) : (byte)0;
            if (index < 0)
            {
                return new PotionStateMessage(sequence, reason, 0, 0, 0, limit, false, 0.0);
            }

            ref Record record = ref _records[index];
            return new PotionStateMessage(sequence, reason, ToUShort(Available(in record, EPotionSize.Small)),
                ToUShort(Available(in record, EPotionSize.Large)), ToByte(BossUses(in record)), limit, record.HasPending,
                record.CooldownEndsAt);
        }

        /// <summary> 참가자가 쓸 수 있는 남은 수, 등록되지 않았으면 0 </summary>
        public int GetAvailable(int participantId, EPotionSize size)
        {
            int index = IndexOf(participantId);
            return (index < 0) ? 0 : Available(in _records[index], size);
        }

        /// <summary> 보스 상한에 세는 사용 수, 확정 사용과 되돌림 대기를 합친다 </summary>
        public int GetBossUses(int participantId)
        {
            int index = IndexOf(participantId);
            return (index < 0) ? 0 : BossUses(in _records[index]);
        }

        public bool HasPending(int participantId)
        {
            int index = IndexOf(participantId);
            return (index >= 0) && (_records[index].HasPending);
        }

        public double GetCooldownEndsAt(int participantId)
        {
            int index = IndexOf(participantId);
            return (index < 0) ? 0.0 : _records[index].CooldownEndsAt;
        }

        // 확정 순간에 생존과 출정 진행을 다시 본다. 대기 중에 죽었으면 회복을 넣지 않고 되돌린다
        private void ResolvePending(ref Record record, double serverTime)
        {
            record.HasPending = false;
            EPotionSize size  = record.PendingSize;
            uint        input = record.PendingInput;
            IPotionUser user    = null;
            bool        canHeal = (_ledger.IsSessionRunning) && (_host.TryGetPotionUser(record.ParticipantId, out user))
                && (user != null) && (user.LifeState == EParticipantLifeState.Alive);
            if (canHeal)
            {
                float healthBefore = user.Health;
                user.Heal(user.MaxHealth * _life.GetPotion(size).HealRate);
                bool healed = user.Health > healthBefore;
                Consume(ref record, size);
                record.CooldownEndsAt = serverTime + _player.PotionSharedCooldown;
                _host.OnPotionState(record.ParticipantId, input, ERejectReason.None);
                if (healed)
                {
                    PotionApplied?.Invoke(record.ParticipantId);
                }

                return;
            }

            // 종료 이벤트 뒤에는 되돌림을 저장할 수 없다(API가 거절하면 큐가 멈춘다). 종료 직전 되돌림(RevertUnapplied)을
            // 넣지 못한 대기만 여기 온다. 사용으로 남기고 기록한다
            if ((!_ledger.IsSessionRunning)
                || (!_ledger.TryRecordRevert(record.ParticipantKey, record.PendingSequence, serverTime, out long revertSequence)))
            {
                Consume(ref record, size);
                Debug.LogWarning($"[Potion] 확정된 물약을 회복도 되돌림도 하지 못해 사용으로 남긴다, participantId={record.ParticipantId}, " +
                    $"size={size}, running={_ledger.IsSessionRunning}");
                _host.OnPotionState(record.ParticipantId, input, ERejectReason.Dead);
                return;
            }

            if (size == EPotionSize.Small)
            {
                ++record.RevertingSmall;
            }
            else
            {
                ++record.RevertingLarge;
            }

            record.RevertSequence = revertSequence;
            _host.OnPotionState(record.ParticipantId, input, ERejectReason.Dead);
        }

        private static void Consume(ref Record record, EPotionSize size)
        {
            if (size == EPotionSize.Small)
            {
                record.SmallStock = Math.Max(0, record.SmallStock - 1);
            }
            else
            {
                record.LargeStock = Math.Max(0, record.LargeStock - 1);
            }

            ++record.CommittedUses;
        }

        // 확정 대기 중인 사용은 빼지 않는다. 대기가 있으면 PendingCommit으로 막고, 대기가 끝나면 확정 결과로 다시 센다
        private static int Available(in Record record, EPotionSize size)
        {
            return (size == EPotionSize.Small)
                ? Math.Max(0, record.SmallStock - record.RevertingSmall)
                : Math.Max(0, record.LargeStock - record.RevertingLarge);
        }

        private static int BossUses(in Record record)
        {
            return record.CommittedUses + record.RevertingSmall + record.RevertingLarge + (record.HasPending ? 1 : 0);
        }

        // 확정 대기 중인 사용도 센다. 대기 중이면 어차피 PendingCommit이지만 상한 판정이 대기 결과에 따라 달라지지 않게 한다
        private bool IsBossLimitReached(in Record record)
        {
            return (_ledger.IsBossSession) && (BossUses(in record) >= GameSessionCodes.Potion.BOSS_USE_LIMIT);
        }

        private int IndexOf(int participantId)
        {
            for (int i = 0; i < _records.Length; ++i)
            {
                if (_records[i].ParticipantId == participantId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static ushort ToUShort(int value)
        {
            return (ushort)Mathf.Clamp(value, 0, ushort.MaxValue);
        }

        private static byte ToByte(int value)
        {
            return (byte)Mathf.Clamp(value, 0, byte.MaxValue);
        }
    }
}
