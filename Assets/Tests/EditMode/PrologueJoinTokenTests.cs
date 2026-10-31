// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class PrologueJoinTokenTests
    {
        // 프롤로그 출정 계약(mode 2)과 저장 단계 읽기를 순수 규칙으로 본다

        /****************************************
        *                Fields
        ****************************************/

        private const string CHARACTER   = "char-1";
        private const string INSTANCE    = "dungeon-01";
        private const string PARTICIPANT = "part-1";
        private const string SORTIE      = "sortie-1";
        private const int    CLASS_ID    = ContractCodes.PlayerClass.SORCERER;
        private const long   SERVER_TIME = 1L;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryReadSortie_PrologueContract_Accepted()
        {
            JoinTokenExchangeResponse response = Response(GameSessionCodes.Mode.PROLOGUE, PrologueCodes.BOSS_ID,
                GameSessionCodes.Difficulty.EASY);

            ERejectReason reason = JoinTokenClaimsRules.TryReadSortie(response, true, out SortieDescriptor sortie);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.IsTrue(sortie.IsPrologue);
            Assert.IsFalse(sortie.IsBoss);
            Assert.IsFalse(sortie.IsNormal);
            Assert.AreEqual(PrologueCodes.BOSS_ID, sortie.BossId);
        }

        [Test]
        public void TryReadSortie_PrologueHardDifficulty_Rejected()
        {
            JoinTokenExchangeResponse response = Response(GameSessionCodes.Mode.PROLOGUE, PrologueCodes.BOSS_ID,
                GameSessionCodes.Difficulty.HARD);

            Assert.AreEqual(ERejectReason.SessionMismatch, JoinTokenClaimsRules.TryReadSortie(response, true, out _));
        }

        [Test]
        public void TryReadSortie_PrologueWithoutBoss_Rejected()
        {
            JoinTokenExchangeResponse response = Response(GameSessionCodes.Mode.PROLOGUE, null, GameSessionCodes.Difficulty.EASY);

            Assert.AreEqual(ERejectReason.SessionMismatch, JoinTokenClaimsRules.TryReadSortie(response, true, out _));
        }

        [Test]
        public void TryReadSortie_UnknownMode_StillRejected()
        {
            JoinTokenExchangeResponse response = Response(GameSessionCodes.Mode.PROLOGUE + 1, PrologueCodes.BOSS_ID,
                GameSessionCodes.Difficulty.EASY);

            Assert.AreEqual(ERejectReason.SessionMismatch, JoinTokenClaimsRules.TryReadSortie(response, true, out _));
        }

        [Test]
        public void Matches_PrologueAndBossWithSameIds_AreDifferentContracts()
        {
            var prologue = new SortieDescriptor(GameSessionCodes.Mode.PROLOGUE, PrologueCodes.MAP_ID, PrologueCodes.BATTLE_ID,
                PrologueCodes.BOSS_ID, GameSessionCodes.Difficulty.EASY);
            var boss = new SortieDescriptor(GameSessionCodes.Mode.BOSS, PrologueCodes.MAP_ID, PrologueCodes.BATTLE_ID,
                PrologueCodes.BOSS_ID, GameSessionCodes.Difficulty.EASY);

            Assert.IsFalse(prologue.Matches(in boss), "다른 종류의 출정 토큰은 같은 세션에 들일 수 없다");
            Assert.IsTrue(prologue.Matches(in prologue));
        }

        [TestCase(PrologueCodes.Stage.INTRO, 0, true)]
        [TestCase(PrologueCodes.Stage.WOLBAEK, PrologueCodes.MAX_POTIONS, true)]
        [TestCase(PrologueCodes.Stage.DONE, 0, true)]
        [TestCase(PrologueCodes.Stage.DONE + 1, 0, false)]
        [TestCase(-1, 0, false)]
        [TestCase(PrologueCodes.Stage.FOREST, -1, false)]
        [TestCase(PrologueCodes.Stage.FOREST, PrologueCodes.MAX_POTIONS + 1, false)]
        public void PrologueResume_IsValid_FollowsContractRanges(int stage, int potions, bool expected)
        {
            var resume = new PrologueResume(stage, potions, false);

            Assert.AreEqual(expected, resume.IsValid());
        }

        [Test]
        public void PrologueResume_Default_HasNoValue()
        {
            PrologueResume resume = default;

            Assert.IsFalse(resume.HasValue);
            Assert.IsFalse(resume.IsValid());
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static JoinTokenExchangeResponse Response(int mode, string bossId, int difficulty)
        {
            return new JoinTokenExchangeResponse(CHARACTER, INSTANCE, ContractCodes.ServerRole.DUNGEON, PARTICIPANT, SORTIE,
                SERVER_TIME, null, CLASS_ID, null, mode, PrologueCodes.MAP_ID, PrologueCodes.BATTLE_ID, bossId, difficulty, null);
        }
    }
}
