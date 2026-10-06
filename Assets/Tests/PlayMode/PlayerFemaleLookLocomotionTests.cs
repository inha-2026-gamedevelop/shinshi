// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Contracts;

namespace Shinshi.Tests
{
    /// <summary> 9조합을 실제 로코모션 컨트롤러로 재생해 본다. 이 어셈블리는 UnityEditor를 참조하지 않으므로
    /// 에셋 로드는 리플렉션으로 AssetDatabase를 호출한다(asmdef는 수정 범위 밖이다) </summary>
    public class PlayerFemaleLookLocomotionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string WARRIOR_PREFAB_PATH      = "Assets/02.Prefab/Characters/PlayerFemaleTripo/PF_FemaleWarrior_Cute.prefab";
        private const string TAOIST_PREFAB_PATH       = "Assets/02.Prefab/Characters/PlayerFemaleTripo/PF_FemaleTaoist_Cute.prefab";
        private const string SPIRITCALLER_PREFAB_PATH = "Assets/02.Prefab/Characters/PlayerFemaleTripo/PF_FemaleSpiritcaller_Cute.prefab";
        private const string LOCOMOTION_CONTROLLER_PATH =
            "Assets/01.Scripts/05.Art/Characters/PlayerFemaleTripo/Animations/AC_FemaleTripo_Locomotion.controller";

        private const string SPEED_PARAMETER     = "Speed";
        private const float  SPEED_IDLE           = 0f;
        private const float  SPEED_WALK           = 2.5f;
        private const float  SPEED_RUN            = 5f;
        private const float  PHASE_SECONDS        = 2f;
        private const int    REPEAT_CREATE_COUNT  = 3;
        private const int    LOD_COUNT            = 3;
        private const string EVIDENCE_ENV_VAR     = "SHINSHI_CUSTOMIZATION_EVIDENCE_DIRECTORY";
        private const int    EVIDENCE_RESOLUTION  = 512;
        private static readonly int[] G_LookCases = { 11, 12, 13, 21, 22, 23, 31, 32, 33 };

        private GameObject _warriorPrefab;
        private GameObject _taoistPrefab;
        private GameObject _spiritcallerPrefab;
        private PlayerFemaleLookCatalogSO _catalog;
        private RuntimeAnimatorController _controller;
        private Transform _parent;
        private readonly List<UnityEngine.Object> _spawned = new List<UnityEngine.Object>();

        /****************************************
        *              Unity Event
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _warriorPrefab      = LoadAsset<GameObject>(WARRIOR_PREFAB_PATH);
            _taoistPrefab       = LoadAsset<GameObject>(TAOIST_PREFAB_PATH);
            _spiritcallerPrefab = LoadAsset<GameObject>(SPIRITCALLER_PREFAB_PATH);
            _controller         = LoadAsset<RuntimeAnimatorController>(LOCOMOTION_CONTROLLER_PATH);
            Assert.IsNotNull(_warriorPrefab, WARRIOR_PREFAB_PATH);
            Assert.IsNotNull(_taoistPrefab, TAOIST_PREFAB_PATH);
            Assert.IsNotNull(_spiritcallerPrefab, SPIRITCALLER_PREFAB_PATH);
            Assert.IsNotNull(_controller, LOCOMOTION_CONTROLLER_PATH);

            _catalog = ScriptableObject.CreateInstance<PlayerFemaleLookCatalogSO>();
            BuildCatalog(_catalog, _warriorPrefab, _taoistPrefab, _spiritcallerPrefab);
            _spawned.Add(_catalog);

            var parentObject = new GameObject("PlayerFemaleLookLocomotionTests_Parent");
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
                    UnityEngine.Object.Destroy(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        /****************************************
        *                Tests
        ****************************************/

        [UnityTest]
        public IEnumerator Locomotion_NineCombinationsAtThreeSpeeds_StayFiniteAndPreserveOriginal(
            [ValueSource(nameof(G_LookCases))] int lookCase)
        {
            int hairId = lookCase / 10;
            int outfitId = lookCase % 10;
            CharacterLook look = new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE, hairId, outfitId);
            Assert.IsTrue(PlayerFemaleLookAssembler.TryAssemble(_catalog, look, _parent, _controller,
                out GameObject result, out string error), error);
            _spawned.Add(result);

