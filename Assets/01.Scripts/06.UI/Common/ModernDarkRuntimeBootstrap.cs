// System
using System;
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Shinshi.Systems;

namespace Shinshi.UI
{
    /// <summary>
    /// Builds the extra mockup windows (party recruit, training, body strengthening, talisman) once per real client scene
    /// and appends their entries to the HUD menu. Skipped on the headless server and when the catalog is missing
    /// </summary>
    public static class ModernDarkRuntimeBootstrap
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string RUNNER_NAME        = "MD_BootstrapRunner";
        private const string BODY_LABEL         = "신체강화";
        private const string TRAINING_LABEL     = "수련";
        private const string TALISMAN_LABEL     = "부적";
        private const string RECRUIT_LABEL      = "파티모집";
        private const float  RETRY_STEP_SECONDS = 0.5f;
        private const int    RETRY_COUNT        = 8;

        private static Runner _runner        = null;
        private static int    _doneHandle    = -1;
        private static bool   _catalogWarned = false;

        /****************************************
        *                Methods
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _runner        = null;
            _doneHandle    = -1;
            _catalogWarned = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (IsHeadless())
            {
                return;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Begin(SceneManager.GetActiveScene());
        }

        private static bool IsHeadless()
        {
            return (Application.isBatchMode) || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                Begin(scene);
            }
        }

        private static void Begin(Scene scene)
        {
            if ((!scene.IsValid()) || (scene.handle == _doneHandle))
            {
                return;
            }

            if (_runner == null)
            {
                var go = new GameObject(RUNNER_NAME);
                go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<Runner>();
            }

            _runner.StopAllCoroutines();
            _runner.StartCoroutine(Setup(scene));
        }

        // HUD가 늦게 생기는 씬이 있어 몇 번만 다시 찾는다. 프레임마다 찾지 않는다
        private static IEnumerator Setup(Scene scene)
        {
            var wait = new WaitForSecondsRealtime(RETRY_STEP_SECONDS);
            for (int attempt = 0; attempt < RETRY_COUNT; ++attempt)
            {
                if (!scene.isLoaded)
                {
                    yield break;
                }

                if (TrySetup(scene))
                {
                    _doneHandle = scene.handle;
                    yield break;
                }

                yield return wait;
            }
        }

        private static bool TrySetup(Scene scene)
        {
            var menu = UnityEngine.Object.FindFirstObjectByType<HudMenuDropdown>(FindObjectsInactive.Include);
            if ((menu == null) || (menu.gameObject.scene.handle != scene.handle))
            {
                return false;
            }

            var skill  = UnityEngine.Object.FindFirstObjectByType<SkillWindowView>(FindObjectsInactive.Include);
            var router = UnityEngine.Object.FindFirstObjectByType<HotkeyRouter>(FindObjectsInactive.Include);
            Transform parent = ResolveParent(menu, skill, out int insertIndex);
            if (parent == null)
            {
                return false;
            }

            ModernDarkUiCatalog catalog = ModernDarkUiCatalog.Load();
            if ((catalog == null) && (!_catalogWarned))
            {
                _catalogWarned = true;
                Debug.LogWarning("[ModernDark] 카탈로그가 없어 수련·신체강화 메뉴를 만들지 않는다. Shinshi/UI/Build Modern Dark Catalog를 실행한다");
            }

            TrainingHost training = BuildTraining(catalog, parent, ref insertIndex, router);
            if (training != null)
            {
                menu.AddEntry(BODY_LABEL, () => training.Toggle(true));
                menu.AddEntry(TRAINING_LABEL, () => training.Toggle(false));
            }

            if (skill != null)
            {
                skill.EnsureModernLayout();
                var skillHost = new SkillHost(skill, FindToggle(skill));
                menu.AddEntry(TALISMAN_LABEL, skillHost.ToggleTalisman);
            }

            PartyRecruitView recruit = PartyRecruitView.Create(parent, (catalog != null) ? catalog.Font : null);
            if (recruit != null)
            {
                PlaceAt(recruit.transform, ref insertIndex);
                AttachEscape(recruit.gameObject, router, () => CloseRecruit(recruit));
                menu.AddEntry(RECRUIT_LABEL, () => ToggleRecruit(recruit));
            }

            return true;
        }

        private static Transform ResolveParent(HudMenuDropdown menu, SkillWindowView skill, out int insertIndex)
        {
            insertIndex = -1;
            if ((skill != null) && (skill.transform.parent != null))
            {
                insertIndex = skill.transform.GetSiblingIndex() + 1;
                return skill.transform.parent;
            }

            Canvas canvas = menu.GetComponentInParent<Canvas>();
            return (canvas != null) ? canvas.rootCanvas.transform : null;
        }

        private static void PlaceAt(Transform window, ref int insertIndex)
        {
            if (insertIndex < 0)
            {
                return;
            }

            window.SetSiblingIndex(Mathf.Min(insertIndex, window.parent.childCount - 1));
            ++insertIndex;
        }

        private static HotkeyWindowToggle FindToggle(SkillWindowView skill)
        {
            foreach (HotkeyWindowToggle toggle in UnityEngine.Object.FindObjectsByType<HotkeyWindowToggle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (ModernDarkKit.Field<GameObject>(toggle, "_window") == skill.gameObject)
                {
                    return toggle;
                }
            }

            return null;
        }

        private static TrainingHost BuildTraining(ModernDarkUiCatalog catalog, Transform parent, ref int insertIndex, HotkeyRouter router)
        {
            if ((catalog == null) || (catalog.TrainingPrefab == null))
            {
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(catalog.TrainingPrefab, parent, false);
            TrainingView view   = instance.GetComponentInChildren<TrainingView>(true);
            if (view == null)
            {
                Debug.LogWarning("[ModernDark] 수련 프리팹에 TrainingView가 없어 사용하지 않는다");
                UnityEngine.Object.Destroy(instance);
                return null;
            }

            view.EnsureModernLayout();
            instance.SetActive(false);
            PlaceAt(instance.transform, ref insertIndex);
            var host = new TrainingHost(instance, view);
            AttachEscape(instance, router, host.CloseIfOpen);
            return host;
        }

        // 던전 서버에서는 열지 않는다. 마을·오프라인 편집 실행에서는 열리고 요청은 창이 마을 접속일 때만 보낸다
        private static void ToggleRecruit(PartyRecruitView recruit)
        {
            if ((!recruit.IsOpen) && (ClientRoles.IsDungeon()))
            {
                return;
            }

            recruit.SetOpen(!recruit.IsOpen);
        }

        private static bool CloseRecruit(PartyRecruitView recruit)
        {
            if (!recruit.IsOpen)
            {
                return false;
            }

            recruit.SetOpen(false);
            return true;
        }

        private static void AttachEscape(GameObject window, HotkeyRouter router, Func<bool> handler)
        {
            if (router == null)
            {
                return;
            }

            window.AddComponent<WindowEscape>().Setup(router, handler);
        }

        /****************************************
        *                Types
        ****************************************/

