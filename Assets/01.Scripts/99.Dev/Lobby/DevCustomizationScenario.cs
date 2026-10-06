// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using Mirror;
using TMPro;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary> 로비, 캐릭터 생성과 선택, 마을 입장을 실제 public UI API로 밟아 외형과 이동 복제를 검증하는 개발 시나리오 </summary>
    public sealed class DevCustomizationScenario : MonoBehaviour
    {
        // 재귀 CLI/Unity 실행이나 Reflection, private 필드 접근 없이 실제 화면 버튼과 production 흐름만 쓴다.
        // LobbyScreen.StartButton/IsBusy, CharacterCreateScreen.GetLookTabButton, CharacterSelectScreen.CreateButton은
        // 모두 실제 production 화면이 쓰는 같은 버튼이다. 하나라도 null이거나 비활성이면 ContractGaps에 남기고 실패한다

        /****************************************
        *                Fields
        ****************************************/

        private const string ARG_SCENARIO        = "-dev-customization-scenario";
        private const string ARG_MODE            = "-dev-cust-mode";
        private const string ARG_CLASS           = "-dev-cust-class";
        private const string ARG_FACE            = "-dev-cust-face";
        private const string ARG_HAIR            = "-dev-cust-hair";
        private const string ARG_OUTFIT          = "-dev-cust-outfit";
        private const string ARG_NAME            = "-dev-cust-name";
        private const string ARG_RESULT          = "-dev-cust-result";
        private const string ARG_REJOINS         = "-dev-cust-rejoins";
        private const string ARG_REMOTE_NAME     = "-dev-cust-remote-name";
        private const string ARG_REMOTE_HAIR     = "-dev-cust-remote-hair";
        private const string ARG_REMOTE_OUTFIT   = "-dev-cust-remote-outfit";
        private const string ARG_OBSERVE_SECONDS = "-dev-cust-observe-seconds";
        private const string ARG_SCREENSHOT      = "-dev-cust-screenshot";

        private const string MODE_CREATE = "create";
        private const string MODE_ENTER  = "enter";

        private const int MIN_CLASS_ID = ContractCodes.PlayerClass.WARRIOR;
        private const int MAX_CLASS_ID = ContractCodes.PlayerClass.SUMMONER;

        // 얼굴 1종, 헤어/의상은 직업과 독립인 3종, 세 부위가 같은 1..3 범위를 쓴다
        private const int MIN_PART_ID = CharacterAppearanceCatalog.HAIR_WARRIOR;
        private const int MAX_PART_ID = CharacterAppearanceCatalog.HAIR_SUMMONER;

        private const int MIN_REJOINS = 0;
        private const int MAX_REJOINS = 10;

        private const int MIN_OBSERVE_SECONDS = 0;
        private const int MAX_OBSERVE_SECONDS = 300;

        private const int FIRST_JOIN_INDEX       = 1;
        private const int SUCCESS_EXIT_CODE      = 0;
        private const int FAILURE_EXIT_CODE      = 1;
        private const int MAX_STATUS_TEXT_LENGTH = 80;

        private const string VISUAL_CAPSULE_NAME      = "Visual";
        private const string ANIMATOR_SPEED_PARAMETER = "Speed";
        private const string RESULT_VERSION           = "1";

        private const float SESSION_TIMEOUT_SECONDS        = 60f;
        private const float SCREEN_TIMEOUT_SECONDS         = 90f;
        private const float TOWN_TIMEOUT_SECONDS           = 90f;
        private const float DISCONNECT_TIMEOUT_SECONDS     = 90f;
        private const float SCREENSHOT_TIMEOUT_SECONDS     = 5f;
        private const float BASELINE_SAMPLE_SECONDS        = 1.5f;
        private const float SAMPLE_INTERVAL_SECONDS        = 0.2f;
        private const float REMOTE_RESCAN_INTERVAL_SECONDS = 1f;

        // 전체 상한은 join/observe 횟수에 비례한다. 각 개별 대기는 위 단계별 상한을 따로 쓴다
        private const float BASE_BUDGET_SECONDS           = 540f;
        private const float REJOIN_BUDGET_SECONDS         = 180f;
        private const float OBSERVE_BUDGET_BUFFER_SECONDS = 60f;

        private ScenarioArgs    _args;
        private ScenarioContext _ctx;
        private bool            _finished;

        /****************************************
        *                Types
        ****************************************/

        private struct ScenarioArgs
        {
            public string Mode;
            public int    ClassId;
            public int    FaceId;
            public int    HairId;
            public int    OutfitId;
            public string Name;
            public string ResultPath;
            public int    Rejoins;
            public string RemoteName;
            public int    RemoteHairId;
            public int    RemoteOutfitId;
            public int    ObserveSeconds;
            public string ScreenshotPath;
        }

        // 아직 찾지 못한 화면 참조를 건네 반복 탐색 사이에 공유하는 참조 상자, out/ref는 코루틴에 쓸 수 없다
        private sealed class ObjectBox<T> where T : UnityEngine.Object
        {
            public T Value;
        }

        [Serializable]
        private sealed class JoinSnapshot
        {
            public int    JoinIndex;
            public uint   NetId;
            public int    CombatId;
            public int    ClassId;
            public string CharacterName = string.Empty;
            public int    FaceId;
            public int    HairId;
            public int    OutfitId;
            public int    AssembledFaceId;
            public int    AssembledHairId;
            public int    AssembledOutfitId;
            public bool   Owned;
            public bool   Authenticated;
            public bool   Assembled;
            public bool   PlaceholderVisible;
            public string AssemblyError = string.Empty;
        }

        [Serializable]
        private sealed class ScenarioResult
        {
            public string Version = RESULT_VERSION;
            public string RunMode = string.Empty;
            public string ExpectedCharacterName = string.Empty;
            public int    ExpectedClassId;
            public int    ExpectedFaceId;
            public int    ExpectedHairId;
            public int    ExpectedOutfitId;
            public int    JoinsRequested;
            public int    JoinsCompleted;
            public int    RejoinsRequested;
            public int    RejoinsCompleted;
            public string[] Steps           = Array.Empty<string>();
            public string[] Errors          = Array.Empty<string>();
            public string[] ContractGaps    = Array.Empty<string>();
            public string[] ScreenshotPaths = Array.Empty<string>();
            public JoinSnapshot[] LocalJoins  = Array.Empty<JoinSnapshot>();
            public JoinSnapshot[] RemoteJoins = Array.Empty<JoinSnapshot>();
            public bool  RemoteEverObserved;
            public float ObserveSecondsRequested;
            public float ObserveSecondsActual;
            public float LocalPeakSpeed;
            public float LocalPeakAnimatorSpeed;
            public int   LocalSpeedSampleCount;
            public int   LocalMatchingSpeedSampleCount;
            public float RemotePeakSpeed;
            public float RemotePeakAnimatorSpeed;
            public int   RemoteSpeedSampleCount;
            public int   RemoteMatchingSpeedSampleCount;
            public bool  NormalShutdownCompleted;
            public bool  Success;
            public int   ExitCode = FAILURE_EXIT_CODE;
        }

        // 여러 코루틴 단계가 공유하는 진행 상태. MonoBehaviour 필드 대신 이 한 묶음으로 넘긴다
        private sealed class ScenarioContext
        {
            public ScenarioArgs   Args;
            public ScenarioResult Result = new ScenarioResult();
            public readonly List<string>       Steps           = new List<string>();
            public readonly List<string>       Errors          = new List<string>();
            public readonly List<string>       ContractGaps    = new List<string>();
            public readonly List<string>       ScreenshotPaths = new List<string>();
            public readonly List<JoinSnapshot> LocalJoins      = new List<JoinSnapshot>();
            public readonly List<JoinSnapshot> RemoteJoins     = new List<JoinSnapshot>();
            public bool  Failed;
            public float MasterDeadline;

            public PlayerCombatState LocalCombat;
            public Animator          LocalAnimator;

            public PlayerCombatState                   RemoteCombat;
            public PlayerFemaleCharacterAppearanceView RemoteAppearance;
            public Animator                            RemoteAnimator;

            public float LocalPeakSpeed;
            public float LocalPeakAnimatorSpeed;
            public int   LocalSpeedSamples;
            public int   LocalMatchingSpeedSamples;
            public float RemotePeakSpeed;
            public float RemotePeakAnimatorSpeed;
            public int   RemoteSpeedSamples;
            public int   RemoteMatchingSpeedSamples;
        }

        /****************************************
        *              Unity Event
        ****************************************/

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !UNITY_SERVER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfRequested()
        {
            if (!DevArgs.Has(ARG_SCENARIO))
            {
                return;
            }

            if (!TryParseArgs(out ScenarioArgs args, out string resultPathForFailure, out string parseError))
            {
                Debug.LogError("[DevCustomizationScenario] 인자 오류: " + parseError);
                WriteFailureResultIfPossible(resultPathForFailure, "Args:invalid");
                Quit(FAILURE_EXIT_CODE);
                return;
            }

            Application.runInBackground = true;
            var host = new GameObject(nameof(DevCustomizationScenario));
            DontDestroyOnLoad(host);
            DevCustomizationScenario scenario = host.AddComponent<DevCustomizationScenario>();
            scenario._args = args;
        }
#endif

        private void Start()
        {
            _ctx = new ScenarioContext { Args = _args };
            FillExpected(_ctx);
            _ctx.MasterDeadline = Time.realtimeSinceStartup + ComputeOverallBudgetSeconds(_args);
            StartCoroutine(Guarded(CoScenario(_ctx), _ctx, "Scenario"));
        }

        // 코루틴이 중첩 대기에 멈추거나 처리하지 못한 예외로 죽어도, 실제 프로세스가 영원히 남지 않게 별도로 감시한다
        private void Update()
        {
            if ((_finished) || (_ctx == null))
            {
                return;
            }

            if (_ctx.Failed)
            {
                FinalizeAndExit(_ctx);
                return;
            }

            if (Time.realtimeSinceStartup >= _ctx.MasterDeadline)
            {
                Fail(_ctx, "Watchdog:masterDeadlineExceeded");
                FinalizeAndExit(_ctx);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // inner가 더 깊은 IEnumerator를 내주면 재귀로 같은 보호를 씌운다. 그래서 바깥에서 한 번만 감싸도
        // 중첩된 모든 단계의 예외를 이 자리에서 잡는다. 메시지가 아니라 타입 이름만 남긴다
        private static IEnumerator Guarded(IEnumerator inner, ScenarioContext ctx, string stepLabel)
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = inner.MoveNext();
                }
                catch (Exception exception)
                {
                    Fail(ctx, stepLabel + ":Exception:" + exception.GetType().Name);
                    yield break;
                }

                if (!moved)
                {
                    yield break;
                }

                object current = inner.Current;
                if (current is IEnumerator nested)
                {
                    yield return Guarded(nested, ctx, stepLabel);
                    if (ctx.Failed)
                    {
                        yield break;
                    }

                    continue;
                }

                yield return current;
            }
        }

        private IEnumerator CoScenario(ScenarioContext ctx)
        {
            LogStep(ctx, "Args:ok");

            yield return CoWaitForSessionAndLobby(ctx);

            if ((!ctx.Failed) && (string.Equals(ctx.Args.Mode, MODE_CREATE, StringComparison.Ordinal)))
            {
                yield return CoCreateCharacter(ctx);
            }

            if (!ctx.Failed)
            {
                yield return CoEnterSelectAndJoin(ctx, FIRST_JOIN_INDEX);
            }

            for (int i = 0; (!ctx.Failed) && (i < ctx.Args.Rejoins); ++i)
            {
                int joinIndex = i + FIRST_JOIN_INDEX + 1;
                yield return CoDisconnectAndReturnToSelect(ctx, "Rejoin" + joinIndex + "Disconnect");
                if (ctx.Failed)
                {
                    break;
                }

                yield return CoEnterSelectAndJoin(ctx, joinIndex);
                if (!ctx.Failed)
                {
                    ctx.Result.RejoinsCompleted = i + 1;
                }
            }

            if (!ctx.Failed)
            {
                yield return CoDisconnectAndReturnToSelect(ctx, "FinalShutdown");
            }

            // 합의된 접근자를 쓰지 못해 남긴 ContractGaps가 있으면 그 경로는 정상 성공으로 보지 않는다
            if ((!ctx.Failed) && (ctx.ContractGaps.Count > 0))
            {
                Fail(ctx, "ContractGap:present");
            }

            if (!ctx.Failed)
            {
                ctx.Result.NormalShutdownCompleted = true;
                ctx.Result.Success  = true;
                ctx.Result.ExitCode = SUCCESS_EXIT_CODE;
                LogStep(ctx, "Scenario:ok");
            }

            FinalizeAndExit(ctx);
        }

        private IEnumerator CoWaitForSessionAndLobby(ScenarioContext ctx)
        {
            yield return CoWaitUntil(ctx, () => (LauncherSession.HasSession) && (!LauncherSession.IsExpired),
                SESSION_TIMEOUT_SECONDS, "LauncherSession:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            LogStep(ctx, "LauncherSession:ok");

            yield return CoWaitUntil(ctx, () => (IsAtScene(Constants.Scene.CHARACTER_SELECT)) || (IsAtScene(Constants.Scene.LOBBY)),
                SCREEN_TIMEOUT_SECONDS, "Lobby:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            if (IsAtScene(Constants.Scene.CHARACTER_SELECT))
            {
                LogStep(ctx, "Lobby:ok");
                yield break;
            }

            var lobbyScreenBox = new ObjectBox<LobbyScreen>();
            yield return CoFindUntil(ctx, lobbyScreenBox, SCREEN_TIMEOUT_SECONDS, "LobbyScreenLoad:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            LobbyScreen lobbyScreen = lobbyScreenBox.Value;
            yield return CoWaitUntil(ctx,
                () => (lobbyScreen.StartButton != null) && (lobbyScreen.StartButton.interactable) && (!lobbyScreen.IsBusy),
                SCREEN_TIMEOUT_SECONDS, "LobbyStartReady:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            if (!TryInvoke(lobbyScreen.StartButton, lobbyScreen.IsBusy))
            {
                Fail(ctx, "LobbyStartClick:unavailable");
                yield break;
            }

            LogStep(ctx, "LobbyStartClick:ok");

            yield return CoWaitUntil(ctx, () => IsAtScene(Constants.Scene.CHARACTER_SELECT), SCREEN_TIMEOUT_SECONDS, "Lobby:selectTimeout");
            if (ctx.Failed)
            {
                yield break;
            }

            LogStep(ctx, "Lobby:ok");
        }

        private IEnumerator CoCreateCharacter(ScenarioContext ctx)
        {
            var selectScreenBox = new ObjectBox<CharacterSelectScreen>();
            yield return CoFindUntil(ctx, selectScreenBox, SCREEN_TIMEOUT_SECONDS, "SelectBeforeCreate:findTimeout");
            if (ctx.Failed)
            {
                yield break;
            }

            CharacterSelectScreen selectScreen = selectScreenBox.Value;
            yield return CoWaitUntil(ctx, () => (selectScreen.IsLoaded) && (!selectScreen.IsBusy),
                SCREEN_TIMEOUT_SECONDS, "SelectBeforeCreate:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            if (selectScreen.CreateButton == null)
            {
                AddContractGap(ctx, "CharacterSelectScreen.CreateButton이 null이다");
                Fail(ctx, "SelectCreateClick:missing");
                yield break;
            }

            yield return CoWaitUntil(ctx, () => (selectScreen.CreateButton.interactable) && (!selectScreen.IsBusy),
                SCREEN_TIMEOUT_SECONDS, "SelectCreateReady:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            if (!TryInvoke(selectScreen.CreateButton, selectScreen.IsBusy))
            {
                Fail(ctx, "SelectCreateClick:unavailable");
                yield break;
            }

            LogStep(ctx, "SelectCreateClick:ok");

            yield return CoWaitUntil(ctx, () => IsAtScene(Constants.Scene.CHARACTER_CREATE), SCREEN_TIMEOUT_SECONDS, "CreateScene:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            var createScreenBox = new ObjectBox<CharacterCreateScreen>();
            yield return CoFindUntil(ctx, createScreenBox, SCREEN_TIMEOUT_SECONDS, "CreateScreenLoad:findTimeout");
            if (ctx.Failed)
            {
                yield break;
            }

            CharacterCreateScreen createScreen = createScreenBox.Value;
            yield return CoWaitUntil(ctx, () => !createScreen.IsBusy, SCREEN_TIMEOUT_SECONDS, "CreateScreenLoad:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            LogStep(ctx, "CreateScreenLoad:ok");

            if (!TryInvoke(createScreen.GetClassButton(ctx.Args.ClassId), createScreen.IsBusy))
            {
                Fail(ctx, "CreateClass:unavailable");
                yield break;
            }

            LogStep(ctx, "CreateClass:ok");

            // 얼굴도 탭+옵션 1종뿐이지만 실제 클릭 경로로 검증한다, 기본값과 같다고 건너뛰지 않는다
            yield return CoSelectLookPart(ctx, createScreen, ECharacterLookPart.Face, CharacterAppearanceCatalog.FACE_CUTE);
            if (ctx.Failed)
            {
                yield break;
            }

            yield return CoSelectLookPart(ctx, createScreen, ECharacterLookPart.Hair, ctx.Args.HairId);
            if (ctx.Failed)
            {
                yield break;
            }

            yield return CoSelectLookPart(ctx, createScreen, ECharacterLookPart.Outfit, ctx.Args.OutfitId);
            if (ctx.Failed)
            {
                yield break;
            }

            yield return CoWaitUntil(ctx, () => createScreen.IsPreviewReady, SCREEN_TIMEOUT_SECONDS, "CreatePreview:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            if (!string.IsNullOrEmpty(ctx.Args.ScreenshotPath))
            {
                yield return CoCaptureScreenshot(ctx, ctx.Args.ScreenshotPath, "create");
                if (ctx.Failed)
                {
                    yield break;
                }
            }

            createScreen.NameInput.text = ctx.Args.Name;
            if (!TryInvoke(createScreen.CheckButton, createScreen.IsBusy))
            {
                Fail(ctx, "CreateCheck:unavailable");
                yield break;
            }

            yield return CoWaitUntil(ctx, () => !createScreen.IsBusy, SCREEN_TIMEOUT_SECONDS, "CreateCheck:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            LogStep(ctx, "CreateCheck:ok:" + Sanitize(createScreen.StatusText.text));

            yield return CoWaitUntil(ctx, () => (createScreen.CreateButton.interactable) && (!createScreen.IsBusy),
                SCREEN_TIMEOUT_SECONDS, "CreateReady:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            createScreen.CreateButton.onClick.Invoke();

            // IsAtScene을 먼저 평가해 성공 시 이미 파괴된 createScreen.IsBusy를 건드리지 않는다
            yield return CoWaitUntil(ctx, () => (IsAtScene(Constants.Scene.CHARACTER_SELECT)) || (!createScreen.IsBusy),
                SCREEN_TIMEOUT_SECONDS, "CreateSubmit:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            if (!IsAtScene(Constants.Scene.CHARACTER_SELECT))
            {
                Fail(ctx, "CreateSubmit:rejected:" + Sanitize(createScreen.StatusText.text));
                yield break;
            }

            LogStep(ctx, "CreateSubmit:ok");
        }

        // 탭을 먼저 눌러 옵션 패널을 활성화한 뒤에만 옵션을 클릭한다. 이미 활성인 탭은 UI가 interactable=false로
        // 표시하므로 그 상태를 명시 단계로만 남기고, 비활성 버튼의 onClick을 직접 호출해 숨기지 않는다
        private IEnumerator CoSelectLookPart(ScenarioContext ctx, CharacterCreateScreen createScreen,
            ECharacterLookPart part, int optionId)
        {
            Button tabButton = createScreen.GetLookTabButton(part);
            if (tabButton == null)
            {
                AddContractGap(ctx, "CharacterCreateScreen.GetLookTabButton(" + part + ")이 null이다");
                Fail(ctx, "LookTab:" + part + ":missing");
                yield break;
            }

            bool alreadyActive = (!tabButton.interactable) && (tabButton.gameObject.activeInHierarchy) && (!createScreen.IsBusy);
            if (alreadyActive)
            {
                LogStep(ctx, "LookTab:" + part + ":already-active");
            }
            else
            {
                if (!TryInvoke(tabButton, createScreen.IsBusy))
                {
                    Fail(ctx, "LookTab:" + part + ":unavailable");
                    yield break;
                }

                LogStep(ctx, "LookTab:" + part + ":ok");
            }

            yield return null; // 탭 전환으로 바뀐 옵션 패널 활성 상태가 반영된 다음 프레임을 쓴다

            Button optionButton = createScreen.GetLookOptionButton(part, optionId);
            if (optionButton == null)
            {
                AddContractGap(ctx, "CharacterCreateScreen.GetLookOptionButton(" + part + "," + optionId + ")이 null이다");
                Fail(ctx, "LookOption:" + part + ":missing");
                yield break;
            }

            if (!TryInvoke(optionButton, createScreen.IsBusy))
            {
                Fail(ctx, "LookOption:" + part + ":unavailable");
                yield break;
            }

            LogStep(ctx, "LookOption:" + part + ":ok");
        }

        private IEnumerator CoEnterSelectAndJoin(ScenarioContext ctx, int joinIndex)
        {
            var selectScreenBox = new ObjectBox<CharacterSelectScreen>();
            yield return CoFindUntil(ctx, selectScreenBox, SCREEN_TIMEOUT_SECONDS, "SelectLoad" + joinIndex + ":findTimeout");
            if (ctx.Failed)
            {
                yield break;
            }

            CharacterSelectScreen selectScreen = selectScreenBox.Value;
            yield return CoWaitUntil(ctx, () => (selectScreen.IsLoaded) && (!selectScreen.IsBusy),
                SCREEN_TIMEOUT_SECONDS, "SelectLoad" + joinIndex + ":timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            int slot = FindCharacterSlot(selectScreen, ctx.Args.Name);
            if (slot < 0)
            {
                Fail(ctx, "SelectFind" + joinIndex + ":notfound");
                yield break;
            }

            CharacterSummary character = selectScreen.Characters[slot];
            bool resolved = CharacterAppearanceCatalog.TryResolve(character.ClassId, character.Appearance, out CharacterAppearance canonical);
            CharacterLook look = default;
            bool hasLook = (resolved) && (CharacterLook.TryFrom(canonical, out look));
            if ((!hasLook) || (character.ClassId != ctx.Args.ClassId) || (!look.Equals(ExpectedLook(ctx.Args))))
            {
                Fail(ctx, "SelectFind" + joinIndex + ":mismatch");
                yield break;
            }

            LogStep(ctx, "SelectFind" + joinIndex + ":ok");

            selectScreen.Selection.Select(slot);
            yield return CoWaitUntil(ctx, () => (selectScreen.StartButton.interactable) && (!selectScreen.IsBusy),
                SCREEN_TIMEOUT_SECONDS, "SelectReady" + joinIndex + ":timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            if ((joinIndex == FIRST_JOIN_INDEX) && (!string.IsNullOrEmpty(ctx.Args.ScreenshotPath)))
            {
                yield return CoCaptureScreenshot(ctx, ctx.Args.ScreenshotPath, "select");
                if (ctx.Failed)
                {
                    yield break;
                }
            }

            selectScreen.StartButton.onClick.Invoke();

            yield return CoWaitUntil(ctx, IsTownAuthenticated, TOWN_TIMEOUT_SECONDS, "TownAuth" + joinIndex + ":timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            LogStep(ctx, "TownAuth" + joinIndex + ":ok");

            yield return CoVerifyLocalPlayer(ctx, joinIndex);
            if (ctx.Failed)
            {
                yield break;
            }

            if (joinIndex == FIRST_JOIN_INDEX)
            {
                if (!string.IsNullOrEmpty(ctx.Args.RemoteName))
                {
                    yield return CoVerifyRemotePlayer(ctx);
                    if (ctx.Failed)
                    {
                        yield break;
                    }
                }

                yield return CoSampleSpeeds(ctx);
                if (ctx.Failed)
                {
                    yield break;
                }

                if (!string.IsNullOrEmpty(ctx.Args.ScreenshotPath))
                {
                    yield return CoCaptureScreenshot(ctx, ctx.Args.ScreenshotPath, null);
                    if (ctx.Failed)
                    {
                        yield break;
                    }
                }
            }

            ctx.Result.JoinsCompleted = joinIndex;
        }

        private IEnumerator CoVerifyLocalPlayer(ScenarioContext ctx, int joinIndex)
        {
            NetworkIdentity localIdentity = NetworkClient.localPlayer;
            if ((localIdentity == null) || (!localIdentity.TryGetComponent(out PlayerCombatState combat))
                || (!localIdentity.TryGetComponent(out PlayerFemaleCharacterAppearanceView appearanceView)))
            {
                Fail(ctx, "TownLocalVerify" + joinIndex + ":missing");
                yield break;
            }

            ctx.LocalCombat = combat;

            // NetPlayer 루트의 고정 자식 이름, PlayerFemaleLookReleaseSetup이 같은 이름으로 검사하고 저장한다
            Transform capsule = localIdentity.transform.Find(VISUAL_CAPSULE_NAME);

            yield return CoWaitUntil(ctx, () => !string.IsNullOrEmpty(combat.CharacterName),
                TOWN_TIMEOUT_SECONDS, "TownLocalVerify" + joinIndex + ":nameTimeout");
            if (ctx.Failed)
            {
                yield break;
            }

            yield return CoWaitUntil(ctx, () => (appearanceView.IsAssembled) || (!string.IsNullOrEmpty(appearanceView.LastError)),
                TOWN_TIMEOUT_SECONDS, "TownLocalVerify" + joinIndex + ":assembleTimeout");
            if (ctx.Failed)
            {
                yield break;
            }

            ctx.LocalAnimator = appearanceView.Animator;

            JoinSnapshot snapshot = BuildSnapshot(joinIndex, combat, appearanceView, capsule);
            ctx.LocalJoins.Add(snapshot);

            if ((!string.Equals(snapshot.CharacterName, ctx.Args.Name, StringComparison.Ordinal))
                || (snapshot.ClassId != ctx.Args.ClassId)
                || (snapshot.FaceId != ctx.Args.FaceId) || (snapshot.HairId != ctx.Args.HairId) || (snapshot.OutfitId != ctx.Args.OutfitId)
                || (snapshot.AssembledFaceId != ctx.Args.FaceId) || (snapshot.AssembledHairId != ctx.Args.HairId)
                || (snapshot.AssembledOutfitId != ctx.Args.OutfitId)
                || (!snapshot.Owned) || (!snapshot.Authenticated) || (!snapshot.Assembled) || (snapshot.PlaceholderVisible))
            {
                Fail(ctx, "TownLocalVerify" + joinIndex + ":mismatch");
                yield break;
            }

            LogStep(ctx, "TownLocalVerify" + joinIndex + ":ok");
        }

        private IEnumerator CoVerifyRemotePlayer(ScenarioContext ctx)
        {
            PlayerCombatState remoteCombat = null;
            yield return CoWaitUntil(ctx, () => TryFindRemote(ctx.Args.RemoteName, out remoteCombat),
                TOWN_TIMEOUT_SECONDS, "RemoteVerify:timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            if ((remoteCombat == null) || (!remoteCombat.TryGetComponent(out PlayerFemaleCharacterAppearanceView remoteAppearance)))
            {
                Fail(ctx, "RemoteVerify:missing");
                yield break;
            }

            ctx.RemoteCombat     = remoteCombat;
            ctx.RemoteAppearance = remoteAppearance;
            Transform capsule = remoteCombat.transform.Find(VISUAL_CAPSULE_NAME);

            yield return CoWaitUntil(ctx, () => (remoteAppearance.IsAssembled) || (!string.IsNullOrEmpty(remoteAppearance.LastError)),
                TOWN_TIMEOUT_SECONDS, "RemoteVerify:assembleTimeout");
            if (ctx.Failed)
            {
                yield break;
            }

            ctx.RemoteAnimator             = remoteAppearance.Animator;
            ctx.Result.RemoteEverObserved = true;

            JoinSnapshot snapshot = BuildSnapshot(FIRST_JOIN_INDEX, remoteCombat, remoteAppearance, capsule);
            ctx.RemoteJoins.Add(snapshot);

            if ((!string.Equals(snapshot.CharacterName, ctx.Args.RemoteName, StringComparison.Ordinal))
                || (snapshot.FaceId != ctx.Args.FaceId) || (snapshot.HairId != ctx.Args.RemoteHairId)
                || (snapshot.OutfitId != ctx.Args.RemoteOutfitId) || (snapshot.Owned) || (!snapshot.Authenticated)
                || (snapshot.AssembledFaceId != ctx.Args.FaceId) || (snapshot.AssembledHairId != ctx.Args.RemoteHairId)
                || (snapshot.AssembledOutfitId != ctx.Args.RemoteOutfitId)
                || (!snapshot.Assembled) || (snapshot.PlaceholderVisible))
            {
                Fail(ctx, "RemoteVerify:mismatch");
                yield break;
            }

            LogStep(ctx, "RemoteVerify:ok");
        }

        private IEnumerator CoSampleSpeeds(ScenarioContext ctx)
        {
            float duration = Mathf.Max(BASELINE_SAMPLE_SECONDS, ctx.Args.ObserveSeconds);
            float deadline = Mathf.Min(Time.realtimeSinceStartup + duration, ctx.MasterDeadline);
            float nextRemoteScan = 0f;
            bool  hasRemote = !string.IsNullOrEmpty(ctx.Args.RemoteName);

            while (Time.realtimeSinceStartup < deadline)
            {
                SampleLocal(ctx);

                if (hasRemote)
                {
                    if (Time.realtimeSinceStartup >= nextRemoteScan)
                    {
                        nextRemoteScan = Time.realtimeSinceStartup + REMOTE_RESCAN_INTERVAL_SECONDS;
                        RescanRemoteIfNeeded(ctx);
                    }

                    SampleRemote(ctx);
                }

                yield return new WaitForSeconds(SAMPLE_INTERVAL_SECONDS);
            }

            ctx.Result.ObserveSecondsActual = duration;

            bool requirePositive = (ctx.Args.ObserveSeconds > 0) && (DevArgs.Has(Constants.Network.ARG_DEV_AUTOPILOT));
            if (requirePositive)
            {
                if ((ctx.LocalPeakSpeed <= 0f) || (ctx.LocalPeakAnimatorSpeed <= 0f) || (ctx.LocalMatchingSpeedSamples < 3))
                {
                    Fail(ctx, "Observe:localPositiveRequired");
                    yield break;
                }

                if ((hasRemote) && ((ctx.RemotePeakSpeed <= 0f) || (ctx.RemotePeakAnimatorSpeed <= 0f) || (ctx.RemoteMatchingSpeedSamples < 3)))
                {
                    Fail(ctx, "Observe:remotePositiveRequired");
                    yield break;
                }
            }

            LogStep(ctx, "Observe:ok");
        }

        private IEnumerator CoDisconnectAndReturnToSelect(ScenarioContext ctx, string stepLabel)
        {
            var manager = NetworkManager.singleton as ShinshiNetworkManager;
            if (manager == null)
            {
                Fail(ctx, stepLabel + ":managerMissing");
                yield break;
            }

            if (NetworkClient.active)
            {
                manager.StopClient();
            }

            // 마을에서 끊기면 ShinshiNetworkManager.OnClientDisconnect가 LobbyFlow.ReturnToCharacterSelect를 직접 부른다
            yield return CoWaitUntil(ctx, () => (!NetworkClient.active) && (IsAtScene(Constants.Scene.CHARACTER_SELECT)),
                DISCONNECT_TIMEOUT_SECONDS, stepLabel + ":timeout");
            if (ctx.Failed)
            {
                yield break;
            }

            var screenBox = new ObjectBox<CharacterSelectScreen>();
            yield return CoFindUntil(ctx, screenBox, DISCONNECT_TIMEOUT_SECONDS, stepLabel + ":findTimeout");
            if (ctx.Failed)
            {
                yield break;
            }

            CharacterSelectScreen screen = screenBox.Value;
            yield return CoWaitUntil(ctx, () => screen.IsLoaded, DISCONNECT_TIMEOUT_SECONDS, stepLabel + ":loadTimeout");
            if (ctx.Failed)
            {
                yield break;
            }

            LogStep(ctx, stepLabel + ":ok");
        }

        private IEnumerator CoCaptureScreenshot(ScenarioContext ctx, string basePath, string suffix)
        {
            string finalPath = AppendSuffix(basePath, suffix);
            string label = "Screenshot:" + (suffix ?? "town");
            try
            {
                string directory = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch (Exception exception)
            {
                Fail(ctx, label + ":dir:" + exception.GetType().Name);
                yield break;
            }

            ScreenCapture.CaptureScreenshot(finalPath);
            yield return new WaitForEndOfFrame();

            float deadline = Mathf.Min(Time.realtimeSinceStartup + SCREENSHOT_TIMEOUT_SECONDS, ctx.MasterDeadline);
            while (true)
            {
                bool ready;
                try
                {
                    ready = (File.Exists(finalPath)) && (new FileInfo(finalPath).Length > 0);
                }
                catch (Exception exception)
                {
                    Fail(ctx, label + ":Exception:" + exception.GetType().Name);
                    yield break;
                }

                if (ready)
                {
                    break;
                }

                if (Time.realtimeSinceStartup >= deadline)
                {
                    Fail(ctx, label + ":timeout");
                    yield break;
                }

                yield return null;
            }

            ctx.ScreenshotPaths.Add(finalPath);
        }

        private static IEnumerator CoWaitUntil(ScenarioContext ctx, Func<bool> condition, float timeoutSeconds, string timeoutCode)
        {
            float deadline = Mathf.Min(Time.realtimeSinceStartup + timeoutSeconds, ctx.MasterDeadline);
            while (true)
            {
                bool ready;
                try
                {
                    ready = condition();
                }
                catch (Exception exception)
                {
                    Fail(ctx, timeoutCode + ":Exception:" + exception.GetType().Name);
                    yield break;
                }

                if (ready)
                {
                    yield break;
                }

                if (Time.realtimeSinceStartup >= deadline)
                {
                    Fail(ctx, timeoutCode);
                    yield break;
                }

                yield return null;
            }
        }

        // scene 전환 직후 아직 Awake/등록 전이면 null일 수 있다. 찾을 때까지만 다시 시도하고 찾으면 그 뒤로는 재탐색하지 않는다
        private static IEnumerator CoFindUntil<T>(ScenarioContext ctx, ObjectBox<T> box, float timeoutSeconds, string timeoutCode)
            where T : UnityEngine.Object
        {
            float deadline = Mathf.Min(Time.realtimeSinceStartup + timeoutSeconds, ctx.MasterDeadline);
            while (true)
            {
                box.Value = UnityEngine.Object.FindFirstObjectByType<T>();
                if (box.Value != null)
                {
                    yield break;
                }

                if (Time.realtimeSinceStartup >= deadline)
                {
                    Fail(ctx, timeoutCode);
                    yield break;
                }

                yield return null;
            }
        }

        private static bool TryInvoke(Button button, bool busy)
        {
            if ((button == null) || (busy) || (!button.gameObject.activeInHierarchy) || (!button.interactable))
            {
                return false;
            }

            button.onClick.Invoke();
            return true;
        }

        private static bool IsAtScene(string sceneName)
        {
            return string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.Ordinal);
        }

        private static bool IsTownAuthenticated()
        {
            return (IsAtScene(Constants.Scene.SHINSHI_TOWN)) && (NetworkClient.isConnected)
                && (NetworkClient.connection != null) && (NetworkClient.connection.isAuthenticated)
                && (NetworkClient.localPlayer != null);
        }

        private static int FindCharacterSlot(CharacterSelectScreen screen, string name)
        {
            IReadOnlyList<CharacterSummary> characters = screen.Characters;
            for (int i = 0; i < characters.Count; ++i)
            {
                if ((characters[i] != null) && (string.Equals(characters[i].Name, name, StringComparison.Ordinal)))
                {
                    return i;
                }
            }

            return -1;
        }

        private static CharacterLook ExpectedLook(ScenarioArgs args)
        {
            return new CharacterLook(args.FaceId, args.HairId, args.OutfitId);
        }

        private static bool TryFindRemote(string remoteName, out PlayerCombatState remoteCombat)
        {
            remoteCombat = null;
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if ((identity == null) || (identity == NetworkClient.localPlayer))
                {
                    continue;
                }

                if ((identity.TryGetComponent(out PlayerCombatState combat))
                    && (string.Equals(combat.CharacterName, remoteName, StringComparison.Ordinal)))
                {
                    remoteCombat = combat;
                    return true;
                }
            }

            return false;
        }

        private static void RescanRemoteIfNeeded(ScenarioContext ctx)
        {
            if (ctx.RemoteCombat != null)
            {
                return;
            }

            if (!TryFindRemote(ctx.Args.RemoteName, out PlayerCombatState remoteCombat))
            {
                return;
            }

            ctx.RemoteCombat     = remoteCombat;
            ctx.RemoteAppearance = remoteCombat.TryGetComponent(out PlayerFemaleCharacterAppearanceView view) ? view : null;
            ctx.RemoteAnimator   = (ctx.RemoteAppearance != null) ? ctx.RemoteAppearance.Animator : null;
            ctx.Result.RemoteEverObserved = true;
        }

        private static void SampleLocal(ScenarioContext ctx)
        {
            if ((ctx.LocalCombat == null) || (ctx.LocalAnimator == null))
            {
                return;
            }

            float speed         = ctx.LocalCombat.LocomotionSpeed;
            float animatorSpeed = ctx.LocalAnimator.GetFloat(ANIMATOR_SPEED_PARAMETER);
            if ((!IsFinite(speed)) || (speed < 0f) || (!IsFinite(animatorSpeed)) || (animatorSpeed < 0f))
            {
                return;
            }

            ++ctx.LocalSpeedSamples;
            if ((speed > 1f) && (Mathf.Abs(animatorSpeed - Mathf.Min(speed, 12f)) < 0.5f))
            {
                ++ctx.LocalMatchingSpeedSamples;
            }
            ctx.LocalPeakSpeed         = Mathf.Max(ctx.LocalPeakSpeed, speed);
            ctx.LocalPeakAnimatorSpeed = Mathf.Max(ctx.LocalPeakAnimatorSpeed, animatorSpeed);
        }

        private static void SampleRemote(ScenarioContext ctx)
        {
            if ((ctx.RemoteCombat == null) || (ctx.RemoteAnimator == null))
            {
                return;
            }

            float speed         = ctx.RemoteCombat.LocomotionSpeed;
            float animatorSpeed = ctx.RemoteAnimator.GetFloat(ANIMATOR_SPEED_PARAMETER);
            if ((!IsFinite(speed)) || (speed < 0f) || (!IsFinite(animatorSpeed)) || (animatorSpeed < 0f))
            {
                return;
            }

            ++ctx.RemoteSpeedSamples;
            if ((speed > 1f) && (Mathf.Abs(animatorSpeed - Mathf.Min(speed, 12f)) < 0.5f))
            {
                ++ctx.RemoteMatchingSpeedSamples;
            }
            ctx.RemotePeakSpeed         = Mathf.Max(ctx.RemotePeakSpeed, speed);
            ctx.RemotePeakAnimatorSpeed = Mathf.Max(ctx.RemotePeakAnimatorSpeed, animatorSpeed);
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }

        private static JoinSnapshot BuildSnapshot(int joinIndex, PlayerCombatState combat,
            PlayerFemaleCharacterAppearanceView appearanceView, Transform capsule)
        {
            var snapshot = new JoinSnapshot
            {
                JoinIndex          = joinIndex,
                NetId              = combat.netId,
                CombatId           = combat.CombatId,
                ClassId            = (int)combat.PlayerClass,
                CharacterName      = combat.CharacterName ?? string.Empty,
                Owned              = combat.isOwned,
                Authenticated      = (NetworkClient.connection != null) && (NetworkClient.connection.isAuthenticated),
                Assembled          = appearanceView.IsAssembled,
                PlaceholderVisible = (capsule != null) && (capsule.gameObject.activeSelf),
                AssemblyError      = appearanceView.LastError ?? string.Empty
            };
            snapshot.AssembledFaceId = appearanceView.CurrentLook.FaceId;
            snapshot.AssembledHairId = appearanceView.CurrentLook.HairId;
            snapshot.AssembledOutfitId = appearanceView.CurrentLook.OutfitId;

            if (CharacterLook.TryFrom(combat.Appearance, out CharacterLook look))
            {
                snapshot.FaceId   = look.FaceId;
                snapshot.HairId   = look.HairId;
                snapshot.OutfitId = look.OutfitId;
            }

            return snapshot;
        }

        private static string AppendSuffix(string path, string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                return path;
            }

            string directory     = Path.GetDirectoryName(path);
            string fileNameNoExt = Path.GetFileNameWithoutExtension(path);
            string extension     = Path.GetExtension(path);
            string fileName      = fileNameNoExt + "_" + suffix + extension;
            return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string flattened = text.Replace('\n', ' ').Replace('\r', ' ');
            return (flattened.Length > MAX_STATUS_TEXT_LENGTH) ? flattened.Substring(0, MAX_STATUS_TEXT_LENGTH) : flattened;
        }

        private static void FillExpected(ScenarioContext ctx)
        {
            ctx.Result.RunMode                = ctx.Args.Mode;
            ctx.Result.ExpectedCharacterName   = ctx.Args.Name;
            ctx.Result.ExpectedClassId         = ctx.Args.ClassId;
            ctx.Result.ExpectedFaceId          = ctx.Args.FaceId;
            ctx.Result.ExpectedHairId          = ctx.Args.HairId;
            ctx.Result.ExpectedOutfitId        = ctx.Args.OutfitId;
            ctx.Result.JoinsRequested          = FIRST_JOIN_INDEX + ctx.Args.Rejoins;
            ctx.Result.RejoinsRequested        = ctx.Args.Rejoins;
            ctx.Result.ObserveSecondsRequested = ctx.Args.ObserveSeconds;
        }

        private static float ComputeOverallBudgetSeconds(ScenarioArgs args)
        {
            return BASE_BUDGET_SECONDS + (args.Rejoins * REJOIN_BUDGET_SECONDS) + args.ObserveSeconds + OBSERVE_BUDGET_BUFFER_SECONDS;
        }

        private static void Fail(ScenarioContext ctx, string code)
        {
            if (ctx.Failed)
            {
                return;
            }

            AddError(ctx, code);
            ctx.Failed = true;
            Debug.LogError("[DevCustomizationScenario] 실패: " + code);
        }

        private static void AddError(ScenarioContext ctx, string code)
        {
            ctx.Errors.Add(code);
        }

        private static void LogStep(ScenarioContext ctx, string code)
        {
            ctx.Steps.Add(code);
            Debug.Log("[DevCustomizationScenario] " + code);
        }

        private static void AddContractGap(ScenarioContext ctx, string description)
        {
            ctx.ContractGaps.Add(description);
            Debug.LogWarning("[DevCustomizationScenario] 계약 공백: " + description);
        }

        private void FinalizeAndExit(ScenarioContext ctx)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            StopAllCoroutines();

            ctx.Result.Steps           = ctx.Steps.ToArray();
            ctx.Result.Errors          = ctx.Errors.ToArray();
            ctx.Result.ContractGaps    = ctx.ContractGaps.ToArray();
            ctx.Result.ScreenshotPaths = ctx.ScreenshotPaths.ToArray();
            ctx.Result.LocalJoins      = ctx.LocalJoins.ToArray();
            ctx.Result.RemoteJoins     = ctx.RemoteJoins.ToArray();

            ctx.Result.LocalPeakSpeed          = ctx.LocalPeakSpeed;
            ctx.Result.LocalPeakAnimatorSpeed  = ctx.LocalPeakAnimatorSpeed;
            ctx.Result.LocalSpeedSampleCount   = ctx.LocalSpeedSamples;
            ctx.Result.LocalMatchingSpeedSampleCount = ctx.LocalMatchingSpeedSamples;
            ctx.Result.RemotePeakSpeed         = ctx.RemotePeakSpeed;
            ctx.Result.RemotePeakAnimatorSpeed = ctx.RemotePeakAnimatorSpeed;
            ctx.Result.RemoteSpeedSampleCount  = ctx.RemoteSpeedSamples;
            ctx.Result.RemoteMatchingSpeedSampleCount = ctx.RemoteMatchingSpeedSamples;

            if (!ctx.Result.Success)
            {
                ctx.Result.ExitCode = FAILURE_EXIT_CODE;
            }

            if (!WriteResult(ctx.Args.ResultPath, ctx.Result))
            {
                ctx.Result.Success = false;
                ctx.Result.ExitCode = FAILURE_EXIT_CODE;
            }
            Quit(ctx.Result.ExitCode);
        }

        private static void WriteFailureResultIfPossible(string resultPath, string errorCode)
        {
            if (string.IsNullOrEmpty(resultPath))
            {
                return;
            }

            var result = new ScenarioResult
            {
                Success  = false,
                ExitCode = FAILURE_EXIT_CODE,
                Errors   = new[] { errorCode }
            };

            WriteResult(resultPath, result);
        }

        private static bool WriteResult(string path, ScenarioResult result)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(result, true);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception exception)
            {
                // 값 자체가 아니라 실패 유형만 남긴다. 경로나 내용에 비밀값은 없지만 습관을 통일한다
                Debug.LogError("[DevCustomizationScenario] 결과 파일 쓰기 실패 (" + exception.GetType().Name + ")");
                return false;
            }
        }

        private static void Quit(int exitCode)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(exitCode);
#endif
        }

        // 인자 전체를 한 번에 로그나 결과에 남기지 않는다. 실패 사유는 항목 이름만 담는다
        private static bool TryParseArgs(out ScenarioArgs args, out string resultPathForFailure, out string error)
        {
            args = default;
            resultPathForFailure = null;
            error = null;

            string[] rawArgs = Environment.GetCommandLineArgs();

            if ((!TryReadString(rawArgs, ARG_RESULT, out string resultPath)) || (!Path.IsPathRooted(resultPath)))
            {
                error = ARG_RESULT + ": 절대 경로가 필요하다";
                return false;
            }

            resultPathForFailure = resultPath;

            if ((!TryReadString(rawArgs, ARG_MODE, out string mode))
                || ((!string.Equals(mode, MODE_CREATE, StringComparison.Ordinal)) && (!string.Equals(mode, MODE_ENTER, StringComparison.Ordinal))))
            {
                error = ARG_MODE + ": create 또는 enter가 필요하다";
                return false;
            }

            if ((!TryReadInt(rawArgs, ARG_CLASS, out int classId)) || (classId < MIN_CLASS_ID) || (classId > MAX_CLASS_ID))
            {
                error = ARG_CLASS + ": " + MIN_CLASS_ID + ".." + MAX_CLASS_ID + " 범위가 필요하다";
                return false;
            }

            int faceId = CharacterAppearanceCatalog.FACE_CUTE;
            if ((TryReadInt(rawArgs, ARG_FACE, out int parsedFaceId)) && (parsedFaceId != CharacterAppearanceCatalog.FACE_CUTE))
            {
                error = ARG_FACE + ": Cute 얼굴 " + CharacterAppearanceCatalog.FACE_CUTE + "만 지원한다";
                return false;
            }

            if ((!TryReadInt(rawArgs, ARG_HAIR, out int hairId)) || (hairId < MIN_PART_ID) || (hairId > MAX_PART_ID))
            {
                error = ARG_HAIR + ": " + MIN_PART_ID + ".." + MAX_PART_ID + " 범위가 필요하다";
                return false;
            }

            if ((!TryReadInt(rawArgs, ARG_OUTFIT, out int outfitId)) || (outfitId < MIN_PART_ID) || (outfitId > MAX_PART_ID))
            {
                error = ARG_OUTFIT + ": " + MIN_PART_ID + ".." + MAX_PART_ID + " 범위가 필요하다";
                return false;
            }

            if ((!TryReadString(rawArgs, ARG_NAME, out string rawName))
                || (!CharacterNameRules.TryNormalize(rawName, out string name, out string _)))
            {
                error = ARG_NAME + ": " + CharacterNameRules.MIN_LENGTH + ".." + CharacterNameRules.MAX_LENGTH + "자 닉네임이 필요하다";
                return false;
            }

            int rejoins = MIN_REJOINS;
            if ((TryReadInt(rawArgs, ARG_REJOINS, out int parsedRejoins)))
            {
                rejoins = parsedRejoins;
            }

            if ((rejoins < MIN_REJOINS) || (rejoins > MAX_REJOINS))
            {
                error = ARG_REJOINS + ": " + MIN_REJOINS + ".." + MAX_REJOINS + " 범위가 필요하다";
                return false;
            }

            int observeSeconds = MIN_OBSERVE_SECONDS;
            if ((TryReadInt(rawArgs, ARG_OBSERVE_SECONDS, out int parsedObserve)))
            {
                observeSeconds = parsedObserve;
            }

            if ((observeSeconds < MIN_OBSERVE_SECONDS) || (observeSeconds > MAX_OBSERVE_SECONDS))
            {
                error = ARG_OBSERVE_SECONDS + ": " + MIN_OBSERVE_SECONDS + ".." + MAX_OBSERVE_SECONDS + " 범위가 필요하다";
                return false;
            }

            bool   hasRemoteName   = TryReadString(rawArgs, ARG_REMOTE_NAME, out string rawRemoteName);
            string remoteName      = null;
            int    remoteHairId    = 0;
            int    remoteOutfitId  = 0;
            if (hasRemoteName)
            {
                if (!CharacterNameRules.TryNormalize(rawRemoteName, out remoteName, out string _))
                {
                    error = ARG_REMOTE_NAME + ": 유효한 닉네임이 필요하다";
                    return false;
                }

                if ((!TryReadInt(rawArgs, ARG_REMOTE_HAIR, out remoteHairId)) || (remoteHairId < MIN_PART_ID) || (remoteHairId > MAX_PART_ID))
                {
                    error = ARG_REMOTE_HAIR + ": " + ARG_REMOTE_NAME + "와 함께 " + MIN_PART_ID + ".." + MAX_PART_ID + " 범위가 필요하다";
                    return false;
                }

                if ((!TryReadInt(rawArgs, ARG_REMOTE_OUTFIT, out remoteOutfitId))
                    || (remoteOutfitId < MIN_PART_ID) || (remoteOutfitId > MAX_PART_ID))
                {
                    error = ARG_REMOTE_OUTFIT + ": " + ARG_REMOTE_NAME + "와 함께 " + MIN_PART_ID + ".." + MAX_PART_ID + " 범위가 필요하다";
                    return false;
                }
            }
            else if ((TryReadInt(rawArgs, ARG_REMOTE_HAIR, out int _)) || (TryReadInt(rawArgs, ARG_REMOTE_OUTFIT, out int _)))
            {
                error = ARG_REMOTE_HAIR + "/" + ARG_REMOTE_OUTFIT + "는 " + ARG_REMOTE_NAME + " 없이 줄 수 없다";
                return false;
            }

            string screenshotPath = null;
            if ((TryReadString(rawArgs, ARG_SCREENSHOT, out string parsedScreenshotPath)))
            {
                if (!Path.IsPathRooted(parsedScreenshotPath))
                {
                    error = ARG_SCREENSHOT + ": 절대 경로가 필요하다";
                    return false;
                }

                screenshotPath = parsedScreenshotPath;
            }

            args = new ScenarioArgs
            {
                Mode           = mode,
                ClassId        = classId,
                FaceId         = faceId,
                HairId         = hairId,
                OutfitId       = outfitId,
                Name           = name,
                ResultPath     = resultPath,
                Rejoins        = rejoins,
                RemoteName     = remoteName,
                RemoteHairId   = remoteHairId,
                RemoteOutfitId = remoteOutfitId,
                ObserveSeconds = observeSeconds,
                ScreenshotPath = screenshotPath
            };

            return true;
        }

        private static bool TryReadString(string[] args, string name, out string value)
        {
            value = null;
            for (int i = 0; i < (args.Length - 1); ++i)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    value = args[i + 1];
                    return !string.IsNullOrEmpty(value);
                }
            }

            return false;
        }

        private static bool TryReadInt(string[] args, string name, out int value)
        {
            value = 0;
            return (TryReadString(args, name, out string text))
                && (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value));
        }
    }
}
