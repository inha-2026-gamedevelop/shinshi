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
        private const int   RAYCAST_MAX_HITS           = 8;
        private const int   OVERLAP_BUFFER_SIZE        = 4;
        private const float GROUND_PROBE_HEIGHT        = 30f;  // m, 구역 중심 위 이 높이에서 아래로 땅을 찾는다
        private const float GROUND_PROBE_DEPTH         = 60f;  // m
        private const float MONSTER_HEIGHT             = 1.6f; // m
        private const float MONSTER_RADIUS             = 0.4f; // m
        private const float OVERLAP_PADDING            = 0.15f; // m, 몬스터끼리 닿지 않게 벌려 두는 여유
        private const float HALF                       = 0.5f;
        private const float CAPSULE_DEFAULT_RADIUS     = 0.5f; // 캡슐 기본 지름 1m, 높이 2m. 루트 스케일이 실제 크기를 정한다
        private const float CAPSULE_DEFAULT_HEIGHT     = 2f;

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
        [SerializeField] private int   _hitsToDie        = 3;
        [SerializeField] private float _chaseSpeed       = 2.5f; // m/s, 플레이어를 쫓는 속도
        [SerializeField] private float _spawnRadius      = 6f;  // m, 문 앞 출현 지점 주위
        [FormerlySerializedAs("_respawnDelay")]
        [SerializeField] private float _retryDelay       = 3f;  // 초, 자리가 없어 한 마리도 못 세웠을 때 다시 시도하기까지

        [Header("구역 (구역마다 문 앞 출현 지점 하나. 비우면 이 오브젝트 자리 한 곳)")]
        [SerializeField] private Transform[] _zoneAnchors;
        [SerializeField, Min(0f)] private float _activateRadius    = 25f; // m, 플레이어가 출현 지점에 이만큼 다가오면 그 구역 무리를 세운다
        [SerializeField, Min(0f)] private float _minPlayerDistance = 4f;  // m, 플레이어와 이 거리 안에는 세우지 않는다

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

        /****************************************
        *               Properties
        ****************************************/

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
        }

        private void Update()
        {
            Vector3 playerPosition = _player.transform.position;

            for (int zone = 0; zone < _zoneAlive.Length; ++zone)
            {
                if ((_zoneSpawned[zone]) || (Time.time < _zoneNextTry[zone]))
                {
                    continue;
                }

                Vector3 toPlayer = playerPosition - GetAnchor(zone).position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude <= (_activateRadius * _activateRadius))
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
        }

        public bool IsZoneSpawned(int zone) { return _zoneSpawned[zone]; }

        /// <summary> 아직 세우기 전에는 계획한 마리 수(무리 크기)를 돌려준다 </summary>
        public int GetZonePlanned(int zone) { return _zoneSpawned[zone] ? _zonePlanned[zone] : _groupSize; }

        public int GetZoneKilled(int zone) { return _zoneKilled[zone]; }

        public bool IsZoneCleared(int zone) { return _zoneSpawned[zone] && (_zoneAlive[zone] == 0); }

        private Transform GetAnchor(int zone)
        {
            return ((_zoneAnchors != null) && (_zoneAnchors.Length > 0)) ? _zoneAnchors[zone] : transform;
        }

        private void SpawnGroup(int zone)
        {
            Vector3 anchor = GetAnchor(zone).position;

            for (int i = 0; i < _groupSize; ++i)
            {
                if (!TryFindSpot(anchor, out Vector3 position))
                {
                    Debug.LogWarning($"[DevSpawner] {zone + 1}구역에서 자리를 못 찾아 {i}마리만 세웠다", this);
                    break;
                }

                CreateMonster(zone, position);
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

        private void CreateMonster(int zone, Vector3 position)
        {
            bool       hasModel = (_models != null) && (_models.Length > 0);
            GameObject body     = hasModel ? new GameObject(MONSTER_NAME) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = MONSTER_NAME;
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(MONSTER_RADIUS * 2f, MONSTER_HEIGHT * HALF, MONSTER_RADIUS * 2f) * _sizeMultiplier;

            // 캡슐 기본 높이가 2m라 스케일 반이 곧 높이 절반이다. 발이 땅에 닿게 높이 절반만큼 올린다
            body.transform.position = position + (Vector3.up * (MONSTER_HEIGHT * HALF * _sizeMultiplier));

            Animator animator = hasModel ? AttachModel(body.transform) : null;

            var monster = body.AddComponent<DevHitFeedbackMonster>();
            monster.Init(_hitsToDie, _playerCollider, _chaseSpeed, animator);
            monster.Died += OnMonsterDied;
            _zoneOfMonster.Add(monster, zone);
            ++_zoneAlive[zone];
        }

        // 보이지 않는 캡슐 충돌체 아래에 모델을 발이 땅에 닿게 붙이고, 모델의 애니메이터를 돌려준다
        private Animator AttachModel(Transform root)
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
            model.transform.localScale    = new Vector3(scale * _sizeMultiplier / rootScale.x, scale * _sizeMultiplier / rootScale.y, scale * _sizeMultiplier / rootScale.z);

            Animator animator = model.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = entry.controller;
                animator.applyRootMotion           = false;
            }

            return animator;
        }

        private bool TryFindSpot(Vector3 anchor, out Vector3 position)
        {
            position = Vector3.zero;

            for (int attempt = 0; attempt < PLACE_ATTEMPTS_PER_MONSTER; ++attempt)
            {
                Vector2 offset = Random.insideUnitCircle * _spawnRadius;
                Vector3 probe  = anchor + new Vector3(offset.x, GROUND_PROBE_HEIGHT, offset.y);

                if ((!TryFindGround(probe, out Vector3 ground)) || (!IsSpotFree(ground)) || (IsNearPlayer(ground)))
                {
                    continue;
                }

                position = ground;
                return true;
            }

            return false;
        }

        // 나무, 지붕 같은 위쪽 충돌체가 아니라 가장 낮은 땅 면을 쓰기 위해 모은 충돌 중 몬스터가 아닌 가장 가까운 것을 쓴다
        private bool TryFindGround(Vector3 from, out Vector3 ground)
        {
            ground = Vector3.zero;

            int count = Physics.RaycastNonAlloc(new Ray(from, Vector3.down), _rayHits, GROUND_PROBE_DEPTH,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            bool  found   = false;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = _rayHits[i];
                if ((hit.distance >= nearest) || (hit.collider.GetComponentInParent<DevHitFeedbackMonster>() != null))
                {
                    continue;
                }

                nearest = hit.distance;
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
        private bool IsSpotFree(Vector3 ground)
        {
            Vector3 center = ground + (Vector3.up * (MONSTER_HEIGHT * HALF * _sizeMultiplier));
            int     count  = Physics.OverlapSphereNonAlloc(center, (MONSTER_RADIUS * _sizeMultiplier) + OVERLAP_PADDING, _overlapHits,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return count == 0;
        }
    }
}
