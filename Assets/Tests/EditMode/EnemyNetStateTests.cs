// Unity
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests.EditMode
{
    public sealed class EnemyNetStateTests
    {
        private const string ENEMY_PREFAB_PATH  = "Assets/02.Prefab/Network/NetEnemy/PF_NetEnemy.prefab";
        private const string EFFECT_PREFAB_PATH = "Assets/02.Prefab/Network/NetEnemy/PF_NetEnemyEffect.prefab";

        private static EnemyTelegraphCue MakeCue(EEnemyAttackShape shape, float range, float width, double start, double end)
        {
            return new EnemyTelegraphCue(1, shape, Vector3.zero, Vector3.forward, range, width, start, end);
        }

        [Test]
        public void NextSequence_AtMaxValue_SkipsZero()
        {
            Assert.AreEqual((ushort)1, EnemyNetState.NextSequence(ushort.MaxValue));
            Assert.AreEqual((ushort)2, EnemyNetState.NextSequence(1));
        }

        [Test]
        public void Progress_BetweenStartAndEnd_ReturnsFraction()
        {
            EnemyTelegraphCue cue = MakeCue(EEnemyAttackShape.Fan, 2f, 90f, 10.0, 12.0);

            Assert.AreEqual(0f, cue.Progress(9.0), 0.0001f);
            Assert.AreEqual(0.5f, cue.Progress(11.0), 0.0001f);
            Assert.AreEqual(1f, cue.Progress(20.0), 0.0001f);
        }

        [Test]
        public void Progress_ZeroLength_ReturnsOneAfterStart()
        {
            EnemyTelegraphCue cue = MakeCue(EEnemyAttackShape.Circle, 2f, 0f, 10.0, 10.0);

            Assert.AreEqual(0f, cue.Progress(9.0), 0.0001f);
            Assert.AreEqual(1f, cue.Progress(10.0), 0.0001f);
        }

        [Test]
        public void Fill_ByShape_ReturnsExpectedPointCount()
        {
            Vector3[] buffer = new Vector3[EnemyTelegraphGeometry.MAX_POINTS];

            Assert.AreEqual(EnemyTelegraphGeometry.ARC_SEGMENTS + 3, EnemyTelegraphGeometry.Fill(buffer, MakeCue(EEnemyAttackShape.Fan, 2f, 90f, 0, 1)));
            Assert.AreEqual(EnemyTelegraphGeometry.ARC_SEGMENTS + 1, EnemyTelegraphGeometry.Fill(buffer, MakeCue(EEnemyAttackShape.Circle, 2f, 0f, 0, 1)));
            Assert.AreEqual(EnemyTelegraphGeometry.RECT_POINTS, EnemyTelegraphGeometry.Fill(buffer, MakeCue(EEnemyAttackShape.Dash, 6f, 1.5f, 0, 1)));
        }

        [Test]
        public void Fill_ZeroRangeOrShortBuffer_ReturnsZero()
        {
            Vector3[] buffer = new Vector3[EnemyTelegraphGeometry.MAX_POINTS];

            Assert.AreEqual(0, EnemyTelegraphGeometry.Fill(buffer, MakeCue(EEnemyAttackShape.Fan, 0f, 90f, 0, 1)));
            Assert.AreEqual(0, EnemyTelegraphGeometry.Fill(new Vector3[2], MakeCue(EEnemyAttackShape.Fan, 2f, 90f, 0, 1)));
        }

        [Test]
        public void Fill_Rectangle_SpansLengthAndWidth()
        {
            Vector3[] buffer = new Vector3[EnemyTelegraphGeometry.MAX_POINTS];
            EnemyTelegraphGeometry.Fill(buffer, MakeCue(EEnemyAttackShape.Line, 6f, 2f, 0, 1));

            Assert.AreEqual(6f, buffer[1].z - buffer[0].z, 0.0001f);
            Assert.AreEqual(2f, Mathf.Abs(buffer[0].x - buffer[3].x), 0.0001f);
        }

        [Test]
        public void ValidatePrefab_BuiltEnemyPrefab_Passes()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ENEMY_PREFAB_PATH);
            if (prefab == null)
            {
                Assert.Inconclusive("PF_NetEnemy가 아직 만들어지지 않았다. EnemyNetworkPrefabBuilder.BuildAll을 먼저 실행한다");
            }

            Assert.IsTrue(EnemyNetworkSpawner.ValidatePrefab(prefab, out string error), error);
        }

        [Test]
        public void ValidateEffectPrefab_BuiltEffectPrefab_Passes()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EFFECT_PREFAB_PATH);
            if (prefab == null)
            {
                Assert.Inconclusive("PF_NetEnemyEffect가 아직 만들어지지 않았다. EnemyNetworkPrefabBuilder.BuildAll을 먼저 실행한다");
            }

            Assert.IsTrue(EnemyNetworkSpawner.ValidateEffectPrefab(prefab, out string error), error);
        }

        [Test]
        public void ValidatePrefab_NullOrBareObject_Fails()
        {
            Assert.IsFalse(EnemyNetworkSpawner.ValidatePrefab(null, out string _));

            GameObject bare = new GameObject("Bare");
            try
            {
                Assert.IsFalse(EnemyNetworkSpawner.ValidatePrefab(bare, out string error));
                Assert.IsNotEmpty(error);
            }
            finally
            {
                Object.DestroyImmediate(bare);
            }
        }
    }
}