            Animator animator = result.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator);

            Assert.IsNotNull(animator.avatar);
            Assert.IsTrue(animator.avatar.isValid);
            Assert.IsTrue(animator.avatar.isHuman);

            float[] speeds = { SPEED_IDLE, SPEED_WALK, SPEED_RUN };
            for (int s = 0; s < speeds.Length; ++s)
            {
                animator.SetFloat(SPEED_PARAMETER, speeds[s]);
                float elapsed = 0f;
                while (elapsed < PHASE_SECONDS)
                {
                    yield return null;
                    elapsed += Time.deltaTime;
                    AssertAllFinite(result);
                }

                string expectedClip = (s == 0) ? "AN_FemaleTripo_Idle" : ((s == 1) ? "Walk" : "Run");
                AnimatorClipInfo[] clipInfos = animator.GetCurrentAnimatorClipInfo(0);
                float expectedWeight = 0f;
                for (int i = 0; i < clipInfos.Length; ++i)
                {
                    if (clipInfos[i].clip.name == expectedClip)
                    {
                        expectedWeight += clipInfos[i].weight;
                    }
                }

                Assert.Greater(expectedWeight, 0.95f, $"Speed={speeds[s]}에서 {expectedClip}을 실제 재생해야 한다");
                AssertBakedMeshesFinite(result);
                CaptureEvidenceIfRequested(result, hairId, outfitId, (s == 0) ? "idle" : ((s == 1) ? "walk" : "run"));
            }

            AssertOriginalPrefabUnaffected(hairId, outfitId);
        }

        [UnityTest]
        public IEnumerator TryAssemble_CreateAndDestroyThreeTimes_LeavesNoLeakedObjects()
        {
            CharacterLook look = new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_SUMMONER, CharacterAppearanceCatalog.OUTFIT_WARRIOR);

            for (int i = 0; i < REPEAT_CREATE_COUNT; ++i)
            {
                Assert.IsTrue(PlayerFemaleLookAssembler.TryAssemble(_catalog, look, _parent, _controller,
                    out GameObject result, out string error), error);
                Assert.AreEqual(1, _parent.childCount);

                UnityEngine.Object.Destroy(result);
                yield return null;

                Assert.AreEqual(0, _parent.childCount);
                Assert.IsNull(GameObject.Find("PlayerFemaleLookAssembler_Scratch"));
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void AssertAllFinite(GameObject root)
        {
            Transform[] bones = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; ++i)
            {
                Vector3 position = bones[i].position;
                Quaternion rotation = bones[i].rotation;
                Assert.IsTrue(IsFinite(position), bones[i].name);
                Assert.IsTrue(IsFinite(rotation), bones[i].name);
            }

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                Assert.IsNotNull(renderers[i].sharedMesh, renderers[i].name);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static void AssertBakedMeshesFinite(GameObject root)
        {
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.AreEqual(27, renderers.Length);
            Mesh baked = new Mesh();
            try
            {
                for (int i = 0; i < renderers.Length; ++i)
                {
                    baked.Clear();
                    renderers[i].BakeMesh(baked);
                    Vector3[] vertices = baked.vertices;
                    Assert.AreEqual(renderers[i].sharedMesh.vertexCount, vertices.Length, renderers[i].name);
                    for (int vertex = 0; vertex < vertices.Length; ++vertex)
                    {
                        Assert.IsTrue(IsFinite(vertices[vertex]), $"{renderers[i].name}: vertex={vertex}");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(baked);
            }
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);
        }

        // 조립은 항상 Instantiate된 복제본만 건드린다. 원본 에셋의 렌더러 개수와 이름이 그대로인지 다시 읽어 확인한다
        private void AssertOriginalPrefabUnaffected(int hairId, int outfitId)
        {
            GameObject outfitPrefab = ResolveNativePrefab(outfitId);
            SkinnedMeshRenderer[] renderers = outfitPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.Greater(renderers.Length, 0);
            for (int i = 0; i < renderers.Length; ++i)
            {
                Assert.IsNotNull(renderers[i].sharedMesh, renderers[i].name);
            }
        }

        private GameObject ResolveNativePrefab(int classLookId)
        {
            if (classLookId == CharacterAppearanceCatalog.OUTFIT_WARRIOR)
            {
                return _warriorPrefab;
            }

            if (classLookId == CharacterAppearanceCatalog.OUTFIT_SORCERER)
            {
                return _taoistPrefab;
            }

            return _spiritcallerPrefab;
        }

        // 선택적 증거 출력. 환경변수가 없으면 아무것도 하지 않고, 실패해도 이미 끝난 필수 검증 결과를 바꾸지 않는다
        private void CaptureEvidenceIfRequested(GameObject result, int hairId, int outfitId, string phase)
        {
            string directory = Environment.GetEnvironmentVariable(EVIDENCE_ENV_VAR);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            LODGroup lodGroup = result.GetComponentInChildren<LODGroup>(true);
            for (int lod = 0; lod < LOD_COUNT; ++lod)
            {
                if (lodGroup != null)
                {
                    lodGroup.ForceLOD(lod);
                }

                string fileName = $"hair{hairId}_outfit{outfitId}_{phase}_lod{lod}.png";
                CaptureSingleFrame(result, Path.Combine(directory, fileName));
            }

            if (lodGroup != null)
            {
                lodGroup.ForceLOD(-1);
            }
        }

        private static void CaptureSingleFrame(GameObject target, string filePath)
        {
            var cameraObject = new GameObject("PlayerFemaleLookLocomotionTests_EvidenceCamera");
            var lightObject = new GameObject("PlayerFemaleLookLocomotionTests_EvidenceLight");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Material groundMaterial = null;
            try
            {
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 2f;
                light.color = Color.white;
                lightObject.transform.rotation = Quaternion.Euler(35f, 210f, 0f);
                ground.name = "PlayerFemaleLookLocomotionTests_EvidenceGround";
                ground.transform.position = new Vector3(target.transform.position.x, -0.01f, target.transform.position.z);
                ground.GetComponent<Collider>().enabled = false;
                Shader groundShader = Shader.Find("Universal Render Pipeline/Lit");
                Assert.IsNotNull(groundShader);
                groundMaterial = new Material(groundShader);
                groundMaterial.SetColor("_BaseColor", new Color(0.2f, 0.22f, 0.25f));
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.15f, 0.2f);
                Bounds bounds = ComputeBounds(target);
                camera.transform.position = bounds.center + (Vector3.forward * (bounds.extents.magnitude + 1f)) + Vector3.up * bounds.extents.y;
                camera.transform.LookAt(bounds.center);

                RenderTexture renderTexture = new RenderTexture(EVIDENCE_RESOLUTION, EVIDENCE_RESOLUTION, 24);
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = renderTexture;
                var capture = new Texture2D(EVIDENCE_RESOLUTION, EVIDENCE_RESOLUTION, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, EVIDENCE_RESOLUTION, EVIDENCE_RESOLUTION), 0, 0);
                capture.Apply();
                RenderTexture.active = previousActive;

                File.WriteAllBytes(filePath, capture.EncodeToPNG());

                UnityEngine.Object.Destroy(capture);
                camera.targetTexture = null;
                UnityEngine.Object.Destroy(renderTexture);
            }
            finally
            {
                UnityEngine.Object.Destroy(cameraObject);
                UnityEngine.Object.Destroy(lightObject);
                UnityEngine.Object.Destroy(ground);
                if (groundMaterial != null)
                {
                    UnityEngine.Object.Destroy(groundMaterial);
                }
            }
        }

        private static Bounds ComputeBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(target.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; ++i)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static void BuildCatalog(PlayerFemaleLookCatalogSO catalog, GameObject warrior, GameObject taoist, GameObject spiritcaller)
        {
            SetEntries(catalog, "_faces", new[] { (CharacterAppearanceCatalog.FACE_CUTE, warrior) });
            SetEntries(catalog, "_hairs", new[]
            {
                (CharacterAppearanceCatalog.HAIR_WARRIOR, warrior),
                (CharacterAppearanceCatalog.HAIR_SORCERER, taoist),
                (CharacterAppearanceCatalog.HAIR_SUMMONER, spiritcaller),
            });
            SetEntries(catalog, "_outfits", new[]
            {
                (CharacterAppearanceCatalog.OUTFIT_WARRIOR, warrior),
                (CharacterAppearanceCatalog.OUTFIT_SORCERER, taoist),
                (CharacterAppearanceCatalog.OUTFIT_SUMMONER, spiritcaller),
            });
        }

        // PlayMode 어셈블리는 UnityEditor를 참조하지 않는다. LookEntry에 public 생성자가 없어 SerializedObject를
        // 쓰는 EditMode 테스트 방식을 그대로 가져올 수 없으므로, private 필드를 리플렉션으로 직접 채운다
        private static void SetEntries(PlayerFemaleLookCatalogSO catalog, string fieldName, (int id, GameObject prefab)[] entries)
        {
            Type entryType = typeof(PlayerFemaleLookCatalogSO.LookEntry);
            Array array = Array.CreateInstance(entryType, entries.Length);
            FieldInfo idField = entryType.GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo prefabField = entryType.GetField("_classPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < entries.Length; ++i)
            {
                object entry = Activator.CreateInstance(entryType);
                idField.SetValue(entry, entries[i].id);
                prefabField.SetValue(entry, entries[i].prefab);
                array.SetValue(entry, i);
            }

            FieldInfo targetField = typeof(PlayerFemaleLookCatalogSO).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            targetField.SetValue(catalog, array);
        }

        private static T LoadAsset<T>(string path) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            Type assetDatabaseType = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
            MethodInfo method = assetDatabaseType.GetMethod("LoadAssetAtPath", new[] { typeof(string), typeof(Type) });
            return (T)method.Invoke(null, new object[] { path, typeof(T) });
#else
            return null;
#endif
        }
    }
}
