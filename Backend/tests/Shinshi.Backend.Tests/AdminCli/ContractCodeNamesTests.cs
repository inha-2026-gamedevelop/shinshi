// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Third Party
using Xunit;

// Project
using Shinshi.AdminCli;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Tests.AdminCli
{
    /// <summary> 숫자 코드에서 계약 상수 이름 역조회 </summary>
    public sealed class ContractCodeNamesTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void RejectReason_EveryContractConstant_MapsBackToItsName()
        {
            List<FieldInfo> fields = Constants(typeof(ContractCodes.RejectReason))
                .Concat(Constants(typeof(AdminContractCodes.RejectReason))).ToList();

            // 게임과 어드민 대역이 겹치면 역조회가 한쪽 이름만 보여 주므로 값이 모두 달라야 한다
            Assert.Equal(fields.Count, fields.Select(field => (int)field.GetRawConstantValue()!).Distinct().Count());
            foreach (FieldInfo field in fields)
            {
                Assert.Equal(field.Name, ContractCodeNames.RejectReason((int)field.GetRawConstantValue()!));
            }
        }

        [Fact]
        public void RejectReason_KnownAndUnknownCodes_ReturnNames()
        {
            Assert.Equal("NOT_FOUND", ContractCodeNames.RejectReason(AdminContractCodes.RejectReason.NOT_FOUND));
            Assert.Equal("TOKEN_REVOKED", ContractCodeNames.RejectReason(ContractCodes.RejectReason.TOKEN_REVOKED));
            Assert.Equal("IDEMPOTENCY_CONFLICT",
                ContractCodeNames.RejectReason(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT));
            Assert.Equal("CHARACTER_NOT_FOUND", ContractCodeNames.RejectReason(1003));
            Assert.Equal("ROOM_NOT_FOUND", ContractCodeNames.RejectReason(1300));
            Assert.Equal("ROOM_PASSWORD_MISMATCH", ContractCodeNames.RejectReason(1301));
            Assert.Equal("INVITE_EXPIRED", ContractCodeNames.RejectReason(1302));
            Assert.Equal(ContractCodeNames.UNKNOWN, ContractCodeNames.RejectReason(-12345));
        }

        [Fact]
        public void EconomyReason_NameOrNumber_ParsesBothWays()
        {
            Assert.Equal("ADMIN_GRANT", ContractCodeNames.EconomyReason(ContractCodes.EconomyReason.ADMIN_GRANT));
            Assert.True(ContractCodeNames.TryParseEconomyReason("admin_revoke", out int byName));
            Assert.Equal(ContractCodes.EconomyReason.ADMIN_REVOKE, byName);
            Assert.True(ContractCodeNames.TryParseEconomyReason("30", out int byNumber));
            Assert.Equal(ContractCodes.EconomyReason.RECOVERY_COMPENSATION, byNumber);
            Assert.False(ContractCodeNames.TryParseEconomyReason("NOT_A_REASON", out _));
        }

        [Theory]
        [InlineData("viewer", AdminContractCodes.Role.VIEWER)]
        [InlineData("Operator", AdminContractCodes.Role.OPERATOR)]
        [InlineData("ADMIN", AdminContractCodes.Role.ADMIN)]
        [InlineData("3", AdminContractCodes.Role.OWNER)]
        public void TryParseRole_NameOrNumber_ReturnsRole(string text, int expected)
        {
            Assert.True(ContractCodeNames.TryParseRole(text, out int role));
            Assert.Equal(expected, role);
        }

        [Fact]
        public void TryParseRole_Unknown_ReturnsFalse()
        {
            Assert.False(ContractCodeNames.TryParseRole("root", out _));
            Assert.False(ContractCodeNames.TryParseRole("4", out _));
        }

        private static IEnumerable<FieldInfo> Constants(Type type)
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && (field.FieldType == typeof(int)));
        }
    }
}
