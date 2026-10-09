// System
using System.Text;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    /// <summary> JoinTokenClaimsRules의 이름/외형 입장 검증 회귀, null/v1 레거시와 유효한 v2의 이름 요구를 가른다 </summary>
    public class JoinTokenClaimsIdentityTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string INSTANCE_ID    = "instance-1";
        private const string CHARACTER_ID   = "char-1";
        private const string PARTICIPANT_ID = "participant-1";
        private const string SORTIE_ID      = "sortie-1";

        /****************************************
        *        Tests - 레거시 null/v1 수용
        ****************************************/

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TryCreate_Town_NullAppearanceAndName_AcceptsWithClassDefault(int classId)
        {
            JoinTokenExchangeResponse response = BuildTownResponse(classId, null, null);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.IsNull(claims.CharacterName);
            AssertClassDefaultAppearance(classId, claims.Appearance);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TryCreate_Dungeon_NullAppearanceAndName_AcceptsWithClassDefault(int classId)
        {
            JoinTokenExchangeResponse response = BuildDungeonResponse(classId, null, null);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.IsNull(claims.CharacterName);
            AssertClassDefaultAppearance(classId, claims.Appearance);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TryCreate_Town_LegacyV1AppearanceAndNullName_AcceptsWithClassDefault(int classId)
        {
            string legacyJson = SerializeAppearance(BuildLegacyV1Appearance(hairId: 0));
            JoinTokenExchangeResponse response = BuildTownResponse(classId, null, legacyJson);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.IsNull(claims.CharacterName);
            AssertClassDefaultAppearance(classId, claims.Appearance);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TryCreate_Dungeon_LegacyV1AppearanceAndNullName_AcceptsWithClassDefault(int classId)
        {
            // v1은 hairId가 무엇이든(기존 자료는 0일 수도, 그 밖의 임의 값일 수도) 결과가 직업 기본값으로 고정된다
            string legacyJson = SerializeAppearance(BuildLegacyV1Appearance(hairId: 999));
            JoinTokenExchangeResponse response = BuildDungeonResponse(classId, null, legacyJson);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.IsNull(claims.CharacterName);
            AssertClassDefaultAppearance(classId, claims.Appearance);
        }

        [Test]
        public void TryCreate_LegacyV1WithStrayFaceOutfitKeysAndNullName_StillAcceptsAsLegacy()
        {
            // 과거 v1 저장소에 남은 faceId/outfitId 잔존 키는 버전이 1이면 무시되고 직업 기본값으로 간다
            string strayJson = BuildStrayV1AppearanceJson();
            JoinTokenExchangeResponse response = BuildTownResponse((int)EPlayerClass.Warrior, null, strayJson);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.IsNull(claims.CharacterName);
            AssertClassDefaultAppearance((int)EPlayerClass.Warrior, claims.Appearance);
        }

        /****************************************
        *        Tests - 유효한 v2 + 유효한 이름
        ****************************************/

        [Test]
        public void TryCreate_Town_ValidV2MixedAppearanceAndValidName_AcceptsWithGivenValues()
        {
            CharacterAppearance mixed = CharacterAppearanceCatalog.Create(CharacterAppearanceCatalog.FACE_CUTE, 3, 2);
            string appearanceJson = SerializeAppearance(mixed);
            JoinTokenExchangeResponse response = BuildTownResponse((int)EPlayerClass.Warrior, "마을검증", appearanceJson);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.AreEqual("마을검증", claims.CharacterName);
            // 허용된 v2는 직업 기본 v3로 정규화된다, 저장된 v2 조합 값은 표시에 쓰이지 않는다
            Assert.IsTrue(claims.Appearance.ContentEquals(CharacterAppearanceCatalog.CreateDefault((int)EPlayerClass.Warrior)));
        }

        /****************************************
        *        Tests - 유효한 v2 + 이름 거절
        ****************************************/

        [Test]
        public void TryCreate_ValidV2WithNullName_RejectsAsTokenRevoked()
        {
            AssertValidV2NameRejected(null);
        }

        [Test]
        public void TryCreate_ValidV2WithEmptyName_RejectsAsTokenRevoked()
        {
            AssertValidV2NameRejected(string.Empty);
        }

        [Test]
        public void TryCreate_ValidV2WithWhitespaceWrappedName_RejectsAsTokenRevoked()
        {
            // 트림 결과가 원문과 달라져 정규화 전/후 동등성 검사에서 떨어진다
            AssertValidV2NameRejected(" 마을 ");
        }

        [Test]
        public void TryCreate_ValidV2WithFullWidthMismatchName_RejectsAsTokenRevoked()
        {
            // NFKC가 전각 라틴/숫자를 반각으로 바꿔 원문과 달라진다
            AssertValidV2NameRejected("ＡＢ１");
        }

        [Test]
        public void TryCreate_ValidV2WithTooShortName_RejectsAsTokenRevoked()
        {
            AssertValidV2NameRejected("가");
        }

        [Test]
        public void TryCreate_ValidV2WithTooLongName_RejectsAsTokenRevoked()
        {
            AssertValidV2NameRejected("가나다라마바사아자차카타파");
        }

        [Test]
        public void TryCreate_ValidV2WithForbiddenCharName_RejectsAsTokenRevoked()
        {
            AssertValidV2NameRejected("마을!");
        }

        /****************************************
        *        Tests - 알 수 없는 직업
        ****************************************/

        [TestCase(ContractCodes.PlayerClass.SWORDSMAN)]
        [TestCase(ContractCodes.PlayerClass.MAGEKNIGHT)]
        [TestCase(ContractCodes.PlayerClass.ARCHER)]
        public void TryCreate_NewClassId_AcceptsWithClassDefault(int classId)
        {
            JoinTokenExchangeResponse town    = BuildTownResponse(classId, null, null);
            JoinTokenExchangeResponse dungeon = BuildDungeonResponse(classId, null, null);

            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, town, out JoinTokenClaims townClaims));
            Assert.AreEqual(ERejectReason.None, JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE_ID, dungeon, out JoinTokenClaims dungeonClaims));
            Assert.AreEqual((EPlayerClass)classId, townClaims.PlayerClass);
            Assert.AreEqual((EPlayerClass)classId, dungeonClaims.PlayerClass);
            AssertClassDefaultAppearance(classId, townClaims.Appearance);
            AssertClassDefaultAppearance(classId, dungeonClaims.Appearance);
        }

        [TestCase(-1)]
        [TestCase(6)]
        public void TryCreate_UnknownClassId_RejectsAsTokenRevoked(int classId)
        {
            JoinTokenExchangeResponse response = BuildTownResponse(classId, null, null);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.TokenRevoked, reason);
        }

        /****************************************
        *        Tests - 외형 구조/버전 거절
        ****************************************/

        [Test]
        public void TryCreate_MalformedAppearanceJson_RejectsAsTokenRevoked()
        {
            JoinTokenExchangeResponse response = BuildTownResponse((int)EPlayerClass.Warrior, null, "{ not json");

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.TokenRevoked, reason);
        }

        [Test]
        public void TryCreate_EmptyAppearanceJsonString_RejectsAndIsNotTreatedAsLegacyNull()
        {
            // 명시적 null과 달리 빈 문자열은 유효한 JSON이 아니므로 레거시로 보지 않고 거절해야 한다
            JoinTokenExchangeResponse response = BuildTownResponse((int)EPlayerClass.Warrior, null, string.Empty);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.TokenRevoked, reason);
        }

        [Test]
        public void TryCreate_UnsupportedAppearanceVersion_RejectsAsTokenRevoked()
        {
            CharacterAppearance unsupported = new CharacterAppearance(CharacterAppearance.CURRENT_VERSION + 1, CharacterAppearance.BODY_TYPE_FEMALE,
                NeutralSliders(CharacterAppearance.FACE_SLIDER_COUNT), NeutralSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN,
                CharacterAppearance.VALUE_MIN, 1, CharacterAppearance.VALUE_MIN, 1, 1);
            JoinTokenExchangeResponse response = BuildTownResponse((int)EPlayerClass.Warrior, null, SerializeAppearance(unsupported));

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.TokenRevoked, reason);
        }

        [Test]
        public void TryCreate_InvalidV2PartIds_RejectsAsTokenRevoked()
        {
            // face=2(허용은 FACE_CUTE=1뿐), hair=4/outfit=4(허용 범위는 1..3 밖)
            CharacterAppearance invalidIds = new CharacterAppearance(CharacterAppearance.VERSION_2, CharacterAppearance.BODY_TYPE_FEMALE,
                NeutralSliders(CharacterAppearance.FACE_SLIDER_COUNT), NeutralSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN,
                CharacterAppearance.VALUE_MIN, 4, CharacterAppearance.VALUE_MIN, 2, 4);
            JoinTokenExchangeResponse response = BuildTownResponse((int)EPlayerClass.Warrior, "마을검증", SerializeAppearance(invalidIds));

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.TokenRevoked, reason);
        }

        [Test]
        public void TryCreate_NonNeutralV2Values_RejectsAsTokenRevoked()
        {
            CharacterAppearance nonNeutral = new CharacterAppearance(CharacterAppearance.VERSION_2, CharacterAppearance.BODY_TYPE_FEMALE,
                NeutralSliders(CharacterAppearance.FACE_SLIDER_COUNT), NeutralSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                CharacterAppearance.SLIDER_NEUTRAL + 1, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN,
                CharacterAppearance.VALUE_MIN, 1, CharacterAppearance.VALUE_MIN, CharacterAppearanceCatalog.FACE_CUTE, 1);
            JoinTokenExchangeResponse response = BuildTownResponse((int)EPlayerClass.Warrior, "마을검증", SerializeAppearance(nonNeutral));

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.TokenRevoked, reason);
        }

        /****************************************
        *        Tests - 기존 SessionMismatch/구조 거절 보존
        ****************************************/

        [Test]
        public void TryCreate_NullResponse_RejectsAsTokenRevoked()
        {
            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, null, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.TokenRevoked, reason);
        }

        [Test]
        public void TryCreate_InstanceIdMismatch_RejectsAsSessionMismatch()
        {
            JoinTokenExchangeResponse response = BuildTownResponse((int)EPlayerClass.Warrior, null, null);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, "other-instance", response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.SessionMismatch, reason);
        }

        [Test]
        public void TryCreate_ServerRoleMismatch_RejectsAsSessionMismatch()
        {
            JoinTokenExchangeResponse response = BuildTownResponse((int)EPlayerClass.Warrior, null, null);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.SessionMismatch, reason);
        }

        [Test]
        public void TryCreate_DungeonMissingParticipantOrSortie_RejectsAsSessionMismatch()
        {
            JoinTokenExchangeResponse response = new JoinTokenExchangeResponse(CHARACTER_ID, INSTANCE_ID,
                (int)EServerRole.Dungeon, null, null, 0L, null, (int)EPlayerClass.Warrior, null, null);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.SessionMismatch, reason);
        }

        [Test]
        public void TryCreate_TownWithSortieAttached_RejectsAsSessionMismatch()
        {
            JoinTokenExchangeResponse response = new JoinTokenExchangeResponse(CHARACTER_ID, INSTANCE_ID,
                (int)EServerRole.Town, null, SORTIE_ID, 0L, null, (int)EPlayerClass.Warrior, null, null);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.SessionMismatch, reason);
        }

        [Test]
        public void TryCreate_UnsafeCharacterId_RejectsAsTokenRevoked()
        {
            JoinTokenExchangeResponse response = new JoinTokenExchangeResponse("not safe!", INSTANCE_ID,
                (int)EServerRole.Town, null, null, 0L, null, (int)EPlayerClass.Warrior, null, null);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.TokenRevoked, reason);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static void AssertValidV2NameRejected(string name)
        {
            CharacterAppearance v2 = CharacterAppearanceCatalog.Create(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);
            string appearanceJson = SerializeAppearance(v2);
            JoinTokenExchangeResponse response = BuildTownResponse((int)EPlayerClass.Warrior, name, appearanceJson);

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.TokenRevoked, reason);
        }

        private static void AssertClassDefaultAppearance(int classId, CharacterAppearance appearance)
        {
            Assert.AreEqual(CharacterAppearance.VERSION_3, appearance.Version);
            Assert.IsTrue(appearance.ContentEquals(CharacterAppearanceCatalog.CreateDefault(classId)));
        }

        private static JoinTokenExchangeResponse BuildTownResponse(int classId, string characterName, string appearanceJson)
        {
            return new JoinTokenExchangeResponse(CHARACTER_ID, INSTANCE_ID, (int)EServerRole.Town, null, null, 0L,
                null, classId, characterName, appearanceJson);
        }

        private static JoinTokenExchangeResponse BuildDungeonResponse(int classId, string characterName, string appearanceJson)
        {
            return new JoinTokenExchangeResponse(CHARACTER_ID, INSTANCE_ID, (int)EServerRole.Dungeon, PARTICIPANT_ID,
                SORTIE_ID, 0L, null, classId, characterName, appearanceJson);
        }

        private static string SerializeAppearance(CharacterAppearance appearance)
        {
            return BackendJson.Serialize(appearance);
        }

        private static CharacterAppearance BuildLegacyV1Appearance(int hairId)
        {
            return new CharacterAppearance(CharacterAppearance.VERSION_1, CharacterAppearance.BODY_TYPE_FEMALE,
                NeutralSliders(CharacterAppearance.FACE_SLIDER_COUNT), NeutralSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN,
                CharacterAppearance.VALUE_MIN, hairId, CharacterAppearance.VALUE_MIN);
        }

        private static string BuildStrayV1AppearanceJson()
        {
            string face = BuildIntArrayJson(CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.FACE_SLIDER_COUNT);
            string body = BuildIntArrayJson(CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.BODY_SLIDER_COUNT);
            return "{\"version\":1,\"bodyType\":0,\"face\":" + face + ",\"body\":" + body
                + ",\"skinTone\":128,\"eyeColor\":0,\"browColor\":0,\"lipColor\":0,\"hairId\":0,\"hairColor\":0,"
                + "\"faceId\":3,\"outfitId\":2}";
        }

        private static string BuildIntArrayJson(int value, int count)
        {
            var builder = new StringBuilder("[");
            for (int i = 0; i < count; ++i)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(value);
            }

            builder.Append(']');
            return builder.ToString();
        }

        private static int[] NeutralSliders(int count)
        {
            int[] sliders = new int[count];
            for (int i = 0; i < count; ++i)
            {
                sliders[i] = CharacterAppearance.SLIDER_NEUTRAL;
            }

            return sliders;
        }
    }
}
