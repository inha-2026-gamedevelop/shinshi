using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Editor
{
    /// <summary> 오프라인 UI 테스트 창. 화면 목록 · 프리셋 · 응답 정책 · 안전한 테스트 사본 열기 · 요청 이력을 한곳에서 다룬다. 네트워크에는 접속하지 않는다 </summary>
    public sealed class UiSandboxWindow : EditorWindow
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH = "Shinshi/UI Sandbox";
        private const float LIST_WIDTH = 230.0f;
        private const int HISTORY_VISIBLE = 12;
        private const string DEFAULT_PRESET_NAME = "UiSandboxScenario";

        [SerializeField] private UiSandboxScenarioSO _preset = null;
        [SerializeField] private string _selectedPageId = string.Empty;
        [SerializeField] private string _workingScenarioJson = string.Empty;
        [SerializeField] private string _baselineScenarioJson = string.Empty;

        private UiSandboxContext _context = null;
        private IUiSandboxPage _attachedPage = null;
        private IUiSandboxPage _switchedPage = null;
        private bool _hostSafe = false;
        private bool _networkWasActive = false;
        private int _snapshotVersion = -1;
        private readonly List<IUiSandboxPage> _orderedPages = new List<IUiSandboxPage>();
        private bool _orderDirty = true;
        private bool _wasBusy = false;
        private bool _forceRebind = true;
        private const ulong NO_SCENE_HANDLE = ulong.MaxValue;
        private ulong _boundSceneHandle = NO_SCENE_HANDLE;
        private string _lastBuildMessage = string.Empty;
        private bool _lastBuildSucceeded = false;
        private Vector2 _listScroll = Vector2.zero;
        private Vector2 _contentScroll = Vector2.zero;

        /****************************************
        *               Lifecycle
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void Open()
        {
            UiSandboxWindow window = GetWindow<UiSandboxWindow>();
            window.titleContent = new GUIContent("UI 샌드박스");
            window.minSize = new Vector2(720.0f, 480.0f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("UI 샌드박스");
            UiSandboxCatalog.Changed += OnCatalogChanged;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += CaptureSnapshot;
            _orderDirty = true;
            _forceRebind = true;
        }

        private void OnDisable()
        {
            UiSandboxCatalog.Changed -= OnCatalogChanged;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= CaptureSnapshot;

            CaptureSnapshot();
            DetachPage();
            _switchedPage = null;
            if (_context != null)
            {
                _context.Dispose();
                _context = null;
            }
        }

        private void OnCatalogChanged()
        {
            _orderDirty = true;
            Repaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                CaptureSnapshot();
            }

            _forceRebind = true;
            Repaint();
        }

        // 작업 중 시나리오 값을 직렬화 필드에 보관해 도메인 리로드와 Play 진입 뒤에 복원한다
        private void CaptureSnapshot()
        {
            if ((_context == null) || (_context.IsDisposed))
            {
                return;
            }

            _workingScenarioJson  = _context.CaptureWorkingJson();
            _baselineScenarioJson = _context.CaptureBaselineJson();
            _snapshotVersion      = _context.Scenario.Version;
        }

        private void CaptureSnapshotIfChanged()
        {
            if ((_context != null) && (!_context.IsDisposed) && (_context.Scenario.Version != _snapshotVersion))
            {
                CaptureSnapshot();
            }
        }

        private void OnEditorUpdate()
        {
            CaptureSnapshotIfChanged();
            bool networkWas = _networkWasActive;
            WatchNetwork();
            if (networkWas != _networkWasActive)
            {
                Repaint();
            }

            bool busy = (_context != null) && (_context.IsBusy);
            if ((busy) || (_wasBusy))
            {
                Repaint();
            }

            _wasBusy = busy;
        }

        /****************************************
        *                  GUI
        ****************************************/

        private void OnGUI()
        {
            EnsureContext();
            SyncRoots();

            IReadOnlyList<IUiSandboxPage> pages = GetOrderedPages();
            if (pages.Count == 0)
            {
                EditorGUILayout.HelpBox("등록된 화면이 없다. 각 모듈이 UiSandboxCatalog.Register로 화면을 등록하면 여기에 나타난다.", MessageType.Info);
                return;
            }

            IUiSandboxPage selected = ResolveSelected(pages);
            if (!ReferenceEquals(selected, _switchedPage))
            {
                SwitchPage(selected);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPageList(pages, selected);
                DrawContent(selected);
            }

            CaptureSnapshotIfChanged();
        }

        private void DrawPageList(IReadOnlyList<IUiSandboxPage> pages, IUiSandboxPage selected)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(LIST_WIDTH)))
            {
                EditorGUILayout.LabelField("화면", EditorStyles.boldLabel);
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

                for (int i = 0; i < pages.Count; ++i)
                {
                    IUiSandboxPage page = pages[i];
                    string kind = IsRealView(page) ? "실제 화면" : "계약 전용";
                    bool isSelected = ReferenceEquals(page, selected);
                    bool toggled = GUILayout.Toggle(isSelected, $"[{kind}] {page.Title}", EditorStyles.miniButton);
                    if ((toggled) && (!isSelected))
                    {
                        _selectedPageId = page.Id;
                        GUI.FocusControl(null);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawContent(IUiSandboxPage page)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                _contentScroll = EditorGUILayout.BeginScrollView(_contentScroll);

                DrawPageHeader(page);
                DrawNetworkWarning();
                DrawPresetSection();
                DrawResponseSection();
                DrawActionSection(page);
                DrawPageControls(page);
                DrawStatusSection();

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPageHeader(IUiSandboxPage page)
        {
            EditorGUILayout.LabelField(page.Title, EditorStyles.boldLabel);

            if (IsRealView(page))
            {
                EditorGUILayout.LabelField("원본 씬", page.SourceScenePath);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "계약 전용 화면이다. 실제 씬이 없어 값을 편집하고 가짜 요청만 확인한다. 가짜 요청은 '안전한 빈 테스트 씬 열기'로 만든 씬에서 Play할 때만 보낸다.",
                    MessageType.Info);
            }

            string requirement = DescribeRequirement(page);
            if (requirement.Length > 0)
            {
                EditorGUILayout.HelpBox(requirement, MessageType.None);
            }

            if (UiSandboxSceneBuilder.TryGetActiveOwnedSandboxScene(out Scene scene))
            {
                EditorGUILayout.LabelField("연결된 테스트 씬", scene.path);
            }
            else if (page.HasView)
            {
                EditorGUILayout.HelpBox(
                    IsRealView(page)
                        ? "연결된 테스트 씬이 없다. '안전한 테스트 사본 열기'로 사본을 만들어야 화면이 연결된다. 원본 씬에는 연결하지 않는다."
                        : "연결된 테스트 씬이 없다. '안전한 빈 테스트 씬 열기'로 빈 씬을 만들어야 화면이 연결된다. 지금 열린 씬에는 연결하지 않는다.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
        }

        /// <summary> 화면 머리말에 보여 줄 Requirement 문구. 실제 화면과 계약 전용 화면 모두 비어 있지 않으면 보여 주고, 비었으면 빈 문자열이다 </summary>
        public static string DescribeRequirement(IUiSandboxPage page)
        {
            if ((page == null) || (string.IsNullOrWhiteSpace(page.Requirement)))
            {
                return string.Empty;
            }

            return UiSandboxSceneBuilder.UsesSourceCopy(page)
                ? $"제약과 한계: {page.Requirement}"
                : $"필요한 화면: {page.Requirement}";
        }

        private void DrawNetworkWarning()
        {
            if (UiSandboxSafetyPolicy.IsNetworkActive(out string reason))
            {
                EditorGUILayout.HelpBox(reason, MessageType.Warning);
            }
        }

        private void DrawPresetSection()
        {
            EditorGUILayout.LabelField("시나리오 프리셋", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            UiSandboxScenarioSO picked = (UiSandboxScenarioSO)EditorGUILayout.ObjectField("프리셋", _preset, typeof(UiSandboxScenarioSO), false);
            if (EditorGUI.EndChangeCheck())
            {
                _preset = picked;
                if (_preset != null)
                {
                    LoadPreset(_preset);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_preset == null))
                {
                    if (GUILayout.Button("프리셋 다시 불러오기"))
                    {
                        LoadPreset(_preset);
                    }

                    if (GUILayout.Button("불러온 프리셋에 저장"))
                    {
                        SaveToLoadedPreset();
                    }
                }

                if (GUILayout.Button("다른 이름으로 저장"))
                {
                    SaveAsNewPreset();
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawResponseSection()
        {
            EditorGUILayout.LabelField("응답 정책", EditorStyles.boldLabel);

            UiSandboxScenarioSO scenario = _context.Scenario;
            UiSandboxResponsePolicy policy = UiSandboxResponsePolicy.FromScenario(scenario);

            EditorGUI.BeginChangeCheck();
            EUiSandboxResponseMode mode = (EUiSandboxResponseMode)EditorGUILayout.EnumPopup("응답 방식", policy.Mode);
            float delay = EditorGUILayout.Slider("지연(초)", policy.DelaySeconds, 0.0f, 10.0f);
            string error = EditorGUILayout.TextField("오류 문구", scenario.GetString(UiSandboxScenarioKeys.RESPONSE_ERROR, string.Empty));
            if (EditorGUI.EndChangeCheck())
            {
                scenario.SetString(UiSandboxScenarioKeys.RESPONSE_MODE, mode.ToString());
                scenario.SetFloat(UiSandboxScenarioKeys.RESPONSE_DELAY, Mathf.Clamp(delay, 0.0f, UiSandboxScenarioKeys.MAX_DELAY_SECONDS));
                scenario.SetString(UiSandboxScenarioKeys.RESPONSE_ERROR, error);
            }

            if (mode != EUiSandboxResponseMode.Success)
            {
                EditorGUILayout.HelpBox($"오류 문구를 비우면 기본 문구를 쓴다: {new UiSandboxResponsePolicy(mode, 0.0f, string.Empty).ResolveFailureMessage()}", MessageType.None);
            }

            EditorGUILayout.Space();
        }

        private void DrawActionSection(IUiSandboxPage page)
        {
            EditorGUILayout.LabelField("동작", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("적용"))
                {
                    ApplyPage();
                }

                if (GUILayout.Button("리셋"))
                {
                    ResetAll();
                }

                using (new EditorGUI.DisabledScope(!UiSandboxSceneBuilder.CanOpenSafeScene(page, EditorApplication.isPlayingOrWillChangePlaymode)))
                {
                    if (GUILayout.Button(IsRealView(page) ? "안전한 테스트 사본 열기" : "안전한 빈 테스트 씬 열기"))
                    {
                        OpenSafeScene(page);
                    }
                }
            }

            if (!string.IsNullOrEmpty(_lastBuildMessage))
            {
                EditorGUILayout.HelpBox(_lastBuildMessage, _lastBuildSucceeded ? MessageType.Info : MessageType.Warning);
            }

            EditorGUILayout.Space();
        }

        private void DrawPageControls(IUiSandboxPage page)
        {
            EditorGUILayout.LabelField("화면 컨트롤", EditorStyles.boldLabel);
            if (ReferenceEquals(_attachedPage, page))
            {
                RunPage(page, "컨트롤 그리기", () => page.DrawControls(_context));
            }
            else
            {
                EditorGUILayout.HelpBox("안전한 테스트 씬이 연결되기 전에는 화면 컨트롤을 쓸 수 없다. 위 시나리오 값은 지금도 편집하고 보관된다.", MessageType.Info);
            }

            EditorGUILayout.Space();
        }

        private void DrawStatusSection()
        {
            EditorGUILayout.LabelField("상태", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(_context.Status) ? "대기 중" : _context.Status, MessageType.None);

            if (!_context.IsPlaying)
            {
                EditorGUILayout.HelpBox("가짜 요청은 Play Mode에서만 실행한다.", MessageType.None);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!_context.IsBusy))
                {
                    if (GUILayout.Button($"진행 중인 요청 취소 {_context.PendingOperation}"))
                    {
                        IUiSandboxPage attached = _attachedPage;
                        CancelAndRestore(_context, attached, (attached != null) && (IsHostSafe(attached)), "사용자 취소");
                    }
                }

                if (GUILayout.Button("이력 지우기"))
                {
                    _context.ClearHistory();
                }
            }

            IReadOnlyList<UiSandboxRequestRecord> history = _context.History;
            int shown = Math.Min(history.Count, HISTORY_VISIBLE);
            for (int i = 0; i < shown; ++i)
            {
                UiSandboxRequestRecord record = history[history.Count - 1 - i];
                EditorGUILayout.LabelField($"#{record.Id} {record.Operation} [{record.State}]", record.Message);
            }
        }

        /****************************************
        *                Actions
        ****************************************/

        private void EnsureContext()
        {
            if ((_context != null) && (!_context.IsDisposed))
            {
                return;
            }

            _context = new UiSandboxContext(_preset, Array.Empty<GameObject>(), null, null, HostRequestBlockReason);
            if (!string.IsNullOrEmpty(_workingScenarioJson))
            {
                _context.RestoreSnapshot(_workingScenarioJson, _baselineScenarioJson);
            }

            _snapshotVersion = -1;
            _attachedPage = null;
            _switchedPage = null;
            _hostSafe = false;
            _boundSceneHandle = NO_SCENE_HANDLE;
            _forceRebind = true;
        }

        // 가짜 요청은 접속이 없고 샌드박스가 만든 사본이 활성 씬일 때만 보낸다. 원본 씬에서 Play해도 요청을 만들지 않는다
        private static string HostRequestBlockReason()
        {
            bool networkActive = UiSandboxSafetyPolicy.IsNetworkActive(out _);
            bool owned = UiSandboxSceneBuilder.TryGetActiveOwnedSandboxScene(out _);
            return UiSandboxSafetyPolicy.GetRequestBlockReason(networkActive, owned);
        }

        // 접속 상태가 바뀌면 진행 중인 요청을 취소하고 화면 연결을 다시 판정한다. 연결을 대신 끊지는 않는다
        private void WatchNetwork()
        {
            bool active = UiSandboxSafetyPolicy.IsNetworkActive(out string reason);
            if (active == _networkWasActive)
            {
                return;
            }

            _networkWasActive = active;
            _forceRebind = true;
            if ((active) && (_context != null) && (!_context.IsDisposed))
            {
                _context.CancelPending(reason);
            }
        }

        // 샌드박스가 만든 사본이 활성 씬일 때만 그 루트에 문맥을 연결한다. 그 밖의 씬에서는 빈 루트로 두고 View를 가진 화면은 연결하지 않는다
        private void SyncRoots()
        {
            WatchNetwork();
            ulong handle = NO_SCENE_HANDLE;
            bool safe = false;
            GameObject[] roots = Array.Empty<GameObject>();
            if (UiSandboxSceneBuilder.TryGetActiveOwnedSandboxScene(out Scene scene))
            {
                handle = scene.handle.GetRawData();
                roots = UiSandboxSceneBuilder.GetRoots(scene);
                safe = roots.Length > 0;
            }

            if ((handle == _boundSceneHandle) && (!_forceRebind))
            {
                return;
            }

            _forceRebind = false;
            _boundSceneHandle = handle;
            _hostSafe = safe;

            IUiSandboxPage page = _switchedPage;
            DetachPage();
            _context.Rebind(roots);
            AttachIfSafe(page);
        }

        private bool IsHostSafe(IUiSandboxPage page)
        {
            return UiSandboxSafetyPolicy.IsHostContextSafe(page.HasView, _hostSafe, UiSandboxSafetyPolicy.IsNetworkActive(out _));
        }

        // 안전한 문맥에서만 화면 코드를 연결하고 적용한다. 안전하지 않으면 시나리오 값만 다룬다
        private void AttachIfSafe(IUiSandboxPage page)
        {
            if ((page == null) || (!IsHostSafe(page)))
            {
                return;
            }

            AttachPage(page);
            ApplyPage();
        }

        private IReadOnlyList<IUiSandboxPage> GetOrderedPages()
        {
            if ((_orderDirty) || (_orderedPages.Count != UiSandboxCatalog.Pages.Count))
            {
                _orderedPages.Clear();
                _orderedPages.AddRange(UiSandboxCatalog.Pages);
                _orderedPages.Sort(ComparePages);
                _orderDirty = false;
            }

            return _orderedPages;
        }

        private static int ComparePages(IUiSandboxPage a, IUiSandboxPage b)
        {
            bool aReal = IsRealView(a);
            bool bReal = IsRealView(b);
            if (aReal != bReal)
            {
                return aReal ? -1 : 1;
            }

            return string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
        }

        private IUiSandboxPage ResolveSelected(IReadOnlyList<IUiSandboxPage> pages)
        {
            IUiSandboxPage found = UiSandboxCatalog.Find(_selectedPageId);
            if (found != null)
            {
                return found;
            }

            _selectedPageId = pages[0].Id;
            return pages[0];
        }

        private static bool IsRealView(IUiSandboxPage page)
        {
            return UiSandboxSceneBuilder.UsesSourceCopy(page);
        }

        private void SwitchPage(IUiSandboxPage next)
        {
            DetachPage();
            _context.CancelPending("화면 전환");
            _switchedPage = next;
            AttachIfSafe(next);
        }

        private void AttachPage(IUiSandboxPage page)
        {
            if (!IsHostSafe(page))
            {
                return;
            }

            _attachedPage = page;
            RunPage(page, "연결", () => page.Attach(_context));
        }

        private void DetachPage()
        {
            IUiSandboxPage page = _attachedPage;
            _attachedPage = null;
            if (page == null)
            {
                return;
            }

            if (_context != null)
            {
                RunPage(page, "해제", page.Detach);
                return;
            }

            try
            {
                page.Detach();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary> 진행 중인 요청을 콜백 없이 취소하고, 화면을 다시 연결해 시나리오 값으로 View를 되돌린다. 요청 전에 View를 먼저 바꾸는 화면이 취소 뒤에 달라진 채 남지 않게 한다. 시나리오 값은 바꾸지 않는다. 취소할 요청이 없으면 false다 </summary>
        public static bool CancelAndRestore(UiSandboxContext context, IUiSandboxPage page, bool hostSafe, string reason)
        {
            if ((context == null) || (context.IsDisposed) || (!context.CancelPending(reason)))
            {
                return false;
            }

            if ((page == null) || (!hostSafe))
            {
                return true;
            }

            string canceledStatus = context.Status;
            try
            {
                page.Detach();
                page.Attach(context);
                page.Apply(context);
                context.SetStatus($"{canceledStatus} - 화면을 시나리오 값으로 되돌렸다");
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                context.SetStatus($"{canceledStatus} - 화면을 되돌리다 예외: {exception.GetType().Name} - {exception.Message}");
            }

            return true;
        }

        private void ApplyPage()
        {
            IUiSandboxPage page = _attachedPage;
            if (page == null)
            {
                return;
            }

            if (!IsHostSafe(page))
            {
                DetachPage();
                _context.CancelPending("안전하지 않은 문맥");
                _context.SetStatus("접속이 살아 있거나 안전한 테스트 씬이 아니어서 화면에 값을 적용하지 않았다");
                return;
            }

            RunPage(page, "적용", () => page.Apply(_context));
        }

        private void ResetAll()
        {
            IUiSandboxPage page = _attachedPage;
            DetachPage();
            _context.ResetScenario();
            if (page != null)
            {
                AttachPage(page);
                ApplyPage();
            }
        }

        private void LoadPreset(UiSandboxScenarioSO preset)
        {
            IUiSandboxPage page = _attachedPage;
            DetachPage();
            _context.LoadScenario(preset);
            if (page != null)
            {
                AttachPage(page);
                ApplyPage();
            }
        }

        // 원본이 있는 화면은 안전한 사본을, 계약 전용 화면은 빈 안전 씬을 연다. 저장 안 된 테스트 씬 편집은 사용자가 확인했을 때만 버린다
        private void OpenSafeScene(IUiSandboxPage page)
        {
            if (!UiSandboxSceneBuilder.CanOpenSafeScene(page, EditorApplication.isPlayingOrWillChangePlaymode))
            {
                return;
            }

            bool discard = false;
            List<string> dirty = UiSandboxSceneBuilder.FindDirtySandboxSceneNames();
            if (dirty.Count > 0)
            {
                string message = $"저장하지 않은 편집이 있는 테스트 씬이 열려 있다.\n{string.Join("\n", dirty)}\n\n"
                    + "화면이 자동으로 만든 배치도 편집으로 보일 수 있다. 시나리오 값은 유지된다. 버리고 다시 만들까?";
                if (!EditorUtility.DisplayDialog("편집 중인 테스트 씬", message, "버리고 다시 만들기", "취소"))
                {
                    return;
                }

                discard = true;
            }

            DetachPage();

            UiSandboxBuildResult result = IsRealView(page)
                ? UiSandboxSceneBuilder.BuildAndOpen(page, discard)
                : UiSandboxSceneBuilder.BuildEmptyAndOpen(page, discard);
            _lastBuildMessage = result.Message;
            _lastBuildSucceeded = result.Success;

            if (result.Success)
            {
                _context.Rebind(result.Roots);
                _boundSceneHandle = result.Scene.handle.GetRawData();
                _hostSafe = result.Roots.Length > 0;
                _forceRebind = false;
            }

            _switchedPage = page;
            AttachIfSafe(page);

            _context.SetStatus(result.Message);
            GUIUtility.ExitGUI();
        }

        private void SaveToLoadedPreset()
        {
            if (_preset == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(_preset);
            if (!EditorUtility.DisplayDialog("프리셋 덮어쓰기", $"현재 시나리오 값으로 프리셋을 덮어쓴다.\n{path}", "덮어쓰기", "취소"))
            {
                return;
            }

            bool saved = UiSandboxPresetStore.SaveAs(_context.Scenario, path, true, out string error);
            ReportSave(saved, path, error);
        }

        private void SaveAsNewPreset()
        {
            string path = EditorUtility.SaveFilePanelInProject("시나리오 프리셋 저장", DEFAULT_PRESET_NAME, "asset", "저장할 위치를 고른다");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            bool exists = AssetDatabase.LoadMainAssetAtPath(path) != null;
            if ((exists) && (!EditorUtility.DisplayDialog("프리셋 덮어쓰기", $"이미 있는 파일이다. 덮어쓸까?\n{path}", "덮어쓰기", "취소")))
            {
                return;
            }

            bool saved = UiSandboxPresetStore.SaveAs(_context.Scenario, path, exists, out string error);
            ReportSave(saved, path, error);
            if (saved)
            {
                _preset = UiSandboxPresetStore.Load(path);
            }
        }

        private void ReportSave(bool saved, string path, string error)
        {
            _context.SetStatus(saved ? $"프리셋을 저장했다: {path}" : $"프리셋을 저장하지 못했다: {error}");
        }

        // 화면 코드의 예외가 창 전체를 깨뜨리지 않도록 막는다. GUI 흐름 예외는 그대로 전달한다
        private void RunPage(IUiSandboxPage page, string action, Action call)
        {
            try
            {
                call();
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _context.SetStatus($"'{page.Title}' {action} 중 예외: {exception.GetType().Name} - {exception.Message}");
            }
        }
    }
}
