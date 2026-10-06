// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Contracts;

namespace Shinshi.Tests
{
    /// <summary> 최소 P09 계층(이름만 같은 빈 오브젝트)으로 적용기의 켜기와 끄기, 재질 교체, 실패 시 무변경을 검증한다 </summary>
    public class PlayerCharacterLookApplierTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int FEMALE = CharacterAppearance.BODY_TYPE_FEMALE;
        private const int MALE   = CharacterAppearance.BODY_TYPE_MALE;

        private const int SKIN_COUNT       = CharacterAppearanceCatalog.P09_SKIN_MAX;
        private const int EYE_COUNT        = CharacterAppearanceCatalog.P09_EYE_COLOR_MAX;
        private const int HAIR_COLOR_COUNT = CharacterAppearanceCatalog.P09_HAIR_COLOR_MAX;
        private const int BUST_COUNT       = CharacterAppearanceCatalog.P09_BUST_MAX;

        private const string RENDERER_NODE   = "TestRenderer";
        private const string UNRELATED_MATERIAL = "M_Other";

        private static readonly string[] G_OutfitParts  = { "Head", "Chest", "Arm", "Waist", "Leg" };
        private static readonly string[] G_BareBodyParts = { "Chest", "Arm", "Leg" };

        private readonly List<Object> _created = new List<Object>();

        private GameObject                  _root;
        private PlayerCharacterLookCatalogSO _catalog;
        private Material[]                  _femaleSkins;
        private Material[]                  _maleSkins;
        private Material[]                  _eyes;
        private Material[]                  _hairColors;
        private Material                    _unrelated;

