// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Dev
{
    /// <summary> DungeonForest 씬을 에디터에서 단독으로 Play하면 서버 없이 호스트 모드로 아처 한 명을 소환하고 카메라를 붙이는 솔로 시험 장치 </summary>
    public sealed class DevSoloForestHost : MonoBehaviour
    {
        // 정식 경로는 Bootstrap에서 시작해 서버가 플레이어를 소환한다. 씬만 단독으로 열면 서버도 플레이어도 없어 카메라가 원점에 서 있다.
        // 이 장치는 씬 파일을 건드리지 않고 실행 때만 스스로 만들어지며, 정식 서버의 전투 런타임과 같은 코드를 호스트 한 프로세스에서 돌린다.
        // 호스트 모드는 서버 전용 경로의 결함을 숨기므로 이 결과는 멀티플레이 검증 증거가 아니다(M0). 던전 진행(적 소환, 웨이브)은 붙이지 않는다.
        // 이 컴포넌트는 에디터 전용 어셈블리에 있어 빌드에 들어가지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const string FOREST_SCENE_NAME = "DungeonForest";
        private const string PLAYER_PREFAB     = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";
        private const string HOST_OBJECT_NAME  = "DevSoloForestHost";
        private const string MANAGER_NAME      = "DevSoloNetworkManager";
        private const string ADDRESS           = "localhost";
        private const ushort PORT              = 17877;

        // 허수아비는 플레이어 소환 지점 앞쪽(월드 +Z 쪽)에 흩어 세운다
        private const string DUMMY_NAME        = "DevSoloDummy";
        private const float  DUMMY_BODY_RADIUS = 0.5f;
        private const float  DUMMY_HEIGHT_HALF = 1f;

        private static readonly Vector3[] DUMMY_OFFSETS =
        {
            new Vector3(0f, 0f, 5f), new Vector3(-3f, 0f, 4f), new Vector3(3f, 0f, 4f), new Vector3(0f, 0f, 9f)
        };

        private DevSoloNetworkManager _manager      = null;
        private bool                  _dummiesPlaced = false;

        // 아처 외 직업을 시험하려면 소환 전에 바꾼다
        private static EPlayerClass _playerClass = EPlayerClass.Archer;

        /****************************************
        *              Unity Event
        ****************************************/

        // 첫 씬이 열린 직후 한 번 본다. 정식 매니저가 이미 있거나 서버가 켜져 있으면(Bootstrap 경로) 아무것도 하지 않는다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if ((SceneManager.GetActiveScene().name != FOREST_SCENE_NAME) || (NetworkManager.singleton != null)
                || (NetworkServer.active) || (NetworkClient.active)
                || (FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include) != null))
            {
                return;
            }

            new GameObject(HOST_OBJECT_NAME).AddComponent<DevSoloForestHost>();
        }

        private void Start()
        {
            Application.runInBackground = true;

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PREFAB);
            if (playerPrefab == null)
            {
                Debug.LogError($"[DevSolo] 플레이어 프리팹을 찾지 못했다: {PLAYER_PREFAB}", this);
                enabled = false;
                return;
            }

            GameDBBootstrap.EnsureInitialized();
            if (!GameDB.IsInitialized)
            {
                Debug.LogError("[DevSolo] GameDB 초기화에 실패해 솔로 호스트를 열지 않는다", this);
                enabled = false;
                return;
            }

            // 서버가 열리기 전에 판정 런타임을 설치해야 플레이어 개체가 생길 때 서버 판정이 붙는다
            CombatServerInstaller.Install(new SoloRoster());

            // 정식 서버는 출정 세션이 진행 중일 때만 기술 전투를 연다. 이 장치에는 출정 세션이 없어 기본값(닫힘)이면
            // 모든 기술이 WrongPhase로 거절되고 궁수 속성도 정해지지 않으므로 직접 연다
            CombatServerInstaller.Runtime.SetSkillCombatPhase(true);

            // 비활성 상태로 만들어 필드를 모두 채운 뒤 켜서 Awake가 완성된 설정을 읽게 한다
            var managerObject = new GameObject(MANAGER_NAME);
            managerObject.SetActive(false);
            var transport = managerObject.AddComponent<TelepathyTransport>();
            transport.port = PORT;
            _manager = managerObject.AddComponent<DevSoloNetworkManager>();
            _manager.transport          = transport;
            _manager.playerPrefab       = playerPrefab;
            _manager.networkAddress     = ADDRESS;
            _manager.dontDestroyOnLoad  = false;
            _manager.PlayerClass        = _playerClass;
            managerObject.SetActive(true);

            _manager.StartHost();
            if (!NetworkServer.active)
            {
                Debug.LogError("[DevSolo] 호스트를 열지 못했다. 같은 포트를 쓰는 다른 프로세스가 있는지 확인한다", this);
            }
        }

        // 플레이어 개체가 생기면 그 앞에 허수아비를 한 번만 세운다
        private void Update()
        {
            if ((_dummiesPlaced) || (!NetworkServer.active) || (NetworkClient.localPlayer == null))
            {
                return;
            }

            _dummiesPlaced = true;
            Vector3 origin = NetworkClient.localPlayer.transform.position;
            for (int i = 0; i < DUMMY_OFFSETS.Length; ++i)
            {
                PlaceDummy(origin + DUMMY_OFFSETS[i]);
            }
        }

        private void PlaceDummy(Vector3 position)
        {
            CombatTargetRegistry registry = CombatServerInstaller.Runtime.Registry;
            var target = new DevSoloDummyTarget(registry.IssueCombatId(), position, DUMMY_BODY_RADIUS);
            if (!registry.Register(target))
            {
                return;
            }

            // 보이기만 하는 껍데기라 충돌체를 떼어 판정과 이동에 끼어들지 않게 한다
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = DUMMY_NAME;
            Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(transform, false);
            visual.transform.position   = position + (Vector3.up * DUMMY_HEIGHT_HALF);
            visual.transform.localScale = new Vector3(DUMMY_BODY_RADIUS * 2f, DUMMY_HEIGHT_HALF, DUMMY_BODY_RADIUS * 2f);
        }

        private void OnDestroy()
        {
            if ((_manager != null) && (NetworkServer.active))
            {
                _manager.StopHost();
            }
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
    }
}
