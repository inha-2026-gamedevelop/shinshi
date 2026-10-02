// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 숫자 코드를 계약 상수 이름으로 바꾼다. 상수를 반사로 읽어 계약에 코드가 늘어도 이 표를 고치지 않는다 </summary>
    internal static class ContractCodeNames
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string UNKNOWN = "UNKNOWN";

        private static readonly Dictionary<int, string> G_RejectReasons =
            Build(typeof(ContractCodes.RejectReason), typeof(AdminContractCodes.RejectReason));
        private static readonly Dictionary<int, string> G_EconomyReasons = Build(typeof(ContractCodes.EconomyReason));
        private static readonly Dictionary<int, string> G_OperationStatuses = Build(typeof(ContractCodes.OperationStatus));

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 게임 거절 코드와 어드민 거절 코드(9000번대)를 함께 찾는다. 모르는 코드는 UNKNOWN </summary>
        public static string RejectReason(int code)
        {
            return G_RejectReasons.TryGetValue(code, out string? name) ? name : UNKNOWN;
        }

        public static string EconomyReason(int code)
        {
            return G_EconomyReasons.TryGetValue(code, out string? name) ? name : UNKNOWN;
        }

        public static string OperationStatus(int code)
        {
            return G_OperationStatuses.TryGetValue(code, out string? name) ? name : UNKNOWN;
        }

        /// <summary> 숫자나 상수 이름(대소문자 무시)으로 원장 사유 코드를 찾는다 </summary>
        public static bool TryParseEconomyReason(string text, out int code)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
            {
                return true;
            }

            foreach (KeyValuePair<int, string> pair in G_EconomyReasons)
            {
                if (string.Equals(pair.Value, text, StringComparison.OrdinalIgnoreCase))
                {
                    code = pair.Key;
                    return true;
                }
            }

            code = 0;
            return false;
        }

        public static string Role(int role)
        {
            return role switch
            {
                AdminContractCodes.Role.VIEWER   => "Viewer",
                AdminContractCodes.Role.OPERATOR => "Operator",
                AdminContractCodes.Role.ADMIN    => "Admin",
                AdminContractCodes.Role.OWNER    => "Owner",
                _ => UNKNOWN
            };
        }

        /// <summary> 역할 이름(대소문자 무시)이나 숫자를 AdminContractCodes.Role 값으로 바꾼다 </summary>
        public static bool TryParseRole(string text, out int role)
        {
            for (int candidate = AdminContractCodes.Role.VIEWER; candidate <= AdminContractCodes.Role.OWNER; ++candidate)
            {
                if (string.Equals(Role(candidate), text, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.ToString(CultureInfo.InvariantCulture), text, StringComparison.Ordinal))
                {
                    role = candidate;
                    return true;
                }
            }

            role = 0;
            return false;
        }

        // 같은 숫자가 두 번 나오면 먼저 읽은 이름을 남긴다. 게임과 어드민 대역이 겹치지 않는지는 테스트가 확인한다
        private static Dictionary<int, string> Build(params Type[] types)
        {
            Dictionary<int, string> names = new Dictionary<int, string>();
            foreach (Type type in types)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if ((!field.IsLiteral) || (field.FieldType != typeof(int)))
                    {
                        continue;
                    }

                    int value = (int)field.GetRawConstantValue()!;
                    names.TryAdd(value, field.Name);
                }
            }

            return names;
        }
    }
}
