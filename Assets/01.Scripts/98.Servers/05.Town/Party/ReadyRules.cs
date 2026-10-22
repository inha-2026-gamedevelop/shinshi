// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 파티 방 생성, 준비, 출정 시작의 순수 판정 </summary>
    public static class ReadyRules
    {
        // 해금 판정은 두 단계다. 방을 만들 때(CheckContent)는 전장이 있는지와 체험판 포함 여부만 본다. 계정 진행(안내 완료,
        // 성공 기록, 여우의 흔적)은 참가자마다 다르므로 준비 확인 응답의 progress로 본인 해금을 판정한다(CheckProgress).
        // 예약 API가 같은 요구 조건으로 다시 판정해 최종 거절한다(MISSING_UNLOCK).
        // 안내 완료는 IntroCompleteRequestMessage -> IntroProgressRelay -> API로 기록한다. 클라이언트가 보내는 시점은
        // TODO(진욱/기웅) 프롤로그와 튜토리얼 구현이 정한다. 그 전까지 숲(region_forest)은
        // 기록이 없는 계정에게 열리지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // 진행 기록이 하나도 없는 계정과 같다. 판정 중에 바꾸지 않는다
        private static readonly ReadinessProgress _emptyProgress = new ReadinessProgress(false, 0, Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 방을 만들거나 준비할 수 있는 전장인지, 통과하면 출정 계약 값을 돌려준다 </summary>
        public static ERejectReason CheckContent(ProgressionCatalogSO catalog, EPartyMode mode, string contentId,
            out PartyContent content)
        {
            content = default;
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (string.IsNullOrEmpty(contentId))
            {
                return ERejectReason.InvalidTarget;
            }

            if (mode == EPartyMode.Normal)
            {
                if ((!catalog.TryGetRegion(contentId, out ProgressionCatalogSO.RegionDefinition region)) || (!region.HasWaveBattle))
                {
                    return ERejectReason.InvalidTarget;
                }

                content = new PartyContent((sbyte)GameSessionCodes.Difficulty.NONE, null, 0);
                return CheckEdition(catalog.TrialEdition, region.AvailableInTrial);
            }

            if (mode != EPartyMode.Boss)
            {
                return ERejectReason.InvalidTarget;
            }

            if (!catalog.TryGetBattle(contentId, out ProgressionCatalogSO.BossBattleDefinition battle))
            {
                return ERejectReason.InvalidTarget;
            }

            sbyte difficulty = (battle.Difficulty == EBossDifficulty.Hard)
                ? (sbyte)GameSessionCodes.Difficulty.HARD
                : (sbyte)GameSessionCodes.Difficulty.EASY;
            content = new PartyContent(difficulty, battle.BossId, battle.TicketCost);
            return CheckEdition(catalog.TrialEdition, battle.AvailableInTrial);
        }

        /// <summary> 준비 확인 응답의 계정 진행으로 이 참가자가 전장을 열었는지 본다. progress가 없는 응답(진행 필드 이전 API)은
        /// allowUnverifiedProgress가 참(개발 빌드)이면 통과시키고 아니면 빈 진행으로 판정한다 </summary>
        public static ERejectReason CheckProgress(ProgressionCatalogSO catalog, EPartyMode mode, string contentId,
            ReadinessProgress progress, bool allowUnverifiedProgress)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if ((progress == null) && (allowUnverifiedProgress))
            {
                return ERejectReason.None;
            }

            ReadinessProgress known = progress ?? _emptyProgress;
            bool unlocked = (mode == EPartyMode.Boss)
                ? SortieUnlockRules.IsBattleUnlocked(catalog, known, contentId)
                : SortieUnlockRules.IsRegionUnlocked(catalog, known, contentId);
            return unlocked ? ERejectReason.None : ERejectReason.MissingUnlock;
        }

        /// <summary> 준비 확인 응답의 한 참가자가 출정 조건(접속, 티켓, 지참 물약)을 갖췄는지, 통과하면 예약에 넣을 물약 배분 </summary>
        public static ERejectReason CheckStatus(ReadinessParticipantStatus status, int ticketCost, string smallPotionId,
            int smallPotions, string largePotionId, int largePotions, out ReservationPotion[] potions)
        {
            potions = null;
            if (status == null)
            {
                return ERejectReason.SessionMismatch;
            }

            if (status.ReasonCode != ContractCodes.RejectReason.NONE)
            {
                return TownRejectRules.FromApi(status.ReasonCode, ERejectReason.SessionMismatch);
            }

            if ((ticketCost > 0) && (status.AvailableTickets < ticketCost))
            {
                return ERejectReason.InsufficientTicket;
            }

            var picked = new List<ReservationPotion>();
            if ((!TryAllocatePotions(status.Potions, smallPotionId, smallPotions, picked))
                || (!TryAllocatePotions(status.Potions, largePotionId, largePotions, picked)))
            {
                return ERejectReason.InsufficientItem;
            }

            potions = picked.ToArray();
            return ERejectReason.None;
        }

        // 던전 PotionServer는 물약 정의마다 귀속 하나만 기억해 사용 이벤트에 싣는다. 여러 귀속으로 나눠 예약하면 두 번째 귀속분은
        // 쓸 수 없고 정산이 반환만 한다. 그래서 한 귀속이 수량을 모두 채울 때만 그 귀속 한 줄로 배정한다(가용 목록 순서의 첫 귀속).
        // TODO(민성): 귀속을 섞어 지참하는 것이 기획상 필요하면 PotionServer가 정의별 귀속 배열을 소비 순서대로 쓰게 바꾼다.
        // 지참 정책이 확정되지 않아 지금은 귀속 한 줄만 배정한다
        /// <summary> 가용 물약에서 한 종류를 수량만큼, 수량을 모두 채우는 한 귀속에서 담는다. 그런 귀속이 없으면 거짓 </summary>
        public static bool TryAllocatePotions(ReservationPotion[] available, string definitionId, int quantity,
            List<ReservationPotion> into)
        {
            if (quantity <= 0)
            {
                return quantity == 0;
            }

            if ((available == null) || (string.IsNullOrEmpty(definitionId)))
            {
                return false;
            }

            for (int i = 0; i < available.Length; ++i)
            {
                ReservationPotion entry = available[i];
                if ((entry == null) || (entry.Quantity < quantity)
                    || (!string.Equals(entry.DefinitionId, definitionId, StringComparison.Ordinal)))
                {
                    continue;
                }

                into.Add(new ReservationPotion(definitionId, entry.BindingKind, quantity));
                return true;
            }

            return false;
        }

        /// <summary> 지참 수량 범위, 0도 허용한다 </summary>
        public static bool IsValidPotionCount(int count)
        {
            return (count >= 0) && (count <= Constants.Town.MAX_POTION_CARRY);
        }

        /// <summary> 방장이 출정을 시작할 수 있는지 </summary>
        public static ERejectReason CanStart(PartyRoom room, int requesterId)
        {
            if (room == null)
            {
                return ERejectReason.RoomNotFound;
            }

            if (room.LeaderId != requesterId)
            {
                return ERejectReason.NotLeader;
            }

            if (room.Phase != EPartyPhase.Open)
            {
                return ERejectReason.WrongPhase;
            }

            if (!room.AllReady)
            {
                return ERejectReason.NotAllReady;
            }

            return ERejectReason.None;
        }

        private static ERejectReason CheckEdition(bool trialEdition, bool availableInTrial)
        {
            return ((trialEdition) && (!availableInTrial)) ? ERejectReason.MissingUnlock : ERejectReason.None;
        }
    }
}