        private sealed class TrainingHost
        {
            private readonly GameObject   _root;
            private readonly TrainingView _view;

            public TrainingHost(GameObject root, TrainingView view)
            {
                _root = root;
                _view = view;
            }

            private bool IsOpen => _root.activeInHierarchy;

            public void Toggle(bool alternate)
            {
                if ((IsOpen) && (_view.ModernAlternate == alternate))
                {
                    Close();
                    return;
                }

                _view.SetModernPresentation(alternate);
                _root.SetActive(true);
                _view.gameObject.SetActive(true);
            }

            public bool CloseIfOpen()
            {
                if (!IsOpen)
                {
                    return false;
                }

                Close();
                return true;
            }

            private void Close()
            {
                _view.Close();
                _root.SetActive(false);
            }
        }

        private sealed class SkillHost
        {
            private readonly SkillWindowView    _view;
            private readonly HotkeyWindowToggle _toggle;

            public SkillHost(SkillWindowView view, HotkeyWindowToggle toggle)
            {
                _view   = view;
                _toggle = toggle;
            }

            public void ToggleTalisman()
            {
                if ((_view.gameObject.activeSelf) && (_view.ModernAlternate))
                {
                    Close();
                    return;
                }

                _view.SetModernPresentation(true);
                if (_toggle != null)
                {
                    _toggle.SetOpen(true);
                    return;
                }

                _view.SetOpen(true);
            }

            private void Close()
            {
                if (_toggle != null)
                {
                    _toggle.RequestClose();
                    return;
                }

                _view.SetOpen(false);
            }
        }

        private sealed class WindowEscape : MonoBehaviour
        {
            private HotkeyRouter _router  = null;
            private Func<bool>   _handler = null;

            public void Setup(HotkeyRouter router, Func<bool> handler)
            {
                _router  = router;
                _handler = handler;
                if (isActiveAndEnabled)
                {
                    _router.Register(EHotkeyAction.CloseWindow, _handler);
                }
            }

            private void OnEnable()
            {
                if ((_router != null) && (_handler != null))
                {
                    _router.Register(EHotkeyAction.CloseWindow, _handler);
                }
            }

            private void OnDisable()
            {
                if ((_router != null) && (_handler != null))
                {
                    _router.Unregister(EHotkeyAction.CloseWindow, _handler);
                }
            }
        }

        private sealed class Runner : MonoBehaviour
        {
        }
    }
}
