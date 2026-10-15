// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 정해진 구역 안에 몬스터 한 무리를 세우고, 모두 죽으면 잠시 뒤 다음 무리를 세우는 시험용 스포너 </summary>
    public sealed class DevHitFeedbackSpawner : MonoBehaviour
    {
        // 플레이어의 Hit 이벤트를 받아 맞은 충돌체가 몬스터면 한 대 때린다. 위치는 구역 중심 원 안에서 무작위로 고르고,
        // 땅 위이면서 다른 충돌체(나무, 바위, 다른 몬스터, 플레이어)와 겹치지 않는 자리만 쓴다

        /****************************************
        *                Fields
        ****************************************/

        private const string MONSTER_NAME = "DevMonster";

        private const int   PLACE_ATTEMPTS_PER_MONSTER = 30;
        private const int   RAYCAST_MAX_HITS           = 8;
        private const int   OVERLAP_BUFFER_SIZE        = 4;
        private const float GROUND_PROBE_HEIGHT        = 30f;  // m, 구역 중심 위 이 높이에서 아래로 땅을 찾는다
        private const float GROUND_PROBE_DEPTH         = 60f;  // m
        private const float MONSTER_HEIGHT             = 1.6f; // m
        private const float MONSTER_RADIUS             = 0.4f; // m
        private const float OVERLAP_PADDING            = 0.15f; // m, 몬스터끼리 닿지 않게 벌려 두는 여유
        private const float HALF                       = 0.5f;

        [Header("참조")]
        [SerializeField] private DevHitFeedbackPlayer _player;
        [SerializeField] private HitFeedbackDirector  _director;

        [Header("무리")]
        [SerializeField] private int   _groupSize        = 24;
        [SerializeField] private int   _hitsToDie        = 3;
        [SerializeField] private float _chaseSpeed       = 2.5f; // m/s, 플레이어를 쫓는 속도
        [SerializeField] private float _spawnRadius      = 9f;  // m
        [SerializeField] private float _respawnDelay     = 3f;  // 초, 무리가 모두 죽은 뒤 다음 무리까지

        private readonly RaycastHit[] _rayHits     = new RaycastHit[RAYCAST_MAX_HITS];
        private readonly Collider[]   _overlapHits = new Collider[OVERLAP_BUFFER_SIZE];

        private Collider _playerCollider = null;
        private int      _alive          = 0;
        private float    _nextSpawnTime  = 0f;

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

            SpawnGroup();
        }

        private void Update()
        {
            if ((_alive == 0) && (Time.time >= _nextSpawnTime))
            {
                SpawnGroup();
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
            if (!monster.TakeHit(attacker, profile, out bool isKill))
            {
                return;
            }

            // 불똥은 몸통 높이에서 공격이 향한 쪽으로 튄다
            Vector3 direction = target.bounds.center - attacker;
            direction.y = 0f;
            direction   = direction.normalized;

            hitPoint.y = target.bounds.center.y;
            _director.PlayHit(profile, hitPoint, direction, isKill, _player, monster.LagTarget);
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
            --_alive;

            if (_alive == 0)
            {
                _nextSpawnTime = Time.time + _respawnDelay;
            }
        }

        private void SpawnGroup()
        {
            for (int i = 0; i < _groupSize; ++i)
            {
                if (!TryFindSpot(out Vector3 position))
                {
                    Debug.LogWarning($"[DevSpawner] 자리를 못 찾아 {i}마리만 세웠다", this);
                    break;
                }

                CreateMonster(position);
            }

            // 한 마리도 못 세웠으면 다음 프레임에 계속 재시도하지 않게 대기 시간을 둔다
            if (_alive == 0)
            {
                _nextSpawnTime = Time.time + _respawnDelay;
            }
        }

        private void CreateMonster(Vector3 position)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = MONSTER_NAME;
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(MONSTER_RADIUS * 2f, MONSTER_HEIGHT * HALF, MONSTER_RADIUS * 2f);

            // 캡슐 기본 높이가 2m라 스케일 반이 곧 높이 절반이다. 발이 땅에 닿게 높이 절반만큼 올린다
            body.transform.position = position + (Vector3.up * (MONSTER_HEIGHT * HALF));

            var monster = body.AddComponent<DevHitFeedbackMonster>();
            monster.Init(_hitsToDie, _playerCollider, _chaseSpeed);
            monster.Died += OnMonsterDied;
            ++_alive;
        }

        private bool TryFindSpot(out Vector3 position)
        {
            position = Vector3.zero;

            for (int attempt = 0; attempt < PLACE_ATTEMPTS_PER_MONSTER; ++attempt)
            {
                Vector2 offset = Random.insideUnitCircle * _spawnRadius;
                Vector3 probe  = transform.position + new Vector3(offset.x, GROUND_PROBE_HEIGHT, offset.y);

                if ((!TryFindGround(probe, out Vector3 ground)) || (!IsSpotFree(ground)))
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

        // 몸통 높이에서 겹치는 충돌체가 하나라도 있으면(나무, 바위, 다른 몬스터, 플레이어) 쓰지 않는다
        private bool IsSpotFree(Vector3 ground)
        {
            Vector3 center = ground + (Vector3.up * (MONSTER_HEIGHT * HALF));
            int     count  = Physics.OverlapSphereNonAlloc(center, MONSTER_RADIUS + OVERLAP_PADDING, _overlapHits,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return count == 0;
        }
    }
}
