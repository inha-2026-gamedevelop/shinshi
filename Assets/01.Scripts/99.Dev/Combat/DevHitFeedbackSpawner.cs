// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Serialization;

namespace Shinshi.Dev
{
    /// <summary> 구역마다 문 앞에 몬스터 한 무리를 한 번만 세우고, 구역별 처치 수를 세는 시험용 스포너 </summary>
    public sealed class DevHitFeedbackSpawner : MonoBehaviour
    {
        // 플레이어의 Hit 이벤트를 받아 맞은 충돌체가 몬스터면 한 대 때린다. 위치는 구역 중심 원 안에서 무작위로 고르고,
        // 땅 위이면서 다른 충돌체(나무, 바위, 다른 몬스터, 플레이어)와 겹치지 않는 자리만 쓴다

        /****************************************
        *                Fields
        ****************************************/

        private const string MONSTER_NAME = "DevMonster";

        // 몬스터 한 종의 모델. 컨트롤러는 이동 애니메이션(Moving)을 가진 것을 쓴다
        [System.Serializable]
        private struct ModelEntry
        {
            public GameObject                prefab;
            public RuntimeAnimatorController controller;
            public float                     scale; // 모델 크기 배율. 0이면 1로 본다
        }

        private const int   PLACE_ATTEMPTS_PER_MONSTER = 30;
        private const int   RAYCAST_MAX_HITS           = 16; // 나무 가지와 지붕 충돌체에 가려 진짜 땅이 잘리지 않게 넉넉히 둔다
        private const int   OVERLAP_BUFFER_SIZE        = 4;
        private const float GROUND_PROBE_HEIGHT        = 30f;  // m, 구역 중심 위 이 높이에서 아래로 땅을 찾는다
        private const float GROUND_PROBE_DEPTH         = 60f;  // m
        private const float MONSTER_HEIGHT             = 1.6f; // m
        private const float MONSTER_RADIUS             = 0.4f; // m
        private const float OVERLAP_PADDING            = 0.15f; // m, 몬스터끼리 닿지 않게 벌려 두는 여유
        private const float HALF                       = 0.5f;
        private const float CAPSULE_DEFAULT_RADIUS     = 0.5f; // 캡슐 기본 지름 1m, 높이 2m. 루트 스케일이 실제 크기를 정한다
        private const float CAPSULE_DEFAULT_HEIGHT     = 2f;
        private const float RADIUS_EPSILON             = 0.001f;
        private const int   GIZMO_SEGMENTS            = 48;
        private const float GIZMO_LIFT                 = 0.2f; // m, 땅에 묻히지 않게 띄우는 높이
        private const float GIZMO_POLE_HEIGHT          = 6f;   // m, 멀리서도 보이는 기둥 높이

        [Header("참조")]
        [SerializeField] private DevHitFeedbackPlayer _player;
        [SerializeField] private HitFeedbackDirector  _director;

        [Header("서버 대역")]
        [SerializeField, Range(0f, 1f)] private float _devCritChance = 0.25f; // 서버가 정해 줄 치명타를 시험하려고 무작위로 내는 확률

        [Header("모델 (비우면 캡슐, 여러 종이면 섞어서 세운다)")]
        [SerializeField] private ModelEntry[] _models;

        [Header("무리")]
        [SerializeField, Min(0.1f)] private float _sizeMultiplier = 1.3f; // 몬스터 전체(충돌체와 모델) 크기 배율
        [SerializeField] private int   _groupSize        = 24;
        [SerializeField, Min(1)] private int _hitsToDie  = 1; // 한 대 맞으면 죽는다
        [SerializeField] private float _chaseSpeed       = 2.5f; // m/s, 플레이어를 쫓는 속도
        [SerializeField] private float _spawnRadius      = 6f;  // m, 문 앞 출현 지점 주위
        [SerializeField, Min(0f)] private float _attackWindup   = 0.5f; // 초, 공격 애니메이션을 틀고 맞히기까지
        [SerializeField, Min(0f)] private float _attackCooldown = 2f;   // 초, 한 번 공격하고 다음 공격까지
        [SerializeField, Min(0f)] private float _maxSpawnRadius = 12f; // m, 기본 반경에 자리가 모자라면 여기까지 넓혀서 찾는다
        [SerializeField, Min(0f)] private float _radiusStep     = 2f;  // m, 반경을 넓히는 단계
        [SerializeField, Min(0f)] private float _activityRadius = 15f; // m, 플레이어가 구역 출현 지점에서 이 반경 안에 들어서면 몬스터가 쫓기 시작한다. 한 번 시작하면 밖으로 나가도 쫓는다. 0이면 처음부터 쫓는다
        [FormerlySerializedAs("_respawnDelay")]
        [SerializeField] private float _retryDelay       = 3f;  // 초, 자리가 없어 한 마리도 못 세웠을 때 다시 시도하기까지

