// System
using System;
using System.Collections.Generic;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 출정 예약의 해금 최종 판정. 마을 서버의 준비 판정과 같은 콘텐츠 요구 조건(ProgressionRules)을 쓴다 </summary>
    public sealed class SortieUnlockPolicy
    {
        /****************************************
        *              Properties
        ****************************************/

        public GrowthContent         Content { get; }
        public EGrowthContentProfile Profile { get; }

        // 출시 프로필이면 참이다. 성장 콘텐츠에 없는 battleId(테스트용 지도, 빈 값)의 출정을 열지 않는다. 일반전 웨이브 구현체가
        // 없는 지도는 종료 조건이 없어 출정이 끝나지 않으므로 출시 빌드에서 막는다. 개발, 테스트는 거짓으로 기존처럼 판정만 생략한다
        public bool RejectUnknownBattles { get; }

        /****************************************
        *                Methods
        ****************************************/

        public SortieUnlockPolicy(GrowthContent content, EGrowthContentProfile profile, bool rejectUnknownBattles = false)
        {
            ArgumentNullException.ThrowIfNull(content);
            Content              = content;
            Profile              = profile;
            RejectUnknownBattles = rejectUnknownBattles;
        }

        /// <summary> 출시 프로필에서 성장 콘텐츠에 없는 battleId(빈 값 포함)면 INVALID_TARGET, 아니면 NONE </summary>
        public int CheckKnownBattle(int mode, string battleId)
        {
            return (RejectUnknownBattles && (TargetOf(mode, battleId) == null))
                ? ContractCodes.RejectReason.INVALID_TARGET
                : ContractCodes.RejectReason.NONE;
        }

        /// <summary> 판정할 대상 ID. 일반전은 battleId가 지역 ID, 보스전은 보스전 ID다. 판정 대상이 아니면 null </summary>
        public string? TargetOf(int mode, string battleId)
        {
            if (string.IsNullOrEmpty(battleId))
            {
                return null;
            }

            return mode switch
            {
                GameSessionCodes.Mode.NORMAL when ProgressionRules.IsKnownTarget(Content,
                    EAccountProgressKind.RegionUnlocked, battleId) => battleId,
                GameSessionCodes.Mode.BOSS when ProgressionRules.IsKnownTarget(Content,
                    EAccountProgressKind.BattleUnlocked, battleId) => battleId,
                _ => null
            };
        }

        /// <summary> 참가자 전원이 대상을 열었는지. 한 명이라도 못 열었으면 MISSING_UNLOCK, 아니면 NONE.
        /// battleId가 비었거나 성장 콘텐츠에 없는 ID면 판정하지 않는다(0012 전 계약 없는 요청과 개발용 지도) </summary>
        public int Check(int mode, string battleId, IEnumerable<AccountProgressState> participants)
        {
            ArgumentNullException.ThrowIfNull(participants);
            string? target = TargetOf(mode, battleId);
            if (target == null)
            {
                return ContractCodes.RejectReason.NONE;
            }

            foreach (AccountProgressState state in participants)
            {
                bool? unlocked = (mode == GameSessionCodes.Mode.BOSS)
                    ? ProgressionRules.IsBattleUnlocked(Content, Profile, state, target)
                    : ProgressionRules.IsRegionUnlocked(Content, Profile, state, target);
                if (unlocked != true)
                {
                    return ContractCodes.RejectReason.MISSING_UNLOCK;
                }
            }

            return ContractCodes.RejectReason.NONE;
        }
    }
}
