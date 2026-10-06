// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 클라이언트 성장 요청을 API 경로와 본문으로 옮기는 순수 계산, 형식이 틀리면 InvalidTarget </summary>
    public static class GrowthRequestRules
    {
        // 장비 ID는 경로에 들어가므로 GUID 형식만 받고 하이픈 형식으로 다시 쓴다. 문자열 인자는 API 검증과 같은 64자 상한이다.
        // 대상 캐릭터, 기대 revision, 확률 판정 값은 서버가 넣는다. 클라이언트 메시지에는 그 자리가 없다

        /****************************************
        *                Fields
        ****************************************/

        private const string GUID_FORMAT = "D";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 확률 판정을 쓰는 작업인지(부적 등록, 장비 강화) </summary>
        public static bool UsesRoll(EGrowthAction action)
        {
            return (action == EGrowthAction.TalismanRegister) || (action == EGrowthAction.EquipmentEnhance);
        }

        /// <summary> 조회 경로 </summary>
        public static string SnapshotPath(string characterId)
        {
            return string.Format(CultureInfo.InvariantCulture, GrowthRoutes.GROWTH_FORMAT, characterId);
        }

        /// <summary> 쓰기 작업의 경로와 본문을 만든다, 형식이 틀리면 InvalidTarget. 수련 단계는 trainingTotalCap 이하만 받는다 </summary>
        public static ERejectReason TryBuild(in GrowthRequestMessage message, string characterId, long expectedRevision, int roll,
            int trainingTotalCap, out string path, out object body)
        {
            path = null;
            body = null;
            if ((string.IsNullOrEmpty(characterId)) || (message.OperationId == Guid.Empty) || (expectedRevision < 0))
            {
                return ERejectReason.InvalidTarget;
            }

            if ((roll < 0) || (roll > GrowthCodes.MAX_ROLL_BASIS_POINTS))
            {
                return ERejectReason.InvalidTarget;
            }

            string operationId = message.OperationId.ToString(GUID_FORMAT);
            string itemId;
            switch (message.Action)
            {
                case EGrowthAction.TalismanRegister:
                    if (!IsShortId(message.TargetId))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    path = Format(GrowthRoutes.TALISMAN_REGISTER_FORMAT, characterId);
                    body = new TalismanRegisterRequest(operationId, expectedRevision, message.TargetId, roll);
                    return ERejectReason.None;
                case EGrowthAction.TalismanUpgrade:
                    if (!IsShortId(message.TargetId))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    path = Format(GrowthRoutes.TALISMAN_UPGRADE_FORMAT, characterId);
                    body = new TalismanUpgradeRequest(operationId, expectedRevision, message.TargetId);
                    return ERejectReason.None;
                case EGrowthAction.TalismanLoadout:
                    if (!TryConvertLoadout(message.Loadout, out TalismanLoadoutEntry[] entries))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    path = Format(GrowthRoutes.TALISMAN_LOADOUT_FORMAT, characterId);
                    body = new TalismanLoadoutRequest(operationId, expectedRevision, entries);
                    return ERejectReason.None;
                case EGrowthAction.TalismanDisassemble:
                    if ((!IsShortId(message.TargetId)) || (message.IntArg <= 0))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    path = Format(GrowthRoutes.TALISMAN_DISASSEMBLE_FORMAT, characterId);
                    body = new TalismanDisassembleRequest(operationId, expectedRevision, message.TargetId, message.IntArg);
                    return ERejectReason.None;
                case EGrowthAction.Training:
                    // 단계 수는 한 번에 총 상한을 넘을 수 없다. API는 현재 투자량과 long으로 더해 다시 판정한다
                    if ((message.IntArg < 0) || (message.IntArg >= Constants.Town.TRAINING_STAT_COUNT) || (message.IntArg2 <= 0)
                        || ((long)message.IntArg2 > trainingTotalCap))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    path = Format(GrowthRoutes.TRAINING_FORMAT, characterId);
                    body = new TrainingRequest(operationId, expectedRevision, message.IntArg, message.IntArg2);
                    return ERejectReason.None;
                case EGrowthAction.TrainingRespec:
                    if (!IsValidAllocation(message.Allocation))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    path = Format(GrowthRoutes.TRAINING_RESPEC_FORMAT, characterId);
                    body = new TrainingRespecRequest(operationId, expectedRevision, (int[])message.Allocation.Clone());
                    return ERejectReason.None;
                case EGrowthAction.EquipmentCraft:
                    if (!IsShortId(message.TargetId))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    path = Format(GrowthRoutes.EQUIPMENT_CRAFT_FORMAT, characterId);
                    body = new EquipmentCraftRequest(operationId, expectedRevision, message.TargetId);
                    return ERejectReason.None;
                case EGrowthAction.EquipmentInherit:
                    if ((!TryNormalizeItemId(message.TargetId, out string sourceId))
                        || (!TryNormalizeItemId(message.SecondaryId, out string targetId)) || (sourceId == targetId))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    path = Format(GrowthRoutes.EQUIPMENT_INHERIT_FORMAT, characterId);
                    body = new EquipmentInheritRequest(operationId, expectedRevision, sourceId, targetId);
                    return ERejectReason.None;
                case EGrowthAction.EquipmentEnhance:
                    if (!TryNormalizeItemId(message.TargetId, out itemId))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    path = Format(GrowthRoutes.EQUIPMENT_ENHANCE_FORMAT, characterId, itemId);
                    body = new EquipmentEnhanceRequest(operationId, expectedRevision, roll);
                    return ERejectReason.None;
                case EGrowthAction.EquipmentEquip:
                case EGrowthAction.EquipmentUnequip:
                case EGrowthAction.EquipmentDismantle:
                    if (!TryNormalizeItemId(message.TargetId, out itemId))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    path = Format(ActionRoute(message.Action), characterId, itemId);
                    body = new EquipmentActionRequest(operationId, expectedRevision);
                    return ERejectReason.None;
                case EGrowthAction.EquipmentLock:
                    if (!TryNormalizeItemId(message.TargetId, out itemId))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    path = Format(GrowthRoutes.EQUIPMENT_LOCK_FORMAT, characterId, itemId);
                    body = new EquipmentLockRequest(operationId, expectedRevision, message.Flag);
                    return ERejectReason.None;
                default:
                    return ERejectReason.InvalidTarget;
            }
        }

        private static string ActionRoute(EGrowthAction action)
        {
            switch (action)
            {
                case EGrowthAction.EquipmentEquip:
                    return GrowthRoutes.EQUIPMENT_EQUIP_FORMAT;
                case EGrowthAction.EquipmentUnequip:
                    return GrowthRoutes.EQUIPMENT_UNEQUIP_FORMAT;
                default:
                    return GrowthRoutes.EQUIPMENT_DISMANTLE_FORMAT;
            }
        }

        private static string Format(string format, string characterId)
        {
            return string.Format(CultureInfo.InvariantCulture, format, characterId);
        }

        private static string Format(string format, string characterId, string itemId)
        {
            return string.Format(CultureInfo.InvariantCulture, format, characterId, itemId);
        }

        private static bool IsShortId(string value)
        {
            if ((string.IsNullOrEmpty(value)) || (value.Length > Constants.Town.MAX_GROWTH_ID_LENGTH))
            {
                return false;
            }

            for (int i = 0; i < value.Length; ++i)
            {
                if ((char.IsWhiteSpace(value[i])) || (char.IsControl(value[i])))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryNormalizeItemId(string value, out string itemId)
        {
            itemId = null;
            if ((string.IsNullOrEmpty(value)) || (value.Length > Constants.Town.MAX_GROWTH_ID_LENGTH)
                || (!Guid.TryParse(value, out Guid parsed)) || (parsed == Guid.Empty))
            {
                return false;
            }

            itemId = parsed.ToString(GUID_FORMAT);
            return true;
        }

        private static bool IsValidAllocation(int[] allocation)
        {
            if ((allocation == null) || (allocation.Length != Constants.Town.TRAINING_STAT_COUNT))
            {
                return false;
            }

            for (int i = 0; i < allocation.Length; ++i)
            {
                if (allocation[i] < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryConvertLoadout(TownLoadoutEntry[] source, out TalismanLoadoutEntry[] entries)
        {
            entries = null;
            if ((source == null) || (source.Length > GrowthCodes.MAX_LOADOUT_ENTRIES))
            {
                return false;
            }

            var converted = new TalismanLoadoutEntry[source.Length];
            for (int i = 0; i < source.Length; ++i)
            {
                if ((!IsShortId(source[i].SkillId)) || (!IsShortId(source[i].EffectId)) || (source[i].Tier < 0))
                {
                    return false;
                }

                converted[i] = new TalismanLoadoutEntry(source[i].SkillId, source[i].Tier, source[i].EffectId);
            }

            entries = converted;
            return true;
        }
    }
}
