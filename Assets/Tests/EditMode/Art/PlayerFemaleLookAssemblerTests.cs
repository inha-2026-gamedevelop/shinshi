// System
using System;
using System.Collections.Generic;
using System.Linq;

// Unity
using UnityEngine;
using UnityEditor;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Contracts;

namespace Shinshi.Tests
{
    /// <summary> 실제 PF_Female{Warrior,Taoist,Spiritcaller}_Cute 프리팹으로 조립기를 검증한다.
    /// SO 카탈로그는 테스트 안에서 SerializedObject로 만든다 - LookEntry에 public 생성자가 없다 </summary>
    public class PlayerFemaleLookAssemblerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string WARRIOR_PREFAB_PATH      = "Assets/02.Prefab/Characters/PlayerFemaleTripo/PF_FemaleWarrior_Cute.prefab";
        private const string TAOIST_PREFAB_PATH       = "Assets/02.Prefab/Characters/PlayerFemaleTripo/PF_FemaleTaoist_Cute.prefab";
        private const string SPIRITCALLER_PREFAB_PATH = "Assets/02.Prefab/Characters/PlayerFemaleTripo/PF_FemaleSpiritcaller_Cute.prefab";

        private const int LOD_COUNT               = 3;
        private const int RENDERERS_PER_LOD        = 9;
        private const int CUTE_SHARED_MESH_COUNT   = 15;
        private const string CUTE_MESH_MARKER      = "Cute";
        private const string HAIR_BONE_MARKER      = "Hair";
        private const float  TRS_TOLERANCE         = 1e-4f;

        private const int UNKNOWN_LOOK_ID = 99;

