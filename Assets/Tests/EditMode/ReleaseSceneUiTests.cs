// System
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Systems;
using Shinshi.UI;
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Contracts;
using Mirror;

namespace Shinshi.Tests
{
    public class ReleaseSceneUiTests
    {
        // 레이아웃 가져오기 도구가 만든 상자는 raycastTarget이 꺼져 있다. 그 위에 Button만 붙이면 코드 호출로는 동작하지만
        // 마우스 클릭이 닿지 않는다(릴리즈 빌드 캐릭터 선택 화면에서 실제로 발생)

        /****************************************
        *                Fields
        ****************************************/

        private bool _ownsPlayMode;
        private bool _previousRunInBackground;
        private bool _overrodeLocalPlayer;
        private NetworkIdentity _previousLocalPlayer;
        private GameObject _previewTestStage;
        private GameObject _previewTestPlayer;

        /****************************************
        *               Cleanup
        ****************************************/

        [UnityTearDown]
        public IEnumerator RestorePlayTestState()
        {
            if (!_ownsPlayMode)
            {
                yield break;
            }
            if (_overrodeLocalPlayer)
            {
                typeof(NetworkClient).GetProperty(nameof(NetworkClient.localPlayer)).SetValue(null, _previousLocalPlayer);
                _overrodeLocalPlayer = false;
                _previousLocalPlayer = null;
            }
            if (_previewTestPlayer != null)
            {
                Object.Destroy(_previewTestPlayer);
            }
            if (_previewTestStage != null)
            {
                Object.Destroy(_previewTestStage);
            }
            _previewTestPlayer = null;
            _previewTestStage = null;
            if (Application.isPlaying)
            {
                yield return null;
            }
            Application.runInBackground = _previousRunInBackground;
            _ownsPlayMode = false;
            if (Application.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(Constants.Scene.LOBBY)]
        [TestCase(Constants.Scene.CHARACTER_SELECT)]
        [TestCase(Constants.Scene.CHARACTER_CREATE)]
        [TestCase(Constants.Scene.SHINSHI_TOWN)]
        [TestCase(Constants.Scene.DUNGEON_FOREST)]
        [TestCase(Constants.Scene.DUNGEON_MUCHEOL)]
        [TestCase(Constants.Scene.DUNGEON_WOLBAEK)]
        public void Selectables_InReleaseScene_ReceivePointerRaycasts(string sceneName)
        {
            string path  = Constants.Scene.RELEASE_FOLDER + sceneName + Constants.Scene.EXTENSION;
            Scene  scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                int checkedCount = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
                    {
                        Assert.IsNotNull(selectable.targetGraphic, $"{sceneName}/{selectable.name}: targetGraphic이 없다");
                        Assert.IsTrue(ReceivesRaycast(selectable),
                            $"{sceneName}/{selectable.name}: 자기 아래 그래픽의 raycastTarget이 모두 꺼져 클릭이 닿지 않는다");
                        ++checkedCount;
                    }
                }

                Assert.Greater(checkedCount, 0, $"{sceneName}: 검사할 버튼이나 입력란이 없다");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void TownMenu_SavedScene_HasLiveTargetsAndNoPreviewController()
        {
            Scene scene = EditorSceneManager.OpenScene(Constants.Scene.RELEASE_FOLDER + Constants.Scene.SHINSHI_TOWN
                + Constants.Scene.EXTENSION, OpenSceneMode.Additive);
            try
            {
                HudMenuDropdown menu = InScene<HudMenuDropdown>(scene);
                var panel = Reference<RectTransform>(menu, "_panel");
                string[] names = { "인벤토리", "경매장", "우편함", "캐릭터 정보", "특성", "거래", "출정 준비", "환경설정", "캐릭터 선택", "게임 종료" };
                foreach (string name in names)
                {
                    Button button = MenuButton(panel, name);
                    Assert.That(button.gameObject.activeSelf, Is.True, name);
                    Assert.That(button.interactable, Is.True, name);
                    Assert.That(button.targetGraphic.raycastTarget, Is.True, name);
                    Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(1), name);
                    Assert.That(button.onClick.GetPersistentTarget(0), Is.Not.Null, name);
                    Assert.That(button.onClick.GetPersistentMethodName(0), Is.Not.Empty, name);
                }
                Assert.That(InScene<ReleaseMenuActions>(scene), Is.Not.Null);
                Assert.That(Reference<HotkeyRouter>(InScene<SettingsView>(scene), "_router"), Is.Not.Null);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Assert.That(root.GetComponentsInChildren<VillageUiPreview>(true), Is.Empty);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [UnityTest]
        public IEnumerator TownMenu_PlayMode_PointerOpensWindowsAndEscapeClosesThem()
        {
            EditorSceneManager.OpenScene(Constants.Scene.RELEASE_FOLDER + Constants.Scene.SHINSHI_TOWN
                + Constants.Scene.EXTENSION, OpenSceneMode.Single);
            yield return new EnterPlayMode();
            ConfigurePlayTest();
            yield return null;

            Scene scene = SceneManager.GetActiveScene();
            HudMenuDropdown menu = InScene<HudMenuDropdown>(scene);
            HotkeyRouter router = InScene<HotkeyRouter>(scene);
            Button menuButton = Reference<Button>(menu, "_menuButton");
            RectTransform panel = Reference<RectTransform>(menu, "_panel");
            string[] names = { "인벤토리", "경매장", "우편함", "캐릭터 정보", "특성" };
            EHotkeyAction[] actions = { EHotkeyAction.Inventory, EHotkeyAction.AuctionHouse, EHotkeyAction.MailBox,
                EHotkeyAction.CharacterInfo, EHotkeyAction.CharacterInfo };
            for (int i = 0; i < names.Length; ++i)
            {
                ClickWithRaycast(menuButton);
                yield return null;
                ClickWithRaycast(MenuButton(panel, names[i]));
                yield return null;
                HotkeyWindowToggle toggle = Window(router, actions[i]);
                Assert.That(toggle.IsOpen, Is.True, names[i]);
                if (actions[i] == EHotkeyAction.CharacterInfo)
                {
                    Assert.That(Reference<GameObject>(InScene<CharDetailView>(scene), "_previewStage").activeSelf, Is.True);
                }
                Assert.That(panel.gameObject.activeSelf, Is.False, names[i]);
                Assert.That(router.Trigger(EHotkeyAction.CloseWindow), Is.True);
                Assert.That(toggle.IsOpen, Is.False, names[i]);
                if (actions[i] == EHotkeyAction.CharacterInfo)
                {
                    Assert.That(Reference<GameObject>(InScene<CharDetailView>(scene), "_previewStage").activeSelf, Is.False);
                }
                Assert.That(panel.gameObject.activeSelf, Is.False);
            }

            // 햄버거 옆 HUD 바로가기 버튼
            Transform shortcuts = menuButton.transform.parent;
            string[] shortcutNames = { "Inventory", "Profile" };
            EHotkeyAction[] shortcutActions = { EHotkeyAction.Inventory, EHotkeyAction.CharacterInfo };
            for (int i = 0; i < shortcutNames.Length; ++i)
            {
                ClickWithRaycast(shortcuts.Find(shortcutNames[i]).GetComponent<Button>());
                yield return null;
                HotkeyWindowToggle toggle = Window(router, shortcutActions[i]);
                Assert.That(toggle.IsOpen, Is.True, shortcutNames[i]);
                Assert.That(router.Trigger(EHotkeyAction.CloseWindow), Is.True);
                Assert.That(toggle.IsOpen, Is.False, shortcutNames[i]);
            }

            ClickWithRaycast(menuButton);
            yield return null;
            yield return CaptureIfRequested("release-town-menu.png");
            ClickWithRaycast(MenuButton(panel, "환경설정"));
            yield return null;
            SettingsView settings = InScene<SettingsView>(scene);
            Assert.That(settings.gameObject.activeInHierarchy, Is.True);
            yield return CaptureIfRequested("release-town-settings.png");
            Assert.That(router.Trigger(EHotkeyAction.CloseWindow), Is.True);
            Assert.That(settings.gameObject.activeSelf, Is.False);
            Assert.That(panel.gameObject.activeSelf, Is.False);

            ClickWithRaycast(menuButton);
            yield return null;
            ClickWithRaycast(MenuButton(panel, "게임 종료"));
            yield return null;
            var popup = (ConfirmPopupView)new SerializedObject(InScene<UiPopupHub>(scene))
                .FindProperty("_confirms").GetArrayElementAtIndex((int)EPopupSize.Small).objectReferenceValue;
            Assert.That(popup.gameObject.activeInHierarchy, Is.True);
            Reference<Button>(popup, "_cancelButton").onClick.Invoke();
            Assert.That(popup.gameObject.activeSelf, Is.False);

            ClickWithRaycast(menuButton);
            yield return null;
            ClickWithRaycast(MenuButton(panel, "캐릭터 선택"));
            yield return null;
            Assert.That(popup.gameObject.activeInHierarchy, Is.True);
            Assert.That(router.Trigger(EHotkeyAction.CloseWindow), Is.True);
            Assert.That(popup.gameObject.activeSelf, Is.False);
            Assert.That(panel.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator CharacterPreview_LocalCharacterChanges_ReplacesLookAndHidesOnDisconnect()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            ConfigurePlayTest();
            var stage = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/02.Prefab/UI/Windows/PF_CharDetailPreviewStage.prefab"));
            _previewTestStage = stage;
            stage.SetActive(true);
            Assert.That(stage.transform.Find("무사 미리보기"), Is.Null);
            var player = new GameObject("PreviewTestPlayer");
            _previewTestPlayer = player;
            var identity = player.AddComponent<NetworkIdentity>();
            var combat = player.AddComponent<PlayerCombatState>();
            var localProperty = typeof(NetworkClient).GetProperty(nameof(NetworkClient.localPlayer));
            var appearanceField = typeof(PlayerCombatState).GetField("_appearance",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            _previousLocalPlayer = NetworkClient.localPlayer;
            _overrodeLocalPlayer = true;
            localProperty.SetValue(null, identity);
            var preview = stage.GetComponent<PlayerFemaleLookPreviewView>();
            Transform model = stage.transform.Find("CurrentCharacter");
            for (int body = 0; body <= 1; ++body)
            {
                var appearance = CharacterAppearanceCatalog.CreateP09(body, 1, 1, 1, 1, 1, 0, body == 0 ? 2 : 0, 31);
                appearanceField.SetValue(combat, appearance);
                yield return null;
                yield return null;
                Assert.That(preview.IsReady, Is.True,
                    $"error='{preview.LastError}', stageActive={stage.activeInHierarchy}, previewEnabled={preview.isActiveAndEnabled}, "
                    + $"localMatches={NetworkClient.localPlayer == identity}, modelActive={model.gameObject.activeSelf}, "
                    + PlayTestDiagnostics());
                Assert.That(preview.Look.BodyType, Is.EqualTo(body));
                Assert.That(model.gameObject.activeSelf, Is.True);
                CapturePreview(stage, "character-info-body-" + body + ".png");
            }
            localProperty.SetValue(null, null);
            yield return null;
            Assert.That(model.gameObject.activeSelf, Is.False);
        }

        private void ConfigurePlayTest()
        {
            _previousRunInBackground = Application.runInBackground;
            _ownsPlayMode = true;
            // MCP 실행은 에디터가 비활성 창이어도 이어진다. 제품 설정 대신 이 시험의 Update 실행만 보장한다.
            Application.runInBackground = true;
        }

        private static string PlayTestDiagnostics()
        {
            return $"focused={Application.isFocused}, background={Application.runInBackground}, "
                + $"playing={Application.isPlaying}, frame={Time.frameCount}, screen={Screen.width}x{Screen.height}";
        }

        private static void CapturePreview(GameObject stage, string name)
        {
            string directory = System.Environment.GetEnvironmentVariable("SHINSHI_UI_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }
            Camera camera = stage.GetComponentInChildren<Camera>();
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = camera.targetTexture;
            var texture = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            texture.Apply();
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name), texture.EncodeToPNG());
            Object.Destroy(texture);
            RenderTexture.active = previous;
        }

        private static IEnumerator CaptureIfRequested(string name)
        {
            string directory = System.Environment.GetEnvironmentVariable("SHINSHI_UI_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory, name));
                yield return null;
                yield return null;
            }
        }

        // 클릭은 맞은 그래픽에서 위로 올라가며 처리기를 찾으므로, targetGraphic이 꺼져 있어도 자기 아래 다른 그래픽이 받으면 눌린다
        private static bool ReceivesRaycast(Selectable selectable)
        {
            foreach (Graphic graphic in selectable.GetComponentsInChildren<Graphic>(true))
            {
                if ((graphic.raycastTarget) && (graphic.GetComponentInParent<Selectable>(true) == selectable))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ClickWithRaycast(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)),
                button = PointerEventData.InputButton.Left,
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0),
                $"{button.name}: active={button.gameObject.activeInHierarchy}, enabled={button.enabled}, "
                + $"interactable={button.interactable}, pointer={pointer.position}, rect={rect.rect}, "
                + $"eventScene={EventSystem.current.gameObject.scene.path}, buttonScene={button.gameObject.scene.path}, "
                + PlayTestDiagnostics());
            GameObject target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            Assert.That(target, Is.EqualTo(button.gameObject), button.name + " 앞에 다른 UI가 클릭을 가립니다: "
                + AnimationUtility.CalculateTransformPath(hits[0].gameObject.transform, null));
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
        }

        private static T InScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }
            Assert.Fail(typeof(T).Name + "이 없습니다.");
            return null;
        }

        private static T Reference<T>(Object target, string field) where T : Object
        {
            return (T)new SerializedObject(target).FindProperty(field).objectReferenceValue;
        }

        private static Button MenuButton(Transform panel, string name)
        {
            foreach (Button button in panel.GetComponentsInChildren<Button>(true))
            {
                if (button.name == name)
                {
                    return button;
                }
            }
            Assert.Fail(name + " 버튼이 없습니다.");
            return null;
        }

        private static HotkeyWindowToggle Window(HotkeyRouter router, EHotkeyAction action)
        {
            foreach (HotkeyWindowToggle toggle in router.GetComponents<HotkeyWindowToggle>())
            {
                if (new SerializedObject(toggle).FindProperty("_action").intValue == (int)action)
                {
                    return toggle;
                }
            }
            Assert.Fail(action + " 창이 없습니다.");
            return null;
        }
    }
}
