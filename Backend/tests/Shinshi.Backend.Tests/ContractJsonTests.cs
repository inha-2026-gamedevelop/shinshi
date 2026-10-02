using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

using Shinshi.Contracts;

namespace Shinshi.Backend.Tests
{
    /// <summary> Unity 공통 DTO의 private DataMember와 숫자 와이어 계약을 검사한다 </summary>
    public sealed class ContractJsonTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long EXACT_LONG_VALUE = 9007199254740993L;
        private const long MAX_LONG_VALUE   = long.MaxValue;
        private const int  TEST_PORT        = 7777;
        private const int  PROTOCOL_VERSION = 1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 상태 응답은 DataMember의 camelCase 키만 내보낸다 </summary>
        [Fact]
        public void Serialize_ServiceStatePrivateFields_UsesOnlyCamelCaseContractKeys()
        {
            ServiceStateResponse state = new ServiceStateResponse(true, "scheduled maintenance",
                new[] { "departure" }, new[] { "test-build" }, EXACT_LONG_VALUE);

            JObject json = SerializeObject(state);

            Assert.Equal(new[] { "allowedBuildIds", "disabledFeatures", "maintenance", "notice", "serverTimeUnixMs" },
                json.Properties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
            Assert.True(json.Value<bool>("maintenance"));
            Assert.Equal("scheduled maintenance", json.Value<string>("notice"));
            Assert.Equal("departure", json["disabledFeatures"]![0]!.Value<string>());
            Assert.Equal("test-build", json["allowedBuildIds"]![0]!.Value<string>());
            Assert.Equal(JTokenType.Integer, json["serverTimeUnixMs"]!.Type);
            Assert.Equal(EXACT_LONG_VALUE, json.Value<long>("serverTimeUnixMs"));
        }

        /// <summary> 53비트 경계와 Int64 최댓값을 직렬화 왕복에서 보존한다 </summary>
        [Theory]
        [InlineData(EXACT_LONG_VALUE)]
        [InlineData(MAX_LONG_VALUE)]
        public void Deserialize_ServiceStateInt64Boundary_PreservesPrivateFields(long serverTime)
        {
            ServiceStateResponse original = new ServiceStateResponse(false, "notice",
                Array.Empty<string>(), new[] { "test-build" }, serverTime);

            ServiceStateResponse restored = RoundTrip(original);

            Assert.False(restored.Maintenance);
            Assert.Equal(original.Notice, restored.Notice);
            Assert.Empty(restored.DisabledFeatures);
            Assert.Equal(original.AllowedBuildIds, restored.AllowedBuildIds);
            Assert.Equal(serverTime, restored.ServerTimeUnixMs);
        }

        /// <summary> 결과 상태와 거절 사유는 문자열로 바꾸지 않고 정수로 보낸다 </summary>
        [Theory]
        [InlineData(ContractCodes.OperationStatus.PENDING, ContractCodes.RejectReason.NONE)]
        [InlineData(ContractCodes.OperationStatus.COMMITTED, ContractCodes.RejectReason.NONE)]
        [InlineData(ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT)]
        public void Serialize_OperationResultCodesAndRevision_PreservesNumericContract(int status, int reasonCode)
        {
            OperationResult original = new OperationResult("operation-placeholder", status, reasonCode,
                MAX_LONG_VALUE, EXACT_LONG_VALUE, "receipt-placeholder");

            JObject json = SerializeObject(original);
            OperationResult restored = RoundTrip(original);

            Assert.Equal(new[] { "committedRevision", "operationId", "reasonCode", "receiptId", "serverTimeUnixMs", "status" },
                json.Properties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
            Assert.Equal(JTokenType.Integer, json["status"]!.Type);
            Assert.Equal(JTokenType.Integer, json["reasonCode"]!.Type);
            Assert.Equal(status, json.Value<int>("status"));
            Assert.Equal(reasonCode, json.Value<int>("reasonCode"));
            Assert.Equal(MAX_LONG_VALUE, json.Value<long>("committedRevision"));
            Assert.Equal(original.OperationId, restored.OperationId);
            Assert.Equal(status, restored.Status);
            Assert.Equal(reasonCode, restored.ReasonCode);
            Assert.Equal(MAX_LONG_VALUE, restored.CommittedRevision);
            Assert.Equal(EXACT_LONG_VALUE, restored.ServerTimeUnixMs);
            Assert.Equal(original.ReceiptId, restored.ReceiptId);
        }

        /// <summary> private 기본 생성자를 써서 입장권의 모든 필드를 다시 읽는다 </summary>
        [Fact]
        public void Deserialize_JoinTicketPrivateConstructor_RoundTripsAllMembers()
        {
            JoinTicket original = new JoinTicket("join-token-placeholder", MAX_LONG_VALUE,
                ContractCodes.ServerRole.DUNGEON, "instance-placeholder", "127.0.0.1", TEST_PORT,
                "character-placeholder", "participant-placeholder", EXACT_LONG_VALUE);

            JObject json = SerializeObject(original);
            JoinTicket restored = RoundTrip(original);

            Assert.Equal(JTokenType.Integer, json["serverRole"]!.Type);
            Assert.Equal(JTokenType.Integer, json["expiresAtUnixMs"]!.Type);
            Assert.Equal(original.JoinToken, restored.JoinToken);
            Assert.Equal(original.ExpiresAtUnixMs, restored.ExpiresAtUnixMs);
            Assert.Equal(original.ServerRole, restored.ServerRole);
            Assert.Equal(original.InstanceId, restored.InstanceId);
            Assert.Equal(original.Address, restored.Address);
            Assert.Equal(original.Port, restored.Port);
            Assert.Equal(original.CharacterId, restored.CharacterId);
            Assert.Equal(original.ParticipantId, restored.ParticipantId);
            Assert.Equal(original.ServerTimeUnixMs, restored.ServerTimeUnixMs);
        }

        /// <summary> 중첩된 인증 DTO도 private DataMember를 읽는다 </summary>
        [Fact]
        public void Deserialize_NestedAuthBuild_RoundTripsPrivateMembers()
        {
            AuthExchangeRequest original = new AuthExchangeRequest("invite-placeholder", string.Empty,
                new ClientBuildInfo("test-build", PROTOCOL_VERSION, "content-placeholder"));

            AuthExchangeRequest restored = RoundTrip(original);

            Assert.Equal(original.InviteCode, restored.InviteCode);
            Assert.Equal(string.Empty, restored.RefreshToken);
            Assert.Equal(original.Build.BuildId, restored.Build.BuildId);
            Assert.Equal(PROTOCOL_VERSION, restored.Build.ProtocolVersion);
            Assert.Equal(original.Build.ContentHash, restored.Build.ContentHash);
        }

        [Fact]
        public void Deserialize_TownJoinTicketRequest_RoundTripsCharacterIdOnly()
        {
            TownJoinTicketRequest original = new TownJoinTicketRequest(Guid.NewGuid().ToString("D"));

            JObject json = SerializeObject(original);
            TownJoinTicketRequest restored = RoundTrip(original);

            Assert.Equal(new[] { "characterId" }, json.Properties().Select(property => property.Name));
            Assert.Equal(original.CharacterId, restored.CharacterId);
        }

        /// <summary> DTO가 엔진과 분리되고 private 직렬화 필드 계약을 유지하는지 검사한다 </summary>
        [Fact]
        public void ContractAssembly_AllDataContracts_UsePrivateDataMembersAndNoUnityReferences()
        {
            Assembly assembly = typeof(ServiceStateResponse).Assembly;
            Type[] contracts = assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<DataContractAttribute>() != null).ToArray();

            Assert.NotEmpty(contracts);
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(),
                reference => reference.Name!.StartsWith("Unity", StringComparison.Ordinal));

            foreach (Type contract in contracts)
            {
                Assert.NotNull(contract.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null, Type.EmptyTypes, modifiers: null));

                FieldInfo[] fields = contract.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where(field => field.GetCustomAttribute<DataMemberAttribute>() != null).ToArray();
                Assert.NotEmpty(fields);

                foreach (FieldInfo field in fields)
                {
                    DataMemberAttribute member = field.GetCustomAttribute<DataMemberAttribute>()!;
                    Assert.True(field.IsPrivate);
                    Assert.False(string.IsNullOrWhiteSpace(member.Name));
                    Assert.True(char.IsLower(member.Name![0]));
                }
            }
        }

        private static JObject SerializeObject<T>(T value)
        {
            return JObject.Parse(JsonConvert.SerializeObject(value, ContractJson.CreateSettings()));
        }

        private static T RoundTrip<T>(T value)
        {
            string json = JsonConvert.SerializeObject(value, ContractJson.CreateSettings());
            T? restored = JsonConvert.DeserializeObject<T>(json, ContractJson.CreateSettings());
            Assert.NotNull(restored);
            return restored;
        }
    }
}