        [Header("구역 (구역마다 문 앞 출현 지점 하나. 비우면 이 오브젝트 자리 한 곳)")]
        [SerializeField] private Transform[] _zoneAnchors;
        [SerializeField, Min(0f)] private float _enterRadius       = 3f;  // m, 구역 입구 지점에서 이 수평 반경 안에 들어서야 그 구역 무리를 세운다
        [SerializeField, Min(0f)] private float _enterHeight       = 3f;  // m, 입구 지점과 이 높이 차 안일 때만 들어선 것으로 본다 (같은 보행 층)
        [SerializeField, Min(0f)] private float _minPlayerDistance = 4f;  // m, 플레이어와 이 거리 안에는 세우지 않는다
        [SerializeField, Min(0f)] private float _groundHeightTolerance = 1f; // m, 입구 지점과 이 높이 차 안의 땅에만 세운다 (지붕, 위층 방지)
        [SerializeField] private bool _spawnFirstZoneAtStart = true;      // 첫 구역은 입구 없이 출정 시작과 함께 세운다
        [SerializeField] private bool _spawnOnPreviousClear  = true;      // 이전 구역을 모두 처치해 그 사이 결계가 풀리면 다음 구역을 세운다. 끄면 입구에 들어설 때 세운다

        [Header("무한 소환 (켜면 처치된 몬스터가 같은 구역에 다시 나온다. 구역은 입구에 들어설 때 세운다)")]
        [SerializeField] private bool _infiniteSpawn = true;
        [SerializeField, Min(0f)] private float _infiniteRespawnDelay = 0.8f; // 초, 처치된 뒤 새 몬스터가 나오기까지

        [Header("허수아비 (지점이 있으면 구역 무리를 세우지 않고 지점마다 체력이 끝없는 캡슐을 세운다. 공격 효과 시험용)")]
        [SerializeField] private Transform[] _dummyPoints;

        [Header("엘리트 (이 구역에 일반 무리와 함께 세운다. 엘리트까지 잡아야 구역 처치가 끝난다)")]
        [SerializeField, Min(0)] private int   _eliteZoneIndex = 5;    // 0부터 센 구역 번호, 5가 6구역
        [SerializeField, Min(0)] private int   _eliteCount     = 2;
        [SerializeField, Min(1)] private int   _eliteHitsToDie = 2;
        [SerializeField, Min(0.1f)] private float _eliteSizeScale = 2f; // 일반 몬스터 대비 몸집 배율

        private readonly RaycastHit[] _rayHits     = new RaycastHit[RAYCAST_MAX_HITS];
        private readonly Collider[]   _overlapHits = new Collider[OVERLAP_BUFFER_SIZE];
        private readonly Dictionary<DevHitFeedbackMonster, int> _zoneOfMonster = new Dictionary<DevHitFeedbackMonster, int>();

        private Collider _playerCollider = null;
        private int[]    _zoneAlive      = null;
        private int[]    _zonePlanned    = null; // 구역에 실제로 세운 마리 수
        private int[]    _zoneKilled     = null;
        private bool[]   _zoneSpawned    = null; // 구역 무리는 한 번만 세운다
        private float[]  _zoneNextTry    = null;
        private float    _firstSpawnTime = -1f;

        private readonly List<float> _respawnTimes = new List<float>(); // 새 몬스터를 세울 시각(Time.time)과 구역, 같은 위치끼리 짝이다
        private readonly List<int>   _respawnZones = new List<int>();

        /****************************************
        *               Properties
        ****************************************/

