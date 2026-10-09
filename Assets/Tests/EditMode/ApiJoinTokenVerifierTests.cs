// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class ApiJoinTokenVerifierTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string INSTANCE      = "dungeon-01";
        private const string OTHER         = "dungeon-02";
        private const string TOWN_INSTANCE = "town-01";
        private const string TOKEN         = "join-token-1";
        private const string CHARACTER     = "char-1";
        private const string PARTICIPANT   = "part-1";
        private const string SORTIE        = "sortie-1";
        private const int    CLASS_ID      = ContractCodes.PlayerClass.SORCERER;
        private const int    AUTH_REQUEST  = 7;
        private const int    STATUS_OK     = 200;
        private const int    STATUS_GONE   = 410;
        private const int    STATUS_DOWN   = 503;

        private double _now;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _now = 0.0;
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Verify_DungeonExchange_UsesSortieParticipantAsKey()
        {
            var       transport = Answer(DungeonResponse(INSTANCE));
            using var gateway   = CreateGateway(transport);
            var       verifier  = CreateVerifier(gateway, INSTANCE, EServerRole.Dungeon);
            var       listener  = new RecordingVerifyListener();

            verifier.Verify(AUTH_REQUEST, TOKEN, listener);
            Deliver(gateway);

            Assert.AreEqual(1, listener.Calls.Count);
            Assert.AreEqual(AUTH_REQUEST, listener.Calls[0].RequestId);
            Assert.AreEqual(ERejectReason.None, listener.Calls[0].Reason);
            Assert.AreEqual(Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + PARTICIPANT, listener.Calls[0].Claims.ParticipantKey);
            Assert.AreEqual(CHARACTER, listener.Calls[0].Claims.CharacterId);
            Assert.AreEqual(INSTANCE, listener.Calls[0].Claims.InstanceId);
            Assert.AreEqual(SORTIE, listener.Calls[0].Claims.SortieId);
            Assert.AreEqual(EPlayerClass.Sorcerer, listener.Calls[0].Claims.PlayerClass);
        }

        [Test]
        public void Verify_ValidToken_SendsTokenWithThisInstanceAndRoleOnce()
        {
            var       transport = Answer(DungeonResponse(INSTANCE));
            using var gateway   = CreateGateway(transport);
            var       verifier  = CreateVerifier(gateway, INSTANCE, EServerRole.Dungeon);

            verifier.Verify(AUTH_REQUEST, TOKEN, new RecordingVerifyListener());
            Deliver(gateway);

            Assert.IsTrue(transport.Requests.TryDequeue(out BackendRequest sent));
            Assert.AreEqual(JoinApiRoutes.JOIN_TOKEN_EXCHANGE, sent.Path);
            Assert.IsFalse(sent.IsRetryable, "1회용 토큰 교환은 재시도하지 않는다");

            var body = BackendJson.Deserialize<JoinTokenExchangeRequest>(sent.BodyJson);
            Assert.AreEqual(TOKEN, body.JoinToken);
            Assert.AreEqual(INSTANCE, body.InstanceId);
            Assert.AreEqual(ContractCodes.ServerRole.DUNGEON, body.ServerRole);
        }

        [Test]
        public void Verify_ResponseForOtherInstance_RejectsWithSessionMismatch()
        {
            var       transport = Answer(DungeonResponse(OTHER));
            using var gateway   = CreateGateway(transport);
            var       verifier  = CreateVerifier(gateway, INSTANCE, EServerRole.Dungeon);
            var       listener  = new RecordingVerifyListener();

            verifier.Verify(AUTH_REQUEST, TOKEN, listener);
            Deliver(gateway);

            Assert.AreEqual(ERejectReason.SessionMismatch, listener.Calls[0].Reason);
            Assert.AreEqual(0, verifier.RememberedCount);
        }

        [Test]
        public void Verify_TownExchange_UsesCharacterAsKey()
        {
            var       response  = new JoinTokenExchangeResponse(CHARACTER, TOWN_INSTANCE, ContractCodes.ServerRole.TOWN, null, null, 1L, CLASS_ID);
            var       transport = Answer(response);
            using var gateway   = CreateGateway(transport);
            var       verifier  = CreateVerifier(gateway, TOWN_INSTANCE, EServerRole.Town);
            var       listener  = new RecordingVerifyListener();

            verifier.Verify(AUTH_REQUEST, TOKEN, listener);
            Deliver(gateway);

            Assert.AreEqual(ERejectReason.None, listener.Calls[0].Reason);
            Assert.AreEqual(Constants.Network.PARTICIPANT_KEY_TOWN_PREFIX + CHARACTER, listener.Calls[0].Claims.ParticipantKey);
            Assert.IsNull(listener.Calls[0].Claims.SortieId);
            Assert.AreEqual(EPlayerClass.Sorcerer, listener.Calls[0].Claims.PlayerClass);
        }

        [Test]
        public void Verify_ResponseWithUnknownClass_RejectsAndDoesNotRememberExchange()
        {
            var       response  = new JoinTokenExchangeResponse(CHARACTER, INSTANCE, ContractCodes.ServerRole.DUNGEON, PARTICIPANT, SORTIE, 1L,
                JoinTokenExchangeResponse.UNKNOWN_CLASS_ID);
            var       transport = Answer(response);
            using var gateway   = CreateGateway(transport);
            var       verifier  = CreateVerifier(gateway, INSTANCE, EServerRole.Dungeon);
            var       listener  = new RecordingVerifyListener();

            verifier.Verify(AUTH_REQUEST, TOKEN, listener);
            Deliver(gateway);

            // 무사로 대신하지 않고 입장을 거절한다
            Assert.AreEqual(ERejectReason.TokenRevoked, listener.Calls[0].Reason);
            Assert.AreEqual(0, verifier.RememberedCount);
        }

        [Test]
        public void Verify_ResultAfterAuthDeadline_IsDropped()
        {
            var       transport = Answer(DungeonResponse(INSTANCE));
            using var gateway   = CreateGateway(transport);
            var       verifier  = CreateVerifier(gateway, INSTANCE, EServerRole.Dungeon);
            var       listener  = new RecordingVerifyListener();

            verifier.Verify(AUTH_REQUEST, TOKEN, listener);
            FakeBackendTransport.WaitForCompleted(gateway, 1);

            // 메인 스레드가 멈춰 있던 사이 인증 마감이 지났다
            _now += Constants.Network.JOIN_TOKEN_VERIFY_DEADLINE_SECONDS + 1.0;
            gateway.Pump();

            Assert.AreEqual(0, listener.Calls.Count);
            Assert.AreEqual(0, verifier.PendingCount);
        }

        [Test]
        public void Verify_ApiRejection_PassesReasonCode()
        {
            string    body      = BackendJson.Serialize(new ApiRejection(ContractCodes.RejectReason.TOKEN_EXPIRED, "b", 1L));
            var       transport = FakeBackendTransport.Always(STATUS_GONE, body);
            using var gateway   = CreateGateway(transport);
            var       verifier  = CreateVerifier(gateway, INSTANCE, EServerRole.Dungeon);
            var       listener  = new RecordingVerifyListener();

            verifier.Verify(AUTH_REQUEST, TOKEN, listener);
            Deliver(gateway);

            Assert.AreEqual(ERejectReason.TokenExpired, listener.Calls[0].Reason);
        }

        [Test]
        public void Verify_BackendDown_ReportsMaintenanceAfterOneAttempt()
        {
            var       transport = FakeBackendTransport.Always(STATUS_DOWN, null);
            using var gateway   = CreateGateway(transport);
            var       verifier  = CreateVerifier(gateway, INSTANCE, EServerRole.Dungeon);
            var       listener  = new RecordingVerifyListener();

            verifier.Verify(AUTH_REQUEST, TOKEN, listener);
            Deliver(gateway);

            Assert.AreEqual(ERejectReason.BackendUnavailable, listener.Calls[0].Reason);
            Assert.AreEqual(1, transport.Calls);
        }

        [Test]
        public void Verify_SameTokenAfterAlreadyConnected_ReusesExchangeUntilAdmitted()
        {
            var       transport = Answer(DungeonResponse(INSTANCE));
            using var gateway   = CreateGateway(transport);
            var       verifier  = CreateVerifier(gateway, INSTANCE, EServerRole.Dungeon);
            var       listener  = new RecordingVerifyListener();

            verifier.Verify(AUTH_REQUEST, TOKEN, listener);
            Deliver(gateway);

            // 인증기가 이미 접속 중으로 거절했고 클라이언트가 같은 토큰으로 다시 왔다. API를 다시 부르지 않는다
            verifier.Verify(AUTH_REQUEST + 1, TOKEN, listener);
            Assert.AreEqual(1, transport.Calls);
            Assert.AreEqual(2, listener.Calls.Count);
            Assert.AreEqual(ERejectReason.None, listener.Calls[1].Reason);
            Assert.AreEqual(listener.Calls[0].Claims.ParticipantKey, listener.Calls[1].Claims.ParticipantKey);

            // 입장한 뒤에는 같은 토큰을 다시 받아 주지 않는다
            verifier.OnAdmitted(AUTH_REQUEST + 1);
            Assert.AreEqual(0, verifier.RememberedCount);
            verifier.Verify(AUTH_REQUEST + 2, TOKEN, listener);
            Deliver(gateway);
            Assert.AreEqual(2, transport.Calls);
        }

        [Test]
        public void Verify_RememberedExchange_ExpiresAfterReuseWindow()
        {
            var       transport = Answer(DungeonResponse(INSTANCE));
            using var gateway   = CreateGateway(transport);
            var       verifier  = CreateVerifier(gateway, INSTANCE, EServerRole.Dungeon);
            var       listener  = new RecordingVerifyListener();

            verifier.Verify(AUTH_REQUEST, TOKEN, listener);
            Deliver(gateway);

            _now += Constants.Network.JOIN_EXCHANGE_REUSE_SECONDS;
            verifier.Verify(AUTH_REQUEST + 1, TOKEN, listener);
            Deliver(gateway);

            Assert.AreEqual(2, transport.Calls);
        }

        [Test]
        public void ClaimsRules_TownWithSortieOrDungeonWithoutSortie_AreMismatch()
        {
            var townWithSortie  = new JoinTokenExchangeResponse(CHARACTER, INSTANCE, ContractCodes.ServerRole.TOWN, PARTICIPANT, SORTIE, 1L, CLASS_ID);
            var dungeonNoSortie = new JoinTokenExchangeResponse(CHARACTER, INSTANCE, ContractCodes.ServerRole.DUNGEON, PARTICIPANT, null, 1L, CLASS_ID);
            var wrongRole       = new JoinTokenExchangeResponse(CHARACTER, INSTANCE, ContractCodes.ServerRole.TOWN, null, null, 1L, CLASS_ID);
            var unsafeId        = new JoinTokenExchangeResponse("char 1", INSTANCE, ContractCodes.ServerRole.TOWN, null, null, 1L, CLASS_ID);

            Assert.AreEqual(ERejectReason.SessionMismatch, JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE, townWithSortie, out _));
            Assert.AreEqual(ERejectReason.SessionMismatch, JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE, dungeonNoSortie, out _));
            Assert.AreEqual(ERejectReason.SessionMismatch, JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE, wrongRole, out _));
            Assert.AreEqual(ERejectReason.TokenRevoked, JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE, unsafeId, out _));
            Assert.AreEqual(ERejectReason.TokenRevoked, JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE, null, out _));
        }

        [TestCase(ContractCodes.PlayerClass.WARRIOR, EPlayerClass.Warrior)]
        [TestCase(ContractCodes.PlayerClass.SORCERER, EPlayerClass.Sorcerer)]
        [TestCase(ContractCodes.PlayerClass.SUMMONER, EPlayerClass.Summoner)]
        [TestCase(ContractCodes.PlayerClass.SWORDSMAN, EPlayerClass.Swordsman)]
        [TestCase(ContractCodes.PlayerClass.MAGEKNIGHT, EPlayerClass.Mageknight)]
        [TestCase(ContractCodes.PlayerClass.ARCHER, EPlayerClass.Archer)]
        public void ClaimsRules_KnownClass_CarriesClassToTownAndDungeonClaims(int classId, EPlayerClass expected)
        {
            var town    = new JoinTokenExchangeResponse(CHARACTER, TOWN_INSTANCE, ContractCodes.ServerRole.TOWN, null, null, 1L, classId);
            var dungeon = new JoinTokenExchangeResponse(CHARACTER, INSTANCE, ContractCodes.ServerRole.DUNGEON, PARTICIPANT, SORTIE, 1L, classId);

            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryCreate(EServerRole.Town, TOWN_INSTANCE, town, out JoinTokenClaims townClaims));
            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE, dungeon, out JoinTokenClaims dungeonClaims));
            Assert.AreEqual(expected, townClaims.PlayerClass);
            Assert.AreEqual(expected, dungeonClaims.PlayerClass);
        }

        [TestCase(JoinTokenExchangeResponse.UNKNOWN_CLASS_ID)]
        [TestCase(-5)]
        [TestCase(6)]
        [TestCase(99)]
        public void ClaimsRules_UnknownClass_RejectsTownAndDungeonEntry(int classId)
        {
            var town    = new JoinTokenExchangeResponse(CHARACTER, TOWN_INSTANCE, ContractCodes.ServerRole.TOWN, null, null, 1L, classId);
            var dungeon = new JoinTokenExchangeResponse(CHARACTER, INSTANCE, ContractCodes.ServerRole.DUNGEON, PARTICIPANT, SORTIE, 1L, classId);

            Assert.AreEqual(ERejectReason.TokenRevoked, JoinTokenClaimsRules.TryCreate(EServerRole.Town, TOWN_INSTANCE, town, out _));
            Assert.AreEqual(ERejectReason.TokenRevoked, JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE, dungeon, out _));
        }

        [Test]
        public void ClaimsRules_CharacterName_CarriesNormalizedNameToTownAndDungeonClaims()
        {
            var town    = new JoinTokenExchangeResponse(CHARACTER, TOWN_INSTANCE, ContractCodes.ServerRole.TOWN, null, null, 1L,
                null, CLASS_ID, "달빛여우");
            var dungeon = new JoinTokenExchangeResponse(CHARACTER, INSTANCE, ContractCodes.ServerRole.DUNGEON, PARTICIPANT, SORTIE,
                1L, null, CLASS_ID, "Fox01");

            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryCreate(EServerRole.Town, TOWN_INSTANCE, town, out JoinTokenClaims townClaims));
            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE, dungeon, out JoinTokenClaims dungeonClaims));
            Assert.AreEqual("달빛여우", townClaims.CharacterName);
            Assert.AreEqual("Fox01", dungeonClaims.CharacterName);
        }

        // 이름이 없는 레거시 캐릭터는 입장을 받고 이름만 비운다
        [Test]
        public void ClaimsRules_MissingName_AdmitsWithoutName()
        {
            var town = new JoinTokenExchangeResponse(CHARACTER, TOWN_INSTANCE, ContractCodes.ServerRole.TOWN, null, null, 1L,
                null, CLASS_ID, null);

            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryCreate(EServerRole.Town, TOWN_INSTANCE, town, out JoinTokenClaims claims));
            Assert.IsNull(claims.CharacterName);
        }

        // DB 이름은 생성 때 정규화돼 저장되므로 정규화 결과와 다른 값은 변조로 보고 입장을 거절한다
        [TestCase("")]
        [TestCase("a")]
        [TestCase(" 달빛여우 ")]
        [TestCase("<b>관리자</b>")]
        public void ClaimsRules_InvalidOrUnnormalizedName_RejectsEntry(string characterName)
        {
            var town = new JoinTokenExchangeResponse(CHARACTER, TOWN_INSTANCE, ContractCodes.ServerRole.TOWN, null, null, 1L,
                null, CLASS_ID, characterName);

            Assert.AreEqual(ERejectReason.TokenRevoked, JoinTokenClaimsRules.TryCreate(EServerRole.Town, TOWN_INSTANCE, town, out _));
        }

        [Test]
        public void ClaimsRules_UnknownApiReason_MapsToTokenRevoked()
        {
            Assert.AreEqual(ERejectReason.TokenRevoked, JoinTokenClaimsRules.FromApiReasonCode(0));
            Assert.AreEqual(ERejectReason.TokenRevoked, JoinTokenClaimsRules.FromApiReasonCode(-5));
            Assert.AreEqual(ERejectReason.AlreadyConnected,
                JoinTokenClaimsRules.FromApiReasonCode(ContractCodes.RejectReason.ALREADY_CONNECTED));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static JoinTokenExchangeResponse DungeonResponse(string instanceId)
        {
            return new JoinTokenExchangeResponse(CHARACTER, instanceId, ContractCodes.ServerRole.DUNGEON, PARTICIPANT, SORTIE, 1L, CLASS_ID);
        }

        private static FakeBackendTransport Answer(JoinTokenExchangeResponse response)
        {
            string body = BackendJson.Serialize(response);
            return new FakeBackendTransport((request, token) => Task.FromResult(new BackendTransportResult(STATUS_OK, body)));
        }

        private BackendGateway CreateGateway(FakeBackendTransport transport)
        {
            return new BackendGateway(transport, ReadClock, FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()), null,
                null);
        }

        private ApiJoinTokenVerifier CreateVerifier(BackendGateway gateway, string instanceId, EServerRole role)
        {
            return new ApiJoinTokenVerifier(gateway, instanceId, role, ReadClock, null);
        }

        private static void Deliver(BackendGateway gateway)
        {
            FakeBackendTransport.WaitForCompleted(gateway, 1);
            gateway.Pump();
        }

        private double ReadClock()
        {
            return _now;
        }

        /****************************************
        *            RecordingVerifyListener
        ****************************************/

        private sealed class RecordingVerifyListener : IJoinTokenVerifyListener
        {
            public List<VerifyCall> Calls { get; } = new List<VerifyCall>();

            public void OnJoinTokenVerified(int requestId, ERejectReason reason, in JoinTokenClaims claims)
            {
                Calls.Add(new VerifyCall(requestId, reason, claims));
            }
        }

        private readonly struct VerifyCall
        {
            public int             RequestId { get; }
            public ERejectReason   Reason    { get; }
            public JoinTokenClaims Claims    { get; }

            public VerifyCall(int requestId, ERejectReason reason, JoinTokenClaims claims)
            {
                RequestId = requestId;
                Reason    = reason;
                Claims    = claims;
            }
        }
    }
}
