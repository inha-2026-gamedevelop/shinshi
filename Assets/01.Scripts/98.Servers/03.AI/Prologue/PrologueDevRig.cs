#if UNITY_EDITOR
// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.AI
{
    /// <summary> Dev_Prologue 씬을 단독으로 Play하면 서버 없이 가짜 세션으로 프롤로그 전체를 끝까지 돌려 보는 시험 장치 </summary>
    public sealed class PrologueDevRig : MonoBehaviour, IPrologueEventSource, IPrologueHost, IPrologueClientLink, IPrologueLocalVitals
    {
        // 실제 서버 세션 대신 이 장치가 사건 수신과 서버 지시를 모두 맡는다. 서버 판정은 없으므로 이 결과는 서버 연결의 증거가 아니다.
        // 화면은 실제 클라이언트가 쓸 PrologueClientPresenter와 PrologueEntryCoordinator가 띄운다. 이 장치는 서버 상태 자리(IPrologueClientLink)를
        // 진행기 값으로 채워 주기만 하므로, 여기서 화면이 끝까지 도는 것은 같은 클라이언트 코드가 도는 것이다.
        // 씬 파일에는 이 컴포넌트를 직렬화하지 않고 씬 이름을 보고 스스로 만들어진다. 서버 어셈블리 타입이 씬에 남지 않게 하려는 것이다.
        // 일러스트와 자막은 PrologueIllustrationCatalog 에셋을 쓴다. 도입과 마무리 문구는 내용이 정해지지 않아 임시 문구다

        /****************************************
        *                Fields
        ****************************************/

        private const string SCENE_NAME      = "Dev_Prologue";
        private const string RIG_OBJECT_NAME = "PrologueDevRig";
        private const string WOLBAEK_PLAYER_BODY_NAME = "PlayerBody";
        private const string OS_FONT_NAME    = "Malgun Gothic";
        private const int    GUI_FONT_SIZE   = 16;
        private const float  PANEL_X         = 16f;
        private const float  PANEL_Y         = 16f;
        private const float  PANEL_WIDTH     = 340f;
        private const int    LOG_LIMIT       = 14;

        // 월백 등장 연출 길이, BossPatternCatalog의 AppearanceSeconds와 같은 값이다
        private const double APPEAR_SECONDS  = 2.5;
        private const double NO_CONTROL_TIME = -1.0;

        private const string CATALOG_PATH = "Assets/09.Data/AI/Prologue/PrologueIllustrationCatalog.asset";

        private const string GUIDE_CATALOG_PATH = "Assets/09.Data/AI/Prologue/MenuGuideCatalog.asset";
        private const string GUIDE_TRAINING     = "menu_training";
        private const string GUIDE_TALISMAN     = "menu_talisman";
        private const string GUIDE_TARGET_KEY   = "panel";
        private const int    GUIDE_TARGET_SORTING = 100;
        private const float  GUIDE_TARGET_MARGIN  = 120f;

        private static readonly Vector2 GUIDE_TARGET_SIZE  = new Vector2(420f, 260f);
        private static readonly Color   GUIDE_TARGET_COLOR = new Color(0.35f, 0.5f, 0.6f, 1f);

        private readonly List<string> _log = new List<string>();

        private PrologueDirector              _director   = null;
        private PrologueClientPresenter       _presenter  = null;
        private PrologueForestDevWorld        _world      = null;
        private GameObject                    _wolbaekRig = null;
        private PrologueEntryCoordinator      _entry      = null;
        private PrologueIllustrationCatalogSO _catalog    = null;
        private MenuGuideController           _guide      = null;
        private MenuGuideView                 _guideView  = null;
        private readonly MemoryMenuGuideRecord _guideRecord = new MemoryMenuGuideRecord();
        private RectTransform                 _guideTarget = null;
        private bool                          _accountCompleted = false;
        private bool                          _damaged    = false;
        private GUIStyle                      _labelStyle = null;
        private GUIStyle                      _buttonStyle = null;
        private bool                          _talismanChecked = false;
        private int                           _potionStock = 0;
        private int                           _lastSpawn  = 0;
        private double                        _controlAt  = NO_CONTROL_TIME;

        /****************************************
        *              Properties
        ****************************************/

        // 서버 상태 자리, 진행기 값을 그대로 보여 준다
        public EPrologueStage Stage          => (_director != null) ? _director.Stage : EPrologueStage.Intro;
        public EForestHint    Hint           => (_director != null) ? _director.CurrentHint : EForestHint.None;
        public bool           IsTalismanOpen => (_director != null) && (_director.BlocksInput);
        public bool           IsWolbaekOpen  => (_director != null) && (_director.IsWolbaekOpen);
        public int            PotionCount    => (_director != null) ? _director.PotionCount : 0;

        // 내 캐릭터 체력 자리, 패널의 "체력 줄었음" 토글이 채운다
        public bool           IsDamaged      => (_damaged) || ((_world != null) && (_world.IsDamaged));

        /****************************************
        *              Events
        ****************************************/

        public event Action      AttackUsed;
        public event Action      SkillQUsed;
        public event Action<int> EnemyKilled;
        public event Action      TelegraphStarted;
        public event Action      TelegraphEnded;
        public event Action      PlayerDown;
        public event Action      PotionUsed;
        public event Action      TalismanApproached;
        public event Action      TalismanConfirmed;
        public event Action      WolbaekIntroFinished;
        public event Action      WolbaekControlStarted;
        public event Action      IllustrationFinished;

        public event Action      Revived;

        /****************************************
        *              Unity Event
        ****************************************/

        // 씬이 열린 직후 한 번 본다. 개발 씬이 아니면 아무것도 하지 않는다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if ((SceneManager.GetActiveScene().name != SCENE_NAME) && (!PrologueMapDevBoot.IsMapScene()))
            {
                return;
            }

            new GameObject(RIG_OBJECT_NAME).AddComponent<PrologueDevRig>();
        }

        private void Start()
        {
            _catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<PrologueIllustrationCatalogSO>(CATALOG_PATH);
            if (_catalog == null)
            {
                Debug.LogError($"[PrologueDevRig] 일러스트 카탈로그를 찾지 못했다: {CATALOG_PATH}", this);
                enabled = false;
                return;
            }

            _entry = PrologueEntryCoordinator.CreateFromCatalog(_catalog);

            // 숲 지형 씬에서는 도형 대역이 걷고 싸우는 시험 세계가 사건을 대신 만든다
            if (PrologueMapDevBoot.IsMapScene())
            {
                _world = PrologueForestDevWorld.Create(IsWorldInputBlocked);
                _world.AttackUsed         += () => AttackUsed?.Invoke();
                _world.SkillQUsed         += () => SkillQUsed?.Invoke();
                _world.EnemyKilled        += (index) => EnemyKilled?.Invoke(index);
                _world.TelegraphStarted   += () => TelegraphStarted?.Invoke();
                _world.TelegraphEnded     += () => TelegraphEnded?.Invoke();
                _world.PlayerDown         += () => PlayerDown?.Invoke();
                _world.PotionUsed         += () => PotionUsed?.Invoke();
                _world.TalismanApproached += () => TalismanApproached?.Invoke();
            }

            SetUpGuide();

            Restart(false);
        }

        private void Update()
        {
            if (_director == null)
            {
                return;
            }

            _director.Tick();

            // 서버가 월백을 열고 등장 연출이 끝나면 조작 시작을 알린다
            if ((_controlAt != NO_CONTROL_TIME) && (Time.timeAsDouble >= _controlAt))
            {
                _controlAt = NO_CONTROL_TIME;
                WolbaekControlStarted?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (_director != null)
            {
                _director.Dispose();
            }

            _entry?.Dispose();
            _guide?.Dispose();
        }

        private void OnGUI()
        {
            if (_director == null)
            {
                return;
            }

            EnsureStyles();

            GUILayout.BeginArea(new Rect(PANEL_X, PANEL_Y, PANEL_WIDTH, Screen.height - (PANEL_Y * 2f)), GUI.skin.box);
            GUILayout.Label($"단계: {_director.Stage}   물약: {_director.PotionCount}", _labelStyle);
            GUILayout.Label($"안내: {_director.CurrentHint}   입력 차단: {_director.BlocksInput}", _labelStyle);

            if (_world != null)
            {
                GUILayout.Label("WASD 이동, J 기본 공격, Q 스킬, R 물약 (부적 가까이 가면 조사 화면)", _labelStyle);
            }

            if (Button("부적에 접근"))    { TalismanApproached?.Invoke(); }
            if (Button("기본 공격 사용")) { AttackUsed?.Invoke(); }
            if (Button("Q 사용"))         { SkillQUsed?.Invoke(); }
            if (Button("현재 적 처치"))   { EnemyKilled?.Invoke(_lastSpawn); }
            if (Button("예고 시작"))      { TelegraphStarted?.Invoke(); }
            if (Button("예고 끝"))        { TelegraphEnded?.Invoke(); }
            if (Button("물약 사용"))      { PotionUsed?.Invoke(); }
            if (Button("체력 0"))         { ForceDown(); }
            if (Button(_damaged ? "체력 정상으로" : "체력 줄었음 (물약 안내 확인)")) { _damaged = !_damaged; }

            GUILayout.Space(6f);
            if (Button("수련 메뉴 첫 진입 (이미 마쳤으면 안 뜸)")) { ShowGuide(GUIDE_TRAINING, false); }
            if (Button("부적 메뉴 첫 진입"))                      { ShowGuide(GUIDE_TALISMAN, false); }
            if (Button("수련 도움말 버튼 (다시 보기)"))           { ShowGuide(GUIDE_TRAINING, true); }
            if (Button("안내 완료 기록 지우기"))                  { _guideRecord.Clear(); Log("안내 완료 기록을 지웠다"); }
            if (Button(_accountCompleted ? "계정: 프롤로그 완료함 (누르면 최초로)" : "계정: 최초 (누르면 완료함으로)"))
            {
                _accountCompleted = !_accountCompleted;
            }

            if (Button("처음부터 (부적 미확인)")) { Restart(false); }
            if (Button("처음부터 (부적 확인함)")) { Restart(true); }

            GUILayout.Space(8f);
            for (int i = 0; i < _log.Count; ++i)
            {
                GUILayout.Label(_log[i], _labelStyle);
            }

            GUILayout.EndArea();
        }

        /****************************************
        *               Host
        ****************************************/

        public void SpawnEnemy(EPrologueEnemyKind kind, int spawnIndex)
        {
            _lastSpawn = spawnIndex;
            Log($"소환 지시: {kind} #{spawnIndex}");
            _world?.SpawnEnemy(kind, spawnIndex);
        }

        public void SetPotions(int count)
        {
            _potionStock = count;
            Log($"물약 재고: {count}");
        }

        public void ReviveToFull(float invulnerableSeconds)
        {
            Log($"체력 회복 + 무적 {invulnerableSeconds}초");
            _world?.Revive(invulnerableSeconds);
            Revived?.Invoke();
        }

        public void StartWolbaek()
        {
            _controlAt = Time.timeAsDouble + APPEAR_SECONDS;
            Log("월백 소환, 플레이어 이동, 체력 최대 회복");

            // 숲 지형 씬에서는 숲 세계를 접고 월백 시험 구동기가 그 자리를 이어받는다
            if (_world != null)
            {
                _world.Hide();
                _wolbaekRig = PrologueMapDevBoot.SpawnWolbaekRig();
                StartCoroutine(FollowWolbaekPlayer(_wolbaekRig));
            }
        }

        public void EndEngagement(EWolbaekEnd reason)
        {
            Log($"월백 교전 종료: {reason}");
            DestroyWolbaekRig();
        }

        public void ReportStage(EPrologueStage stage, int wolbaekEntryPotions)
        {
            Log($"단계 저장: {stage} (월백 진입 물약 {wolbaekEntryPotions})");

            if (stage == EPrologueStage.Done)
            {
                // 실제로는 신시 도착과 완료 기록을 확인한 마을 쪽이 부른다. 시험 장치에는 마을이 없어 완료 처리 직후에 부른다
                PrologueTownNotice.ShowCompletedFromCatalog(_catalog);
            }
        }

        /****************************************
        *            Client Link
        ****************************************/

        // 클라이언트가 화면을 끝냈다고 서버에 알리는 자리, 시험 장치는 서버 사건으로 되돌려 보낸다
        public void SendTalismanConfirmed()     { TalismanConfirmed?.Invoke(); }
        public void SendIllustrationFinished()  { IllustrationFinished?.Invoke(); }
        public void SendWolbaekIntroFinished()  { WolbaekIntroFinished?.Invoke(); }

        /****************************************
        *                Methods
        ****************************************/

        // 처음부터 다시 시작한다. 진행기는 부적 확인 여부를 만들 때 받으므로 새로 만든다
        private void Restart(bool talismanChecked)
        {
            if (_director != null)
            {
                _director.Dispose();
            }

            if (_presenter != null)
            {
                Destroy(_presenter.gameObject);
                _presenter = null;
            }

            DestroyWolbaekRig();
            _world?.Hide();

            _log.Clear();
            _talismanChecked = talismanChecked;
            _controlAt       = NO_CONTROL_TIME;

            _director = new PrologueDirector(this, this, () => Time.timeAsDouble, _talismanChecked);

            // 로비가 하는 일: 계정 완료 여부로 선택창을 띄우거나 바로 시작한다
            Log(_accountCompleted ? "선택창 표시: 계정이 이미 프롤로그를 마쳤다" : "계정 최초: 선택창 없이 프롤로그 시작");
            _entry.Begin(_accountCompleted, OnStartPrologue, OnSkipToTown);
        }

        private void OnStartPrologue()
        {
            Log("프롤로그 진행");
            _director.Start(EPrologueStage.Intro, 0);

            // 실제 흐름에서는 서버 세션이 열린 뒤 클라이언트가 이 연결부를 만든다
            _presenter = PrologueClientPresenter.Create(this, this, _catalog);

            // 숲 구간의 플레이어 대역은 일러스트 뒤에서 대기한다. 조작은 화면이 닫혀야 받는다
            _world?.Begin();
        }

        // 조사 화면이나 일러스트가 떠 있는 동안은 조작을 막는다
        private bool IsWorldInputBlocked()
        {
            if (_director == null)
            {
                return true;
            }

            return (_director.BlocksInput) || ((_presenter != null) && (_presenter.IsShowingIllustration));
        }

        private void ForceDown()
        {
            if ((_world != null) && (_director.Stage == EPrologueStage.Forest))
            {
                _world.ForceDown();
                return;
            }

            PlayerDown?.Invoke();
        }

        // 월백 구동기는 첫 프레임에 자기 플레이어 도형을 만든다. 만들어지면 카메라를 그쪽으로 옮긴다
        private IEnumerator FollowWolbaekPlayer(GameObject rig)
        {
            Transform body = null;
            while ((rig != null) && (body == null))
            {
                body = rig.transform.Find(WOLBAEK_PLAYER_BODY_NAME);
                yield return null;
            }

            if (body != null)
            {
                PrologueDevCamera.Follow(body);
            }
        }

        private void DestroyWolbaekRig()
        {
            if (_wolbaekRig != null)
            {
                Destroy(_wolbaekRig);
                _wolbaekRig = null;
            }
        }

        // 건너뛰기를 골랐다면 도입 없이 바로 완료다. 완료 기록과 기본 지급품은 서버가 맡는다
        private void OnSkipToTown()
        {
            Log("건너뛰고 신시로 이동");
            _director.Start(PrologueEntryRules.StartStage(EPrologueChoice.SkipToTown), 0);
            _director.Complete();
        }

        // 성장 메뉴 안내를 시험하기 위한 가짜 메뉴 패널과 진행기를 만든다
        private void SetUpGuide()
        {
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<MenuGuideCatalogSO>(GUIDE_CATALOG_PATH);
            if (catalog == null)
            {
                Debug.LogWarning($"[PrologueDevRig] 성장 안내 카탈로그를 찾지 못했다: {GUIDE_CATALOG_PATH}", this);
                return;
            }

            _guideView = MenuGuideView.Create();
            PrologueDevFont.Apply(_guideView);
            _guide = new MenuGuideController(catalog, _guideRecord, _guideView);

            var canvasObject = new GameObject("DevGuideTargetCanvas");
            canvasObject.transform.SetParent(transform, false);
            PrologueUi.AddCanvas(canvasObject, GUIDE_TARGET_SORTING, false);
            _guideTarget = PrologueUi.CreateImage("Panel", canvasObject.transform, GUIDE_TARGET_COLOR, false);
            _guideTarget.anchorMin        = new Vector2(1f, 0.5f);
            _guideTarget.anchorMax        = new Vector2(1f, 0.5f);
            _guideTarget.pivot            = new Vector2(1f, 0.5f);
            _guideTarget.anchoredPosition = new Vector2(-GUIDE_TARGET_MARGIN, 0f);
            _guideTarget.sizeDelta        = GUIDE_TARGET_SIZE;
        }

        private void ShowGuide(string menuId, bool replay)
        {
            if (_guide == null)
            {
                return;
            }

            bool shown = replay ? _guide.Replay(menuId, ResolveGuideTarget) : _guide.TryShowFirstTime(menuId, ResolveGuideTarget);
            Log(shown ? $"성장 안내 표시: {menuId}" : $"성장 안내 안 뜸: {menuId} (이미 마쳤거나 등록 안 됨)");
        }

        private RectTransform ResolveGuideTarget(string key)
        {
            return (key == GUIDE_TARGET_KEY) ? _guideTarget : null;
        }

        private void Log(string line)
        {
            _log.Add(line);
            if (_log.Count > LOG_LIMIT)
            {
                _log.RemoveAt(0);
            }
        }

        private bool Button(string label)
        {
            return GUILayout.Button(label, _buttonStyle);
        }

        // IMGUI 기본 글꼴은 한글이 깨져서 OS 한글 글꼴을 쓴다
        private void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

            Font font = Font.CreateDynamicFontFromOSFont(OS_FONT_NAME, GUI_FONT_SIZE);

            _labelStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = GUI_FONT_SIZE, wordWrap = true };
            _labelStyle.normal.textColor = Color.white;

            _buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = GUI_FONT_SIZE };
        }
    }
}
#endif
