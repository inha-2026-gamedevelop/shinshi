// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class JoinContractsJsonTests
    {
        // 시험 어셈블리는 Newtonsoft를 직접 참조하지 않는다. 서버가 실제로 쓰는 BackendJson을 거쳐 같은 설정을 시험한다

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ExchangeRequest_RoundTrip_KeepsPrivateFieldsWithCamelCaseKeys()
        {
            string json = BackendJson.Serialize(new JoinTokenExchangeRequest("tok", "dungeon-01", ContractCodes.ServerRole.DUNGEON));

            StringAssert.Contains("\"joinToken\":\"tok\"", json);
            StringAssert.Contains("\"instanceId\":\"dungeon-01\"", json);
            StringAssert.Contains("\"serverRole\":1", json);

            var read = BackendJson.Deserialize<JoinTokenExchangeRequest>(json);
            Assert.AreEqual("tok", read.JoinToken);
            Assert.AreEqual("dungeon-01", read.InstanceId);
            Assert.AreEqual(ContractCodes.ServerRole.DUNGEON, read.ServerRole);
        }

        [Test]
        public void ExchangeResponse_RoundTrip_KeepsAllFields()
        {
            var source = new JoinTokenExchangeResponse("char-1", "dungeon-01", ContractCodes.ServerRole.DUNGEON, "part-1", "sortie-1",
                1234567890123L, ContractCodes.PlayerClass.SUMMONER);

            string json = BackendJson.Serialize(source);
            var    read = BackendJson.Deserialize<JoinTokenExchangeResponse>(json);

            StringAssert.Contains("\"classId\":2", json);
            Assert.AreEqual("char-1", read.CharacterId);
            Assert.AreEqual("dungeon-01", read.InstanceId);
            Assert.AreEqual(ContractCodes.ServerRole.DUNGEON, read.ServerRole);
            Assert.AreEqual("part-1", read.ParticipantId);
            Assert.AreEqual("sortie-1", read.SortieId);
            Assert.AreEqual(1234567890123L, read.ServerTimeUnixMs);
            Assert.AreEqual(ContractCodes.PlayerClass.SUMMONER, read.ClassId);
        }

        [Test]
        public void ExchangeResponse_UnknownFieldAndMissingField_AreTolerated()
        {
            // v1 안에서는 필드 추가만 허용하므로 모르는 키를 무시하고, 마을 응답처럼 빠진 키는 기본값으로 읽는다
            const string JSON = "{\"characterId\":\"c\",\"instanceId\":\"town-01\",\"serverRole\":0,\"newField\":5}";

            var read = BackendJson.Deserialize<JoinTokenExchangeResponse>(JSON);

            Assert.AreEqual("c", read.CharacterId);
            Assert.IsNull(read.ParticipantId);
            Assert.IsNull(read.SortieId);
        }

        [Test]
        public void ExchangeResponse_MissingClassId_ReadsAsUnknownNotWarrior()
        {
            const string JSON = "{\"characterId\":\"c\",\"instanceId\":\"town-01\",\"serverRole\":0}";

            var read = BackendJson.Deserialize<JoinTokenExchangeResponse>(JSON);

            Assert.AreEqual(JoinTokenExchangeResponse.UNKNOWN_CLASS_ID, read.ClassId);
            Assert.AreNotEqual(ContractCodes.PlayerClass.WARRIOR, read.ClassId);
        }

        [Test]
        public void JoinTicket_RoundTrip_KeepsAllFields()
        {
            var source = new JoinTicket("tok", 60000L, ContractCodes.ServerRole.TOWN, "town-01", "127.0.0.1", 7777, "char-1", null, 5L);

            var read = BackendJson.Deserialize<JoinTicket>(BackendJson.Serialize(source));

            Assert.AreEqual("tok", read.JoinToken);
            Assert.AreEqual(60000L, read.ExpiresAtUnixMs);
            Assert.AreEqual("town-01", read.InstanceId);
            Assert.AreEqual(7777, read.Port);
            Assert.AreEqual("char-1", read.CharacterId);
            Assert.IsNull(read.ParticipantId);
        }

        [Test]
        public void ApiRejection_ReasonCode_IsReadable()
        {
            string json = BackendJson.Serialize(new ApiRejection(ContractCodes.RejectReason.SESSION_MISMATCH, "build", 1L));

            Assert.AreEqual(ContractCodes.RejectReason.SESSION_MISMATCH, BackendJson.ReadReasonCode(json));
            Assert.AreEqual(0, BackendJson.ReadReasonCode("<html>"));
            Assert.AreEqual(0, BackendJson.ReadReasonCode(null));
        }

        [Test]
        public void Deserialize_TypeNameInBody_DoesNotChangeType()
        {
            const string JSON = "{\"$type\":\"System.Object, mscorlib\",\"characterId\":\"c\"}";

            Assert.IsTrue(BackendJson.TryDeserialize(JSON, typeof(JoinTokenExchangeResponse), out object value));
            Assert.IsInstanceOf<JoinTokenExchangeResponse>(value);
        }
    }
}
