// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

// 서드파티
using TMPro;

// 테스트
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class ModernDarkGrowthLayoutTests
    {
        // 신체 강화 · 수련(TrainingView)과 스킬 · 부적(SkillWindowView)은 각각 같은 뷰의 두 표시 모드다.
        // 레이아웃은 멱등이어야 하고, 모드 전환은 표시만 바꾸며 직렬화 참조 · 부적 묶음 · 닫기 확인 상태를 건드리지 않아야 한다

        private const string TRAINING_PREFAB = "Assets/02.Prefab/UI/Training/PF_TrainingWindow.prefab";
        private const string SKILL_PREFAB    = "Assets/02.Prefab/UI/Windows/PF_SkillWindow.prefab";
        private const string OPTION_PREFAB   = "Assets/02.Prefab/UI/Windows/PF_EquipmentOptionWindow.prefab";
        private const string APPLIED         = "MD_Applied";

        private readonly List<GameObject> _roots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _roots)
            {
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            _roots.Clear();
        }

        [Test]
        public void Training_EnsureModernLayout_CalledTwice_AppliesOnce()
        {
            var view = Spawn<TrainingView>(TRAINING_PREFAB);
            view.EnsureModernLayout();
            int children = view.transform.childCount;
            view.EnsureModernLayout();

            Assert.AreEqual(1, CountNamed(view.transform, APPLIED));
            Assert.AreEqual(children, view.transform.childCount);
        }

        [Test]
        public void Training_SetModernPresentation_SwitchesDecorAndTrainLabelOnly()
        {
            var view = Spawn<TrainingView>(TRAINING_PREFAB);
            view.EnsureModernLayout();
            var buttons = ModernDarkKit.Field<Button[]>(view, "_statButtons");
            var train   = ModernDarkKit.Field<Button>(view, "_trainButton");
            Button first = buttons[0];

            view.SetModernPresentation(true);
            Assert.IsTrue(Child(view.transform, "MD_A_Title").gameObject.activeSelf);
            Assert.IsFalse(Child(view.transform, "MD_B_Title").gameObject.activeSelf);
            Assert.AreEqual("강화", train.GetComponentInChildren<TMP_Text>(true).text);

            view.SetModernPresentation(false);
            Assert.IsFalse(Child(view.transform, "MD_A_Title").gameObject.activeSelf);
            Assert.IsTrue(Child(view.transform, "MD_B_Title").gameObject.activeSelf);
            Assert.AreEqual("수련 시작", train.GetComponentInChildren<TMP_Text>(true).text);

            Assert.AreSame(first, ModernDarkKit.Field<Button[]>(view, "_statButtons")[0]);
            Assert.AreSame(train, ModernDarkKit.Field<Button>(view, "_trainButton"));
            Assert.AreEqual(1, CountNamed(view.transform, APPLIED));
        }

        [Test]
        public void Training_SetModernPresentation_BeforeLayout_IsKeptForLaterEnsure()
        {
            var view = Spawn<TrainingView>(TRAINING_PREFAB);
            Assert.DoesNotThrow(() => view.SetModernPresentation(false));
            Assert.IsFalse(view.ModernAlternate);

            view.EnsureModernLayout();
            Assert.IsFalse(Child(view.transform, "MD_A_Title").gameObject.activeSelf);
            Assert.IsTrue(Child(view.transform, "MD_B_Title").gameObject.activeSelf);
        }

        [Test]
        public void Training_EnsureModernLayout_KeepsAllSerializedWidgetsReachable()
        {
            var view = Spawn<TrainingView>(TRAINING_PREFAB);
            view.EnsureModernLayout();

            foreach (string field in new[] { "_minusButton", "_plusButton", "_trainButton", "_revertButton", "_respecButton", "_closeButton" })
            {
                var button = ModernDarkKit.Field<Button>(view, field);
                Assert.IsNotNull(button, field);
                Assert.IsTrue(button.gameObject.activeInHierarchy, field);
            }

            var stages = ModernDarkKit.Field<TMP_Text[]>(view, "_stageLabels");
            Assert.AreEqual(5, stages.Length);
            foreach (TMP_Text stage in stages)
            {
                Assert.IsTrue(stage.gameObject.activeInHierarchy);
            }
        }

        [Test]
        public void Skill_EnsureModernLayout_CalledTwice_AppliesOnce()
        {
            var view = Spawn<SkillWindowView>(SKILL_PREFAB);
            view.EnsureModernLayout();
            int children = view.transform.childCount;
            view.EnsureModernLayout();

            Assert.AreEqual(1, CountNamed(view.transform, APPLIED));
            Assert.AreEqual(children, view.transform.childCount);
        }

        [Test]
        public void Skill_SetModernPresentation_SwitchesActionsAndModeButtons()
        {
            var view = Spawn<SkillWindowView>(SKILL_PREFAB);
            view.EnsureModernLayout();
            var register = ModernDarkKit.Field<Button>(view, "_registerButton");

            view.SetModernPresentation(true);
            Assert.IsTrue(Child(view.transform, "MD_A_ToSkill").gameObject.activeSelf);
            Assert.IsFalse(Child(view.transform, "MD_B_ToTalisman").gameObject.activeSelf);
            Assert.IsTrue(register.gameObject.activeSelf);

            view.SetModernPresentation(false);
            Assert.IsFalse(Child(view.transform, "MD_A_ToSkill").gameObject.activeSelf);
            Assert.IsTrue(Child(view.transform, "MD_B_ToTalisman").gameObject.activeSelf);
            Assert.IsFalse(register.gameObject.activeSelf);
            Assert.AreSame(register, ModernDarkKit.Field<Button>(view, "_registerButton"));
        }

        [Test]
        public void Skill_ModeButtons_SwitchPresentationInPlace()
        {
            var view = Spawn<SkillWindowView>(SKILL_PREFAB);
            view.EnsureModernLayout();
            view.SetModernPresentation(false);

            Child(view.transform, "MD_B_ToTalisman").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(view.ModernAlternate);

            Child(view.transform, "MD_A_ToSkill").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(view.ModernAlternate);
        }

        [Test]
        public void Skill_SetModernPresentation_KeepsTalismanGroupAndChoiceStateOwnership()
        {
            var view = Spawn<SkillWindowView>(SKILL_PREFAB);
            view.EnsureModernLayout();
            var group   = ModernDarkKit.Field<GameObject>(view, "_talismanGroup");
            var choices = ModernDarkKit.Field<Button[]>(view, "_choiceButtons");

            view.SetTalismanArea(false, "기본 공격에는 부적이 없습니다");
            view.SetChoice(0, 0, false, string.Empty, string.Empty, null, false, false, false);
            view.SetModernPresentation(true);
            view.SetModernPresentation(false);
            Assert.IsFalse(group.activeSelf);
            Assert.IsFalse(choices[0].gameObject.activeSelf);

            view.SetTalismanArea(true, string.Empty);
            view.SetChoice(0, 0, true, "효과", "상태", null, true, false, false);
            Assert.IsTrue(group.activeSelf);
            Assert.IsTrue(choices[0].gameObject.activeSelf);

            foreach (Button choice in choices)
            {
                Assert.IsTrue(choice.transform.IsChildOf(group.transform), "후보 칸은 부적 묶음 안에 남아 켜기 · 끄기를 따라야 한다");
            }
        }

        [Test]
        public void Skill_EnsureModernLayout_KeepsConfirmPanelHiddenAndOnTop()
        {
            var view = Spawn<SkillWindowView>(SKILL_PREFAB);
            view.ShowConfirm(false);
            view.EnsureModernLayout();
            view.SetModernPresentation(true);

            var confirm = ModernDarkKit.Field<GameObject>(view, "_confirmPanel");
            Assert.IsFalse(confirm.activeSelf);
            Assert.AreEqual(view.transform.childCount - 1, confirm.transform.GetSiblingIndex());

            view.ShowConfirm(true);
            Assert.IsTrue(confirm.activeSelf);
        }

        [Test]
        public void Training_Art_ExistsPerModeAsRawImage_WhenResourcesPresent()
        {
            var view = Spawn<TrainingView>(TRAINING_PREFAB);
            view.EnsureModernLayout();

            bool hasBody  = Resources.Load<Texture2D>("ModernDarkGrowth/BodyMeridian") != null;
            bool hasDummy = Resources.Load<Texture2D>("ModernDarkGrowth/TrainingDummy") != null;
            Assert.AreEqual(hasBody, view.transform.Find("MD_A_BodyArt") != null);
            Assert.AreEqual(hasDummy, view.transform.Find("MD_B_DummyArt") != null);

            view.SetModernPresentation(true);
            Transform body = view.transform.Find("MD_A_BodyArt");
            if (body != null)
            {
                Assert.IsTrue(body.gameObject.activeSelf);
                Assert.IsFalse(body.GetComponent<RawImage>().raycastTarget);
                Assert.IsNull(body.GetComponent<Image>());
            }

            view.SetModernPresentation(false);
            Transform dummy = view.transform.Find("MD_B_DummyArt");
            if (dummy != null)
            {
                Assert.IsTrue(dummy.gameObject.activeSelf);
            }

            if (body != null)
            {
                Assert.IsFalse(body.gameObject.activeSelf);
            }
        }

        [Test]
        public void Skill_Icons_FollowSelectedTabAndChoice_AndHideWithoutSprite()
        {
            var view = Spawn<SkillWindowView>(SKILL_PREFAB);
            view.EnsureModernLayout();
            var texture = new Texture2D(4, 4);
            Sprite skill    = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
            Sprite talisman = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);

            Image skillIcon    = Child(view.transform, "MD_B_SkillIcon").GetComponent<Image>();
            Image talismanIcon = Child(view.transform, "MD_A_TalismanIcon").GetComponent<Image>();
            Assert.IsFalse(skillIcon.enabled);
            Assert.IsFalse(talismanIcon.enabled);

            view.SetTab(0, "기술", skill, true);
            Assert.AreSame(skill, skillIcon.sprite);
            Assert.IsTrue(skillIcon.enabled);
            Assert.AreSame(skill, talismanIcon.sprite, "부적을 고르지 않았으면 기술 아이콘을 쓴다");

            view.SetChoice(0, 0, true, "효과", "상태", talisman, true, false, false);
            Assert.AreSame(talisman, talismanIcon.sprite);
            Assert.IsFalse(skillIcon.raycastTarget);

            view.SetChoice(0, 0, true, "효과", "상태", talisman, false, false, false);
            Assert.AreSame(skill, talismanIcon.sprite);

            view.SetTab(0, "기술", null, true);
            Assert.IsFalse(skillIcon.enabled);
            Assert.AreEqual(1, CountNamed(view.transform, APPLIED));

            Object.DestroyImmediate(texture);
        }

        [Test]
        public void Skill_SplitCostLabel_KeepsContentAndBreaksNameFromCost()
        {
            Assert.AreEqual("등록\n카드 1 + 정기 150 (50%)", SkillWindowModernLayout.SplitCostLabel("등록  카드 1 + 정기 150 (50%)"));
            Assert.AreEqual("강화", SkillWindowModernLayout.SplitCostLabel("강화"));
            Assert.IsNull(SkillWindowModernLayout.SplitCostLabel(null));
        }

        [Test]
        public void EquipmentOption_EnsureModernLayout_CalledTwice_AppliesOnce()
        {
            var view = Spawn<EquipmentOptionView>(OPTION_PREFAB);
            view.EnsureModernLayout();
            int children = view.transform.childCount;
            view.EnsureModernLayout();

            Assert.AreEqual(1, CountNamed(view.transform, APPLIED));
            Assert.AreEqual(children, view.transform.childCount);
            Assert.AreEqual(new Vector2(1500f, 820f), ((RectTransform)view.transform).sizeDelta);
        }

        [Test]
        public void EquipmentOption_EnsureModernLayout_KeepsAllSerializedWidgetsReachable()
        {
            var view = Spawn<EquipmentOptionView>(OPTION_PREFAB);
            view.gameObject.SetActive(true);
            view.EnsureModernLayout();

            foreach (string field in new[] { "_closeButton", "_rerollButton" })
            {
                var button = ModernDarkKit.Field<Button>(view, field);
                Assert.IsNotNull(button, field);
                Assert.IsTrue(button.gameObject.activeInHierarchy, field);
            }

            foreach (string field in new[] { "_itemLabel", "_costLabel", "_pityLabel", "_statusLabel", "_rerollLabel" })
            {
                var label = ModernDarkKit.Field<TMP_Text>(view, field);
                Assert.IsNotNull(label, field);
                Assert.IsTrue(label.gameObject.activeInHierarchy, field);
            }

            var slots = ModernDarkKit.Field<Button[]>(view, "_slotButtons");
            Assert.AreEqual(6, slots.Length);
            foreach (Button slot in slots)
            {
                Assert.IsTrue(slot.gameObject.activeInHierarchy);
            }

            foreach (TMP_Text line in ModernDarkKit.Field<TMP_Text[]>(view, "_lineLabels"))
            {
                Assert.IsTrue(line.gameObject.activeInHierarchy);
            }

            Assert.IsFalse(Child(view.transform, "MD_Title").GetComponent<TMP_Text>().raycastTarget);
        }

        private T Spawn<T>(string path) where T : Component
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            GameObject instance = Object.Instantiate(prefab);
            _roots.Add(instance);
            var view = instance.GetComponent<T>();
            Assert.IsNotNull(view, typeof(T).Name);
            return view;
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            Assert.IsNotNull(child, name);
            return child;
        }

        private static int CountNamed(Transform parent, string name)
        {
            int count = 0;
            for (int i = 0; i < parent.childCount; ++i)
            {
                if (parent.GetChild(i).name == name)
                {
                    ++count;
                }
            }

            return count;
        }
    }
}
