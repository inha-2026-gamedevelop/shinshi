// System
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class GrowthContentConsistencyTests
    {
        // 규칙 수치의 원본은 GameDB 에셋(SO)이다. 백엔드는 growth-content.json과 LifeContent 상수를 읽으므로 둘이 에셋과
        // 어긋나면 마을, 던전 서버와 API가 다른 규칙으로 판정한다. 파일이 낡으면 여기서 실패하고 Shinshi/Export Growth Content로
        // 다시 쓴다.
        //
        // LifeContent는 내보내기 파일이 없어 백엔드 C# 소스의 상수 선언을 정규식으로 읽어 대조한다. 테스트에 수치를 따로 적으면
        // 에셋, 백엔드, 테스트 세 곳이 생기고 백엔드 상수가 바뀌어도 테스트가 알아채지 못한다

        /****************************************
        *                Fields
        ****************************************/

        private const string LIFE_CONTENT_PATH       = "Backend/src/Shinshi.Data/Life/LifeContent.cs";
        private const string SESSION_REPOSITORY_PATH = "Backend/src/Shinshi.Data/Sessions/PostgresGameSessionRepository.cs";
        private const string CONST_PATTERN           = @"public\s+const\s+(?:string|long|int)\s+(?<name>[A-Z_]+)\s*=\s*(?<value>[^;]+);";
        private const string QUOTE                   = "\"";

        private GameDatabaseSO _root;
        private string         _projectRoot;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root        = Resources.Load<GameDatabaseSO>(Constants.Resource.GAME_DB_PATH);
            _projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Assert.IsNotNull(_root, "GameDB 에셋이 없다");
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void GrowthContentJson_MatchesGameDatabaseAssets()
        {
            string path = GrowthContentExport.GetPath(_projectRoot);
            Assert.IsTrue(File.Exists(path), path);

            List<string> differences = GrowthContentExport.Compare(_root, File.ReadAllText(path));

            Assert.IsEmpty(differences, "growth-content.json이 SO와 다르다. Shinshi/Export Growth Content로 다시 쓴다:\n" +
                string.Join("\n", differences));
        }

        [Test]
        public void Serialize_RoundTrip_ComparesEqual()
        {
            string json = GrowthContentExport.Serialize(_root);

            Assert.IsEmpty(GrowthContentExport.Compare(_root, json));
            StringAssert.Contains("\"schemaVersion\": 4", json);
        }

        [Test]
        public void LifeContentConstants_MatchLifeConfigAsset()
        {
            Dictionary<string, string> constants = ReadConstants(LIFE_CONTENT_PATH);
            LifeConfigSO life = _root.Life;
            LifeConfigSO.PotionDefinition small = life.GetPotion(EPotionSize.Small);
            LifeConfigSO.PotionDefinition large = life.GetPotion(EPotionSize.Large);
            Assert.IsTrue(life.TryGetHerb(life.GatherHerbId, out LifeConfigSO.HerbDefinition herb));

            AssertText(constants, "HERB_ID", life.GatherHerbId);
            AssertNumber(constants, "HERB_STACK", herb.StackLimit);
            AssertNumber(constants, "HERBS_PER_GATHER", life.HerbsPerGather);
            AssertText(constants, "POTION_SMALL_ID", small.DefinitionId);
            AssertText(constants, "POTION_LARGE_ID", large.DefinitionId);
            AssertNumber(constants, "POTION_SMALL_COST", small.HerbCost);
            AssertNumber(constants, "POTION_LARGE_COST", large.HerbCost);
            AssertNumber(constants, "POTION_OUTPUT", small.OutputCount);
            AssertNumber(constants, "POTION_OUTPUT", large.OutputCount);
            AssertNumber(constants, "POTION_STACK", small.StackLimit);
            AssertNumber(constants, "POTION_STACK", large.StackLimit);
            AssertNumber(constants, "ACTION_MAX", life.MaxActionEnergy);
            AssertNumber(constants, "ACTION_COST_PER_GATHER", life.ActionCostPerGather);
            AssertNumber(constants, "ACTION_RECOVER_INTERVAL_SECONDS", Mathf.RoundToInt(life.ActionRecoverInterval));
            AssertNumber(constants, "ACTION_RECOVER_AMOUNT", life.ActionRecoverAmount);
            Assert.AreEqual(life.ActionRecoverInterval, Mathf.Round(life.ActionRecoverInterval), "초 단위 정수여야 백엔드와 같다");
        }

        [Test]
        public void BossPotionLimit_MatchesSessionConfigAsset()
        {
            // 단일 원본은 공용 계약 상수다. API는 그 상수를 그대로 쓰고, SO 값은 같아야 한다
            Dictionary<string, string> constants = ReadConstants(SESSION_REPOSITORY_PATH);

            Assert.AreEqual(GameSessionCodes.Potion.BOSS_USE_LIMIT, _root.Session.BossPotionUseLimit);
            Assert.AreEqual("GameSessionCodes.Potion.BOSS_USE_LIMIT", constants["BOSS_POTION_USE_LIMIT"]);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private Dictionary<string, string> ReadConstants(string relativePath)
        {
            string path = Path.Combine(_projectRoot, relativePath);
            Assert.IsTrue(File.Exists(path), path);

            var constants = new Dictionary<string, string>();
            foreach (Match match in Regex.Matches(File.ReadAllText(path), CONST_PATTERN))
            {
                constants[match.Groups["name"].Value] = match.Groups["value"].Value.Trim();
            }

            return constants;
        }

        private static void AssertText(Dictionary<string, string> constants, string name, string expected)
        {
            Assert.IsTrue(constants.TryGetValue(name, out string value), name + " 상수가 없다");
            Assert.AreEqual(QUOTE + expected + QUOTE, value, name);
        }

        private static void AssertNumber(Dictionary<string, string> constants, string name, long expected)
        {
            Assert.IsTrue(constants.TryGetValue(name, out string value), name + " 상수가 없다");
            Assert.IsTrue(long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed),
                name + " = " + value);
            Assert.AreEqual(expected, parsed, name);
        }
    }
}