        private GameObject _warriorPrefab;
        private GameObject _taoistPrefab;
        private GameObject _spiritcallerPrefab;
        private PlayerFemaleLookCatalogSO _catalog;
        private Transform _parent;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /****************************************
        *              Unity Event
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _warriorPrefab      = AssetDatabase.LoadAssetAtPath<GameObject>(WARRIOR_PREFAB_PATH);
            _taoistPrefab       = AssetDatabase.LoadAssetAtPath<GameObject>(TAOIST_PREFAB_PATH);
            _spiritcallerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SPIRITCALLER_PREFAB_PATH);
            Assert.IsNotNull(_warriorPrefab, WARRIOR_PREFAB_PATH);
            Assert.IsNotNull(_taoistPrefab, TAOIST_PREFAB_PATH);
            Assert.IsNotNull(_spiritcallerPrefab, SPIRITCALLER_PREFAB_PATH);

            _catalog = ScriptableObject.CreateInstance<PlayerFemaleLookCatalogSO>();
            BuildCatalog(_catalog, _warriorPrefab, _taoistPrefab, _spiritcallerPrefab);

            var parentObject = new GameObject("PlayerFemaleLookAssemblerTests_Parent");
            _parent = parentObject.transform;
            _spawned.Add(parentObject);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; ++i)
            {
                if (_spawned[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
            UnityEngine.Object.DestroyImmediate(_catalog);
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(1, 1)] [TestCase(1, 2)] [TestCase(1, 3)]
        [TestCase(2, 1)] [TestCase(2, 2)] [TestCase(2, 3)]
        [TestCase(3, 1)] [TestCase(3, 2)] [TestCase(3, 3)]
        public void TryAssemble_AllNineHairOutfitCombinations_SucceedWithIntactLodAndSecondaryMotion(int hairId, int outfitId)
        {
            CharacterLook look = new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE, hairId, outfitId);
            bool success = PlayerFemaleLookAssembler.TryAssemble(_catalog, look, _parent, null,
                out GameObject result, out string error);

            Assert.IsTrue(success, error);
            Track(result);
            Assert.IsTrue(result.activeSelf);
            Assert.AreSame(_parent, result.transform.parent);

            Animator animator = result.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator);
            Assert.IsNotNull(animator.avatar);
            Assert.IsTrue(animator.avatar.isValid);
            Assert.IsTrue(animator.avatar.isHuman);
            Assert.IsFalse(animator.applyRootMotion);

            Collider[] colliders = result.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
            {
                Assert.IsFalse(colliders[i].enabled, colliders[i].name);
            }

            LODGroup lodGroup = result.GetComponentInChildren<LODGroup>(true);
            Assert.IsNotNull(lodGroup);
            LOD[] lods = lodGroup.GetLODs();
            Assert.AreEqual(LOD_COUNT, lods.Length);
            for (int i = 0; i < lods.Length; ++i)
            {
                Assert.AreEqual(RENDERERS_PER_LOD, lods[i].renderers.Length, $"LOD{i}");
                for (int r = 0; r < lods[i].renderers.Length; ++r)
                {
                    Assert.IsNotNull(lods[i].renderers[r], $"LOD{i} renderer {r}");
                }
            }

            Shinshi.Art.PlayerFemaleCharacterSecondaryMotionBinding binding =
                result.GetComponentInChildren<Shinshi.Art.PlayerFemaleCharacterSecondaryMotionBinding>(true);
            Assert.IsNotNull(binding);
            Assert.IsTrue(binding.IsBound, binding.ValidationError);
            for (int i = 0; i < binding.Renderers.Length; ++i)
            {
                SkinnedMeshRenderer renderer = binding.Renderers[i];
                Assert.IsNotNull(renderer, $"renderer index {i}");
                Assert.IsTrue(renderer.transform.IsChildOf(result.transform), renderer.name);
            }

            GameObject nativeOutfit = ResolveNativePrefab(outfitId);
            PlayerFemaleCharacterSecondaryMotionBinding nativeBinding = nativeOutfit.GetComponentInChildren<PlayerFemaleCharacterSecondaryMotionBinding>(true);
            Assert.AreEqual(5, nativeBinding.Capsules.Length);
            Assert.AreEqual(nativeBinding.Capsules.Length, binding.Capsules.Length);
            for (int i = 0; i < nativeBinding.Capsules.Length; ++i)
            {
                SecondaryMotionCapsule expected = nativeBinding.Capsules[i];
                SecondaryMotionCapsule actual = binding.Capsules[i];
                Assert.AreEqual(expected.Anchor.name, actual.Anchor.name);
                Assert.IsTrue(actual.Anchor.IsChildOf(result.transform));
                Assert.AreEqual(expected.Start, actual.Start);
                Assert.AreEqual(expected.End, actual.End);
                Assert.AreEqual(expected.Radius, actual.Radius);
            }
        }

        [TestCase(1, 1)] [TestCase(1, 2)] [TestCase(1, 3)]
        [TestCase(2, 1)] [TestCase(2, 2)] [TestCase(2, 3)]
        [TestCase(3, 1)] [TestCase(3, 2)] [TestCase(3, 3)]
        public void TryAssemble_HairSkinning_MatchesNativeDonorAtRest(int hairId, int outfitId)
        {
            GameObject donor = UnityEngine.Object.Instantiate(ResolveNativePrefab(hairId), _parent);
            Track(donor);
            CharacterLook look = new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE, hairId, outfitId);
            Assert.IsTrue(PlayerFemaleLookAssembler.TryAssemble(_catalog, look, _parent, null,
                out GameObject result, out string error), error);
            Track(result);

            LOD[] donorLods = donor.GetComponentInChildren<LODGroup>(true).GetLODs();
            LOD[] actualLods = result.GetComponentInChildren<LODGroup>(true).GetLODs();
            for (int lod = 0; lod < LOD_COUNT; ++lod)
            {
                SkinnedMeshRenderer expected = FindHairRenderer(donorLods[lod]);
                SkinnedMeshRenderer actual = FindHairRenderer(actualLods[lod]);
                Assert.AreSame(expected.sharedMesh, actual.sharedMesh);
                Mesh expectedBake = new Mesh();
                Mesh actualBake = new Mesh();
                try
                {
                    expected.BakeMesh(expectedBake);
                    actual.BakeMesh(actualBake);
                    Vector3[] expectedVertices = expectedBake.vertices;
                    Vector3[] actualVertices = actualBake.vertices;
                    Assert.AreEqual(expectedVertices.Length, actualVertices.Length);
                    for (int vertex = 0; vertex < expectedVertices.Length; ++vertex)
                    {
                        Vector3 expectedPoint = donor.transform.InverseTransformPoint(expected.transform.TransformPoint(expectedVertices[vertex]));
                        Vector3 actualPoint = result.transform.InverseTransformPoint(actual.transform.TransformPoint(actualVertices[vertex]));
                        Assert.Less(Vector3.Distance(expectedPoint, actualPoint), 0.001f, $"hair={hairId}, outfit={outfitId}, LOD={lod}, vertex={vertex}");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(expectedBake);
                    UnityEngine.Object.DestroyImmediate(actualBake);
                }
            }
        }

        [Test]
        [TestCase(1, 1)] [TestCase(1, 2)] [TestCase(1, 3)]
        [TestCase(2, 1)] [TestCase(2, 2)] [TestCase(2, 3)]
        [TestCase(3, 1)] [TestCase(3, 2)] [TestCase(3, 3)]
        public void TryAssemble_CuteSharedMeshes_KeepSameMeshReferenceAsNativePrefab(int hairId, int outfitId)
        {
            GameObject nativePrefab = (outfitId == 1) ? _warriorPrefab : ((outfitId == 2) ? _taoistPrefab : _spiritcallerPrefab);
            bool wasDirty = EditorUtility.IsDirty(nativePrefab);
            Dictionary<string, Mesh> native = FindCuteMeshesByName(nativePrefab.transform);
            CharacterLook look = new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE, hairId, outfitId);
            Assert.IsTrue(PlayerFemaleLookAssembler.TryAssemble(_catalog, look, _parent, null,
                out GameObject result, out string error), error);
            Track(result);

            Dictionary<string, Mesh> assembled = FindCuteMeshesByName(result.transform);

            Assert.AreEqual(CUTE_SHARED_MESH_COUNT, native.Count);
            Assert.AreEqual(native.Count, assembled.Count);
            int shapeCount = 0;
            foreach (KeyValuePair<string, Mesh> entry in native)
            {
                Assert.IsTrue(assembled.ContainsKey(entry.Key), entry.Key);
                Assert.AreSame(entry.Value, assembled[entry.Key], entry.Key);
                shapeCount += entry.Value.blendShapeCount;
                for (int shape = 0; shape < entry.Value.blendShapeCount; ++shape)
                {
                    Assert.AreEqual(1, entry.Value.GetBlendShapeFrameCount(shape), entry.Key);
                    Assert.AreEqual(10000f, entry.Value.GetBlendShapeFrameWeight(shape, 0), entry.Key);
                }
            }

            Assert.AreEqual(54, shapeCount, "Cute의 전체 표정 채널을 보존해야 한다");
            Assert.AreEqual(wasDirty, EditorUtility.IsDirty(nativePrefab));
        }

        [Test]
        public void TryAssemble_HairFromDifferentClass_CommonBoneLocalTrsMatchesNativeOutfit()
        {
            CharacterLook look = new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_SUMMONER, CharacterAppearanceCatalog.OUTFIT_WARRIOR);
            Assert.IsTrue(PlayerFemaleLookAssembler.TryAssemble(_catalog, look, _parent, null,
                out GameObject result, out string error), error);
            Track(result);

            Dictionary<string, Transform> native = FindBonesByName(_warriorPrefab.transform);
            Dictionary<string, Transform> assembled = FindBonesByName(result.transform);

            int compared = 0;
            foreach (KeyValuePair<string, Transform> entry in native)
            {
                if (!assembled.TryGetValue(entry.Key, out Transform match))
                {
                    continue;
                }

                ++compared;
                Assert.Less(Vector3.Distance(entry.Value.localPosition, match.localPosition), TRS_TOLERANCE, entry.Key);
                Assert.Less(Quaternion.Angle(entry.Value.localRotation, match.localRotation), TRS_TOLERANCE, entry.Key);
                Assert.Less(Vector3.Distance(entry.Value.localScale, match.localScale), TRS_TOLERANCE, entry.Key);
            }

            // 공통 52본 이상은 이름으로 맞아떨어져야 한다. 여기서 0이면 비교 로직 자체가 깨진 것이다
            Assert.Greater(compared, 0);
        }

        [Test]
        public void TryAssemble_HairFromDifferentClass_CarriesOverAllDonorHairChainsAndCapsules()
        {
            // 도너(Spiritcaller) 원본의 헤어 체인/캡슐 수를 먼저 측정한다 - 이 인스턴스는 조립에 쓰이지 않는다
            GameObject donorReference = UnityEngine.Object.Instantiate(_spiritcallerPrefab);
            Track(donorReference);
            Shinshi.Art.PlayerFemaleCharacterSecondaryMotionBinding donorBinding =
                donorReference.GetComponentInChildren<Shinshi.Art.PlayerFemaleCharacterSecondaryMotionBinding>(true);
            Assert.IsNotNull(donorBinding);
            int donorHairChainCount    = donorBinding.Chains.Count(IsHairRelatedChain);
            int donorHairCapsuleCount  = donorBinding.Capsules.Count(IsHairRelatedCapsule);
            Assert.Greater(donorHairChainCount, 0, "도너 헤어에 체인이 하나도 없으면 이 테스트로 회귀를 잡을 수 없다");

            CharacterLook look = new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_SUMMONER, CharacterAppearanceCatalog.OUTFIT_WARRIOR);
            Assert.IsTrue(PlayerFemaleLookAssembler.TryAssemble(_catalog, look, _parent, null,
                out GameObject result, out string error), error);
            Track(result);

            Shinshi.Art.PlayerFemaleCharacterSecondaryMotionBinding resultBinding =
                result.GetComponentInChildren<Shinshi.Art.PlayerFemaleCharacterSecondaryMotionBinding>(true);
            Assert.IsNotNull(resultBinding);
            int resultHairChainCount   = resultBinding.Chains.Count(IsHairRelatedChain);
            int resultHairCapsuleCount = resultBinding.Capsules.Count(IsHairRelatedCapsule);

            Assert.AreEqual(donorHairChainCount, resultHairChainCount,
                "도너의 헤어 체인(예: Spiritcaller 앞머리 2단 체인)이 재조립 결과에서 빠지면 안 된다");
            Assert.AreEqual(donorHairCapsuleCount, resultHairCapsuleCount);

            for (int i = 0; i < resultBinding.Chains.Length; ++i)
            {
                if (!IsHairRelatedChain(resultBinding.Chains[i]))
                {
                    continue;
                }

                Shinshi.Art.SecondaryMotionChain chain = resultBinding.Chains[i];
                Assert.IsTrue(chain.Root.IsChildOf(result.transform));
                for (int b = 0; b < chain.BoneCount; ++b)
                {
                    Transform bone = chain.GetBone(b);
                    Assert.IsNotNull(bone);
                    Assert.IsTrue(bone.IsChildOf(result.transform));
                }
            }
        }

        [Test]
        public void TryAssemble_SameClassPrefabForHairAndOutfit_DoesNotDirtySourceAsset()
        {
            bool wasDirty = EditorUtility.IsDirty(_warriorPrefab);

            CharacterLook look = new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);
            Assert.IsTrue(PlayerFemaleLookAssembler.TryAssemble(_catalog, look, _parent, null,
                out GameObject result, out string error), error);
            Track(result);

            Assert.AreEqual(wasDirty, EditorUtility.IsDirty(_warriorPrefab));
        }

        [Test]
        public void TryAssemble_UnknownOutfitId_ReturnsFalseAndLeavesNoScratchObject()
        {
            CharacterLook look = new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_WARRIOR, UNKNOWN_LOOK_ID);

            bool success = PlayerFemaleLookAssembler.TryAssemble(_catalog, look, _parent, null,
                out GameObject result, out string error);

            Assert.IsFalse(success);
            Assert.IsNull(result);
            Assert.IsNotEmpty(error);
            Assert.AreEqual(0, _parent.childCount);
            Assert.IsNull(GameObject.Find("PlayerFemaleLookAssembler_Scratch"));
        }

        [Test]
        public void TryAssemble_ForcedFailureThenValidRetry_CleansUpAndSucceedsOnRetry()
        {
            GameObject brokenPrefab = new GameObject("PlayerFemaleLookAssemblerTests_BrokenPrefab");
            Track(brokenPrefab);
            brokenPrefab.SetActive(false);
            BuildCatalog(_catalog, brokenPrefab, _taoistPrefab, _spiritcallerPrefab);
            CharacterLook brokenLook = new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);
            int scratchCount = CountScratchInstances();
            Assert.IsFalse(PlayerFemaleLookAssembler.TryAssemble(_catalog, brokenLook, _parent, null,
                out GameObject brokenResult, out string failure));
            Assert.IsNull(brokenResult);
            StringAssert.Contains("Animator", failure);
            Assert.AreEqual(0, _parent.childCount);
            Assert.AreEqual(scratchCount, CountScratchInstances());

            BuildCatalog(_catalog, _warriorPrefab, _taoistPrefab, _spiritcallerPrefab);
            CharacterLook validLook = new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);
            bool success = PlayerFemaleLookAssembler.TryAssemble(_catalog, validLook, _parent, null,
                out GameObject validResult, out string error);

            Assert.IsTrue(success, error);
            Track(validResult);
            Assert.AreEqual(1, _parent.childCount);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private GameObject ResolveNativePrefab(int lookId)
        {
            return (lookId == 1) ? _warriorPrefab : ((lookId == 2) ? _taoistPrefab : _spiritcallerPrefab);
        }

        private static int CountScratchInstances()
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            int count = 0;
            for (int i = 0; i < objects.Length; ++i)
            {
                if ((objects[i].name == "PlayerFemaleLookAssembler_Scratch") && (!EditorUtility.IsPersistent(objects[i])))
                {
                    ++count;
                }
            }

            return count;
        }

        private static SkinnedMeshRenderer FindHairRenderer(LOD lod)
        {
            for (int i = 0; i < lod.renderers.Length; ++i)
            {
                if ((lod.renderers[i] is SkinnedMeshRenderer renderer) && (renderer.name.Contains("_Hair_")))
                {
                    return renderer;
                }
            }

            Assert.Fail("LOD에서 헤어 SkinnedMeshRenderer를 찾을 수 없다");
            return null;
        }

        private void Track(GameObject instance)
        {
            if (instance != null)
            {
                _spawned.Add(instance);
            }
        }

        private static void BuildCatalog(PlayerFemaleLookCatalogSO catalog, GameObject warrior, GameObject taoist, GameObject spiritcaller)
        {
            SerializedObject serialized = new SerializedObject(catalog);
            SetEntries(serialized, "_faces", new[] { (CharacterAppearanceCatalog.FACE_CUTE, warrior) });
            SetEntries(serialized, "_hairs", new[]
            {
                (CharacterAppearanceCatalog.HAIR_WARRIOR, warrior),
                (CharacterAppearanceCatalog.HAIR_SORCERER, taoist),
                (CharacterAppearanceCatalog.HAIR_SUMMONER, spiritcaller),
            });
            SetEntries(serialized, "_outfits", new[]
            {
                (CharacterAppearanceCatalog.OUTFIT_WARRIOR, warrior),
                (CharacterAppearanceCatalog.OUTFIT_SORCERER, taoist),
                (CharacterAppearanceCatalog.OUTFIT_SUMMONER, spiritcaller),
            });
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEntries(SerializedObject serialized, string propertyName, (int id, GameObject prefab)[] entries)
        {
            SerializedProperty array = serialized.FindProperty(propertyName);
            array.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; ++i)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_id").intValue = entries[i].id;
                element.FindPropertyRelative("_classPrefab").objectReferenceValue = entries[i].prefab;
            }
        }

        private static Dictionary<string, Mesh> FindCuteMeshesByName(Transform root)
        {
            Dictionary<string, Mesh> result = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                if (renderers[i].name.Contains(CUTE_MESH_MARKER))
                {
                    result[renderers[i].name] = renderers[i].sharedMesh;
                }
            }

            return result;
        }

        private static Dictionary<string, Transform> FindBonesByName(Transform root)
        {
            Dictionary<string, Transform> result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            CollectByName(root, result);
            return result;
        }

        private static void CollectByName(Transform root, Dictionary<string, Transform> result)
        {
            // 조립 결과는 이름이 겹치는 그래프트 본을 만들 수 있으므로 처음 만난 항목만 쓴다
            if (!result.ContainsKey(root.name))
            {
                result[root.name] = root;
            }

            for (int i = 0; i < root.childCount; ++i)
            {
                CollectByName(root.GetChild(i), result);
            }
        }

        private static bool IsHairRelatedChain(Shinshi.Art.SecondaryMotionChain chain)
        {
            if ((chain == null) || (chain.Root == null))
            {
                return false;
            }

            if (chain.Root.name.Contains(HAIR_BONE_MARKER))
            {
                return true;
            }

            for (int i = 0; i < chain.BoneCount; ++i)
            {
                Transform bone = chain.GetBone(i);
                if ((bone != null) && (bone.name.Contains(HAIR_BONE_MARKER)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsHairRelatedCapsule(Shinshi.Art.SecondaryMotionCapsule capsule)
        {
            return (capsule != null) && (capsule.Anchor != null) && (capsule.Anchor.name.Contains(HAIR_BONE_MARKER));
        }
    }
}