        /// <summary> 켜면 처치된 몬스터가 같은 구역에 다시 나온다 </summary>
        public bool InfiniteSpawn { get => _infiniteSpawn; set => _infiniteSpawn = value; }

        /// <summary> 서버 대역이 무작위로 내는 치명타 확률(0에서 1) </summary>
        public float DevCritChance { get => _devCritChance; set => _devCritChance = Mathf.Clamp01(value); }

        public int ZoneCount => (_zoneAlive != null) ? _zoneAlive.Length : 0;

        /// <summary> 첫 무리를 세운 시각(Time.time), 아직이면 음수. 제한 시간은 여기서부터 센다 </summary>
        public float FirstSpawnTime => _firstSpawnTime;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if ((_player == null) || (_director == null))
            {
                Debug.LogError("[DevSpawner] 플레이어나 타격감 재생기가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _playerCollider = _player.GetComponent<Collider>();
            _player.Hit  += OnPlayerHit;
            _player.Slam += OnPlayerSlam;
        }

#if UNITY_EDITOR
        // 씬 뷰에서 구역마다 출현 범위(노랑), 진입 판정 범위(하늘색), 몬스터 활동 반경(빨강)을 보여 준다. 위치를 옮길 때 눈으로 맞추기 위한 것이다
        private void OnDrawGizmos()
        {
            if ((_zoneAnchors == null) || (_zoneAnchors.Length == 0))
            {
                return;
            }

            for (int zone = 0; zone < _zoneAnchors.Length; ++zone)
            {
                Transform anchor = _zoneAnchors[zone];
                if (anchor == null)
                {
                    continue;
                }

                Vector3 center = anchor.position + (Vector3.up * GIZMO_LIFT);

                Gizmos.color = Color.yellow;
                DrawGizmoCircle(center, _spawnRadius);

                Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
                DrawGizmoCircle(center, _maxSpawnRadius);

                Gizmos.color = Color.cyan;
                DrawGizmoCircle(center, _enterRadius);

                if (_activityRadius > 0f)
                {
                    Gizmos.color = Color.red;
                    DrawGizmoCircle(center, _activityRadius);
                }

                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(center, center + (Vector3.up * GIZMO_POLE_HEIGHT));

                UnityEditor.Handles.Label(center + (Vector3.up * (GIZMO_POLE_HEIGHT + GIZMO_LIFT)), $"{zone + 1}구역 ({_groupSize}마리)");
            }
        }

        private static void DrawGizmoCircle(Vector3 center, float radius)
        {
            Vector3 previous = center + (Vector3.right * radius);
            for (int i = 1; i <= GIZMO_SEGMENTS; ++i)
            {
                float   angle = (Mathf.PI * 2f * i) / GIZMO_SEGMENTS;
                Vector3 next  = center + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
#endif

        private void OnDisable()
        {
            if (_player != null)
            {
                _player.Hit  -= OnPlayerHit;
                _player.Slam -= OnPlayerSlam;
            }
        }

        private void Start()
        {
            // 플레이어의 카메라 도우미는 플레이어 Awake에서 만들어지므로 모든 Awake가 끝난 Start에서 잇는다
            _director.Init(_player.FeedbackCamera);

            int zoneCount  = ((_zoneAnchors != null) && (_zoneAnchors.Length > 0)) ? _zoneAnchors.Length : 1;
            _zoneAlive   = new int[zoneCount];
            _zonePlanned = new int[zoneCount];
            _zoneKilled  = new int[zoneCount];
            _zoneSpawned = new bool[zoneCount];
            _zoneNextTry = new float[zoneCount];

            if ((_dummyPoints != null) && (_dummyPoints.Length > 0))
            {
                SpawnDummies();
            }
        }

        private void Update()
        {
            Vector3 playerPosition = _player.transform.position;
            UpdateRespawns();

            for (int zone = 0; zone < _zoneAlive.Length; ++zone)
            {
                if ((_zoneSpawned[zone]) || (Time.time < _zoneNextTry[zone]))
                {
                    continue;
                }

                if (ShouldSpawn(zone, playerPosition))
                {
                    SpawnGroup(zone);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 서버가 보내 줄 타격 결과를 대신해 플레이어가 맞힌 몬스터를 받는다. 맞힌 것으로 확정되면 프로필대로 타격감을 재생한다
        private void OnPlayerHit(Collider target, Vector3 hitPoint, bool isHeavy)
        {
            if (!target.TryGetComponent(out DevHitFeedbackMonster monster))
            {
                return;
            }

            HitFeedbackProfile profile  = _director.GetProfile(isHeavy);
            Vector3            attacker = _player.transform.position;

            // 실제로는 서버가 치명타를 정해 보내 준다. 서버가 없는 시험 씬에서는 그 대역으로 무작위로 정한다
            bool isCritical = Random.value < _devCritChance;
            if (!monster.TakeHit(attacker, profile, isCritical, out bool isKill))
            {
                return;
            }

            // 불똥은 몸통 높이에서 공격이 향한 쪽으로 튄다
            Vector3 direction = target.bounds.center - attacker;
            direction.y = 0f;
            direction   = direction.normalized;

            hitPoint.y = target.bounds.center.y;
            _director.PlayHit(profile, hitPoint, direction, isKill, isCritical, _player, monster.LagTarget);
        }

        // 강공격이 내려친 자리에서 링과 먼지를 터뜨리고, 반지름 안의 몬스터를 한꺼번에 밀어낸다
        private void OnPlayerSlam(Vector3 center)
        {
            HitFeedbackProfile profile = _director.GetProfile(true);
            _director.PlaySlam(profile, center);

            if (profile.ShockwaveRadius > 0f)
            {
                DevHitFeedbackMonster.ShoveAround(center, profile.ShockwaveRadius, profile.ShockwaveSpeed);
            }
        }

        private void OnMonsterDied(DevHitFeedbackMonster monster)
        {
            monster.Died -= OnMonsterDied;

            if (!_zoneOfMonster.Remove(monster, out int zone))
            {
                return;
            }

            --_zoneAlive[zone];
            ++_zoneKilled[zone];

            if (_infiniteSpawn)
            {
                _respawnTimes.Add(Time.time + _infiniteRespawnDelay);
                _respawnZones.Add(zone);
            }
        }

        // 처치된 만큼 같은 구역에 한 마리씩 다시 세운다. 자리가 없으면 잠시 뒤 다시 시도한다. 끄면 기다리던 것도 버린다
        private void UpdateRespawns()
        {
            if (!_infiniteSpawn)
            {
                _respawnTimes.Clear();
                _respawnZones.Clear();
                return;
            }

            for (int i = _respawnTimes.Count - 1; i >= 0; --i)
            {
                if (Time.time < _respawnTimes[i])
                {
                    continue;
                }

                int     zone   = _respawnZones[i];
                Vector3 anchor = GetAnchor(zone).position;
                if (!TryFindSpot(anchor, _sizeMultiplier, out Vector3 position))
                {
                    _respawnTimes[i] = Time.time + _retryDelay;
                    continue;
                }

                CreateMonster(zone, position, _sizeMultiplier, _hitsToDie);
                _respawnTimes.RemoveAt(i);
                _respawnZones.RemoveAt(i);
            }
        }

        public bool IsZoneSpawned(int zone) { return _zoneSpawned[zone]; }

        /// <summary> 아직 세우기 전에는 계획한 마리 수(무리 크기)를 돌려준다 </summary>
        public int GetZonePlanned(int zone) { return _zoneSpawned[zone] ? _zonePlanned[zone] : (_groupSize + ((zone == _eliteZoneIndex) ? _eliteCount : 0)); }

        public int GetZoneKilled(int zone) { return _zoneKilled[zone]; }

        public bool IsZoneCleared(int zone) { return _zoneSpawned[zone] && (_zoneAlive[zone] == 0); }

        // 구역 사이 결계는 앞 구역을 모두 처치하면 풀리므로, 그 시점이 곧 다음 구역을 세울 때다
        private bool ShouldSpawn(int zone, Vector3 playerPosition)
        {
            if ((zone == 0) && (_spawnFirstZoneAtStart))
            {
                return true;
            }

            // 무한 소환 중에는 구역이 끝나지 않으므로 앞 구역 처치를 기다리지 않고 입구에 들어설 때 세운다
            if ((_spawnOnPreviousClear) && (!_infiniteSpawn) && (zone > 0))
            {
                return IsZoneCleared(zone - 1);
            }

            return HasEntered(zone, playerPosition);
        }

        // 구역 입구 지점 반경 안, 같은 높이 층에 들어서야 진입으로 본다
        private bool HasEntered(int zone, Vector3 playerPosition)
        {
            Vector3 toPlayer = playerPosition - GetAnchor(zone).position;
            if (Mathf.Abs(toPlayer.y) > _enterHeight)
            {
                return false;
            }

            toPlayer.y = 0f;
            return toPlayer.sqrMagnitude <= (_enterRadius * _enterRadius);
        }

        private Transform GetAnchor(int zone)
        {
            return ((_zoneAnchors != null) && (_zoneAnchors.Length > 0)) ? _zoneAnchors[zone] : transform;
        }

        private void SpawnGroup(int zone)
        {
            Vector3 anchor = GetAnchor(zone).position;

            // 몸집이 큰 엘리트가 자리를 먼저 잡도록 일반 무리보다 앞서 세운다
            int eliteCount = (zone == _eliteZoneIndex) ? _eliteCount : 0;
            for (int i = 0; i < eliteCount; ++i)
            {
                float size = _sizeMultiplier * _eliteSizeScale;
                if (!TryFindSpot(anchor, size, out Vector3 position))
                {
                    Debug.LogWarning($"[DevSpawner] {zone + 1}구역에서 엘리트 자리를 못 찾아 {i}마리만 세웠다", this);
                    break;
                }

                CreateMonster(zone, position, size, _eliteHitsToDie);
            }

            for (int i = 0; i < _groupSize; ++i)
            {
                if (!TryFindSpot(anchor, _sizeMultiplier, out Vector3 position))
                {
                    Debug.LogWarning($"[DevSpawner] {zone + 1}구역에서 자리를 못 찾아 {i}마리만 세웠다", this);
                    break;
                }

                CreateMonster(zone, position, _sizeMultiplier, _hitsToDie);
            }

            // 한 마리도 못 세웠으면 다음 프레임에 계속 재시도하지 않게 대기 시간을 둔다
            if (_zoneAlive[zone] == 0)
            {
                _zoneNextTry[zone] = Time.time + _retryDelay;
                return;
            }

            _zoneSpawned[zone] = true;
            _zonePlanned[zone] = _zoneAlive[zone];

            if (_firstSpawnTime < 0f)
            {
                _firstSpawnTime = Time.time;
            }
        }

        // 구역 무리는 모두 세운 것으로 표시해 더 나오지 않게 하고, 지점마다 허수아비를 세운다. 지점의 높이가 곧 발 높이다
        private void SpawnDummies()
        {
            for (int zone = 0; zone < _zoneSpawned.Length; ++zone)
            {
                _zoneSpawned[zone] = true;
            }

            for (int i = 0; i < _dummyPoints.Length; ++i)
            {
                if (_dummyPoints[i] != null)
                {
                    CreateMonster(0, _dummyPoints[i].position, _sizeMultiplier, 1).SetTrainingDummy();
                }
            }
        }

        private DevHitFeedbackMonster CreateMonster(int zone, Vector3 position, float size, int hitsToDie)
        {
            bool       hasModel = (_models != null) && (_models.Length > 0);
            GameObject body     = hasModel ? new GameObject(MONSTER_NAME) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = MONSTER_NAME;
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(MONSTER_RADIUS * 2f, MONSTER_HEIGHT * HALF, MONSTER_RADIUS * 2f) * size;

            // 캡슐 기본 높이가 2m라 스케일 반이 곧 높이 절반이다. 발이 땅에 닿게 높이 절반만큼 올린다
            body.transform.position = position + (Vector3.up * (MONSTER_HEIGHT * HALF * size));

            Animator animator = hasModel ? AttachModel(body.transform, size) : null;

            var monster = body.AddComponent<DevHitFeedbackMonster>();
            monster.Init(hitsToDie, _playerCollider, _chaseSpeed, animator, _attackWindup, _attackCooldown);
            monster.SetBloodFeedback(_director.Blood);
            monster.SetLeash(GetAnchor(zone).position, _activityRadius);
            monster.Died += OnMonsterDied;
            _zoneOfMonster.Add(monster, zone);
            ++_zoneAlive[zone];
            return monster;
        }

        // 보이지 않는 캡슐 충돌체 아래에 모델을 발이 땅에 닿게 붙이고, 모델의 애니메이터를 돌려준다
        private Animator AttachModel(Transform root, float size)
        {
            ModelEntry entry = _models[Random.Range(0, _models.Length)];

            var capsule = root.gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = CAPSULE_DEFAULT_RADIUS;
            capsule.height = CAPSULE_DEFAULT_HEIGHT;

            GameObject model = Instantiate(entry.prefab, root);
            float      scale = (entry.scale > 0f) ? entry.scale : 1f;

            // 루트 크기를 상쇄해 모델 크기가 월드 기준으로 나오게 하고, 몸 중심에서 발 높이만큼 내린다
            Vector3 rootScale = root.localScale;
            model.transform.localPosition = Vector3.down * (CAPSULE_DEFAULT_HEIGHT * HALF);
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale    = new Vector3(scale * size / rootScale.x, scale * size / rootScale.y, scale * size / rootScale.z);

            Animator animator = model.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = entry.controller;
                animator.applyRootMotion           = false;
            }

            return animator;
        }

        private bool TryFindSpot(Vector3 anchor, float size, out Vector3 position)
        {
            position = Vector3.zero;

            // 기본 반경에서 자리를 못 찾으면 반경을 단계적으로 넓혀 최대 반경까지 찾는다. 무리 수를 다 채우는 것이 우선이다
            for (float radius = _spawnRadius; radius <= (_maxSpawnRadius + RADIUS_EPSILON); radius += _radiusStep)
            {
                for (int attempt = 0; attempt < PLACE_ATTEMPTS_PER_MONSTER; ++attempt)
                {
                    Vector2 offset = Random.insideUnitCircle * radius;
                    Vector3 probe  = anchor + new Vector3(offset.x, GROUND_PROBE_HEIGHT, offset.y);

                    if ((!TryFindGround(probe, anchor.y, out Vector3 ground)) || (!IsSpotFree(ground, size)) || (IsNearPlayer(ground)))
                    {
                        continue;
                    }

                    position = ground;
                    return true;
                }

                if (_radiusStep <= 0f)
                {
                    break;
                }
            }

            return false;
        }

        // 위에서 쏜 광선이 지붕, 나무, 위층 바닥에 먼저 맞으면 몬스터가 한 층 위에 서므로, 맞은 면 중 입구 지점 높이에
        // 가장 가까운 것을 쓰고 높이 차가 허용치를 넘으면 그 자리는 버린다
        private bool TryFindGround(Vector3 from, float referenceY, out Vector3 ground)
        {
            ground = Vector3.zero;

            int count = Physics.RaycastNonAlloc(new Ray(from, Vector3.down), _rayHits, GROUND_PROBE_DEPTH,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float closest = float.MaxValue;
            bool  found   = false;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit       = _rayHits[i];
                float      heightGap = Mathf.Abs(hit.point.y - referenceY);
                if ((heightGap >= closest) || (heightGap > _groundHeightTolerance) || (hit.collider.GetComponentInParent<DevHitFeedbackMonster>() != null))
                {
                    continue;
                }

                closest = heightGap;
                ground  = hit.point;
                found   = true;
            }

            return found;
        }

        private bool IsNearPlayer(Vector3 ground)
        {
            Vector3 toPlayer = _player.transform.position - ground;
            toPlayer.y = 0f;
            return toPlayer.sqrMagnitude < (_minPlayerDistance * _minPlayerDistance);
        }

        // 몸통 높이에서 겹치는 충돌체가 하나라도 있으면(나무, 바위, 다른 몬스터, 플레이어) 쓰지 않는다
        private bool IsSpotFree(Vector3 ground, float size)
        {
            Vector3 center = ground + (Vector3.up * (MONSTER_HEIGHT * HALF * size));
            int     count  = Physics.OverlapSphereNonAlloc(center, (MONSTER_RADIUS * size) + OVERLAP_PADDING, _overlapHits,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return count == 0;
        }
    }
}