        /****************************************
        *               Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            Shader shader = FindAnyShader();
            if (shader == null)
            {
                Assert.Ignore("재질을 만들 셰이더를 찾지 못했다");
            }

            _femaleSkins = NewMaterials(shader, "M_P09_Female_Skin_", SKIN_COUNT);
            _maleSkins   = NewMaterials(shader, "M_P09_Male_Skin_", SKIN_COUNT);
            _eyes        = NewMaterials(shader, "M_P09_Eye_", EYE_COUNT);
            _hairColors  = NewMaterials(shader, "M_P09_Hair_", HAIR_COLOR_COUNT);
            _unrelated   = NewMaterial(shader, UNRELATED_MATERIAL);

            _catalog = ScriptableObject.CreateInstance<PlayerCharacterLookCatalogSO>();
            _created.Add(_catalog);
            FillCatalog();

            _root = BuildHierarchy(shader);
            _created.Add(_root);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; ++i)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryApply_NullInstanceOrCatalog_ReturnsFalse()
        {
            CharacterP09Look look = Look(CharacterAppearanceCatalog.CreateDefault(ContractCodes.PlayerClass.WARRIOR));

            Assert.IsFalse(PlayerCharacterLookApplier.TryApply(null, _catalog, look, out string error1));
            Assert.IsNotEmpty(error1);
            Assert.IsFalse(PlayerCharacterLookApplier.TryApply(_root, null, look, out string error2));
            Assert.IsNotEmpty(error2);
        }

        [Test]
        public void TryApply_MissingNode_ReturnsFalseAndChangesNothing()
        {
            Object.DestroyImmediate(Find("Male_FacialHair_05").gameObject);
            string before = Snapshot();

            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, MaleLook(2, 1, 4, 3, 3, 1, 21), out string error);

            Assert.IsFalse(applied);
            StringAssert.Contains("Male_FacialHair_05", error);
            Assert.AreEqual(before, Snapshot());
        }

        [Test]
        public void TryApply_MissingBustBone_ReturnsFalseAndChangesNothing()
        {
            Object.DestroyImmediate(Find("bust_01_R").gameObject);
            string before = Snapshot();

            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, FemaleLook(1, 1, 1, 1, 1, 2, 31), out string error);

            Assert.IsFalse(applied);
            StringAssert.Contains("가슴", error);
            Assert.AreEqual(before, Snapshot());
        }

        [Test]
        public void TryApply_UnknownColorId_ReturnsFalseAndChangesNothing()
        {
            string before = Snapshot();

            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, FemaleLook(1, 1, 1, EYE_COUNT + 1, 1, 2, 31), out _);

            Assert.IsFalse(applied);
            Assert.AreEqual(before, Snapshot());
        }

        [Test]
        public void TryApply_FemaleBustSizeMissingFromCatalog_ReturnsFalseAndChangesNothing()
        {
            string before = Snapshot();

            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, FemaleLook(1, 1, 1, 1, 1, BUST_COUNT + 1, 31), out _);

            Assert.IsFalse(applied);
            Assert.AreEqual(before, Snapshot());
        }

        [Test]
        public void TryApply_Male_TogglesBranchesFacialHairAndHair()
        {
            // 남성, 얼굴 2, 헤어 1, 수염 3
            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, MaleLook(2, 1, 3, 1, 1, 1, 31), out string error);

            Assert.IsTrue(applied, error);
            Assert.IsTrue(IsOn("Male"));
            Assert.IsFalse(IsOn("Female"));
            Assert.IsFalse(IsOn("Male_Face_01"));
            Assert.IsTrue(IsOn("Male_Face_02"));
            Assert.IsFalse(IsOn("Male_Face_03"));
            Assert.IsTrue(IsOn("Hair_01"));
            Assert.IsFalse(IsOn("Hair_02"));
            Assert.IsFalse(IsOn("SkinHead_Male"));
            Assert.IsFalse(IsOn("SkinHead_Female"));
            for (int i = 1; i <= CharacterAppearanceCatalog.P09_FACIAL_HAIR_MAX; ++i)
            {
                Assert.AreEqual(i == 3, IsOn($"Male_FacialHair_{i:D2}"), $"facialHair {i}");
            }
        }

        [Test]
        public void TryApply_MaleWithoutFacialHair_TurnsAllFacialHairOff()
        {
            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, MaleLook(1, 2, 0, 1, 1, 1, 31), out string error);

            Assert.IsTrue(applied, error);
            Assert.IsTrue(IsOn("Hair_02"));
            Assert.IsFalse(IsOn("Hair_01"));
            for (int i = 1; i <= CharacterAppearanceCatalog.P09_FACIAL_HAIR_MAX; ++i)
            {
                Assert.IsFalse(IsOn($"Male_FacialHair_{i:D2}"), $"facialHair {i}");
            }
        }

        [Test]
        public void TryApply_Male_ResetsBustScaleToOne()
        {
            Find("bust_01_L").localScale = Vector3.one * 2f;
            Find("bust_01_R").localScale = Vector3.one * 2f;

            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, MaleLook(1, 1, 0, 1, 1, 1, 31), out string error);

            Assert.IsTrue(applied, error);
            Assert.AreEqual(Vector3.one, Find("bust_01_L").localScale);
            Assert.AreEqual(Vector3.one, Find("bust_01_R").localScale);
        }

        [Test]
        public void TryApply_Female_UsesCatalogBustScaleOnBothBones()
        {
            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, FemaleLook(3, 3, 1, 1, 1, 3, 31), out string error);

            Assert.IsTrue(applied, error);
            _catalog.TryGetBustScale(3, out Vector3 expected);
            Assert.AreEqual(expected, Find("bust_01_L").localScale);
            Assert.AreEqual(expected, Find("bust_01_R").localScale);
            Assert.IsTrue(IsOn("Female"));
            Assert.IsFalse(IsOn("Male"));
            Assert.IsFalse(IsOn("Female_Face_01"));
            Assert.IsFalse(IsOn("Female_Face_02"));
            Assert.IsTrue(IsOn("Female_Face_03"));
            for (int i = 1; i <= CharacterAppearanceCatalog.P09_FACIAL_HAIR_MAX; ++i)
            {
                Assert.IsFalse(IsOn($"Male_FacialHair_{i:D2}"), $"facialHair {i}");
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void TryApply_DifferentBustSizes_ProduceDifferentScales(int bust)
        {
            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, FemaleLook(1, 1, 1, 1, 1, bust, 31), out string error);

            Assert.IsTrue(applied, error);
            _catalog.TryGetBustScale(bust, out Vector3 expected);
            Assert.AreEqual(expected, Find("bust_01_L").localScale);
        }

        [Test]
        public void TryApply_SkinHead_TogglesOnlyActiveSexSkinHeadAndBothHairsOff()
        {
            PlayerCharacterLookApplier.TryApply(_root, _catalog, MaleLook(1, 3, 0, 1, 1, 1, 31), out string maleError);
            Assert.IsTrue(IsOn("SkinHead_Male"), maleError);
            Assert.IsFalse(IsOn("SkinHead_Female"));
            Assert.IsFalse(IsOn("Hair_01"));
            Assert.IsFalse(IsOn("Hair_02"));

            PlayerCharacterLookApplier.TryApply(_root, _catalog, FemaleLook(1, 3, 1, 1, 1, 2, 31), out string femaleError);
            Assert.IsTrue(IsOn("SkinHead_Female"), femaleError);
            Assert.IsFalse(IsOn("SkinHead_Male"));
        }

        [Test]
        public void TryApply_PartialOutfit_TogglesArmorAndBareBodyForActiveSex()
        {
            int mask = CharacterAppearanceCatalog.P09_OUTFIT_CHEST | CharacterAppearanceCatalog.P09_OUTFIT_LEG;

            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, FemaleLook(1, 1, 1, 1, 1, 2, mask), out string error);

            Assert.IsTrue(applied, error);
            Assert.IsFalse(IsOn("Fem_Armor_003_Head"));
            Assert.IsTrue(IsOn("Fem_Armor_003_Chest"));
            Assert.IsFalse(IsOn("Fem_Armor_003_Arm"));
            Assert.IsFalse(IsOn("Fem_Armor_003_Waist"));
            Assert.IsTrue(IsOn("Fem_Armor_003_Leg"));
            Assert.IsFalse(IsOn("Female_Body_Chest"));
            Assert.IsTrue(IsOn("Female_Body_Arm"));
            Assert.IsFalse(IsOn("Female_Body_Leg"));
        }

        [Test]
        public void TryApply_NoOutfit_ShowsAllBareBodiesAndHidesAllArmor()
        {
            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, MaleLook(1, 1, 0, 1, 1, 1, 0), out string error);

            Assert.IsTrue(applied, error);
            for (int i = 0; i < G_OutfitParts.Length; ++i)
            {
                Assert.IsFalse(IsOn($"Male_Armor_003_{G_OutfitParts[i]}"), G_OutfitParts[i]);
            }

            for (int i = 0; i < G_BareBodyParts.Length; ++i)
            {
                Assert.IsTrue(IsOn($"Male_Body_{G_BareBodyParts[i]}"), G_BareBodyParts[i]);
            }
        }

        [Test]
        public void TryApply_FullOutfit_HidesAllBareBodiesAndShowsAllArmor()
        {
            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog,
                FemaleLook(1, 1, 1, 1, 1, 2, CharacterAppearanceCatalog.P09_OUTFIT_ALL), out string error);

            Assert.IsTrue(applied, error);
            for (int i = 0; i < G_OutfitParts.Length; ++i)
            {
                Assert.IsTrue(IsOn($"Fem_Armor_003_{G_OutfitParts[i]}"), G_OutfitParts[i]);
            }

            for (int i = 0; i < G_BareBodyParts.Length; ++i)
            {
                Assert.IsFalse(IsOn($"Female_Body_{G_BareBodyParts[i]}"), G_BareBodyParts[i]);
            }
        }

        [Test]
        public void TryApply_InactiveSex_KeepsPrefabDefaultArmorAndFaceOne()
        {
            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, MaleLook(3, 1, 0, 1, 1, 1, 0), out string error);

            Assert.IsTrue(applied, error);
            Assert.IsFalse(IsOn("Female"));
            Assert.IsTrue(IsOn("Female_Face_01"));
            Assert.IsFalse(IsOn("Female_Face_02"));
            Assert.IsTrue(IsOn("Fem_Armor_003_Chest"));
            Assert.IsFalse(IsOn("Female_Body_Chest"));
        }

        [Test]
        public void TryApply_ReplacesSkinEyeAndHairColorSlotsAndKeepsOthers()
        {
            // 피부 2, 눈 4, 헤어 색 7
            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, FemaleLook(1, 1, 7, 4, 2, 2, 31), out string error);

            Assert.IsTrue(applied, error);
            Material[] slots = Find(RENDERER_NODE).GetComponent<MeshRenderer>().sharedMaterials;
            Assert.AreSame(_femaleSkins[1], slots[0]);
            Assert.AreSame(_maleSkins[1], slots[1]);
            Assert.AreSame(_eyes[3], slots[2]);
            Assert.AreSame(_hairColors[6], slots[3]);
            Assert.AreSame(_unrelated, slots[4]);
        }

        [Test]
        public void TryApply_AppliedTwiceWithDifferentLooks_ReplacesAgainByNamePrefix()
        {
            PlayerCharacterLookApplier.TryApply(_root, _catalog, FemaleLook(1, 1, 7, 4, 2, 2, 31), out _);

            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, FemaleLook(1, 1, 1, 1, 3, 2, 31), out string error);

            Assert.IsTrue(applied, error);
            Material[] slots = Find(RENDERER_NODE).GetComponent<MeshRenderer>().sharedMaterials;
            Assert.AreSame(_femaleSkins[2], slots[0]);
            Assert.AreSame(_eyes[0], slots[2]);
            Assert.AreSame(_hairColors[0], slots[3]);
        }

        [Test]
        public void TryApply_Success_ReturnsEmptyError()
        {
            bool applied = PlayerCharacterLookApplier.TryApply(_root, _catalog, FemaleLook(1, 1, 1, 1, 1, 2, 31), out string error);

            Assert.IsTrue(applied);
            Assert.IsEmpty(error);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static CharacterP09Look Look(CharacterAppearance appearance)
        {
            CharacterP09Look.TryFrom(appearance, out CharacterP09Look look);
            return look;
        }

        // 순서는 face, hair, hairColor, eye, skin, bust, outfit, 남성은 hair 다음 수염
        private static CharacterP09Look FemaleLook(int face, int hair, int hairColor, int eye, int skin, int bust, int outfit)
        {
            return new CharacterP09Look(FEMALE, face, hair, hairColor, eye, skin, 0, bust, outfit);
        }

        private static CharacterP09Look MaleLook(int face, int hair, int facialHair, int hairColor, int eye, int skin, int outfit)
        {
            return new CharacterP09Look(MALE, face, hair, hairColor, eye, skin, facialHair, 0, outfit);
        }

        private Transform Find(string name)
        {
            Transform[] all = _root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; ++i)
            {
                if (all[i].name == name)
                {
                    return all[i];
                }
            }

            Assert.Fail($"테스트 계층에 {name} 노드가 없다");
            return null;
        }

        private bool IsOn(string name) { return Find(name).gameObject.activeSelf; }

        // 변경 감지용, 켜짐 상태와 크기와 재질 이름과 참조를 모두 이어 붙인다
        private string Snapshot()
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            Transform[] all = _root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; ++i)
            {
                builder.Append(all[i].name).Append(':').Append(all[i].gameObject.activeSelf).Append(':').Append(all[i].localScale).Append('|');
            }

            Renderer[] renderers = _root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                Material[] slots = renderers[i].sharedMaterials;
                for (int s = 0; s < slots.Length; ++s)
                {
                    builder.Append(slots[s].GetInstanceID()).Append(',');
                }
            }

            return builder.ToString();
        }

        private GameObject BuildHierarchy(Shader shader)
        {
            GameObject root = new GameObject("P09Root");
            Transform female = NewChild(root.transform, PlayerCharacterLookApplier.FEMALE_BRANCH);
            Transform male   = NewChild(root.transform, PlayerCharacterLookApplier.MALE_BRANCH);

            AddSexParts(female, "Female", "Fem");
            AddSexParts(male, "Male", "Male");
            NewChild(root.transform, "Hair_01");
            NewChild(root.transform, "Hair_02");
            NewChild(female, "SkinHead_Female");
            NewChild(male, "SkinHead_Male");
            for (int i = 1; i <= CharacterAppearanceCatalog.P09_FACIAL_HAIR_MAX; ++i)
            {
                NewChild(male, $"Male_FacialHair_{i:D2}");
            }

            NewChild(root.transform, "bust_01_L").localScale = Vector3.one * 2f;
            NewChild(root.transform, "bust_01_R").localScale = Vector3.one * 2f;

            Transform rendererNode = NewChild(root.transform, RENDERER_NODE);
            MeshRenderer meshRenderer = rendererNode.gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = new[] { _femaleSkins[0], _maleSkins[0], _eyes[0], _hairColors[0], _unrelated };
            return root;
        }

        private static void AddSexParts(Transform branch, string sex, string armorPrefix)
        {
            for (int face = 1; face <= CharacterAppearanceCatalog.P09_FACE_MAX; ++face)
            {
                NewChild(branch, $"{sex}_Face_{face:D2}");
            }

            for (int i = 0; i < G_OutfitParts.Length; ++i)
            {
                NewChild(branch, $"{armorPrefix}_Armor_003_{G_OutfitParts[i]}");
            }

            for (int i = 0; i < G_BareBodyParts.Length; ++i)
            {
                NewChild(branch, $"{sex}_Body_{G_BareBodyParts[i]}");
            }
        }

        private static Transform NewChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        // 카탈로그의 비공개 배열은 SerializedObject로 채운다
        private void FillCatalog()
        {
            SerializedObject serialized = new SerializedObject(_catalog);
            SetMaterials(serialized, "_femaleSkins", _femaleSkins);
            SetMaterials(serialized, "_maleSkins", _maleSkins);
            SetMaterials(serialized, "_eyes", _eyes);
            SetMaterials(serialized, "_hairColors", _hairColors);

            SerializedProperty bust = serialized.FindProperty("_bustScales");
            bust.arraySize = BUST_COUNT;
            for (int i = 0; i < BUST_COUNT; ++i)
            {
                bust.GetArrayElementAtIndex(i).vector3Value = Vector3.one * (1f + ((i + 1) * 0.25f));
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetMaterials(SerializedObject serialized, string field, Material[] materials)
        {
            SerializedProperty array = serialized.FindProperty(field);
            Assert.IsNotNull(array, field);
            array.arraySize = materials.Length;
            for (int i = 0; i < materials.Length; ++i)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = materials[i];
            }
        }

        private Material[] NewMaterials(Shader shader, string prefix, int count)
        {
            Material[] materials = new Material[count];
            for (int i = 0; i < count; ++i)
            {
                materials[i] = NewMaterial(shader, $"{prefix}{i + 1:D2}");
            }

            return materials;
        }

        private Material NewMaterial(Shader shader, string name)
        {
            Material material = new Material(shader) { name = name };
            _created.Add(material);
            return material;
        }

        private static Shader FindAnyShader()
        {
            string[] names = { "Sprites/Default", "Hidden/InternalErrorShader", "Universal Render Pipeline/Lit", "Standard" };
            for (int i = 0; i < names.Length; ++i)
            {
                Shader shader = Shader.Find(names[i]);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }
    }
}
