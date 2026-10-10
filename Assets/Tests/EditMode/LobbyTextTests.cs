// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class LobbyTextTests
    {
        // 로비와 파티 화면이 쓰는 직업 표시명, 설명이 모든 직업 ID(0~5)에 있는지 본다

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(ContractCodes.PlayerClass.WARRIOR, "무사")]
        [TestCase(ContractCodes.PlayerClass.SORCERER, "도술사")]
        [TestCase(ContractCodes.PlayerClass.SUMMONER, "신령사")]
        [TestCase(ContractCodes.PlayerClass.SWORDSMAN, "검사")]
        [TestCase(ContractCodes.PlayerClass.MAGEKNIGHT, "마검사")]
        [TestCase(ContractCodes.PlayerClass.ARCHER, "궁수")]
        public void ClassName_KnownClass_ReturnsDisplayName(int classId, string expected)
        {
            Assert.AreEqual(expected, LobbyText.ClassName(classId));
        }

        [Test]
        public void ClassDescription_EveryKnownClass_IsNotEmpty()
        {
            for (int classId = ContractCodes.PlayerClass.WARRIOR; classId <= ContractCodes.PlayerClass.ARCHER; ++classId)
            {
                Assert.IsNotEmpty(LobbyText.ClassDescription(classId), $"classId={classId}");
            }
        }

        [TestCase(-1)]
        [TestCase(6)]
        public void ClassName_UnknownClass_ReturnsFallbackWithoutThrowing(int classId)
        {
            Assert.AreEqual("알 수 없는 직업", LobbyText.ClassName(classId));
            Assert.IsEmpty(LobbyText.ClassDescription(classId));
        }
    }
}
