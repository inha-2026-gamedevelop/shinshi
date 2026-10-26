// System
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public sealed class EnemyCorpseRagdollTests
    {
        private const int   OVER_OLD_LIMIT    = 12;
        private const int   CORPSE_LIMIT      = 16;
        private const int   OVER_CORPSE_LIMIT = 20;
        private const int   INITIAL_BATCH     = 8;
        private const float FREEZE_WAIT       = 3.25f;
        private const float BONE_LENGTH       = 0.5f;
        private const float BODY_RADIUS       = 0.08f;
        private const float SOURCE_HEIGHT     = 3f;
        private const float SOURCE_SPACING    = 2f;
        private const float FLOOR_SIZE        = 100f;
        private const float JOINT_LIMIT       = 30f;

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Mesh>       _meshes  = new List<Mesh>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            EnemyCorpse.ClearAll();
            yield return null;
            var floor = new GameObject("CorpseTestGround");
            floor.transform.position = Vector3.down * BONE_LENGTH;
            floor.AddComponent<BoxCollider>().size = new Vector3(FLOOR_SIZE, 1f, FLOOR_SIZE);
            _objects.Add(floor);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            EnemyCorpse.ClearAll();
            for (int i = 0; i < _objects.Count; ++i)
            {
                if (_objects[i] != null)
                {
                    Object.Destroy(_objects[i]);
                }
            }
            yield return null;
            for (int i = 0; i < _meshes.Count; ++i)
            {
                if (_meshes[i] != null)
                {
                    Object.Destroy(_meshes[i]);
                }
            }
            _objects.Clear();
            _meshes.Clear();
        }

        [UnityTest]
        public IEnumerator Launch_PrebuiltRigOverEight_AllCorpsesUseBonePhysics()
        {
            LaunchBatch(CreateRig(true), OVER_OLD_LIMIT);
            yield return null;
            yield return null;
            AssertRagdolls(OVER_OLD_LIMIT);
        }

        [UnityTest]
        public IEnumerator Launch_GeneratedRigOverEight_AllCorpsesUseBonePhysics()
        {
            LaunchBatch(CreateRig(false), OVER_OLD_LIMIT);
            yield return null;
            yield return null;
            AssertRagdolls(OVER_OLD_LIMIT);
        }

        [UnityTest]
        public IEnumerator Launch_AfterEarlierRagdollsFreeze_NewCorpseStillUsesBonePhysics()
        {
            GameObject source = CreateRig(true);
            LaunchBatch(source, INITIAL_BATCH);
            yield return null;
            yield return new WaitForSeconds(FREEZE_WAIT);
            EnemyCorpse[] earlier = Object.FindObjectsByType<EnemyCorpse>(FindObjectsSortMode.None);
            Assert.AreEqual(INITIAL_BATCH, earlier.Length);
            foreach (EnemyCorpse corpse in earlier)
            {
                foreach (Rigidbody body in corpse.GetComponentsInChildren<Rigidbody>())
                {
                    Assert.IsTrue(body.isKinematic, "Earlier corpse should have stopped simulating");
                }
            }
            source.transform.position = new Vector3(INITIAL_BATCH * SOURCE_SPACING, SOURCE_HEIGHT, 0f);
            Assert.IsTrue(EnemyCorpse.Launch(source, Vector3.forward, 1f, 0));
            yield return null;
            yield return null;
            AssertRagdolls(INITIAL_BATCH + 1);
        }

        [UnityTest]
        public IEnumerator Launch_OverCorpseCapacity_RetainsSixteenRagdolls()
        {
            LaunchBatch(CreateRig(true), OVER_CORPSE_LIMIT);
            yield return null;
            yield return null;
            AssertRagdolls(CORPSE_LIMIT);
        }

        [UnityTest]
        public IEnumerator Launch_ModelWithoutBones_KeepsFallbackFlight()
        {
            var source = new GameObject("UnriggedCorpseSource");
            source.transform.position = Vector3.up * SOURCE_HEIGHT;
            _objects.Add(source);
            Assert.IsTrue(EnemyCorpse.Launch(source, Vector3.forward, 1f, 0));
            yield return null;
            yield return null;
            EnemyCorpse[] corpses = Object.FindObjectsByType<EnemyCorpse>(FindObjectsSortMode.None);
            Assert.AreEqual(1, corpses.Length);
            Assert.IsNull(corpses[0].GetComponent<EnemyCorpseRagdoll>());
            Assert.Greater(corpses[0].transform.position.z, source.transform.position.z);
        }

        private void LaunchBatch(GameObject source, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                source.transform.position = new Vector3(i * SOURCE_SPACING, SOURCE_HEIGHT, 0f);
                Assert.IsTrue(EnemyCorpse.Launch(source, Vector3.forward, 1f, 0));
            }
        }

        private static void AssertRagdolls(int expected)
        {
            EnemyCorpse[] corpses = Object.FindObjectsByType<EnemyCorpse>(FindObjectsSortMode.None);
            Assert.AreEqual(expected, corpses.Length);
            foreach (EnemyCorpse corpse in corpses)
            {
                Assert.IsNotNull(corpse.GetComponent<EnemyCorpseRagdoll>(), corpse.name + " stayed rigid");
                Assert.Greater(corpse.GetComponentsInChildren<Rigidbody>().Length, 0);
            }
        }

        private GameObject CreateRig(bool prebuilt)
        {
            var owner = new GameObject(prebuilt ? "PrebuiltCorpseSource" : "GeneratedCorpseSource");
            _objects.Add(owner);
            var hips = new GameObject("Hips").transform;
            hips.SetParent(owner.transform, false);
            var spine = new GameObject("Spine").transform;
            spine.SetParent(hips, false);
            spine.localPosition = Vector3.up * BONE_LENGTH;
            var head = new GameObject("Head").transform;
            head.SetParent(spine, false);
            head.localPosition = Vector3.up * BONE_LENGTH;
            Transform[] bones = { hips, spine, head };
            var skin = owner.AddComponent<SkinnedMeshRenderer>();
            skin.rootBone = hips;
            skin.bones = bones;
            skin.enabled = false;
            var mesh = new Mesh { name = "CorpseTestSkin" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right * BODY_RADIUS, Vector3.up * BONE_LENGTH };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1f }
            };
            mesh.bindposes = new[]
            {
                hips.worldToLocalMatrix * owner.transform.localToWorldMatrix,
                spine.worldToLocalMatrix * owner.transform.localToWorldMatrix,
                head.worldToLocalMatrix * owner.transform.localToWorldMatrix
            };
            skin.sharedMesh = mesh;
            _meshes.Add(mesh);
            if (prebuilt)
            {
                Rigidbody parent = AddBody(hips);
                AddBody(spine);
                CharacterJoint joint = spine.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parent;
                joint.lowTwistLimit = new SoftJointLimit { limit = -JOINT_LIMIT };
                joint.highTwistLimit = new SoftJointLimit { limit = JOINT_LIMIT };
                joint.swing1Limit = new SoftJointLimit { limit = JOINT_LIMIT };
                joint.swing2Limit = new SoftJointLimit { limit = JOINT_LIMIT };
            }
            return owner;
        }

        private static Rigidbody AddBody(Transform bone)
        {
            Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = BODY_RADIUS;
            capsule.height = BONE_LENGTH;
            capsule.enabled = false;
            return body;
        }
    }
}
