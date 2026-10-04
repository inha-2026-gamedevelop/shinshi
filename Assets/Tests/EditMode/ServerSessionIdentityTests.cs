// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    /// <summary> ServerSessionRegistry가 JoinTokenClaims의 이름/외형/직업을 결합에 그대로 보존하는지, 재합류 때 참가자
    /// 번호가 던전(Release)과 마을(Forget)에서 서로 다르게 다뤄지는지 </summary>
    public class ServerSessionIdentityTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string INSTANCE_ID    = "instance-1";
        private const string CHARACTER_ID   = "char-1";
        private const string PARTICIPANT_ID = "participant-1";
        private const string SORTIE_ID      = "sortie-1";
        private const int    CAPACITY       = 4;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryAdmit_Town_PreservesCharacterNameAppearanceAndPlayerClass()
        {
            JoinTokenClaims claims = BuildTownClaims();
            var registry = new ServerSessionRegistry(CAPACITY);

            ERejectReason reason = registry.TryAdmit(in claims, out SessionBinding binding);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.AreEqual(EPlayerClass.Warrior, binding.PlayerClass);
            Assert.AreEqual("마을검증", binding.CharacterName);
            Assert.AreEqual(CharacterAppearance.VERSION_2, binding.Appearance.Version);
            Assert.AreEqual(CharacterAppearanceCatalog.FACE_CUTE, binding.Appearance.FaceId);
            Assert.AreEqual(3, binding.Appearance.HairId);
            Assert.AreEqual(2, binding.Appearance.OutfitId);
        }

        [Test]
        public void TryAdmit_DungeonReleaseThenReAdmitSameKey_KeepsSameParticipantId()
        {
            JoinTokenClaims claims = BuildDungeonClaims();
            var registry = new ServerSessionRegistry(CAPACITY);

            ERejectReason firstReason = registry.TryAdmit(in claims, out SessionBinding firstBinding);
            registry.Release(firstBinding);
            ERejectReason secondReason = registry.TryAdmit(in claims, out SessionBinding secondBinding);

            Assert.AreEqual(ERejectReason.None, firstReason);
            Assert.AreEqual(ERejectReason.None, secondReason);
            Assert.AreEqual(firstBinding.ParticipantId, secondBinding.ParticipantId);
            Assert.AreEqual("마을검증", secondBinding.CharacterName);
            Assert.AreEqual(3, secondBinding.Appearance.HairId);
            Assert.AreEqual(2, secondBinding.Appearance.OutfitId);
        }

        [Test]
        public void TryAdmit_TownForgetThenReAdmitSameKey_GetsNewParticipantId()
        {
            JoinTokenClaims claims = BuildTownClaims();
            var registry = new ServerSessionRegistry(CAPACITY);

            ERejectReason firstReason = registry.TryAdmit(in claims, out SessionBinding firstBinding);
            registry.Forget(firstBinding);
            ERejectReason secondReason = registry.TryAdmit(in claims, out SessionBinding secondBinding);

            Assert.AreEqual(ERejectReason.None, firstReason);
            Assert.AreEqual(ERejectReason.None, secondReason);
            Assert.AreNotEqual(firstBinding.ParticipantId, secondBinding.ParticipantId);
            Assert.AreEqual("마을검증", secondBinding.CharacterName);
            Assert.AreEqual(3, secondBinding.Appearance.HairId);
            Assert.AreEqual(2, secondBinding.Appearance.OutfitId);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static JoinTokenClaims BuildTownClaims()
        {
            JoinTokenExchangeResponse response = BuildResponse((int)EServerRole.Town, null, null);
            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Town, INSTANCE_ID, response, out JoinTokenClaims claims);
            Assert.AreEqual(ERejectReason.None, reason, "준비 단계의 Town claims 생성이 실패했다");
            return claims;
        }

        private static JoinTokenClaims BuildDungeonClaims()
        {
            JoinTokenExchangeResponse response = BuildResponse((int)EServerRole.Dungeon, PARTICIPANT_ID, SORTIE_ID);
            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE_ID, response, out JoinTokenClaims claims);
            Assert.AreEqual(ERejectReason.None, reason, "준비 단계의 Dungeon claims 생성이 실패했다");
            return claims;
        }

        private static JoinTokenExchangeResponse BuildResponse(int serverRole, string participantId, string sortieId)
        {
            CharacterAppearance mixed = CharacterAppearanceCatalog.Create(CharacterAppearanceCatalog.FACE_CUTE, 3, 2);
            string appearanceJson = BackendJson.Serialize(mixed);
            return new JoinTokenExchangeResponse(CHARACTER_ID, INSTANCE_ID, serverRole, participantId, sortieId, 0L,
                null, (int)EPlayerClass.Warrior, "마을검증", appearanceJson);
        }
    }
}
