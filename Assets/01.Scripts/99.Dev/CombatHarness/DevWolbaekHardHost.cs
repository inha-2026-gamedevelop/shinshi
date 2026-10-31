// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using Mirror;
using Unity.Cinemachine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Dev
{
    // 월백 하드 패턴을 한 개씩 골라 반복해서 보는 단독 호스트 시험 장치다. 서버와 클라이언트를 한 프로세스(호스트)에서 돌리고,
    // 보스는 운영과 같은 경로로 만든다: 서버의 BossServer가 패턴을 돌리고, BossNetworkSpawner가 네트워크 보스 프리팹(BossState)을 스폰하고,
    // 클라이언트 바인더(BossNetworkViewBinder)가 복제된 사건을 받아 표현한다. 표현을 위해 따로 짠 재생 코드는 없다.
    // 운영과 다른 점은 셋이다. (1) 카탈로그를 고른 패턴 하나만 돌려주는 래퍼로 감싸 그 패턴만 반복해서 고르게 한다
    // (2) 플레이어가 맞아도 죽지 않게 피해 배율을 거의 0으로 둔다 (3) 보스 체력을 크게 잡아 플레이어 공격으로 페이즈가 넘어가지 않게 한다.
    // 호스트 모드는 서버 전용 경로의 결함을 숨기므로 이 결과는 멀티플레이 검증 증거가 아니다(M0). 이 컴포넌트는 에디터 전용 어셈블리에 있어 빌드에 들어가지 않는다
    /// <summary> 월백 하드 패턴 하나를 골라 반복 재생하는 단독 호스트 장치, 화면 버튼과 단축키로 조작한다 </summary>
    public sealed class DevWolbaekHardHost : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string PLAYER_PREFAB    = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";
        private const string BOSS_PREFAB      = "Assets/02.Prefab/Network/NetBoss/PF_NetWolbaekBoss.prefab";
        private const string MANAGER_NAME     = "DevWolbaekHardNetworkManager";
        private const string ADDRESS          = "localhost";
        private const ushort PORT             = 17878;

        private const float  PLAYER_START_Z   = 12f;    // m, 보스는 원점이고 플레이어는 이 거리만큼 떨어져 선다. 근거리 배율이 0인 패턴도 고르게 되는 거리다
        private const float  DAMAGE_SCALE     = 0.0001f; // 보스 공격이 플레이어를 죽이지 않게 하는 피해 배율. 맞음 판정과 피격 연출은 그대로 나간다
        private const float  HEALTH_SCALE     = 1000f;
        private const float  GROUND_THICKNESS = 0.02f;

        private const float  UI_SCALE         = 1f;
        private const float  PANEL_MARGIN     = 8f;
        private const float  PANEL_WIDTH      = 250f;
        private const float  PANEL_GAP        = 6f;
        private const float  PANEL_MARGIN_SIDES = 2f; // 위와 아래 여백
        private const float  OPEN_BUTTON_WIDTH  = 120f;
        private const float  OPEN_BUTTON_HEIGHT = 30f;
        private const int    PHASE_ONE_COUNT  = 6;
        private const int    NO_SELECTION     = -1;

        private static readonly string[] PATTERN_IDS =
        {
            BossPatternCatalogSO.WOLBAEK_HARD_P01_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P02_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P03_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P04_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P05_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P06_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P01_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P02_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P03_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P04_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P05_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P06_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P07_ID
        };

        private static readonly string[] PATTERN_LABELS =
        {
            "1P P01 돌진",
            "1P P02 부채꼴",
            "1P P03 원형 폭발",
            "1P P04 투사체",
            "1P P05 장판",
            "1P P06 직선 폭발",
            "2P P01 단계형 별 빔",
            "2P P02 격자 순차 빔",
            "2P P03 잔류 부채꼴 연쇄",
            "2P P04 추적 장판",
            "2P P05 전방위 난사",
            "2P P06 동시 원형 폭발",
            "2P P07 워프 충격파"
        };

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private int _startIndex = 0;

        private DevSoloNetworkManager   _manager;
        private BossNetworkSpawner      _spawner;
        private SingleHardCatalog       _catalog;
        private BossServer              _boss;
        private BossState               _state;

        private CinemachineCamera _camera;
        private Transform         _arenaAnchor;

        private int     _selected    = NO_SELECTION;
        private bool    _followArena = true; // 플레이어를 따라가면 보스가 화면 밖으로 나가므로 기본은 전장 중심이다
        private bool    _isPanelOpen = true;
        private string  _notice      = string.Empty;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            Application.runInBackground = true;

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PREFAB);
            var bossPrefab   = AssetDatabase.LoadAssetAtPath<GameObject>(BOSS_PREFAB);
            if ((playerPrefab == null) || (bossPrefab == null))
            {
                Fail("플레이어나 보스 프리팹을 찾지 못했다");
                return;
            }

            GameDBBootstrap.EnsureInitialized();
            if (!GameDB.IsInitialized)
            {
                Fail("GameDB 초기화에 실패해 호스트를 열지 않는다");
                return;
            }

            if (!GameDB.Progression.TryGetRegion(Constants.Combat.DEV_BOSS_REGION_ID, out _))
            {
                Fail($"전장 지역이 없다, region={Constants.Combat.DEV_BOSS_REGION_ID}");
                return;
            }

            _catalog = new SingleHardCatalog(GameDB.BossPatterns);
            if (!_catalog.TryGetBoss(Constants.Combat.DEV_BOSS_ID, out BossPatternCatalogSO.BossDefinition definition))
            {
                Fail("월백 정의가 카탈로그에 없다");
                return;
            }

            // 카메라 묶음 프리팹(PF_GameCamera)이 씬에 미리 놓여 있어 직렬화 참조를 걸 수 없는 프리팹 내부 컴포넌트다. 시작할 때 한 번만 찾아 캐싱한다
            _camera = FindAnyObjectByType<CinemachineCamera>();

            BuildGround(definition.ArenaDiameter);
            BuildStartPosition();

            // 서버가 열리기 전에 판정 런타임을 설치해야 플레이어 개체가 생길 때 서버 판정이 붙는다
            CombatServerInstaller.Install(new SoloRoster());
            CombatServerInstaller.Runtime.SetSkillCombatPhase(true);

            // 비활성 상태로 만들어 필드를 모두 채운 뒤 켜서 Awake가 완성된 설정을 읽게 한다
            var managerObject = new GameObject(MANAGER_NAME);
            managerObject.SetActive(false);
            var transport = managerObject.AddComponent<TelepathyTransport>();
            transport.port = PORT;
            _manager = managerObject.AddComponent<DevSoloNetworkManager>();
            _manager.transport         = transport;
            _manager.playerPrefab      = playerPrefab;
            _manager.networkAddress    = ADDRESS;
            _manager.dontDestroyOnLoad = false;
            _manager.spawnPrefabs.Add(bossPrefab);
            managerObject.SetActive(true);

            _spawner = new BossNetworkSpawner(bossPrefab, _manager.spawnPrefabs);
            _manager.StartHost();
            if (!NetworkServer.active)
            {
                Fail("호스트를 열지 못했다. 같은 포트를 쓰는 다른 프로세스가 있는지 확인한다");
                return;
            }

            _selected = Mathf.Clamp(_startIndex, 0, PATTERN_IDS.Length - 1);
        }

        private void Update()
        {
            HandleKeys();

            if ((_manager == null) || (!NetworkServer.active))
            {
                return;
            }

            // 플레이어 개체가 생긴 뒤에 보스를 연다. 보스가 대상이 없는 채로 열리면 첫 선택이 비어 버린다
            if ((_boss == null) && (_selected != NO_SELECTION) && (NetworkClient.localPlayer != null))
            {
                CreateBoss();
            }

            if (_boss == null)
            {
                return;
            }

            // 운영은 서버 틱에서 같은 순서로 부른다: 보스 틱 뒤에 복제 상태를 기록한다
            _boss.Tick(NetworkTime.time);
            if (_state != null)
            {
                _spawner.Publish(_boss, _state);
            }
        }

        // 로컬 플레이어 카메라 묶음(LocalPlayerCameraBinder)은 처음 한 번만 대상을 정하므로 그 뒤에는 이 장치가 대상을 바꿔도 되돌리지 않는다
        private void LateUpdate()
        {
            if ((!NetworkServer.active) || (_arenaAnchor == null))
            {
                return;
            }

            if (_camera == null)
            {
                return;
            }

            Transform target = _followArena ? _arenaAnchor : ((NetworkClient.localPlayer != null) ? NetworkClient.localPlayer.transform : null);
            if ((target != null) && (_camera.Target.TrackingTarget != target))
            {
                _camera.Target.TrackingTarget = target;
            }
        }

        private void OnGUI()
        {
            if (!_isPanelOpen)
            {
                if (GUI.Button(new Rect(PANEL_MARGIN, PANEL_MARGIN, OPEN_BUTTON_WIDTH, OPEN_BUTTON_HEIGHT), "패널 열기 (H)"))
                {
                    _isPanelOpen = true;
                }

                return;
            }

            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(UI_SCALE, UI_SCALE, 1f));
            DrawPanel();
            GUI.matrix = saved;
        }

        private void OnDestroy()
        {
            DestroyBoss();
            if ((_manager != null) && (NetworkServer.active))
            {
                _manager.StopHost();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Fail(string reason)
        {
            _notice = reason;
            Debug.LogError($"[DevWolbaekHard] {reason}", this);
        }

        // 전장 바닥. 보스 정의의 전장 지름과 같은 원판이다. 우클릭 이동은 커서 아래 바닥을 레이캐스트해 목적지를 얻으므로
        // 충돌체가 있어야 하고, 원기둥 기본 충돌체(캡슐)는 모양이 달라 메시 충돌체로 바꾼다
        private void BuildGround(float diameter)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ground.name = "DevWolbaekHardGround";
            Destroy(ground.GetComponent<Collider>());
            ground.AddComponent<MeshCollider>();
            ground.transform.SetParent(transform, false);
            ground.transform.position   = new Vector3(0f, -GROUND_THICKNESS, 0f);
            ground.transform.localScale = new Vector3(diameter, GROUND_THICKNESS, diameter);
        }

        // 플레이어는 보스(원점)를 바라보며 PLAYER_START_Z 만큼 떨어져 선다
        private void BuildStartPosition()
        {
            var start = new GameObject("DevWolbaekHardStart");
            start.transform.SetParent(transform, false);
            start.transform.SetPositionAndRotation(new Vector3(0f, 0f, PLAYER_START_Z), Quaternion.LookRotation(Vector3.back));
            start.AddComponent<NetworkStartPosition>();

            var anchor = new GameObject("DevWolbaekHardArenaAnchor");
            anchor.transform.SetParent(transform, false);
            _arenaAnchor = anchor.transform;
        }

        // 지금 보스를 치우고 고른 패턴으로 새로 연다. 패턴을 바꾸거나 처음부터 다시 볼 때 쓴다
        private void RestartWith(int index)
        {
            if (index == NO_SELECTION)
            {
                return;
            }

            DestroyBoss();
            _selected = Mathf.Clamp(index, 0, PATTERN_IDS.Length - 1);
        }

        private void CreateBoss()
        {
            CombatServerRuntime runtime = CombatServerInstaller.Runtime;
            if (runtime == null)
            {
                return;
            }

            _catalog.SelectedId = PATTERN_IDS[_selected];

            ProgressionCatalogSO progression = GameDB.Progression;
            progression.TryGetRegion(Constants.Combat.DEV_BOSS_REGION_ID, out ProgressionCatalogSO.RegionDefinition region);
            float referenceAttack = progression.GetReferenceAttack(region);
            float referenceHealth = progression.GetReferenceHealth(region);

            var scaledResolver = new BossDamageScaler(runtime.Resolver, DAMAGE_SCALE);
            var boss = new BossServer(Constants.Combat.DEV_BOSS_ID, runtime.Registry.IssueCombatId(), Vector3.zero, Vector3.forward,
                Vector3.zero, referenceAttack, referenceHealth, HEALTH_SCALE, _catalog, runtime.Registry, scaledResolver,
                new BossTerrainProbe(0), new SeededBossRandom(Environment.TickCount), connections: new AllConnected());

            if (!boss.Begin(NetworkTime.time))
            {
                Fail("보스 전투를 열지 못했다");
                _selected = NO_SELECTION;
                return;
            }

            if (!runtime.Registry.Register(boss))
            {
                Fail("전투 대상 등록이 거절됐다");
                _selected = NO_SELECTION;
                return;
            }

            // 스폰은 첫 보스 틱보다 앞에 둔다. 스폰 메시지가 첫 예고 Rpc보다 먼저 가야 클라이언트가 사건을 놓치지 않는다
            if (!_spawner.TrySpawn(boss, out _state))
            {
                Fail("보스 개체를 스폰하지 못했다");
                runtime.Registry.Unregister(boss.CombatId);
                _selected = NO_SELECTION;
                return;
            }

            _boss   = boss;
            _notice = string.Empty;
        }

        private void DestroyBoss()
        {
            if (_boss == null)
            {
                return;
            }

            // 진행 중이던 예고와 지속물을 같은 시각에 정리하고 개체를 치운다
            _boss.EndCombat(NetworkTime.time);
            _spawner.Despawn(_boss, _state);
            CombatServerInstaller.Runtime.Registry.Unregister(_boss.CombatId);
            _boss  = null;
            _state = null;
        }

        /****************************************
        *                Input
        ****************************************/

        // 숫자, Q W E R, Space, Z X C는 플레이어 입력(물약, 스킬, 회피)이라 쓰지 않는다
        private void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                RestartWith(_selected);
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _followArena = !_followArena;
            }

            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                RestartWith((_selected + PATTERN_IDS.Length - 1) % PATTERN_IDS.Length);
            }

            if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                RestartWith((_selected + 1) % PATTERN_IDS.Length);
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                _isPanelOpen = !_isPanelOpen;
            }
        }

        /****************************************
        *                  GUI
        ****************************************/

        private void DrawPanel()
        {
            GUILayout.BeginArea(new Rect(PANEL_MARGIN, PANEL_MARGIN, PANEL_WIDTH, (Screen.height / UI_SCALE) - (PANEL_MARGIN * PANEL_MARGIN_SIDES)), GUI.skin.box);
            GUILayout.Label("월백 하드 패턴 시험 (호스트)");

            for (int i = 0; i < PATTERN_LABELS.Length; ++i)
            {
                if (i == PHASE_ONE_COUNT)
                {
                    GUILayout.Space(PANEL_GAP);
                }

                string mark = (i == _selected) ? "  <" : string.Empty;
                if (GUILayout.Button(PATTERN_LABELS[i] + mark))
                {
                    RestartWith(i);
                }
            }

            GUILayout.Space(PANEL_GAP);
            if (GUILayout.Button("처음부터 다시 (Backspace)"))
            {
                RestartWith(_selected);
            }

            if (GUILayout.Button($"카메라: {(_followArena ? "전장 중심" : "플레이어")} (Tab)"))
            {
                _followArena = !_followArena;
            }

            GUILayout.Space(PANEL_GAP);
            if (_boss != null)
            {
                GUILayout.Label($"보스 {_boss.FlowState}  페이즈 {_boss.Phase}  위치 {_boss.Position.x:0.0}, {_boss.Position.z:0.0}");
                GUILayout.Label($"활성: 투사체 {_boss.ActiveProjectileCount}  빔 {_boss.ActiveBeamCount}  별 {_boss.ActiveStarCount}");
            }

            if (!string.IsNullOrEmpty(_notice))
            {
                GUILayout.Label(_notice);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("우클릭 이동, [ ] 이전/다음 패턴, Backspace 다시, Tab 카메라, H 패널");
            GUILayout.Label("보스와 거리가 패턴이 싫어하는 구간이면 보스가 그 패턴을 고르지 않는다(거리 배율 0)");
            GUILayout.EndArea();
        }

        /****************************************
        *               Nested
        ****************************************/

        // 솔로에는 닫힌 참가자(재접속 유예 만료)가 없다
        private sealed class SoloRoster : IParticipantRoster
        {
            public bool IsClosed(int participantId)
            {
                return false;
            }

            public void Close(int participantId)
            {
            }
        }

        // 하드 P04(추적 장판)가 접속한 대상만 고르는 데 쓰는 입력, 솔로에서는 모두 접속 중이다
        private sealed class AllConnected : IBossParticipantConnection
        {
            public bool IsConnected(int combatId)
            {
                return true;
            }
        }

        // 하드 난이도 카탈로그에서 고른 패턴 하나만 돌려준다. 페이즈 번호는 무시한다: 2페이즈 패턴도 체력 전환 없이 1페이즈 두뇌가 그대로 고른다
        // 선택 정의가 후보의 전부라 두뇌의 가중 선택은 이 패턴을 반복한다(최근 사용 배율은 0보다 커서 후보에서 빠지지 않는다)
        private sealed class SingleHardCatalog : IBossPatternCatalog, IBossPatternReadiness
        {
            private readonly BossPatternCatalogSO     _asset;
            private readonly BossPatternCatalogSource _hard;

            public string SelectedId { get; set; }

            public SingleHardCatalog(BossPatternCatalogSO asset)
            {
                _asset = asset;
                _hard  = new BossPatternCatalogSource(asset, EBossDifficulty.Hard);
            }

            public bool TryGetBoss(string bossId, out BossPatternCatalogSO.BossDefinition boss)
            {
                return _hard.TryGetBoss(bossId, out boss);
            }

            public void CollectPatterns(string bossId, int phase, List<PatternDefinition> results)
            {
                results.Clear();
                if ((!string.IsNullOrEmpty(SelectedId)) && (_asset.TryGetPattern(SelectedId, out PatternDefinition definition)))
                {
                    results.Add(definition);
                }
            }

            public bool IsTuned(string bossId)
            {
                return true;
            }

            public bool IsPatternReady(string bossId, PatternDefinition pattern)
            {
                return _hard.IsPatternReady(bossId, pattern);
            }
        }
    }
}
