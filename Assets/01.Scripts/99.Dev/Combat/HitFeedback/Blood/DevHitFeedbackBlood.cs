// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.Rendering;

// 서드파티
using DG.Tweening;

namespace Shinshi.Dev
{
    /// <summary> 피격 분사, 사체의 낙하 방울, 지형에 붙는 지속 혈흔을 관리하는 개발 씬용 풀 </summary>
    public sealed class DevHitFeedbackBlood : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SPRAY_RESOURCE        = "DevHitBlood/M_BloodSpray";
        private const string STAIN_RESOURCE        = "DevHitBlood/M_BloodGround";
        private const int    SPRAY_SLOTS           = 12;
        private const int    DROP_CAPACITY         = 256;
        private const int    TRAIL_CAPACITY        = 32;
        private const int    STAIN_CAPACITY        = 160;
        private const int    GROUND_HIT_BUFFER     = 24;
        private const int    ATLAS_TILES           = 4;
        private const int    GRID_SIZE             = 3;
        private const int    QUAD_INDICES          = 6;
        private const int    KILL_DROP_MULTIPLIER  = 2;
        private const float  HALF                  = 0.5f;
        private const float  PROBE_UP              = 0.8f;
        private const float  PROBE_DISTANCE        = 12f;
        private const float  VERTEX_PROBE_DISTANCE = PROBE_UP * 2f; // 꼭짓점 위아래로 같은 폭만 찾는다
        private const float  MIN_GROUND_UP         = 0.55f;
        private const float  SURFACE_OFFSET        = 0.02f;
        private const float  MAX_SURFACE_STEP      = 0.65f;
        private const float  MIN_DIRECTION_SQR     = 0.0001f;
        private const float  FULL_TURN             = 360f;
        private const float  ATLAS_INSET           = 0.008f;
        private const float  DROP_UP_SPEED         = 2.5f;
        private const float  DROP_RANDOM_SPEED     = 1.6f;
        private const float  TRAIL_VELOCITY_RATIO  = 0.3f;
        private const float  STILL_SPEED_SQR       = 0.25f;
        private const float  STILL_SECONDS         = 0.35f;
        private const float  MIN_BLEED_SECONDS     = 0.6f;
        private const float  KILL_STAIN_SCALE      = 1.2f;
        private const float  CRIT_STAIN_SCALE      = 1.1f;
        private const float  SPRAY_GRAVITY         = 1.6f;
        private const float  SPRAY_CONE_ANGLE      = 65f;
        private const float  SPRAY_CONE_RADIUS     = 0.12f;
        private const float  SPRAY_STRETCH         = 0.035f;
        private const float  DROP_STRETCH          = 0.012f;
        private const float  STRETCH_LENGTH        = 1.3f;
        private const float  CPU_PARTICLE_LIFETIME = 1f; // 방울은 시뮬레이션 속도 0으로 직접 옮기므로 수명은 표시용이다
        private const float  MIN_FADE_SECONDS      = 0.01f;

        private static readonly int s_colorId = Shader.PropertyToID("_BaseColor");

        [Header("분사 (개수, m/s, m, 초)")]
        [SerializeField, Min(1)] private int _hitSprayCount  = 24;
        [SerializeField, Min(1)] private int _killSprayCount = 42;
        [SerializeField] private Vector2 _spraySpeed    = new Vector2(3f, 8f);
        [SerializeField] private Vector2 _spraySize     = new Vector2(0.55f, 1f);
        [SerializeField] private Vector2 _sprayLifetime = new Vector2(0.16f, 0.4f);
        [SerializeField] private Color _freshColor = new Color(0.7f, 0.008f, 0.018f, 0.96f);

        [Header("떨어지는 방울")]
        [SerializeField, Min(1)] private int _hitDropCount = 7;
        [SerializeField] private Vector2 _dropSize = new Vector2(0.25f, 0.42f);
        [SerializeField, Min(0.1f)] private float _dropSeconds = 2.5f;
        [SerializeField, Min(0.01f)] private float _dripInterval = 0.12f;
        [SerializeField, Min(0.1f)] private float _bleedSeconds = 2.6f;

        [Header("바닥 혈흔 (m, 초)")]
        [SerializeField] private Vector2 _impactStainSize = new Vector2(1.2f, 2.2f);
        [SerializeField] private Vector2 _dripStainSize   = new Vector2(0.4f, 0.8f);
        [SerializeField, Min(0.1f)] private float _stainSeconds = 45f;
        [SerializeField, Min(0.1f)] private float _fadeSeconds  = 5f;
        [SerializeField] private Color _stainColor = new Color(0.52f, 0.007f, 0.015f, 0.95f);
        [SerializeField] private LayerMask _groundLayers = ~0;

        [Header("전용 재질 (비우면 Resources에서 로드)")]
        [SerializeField] private Material _sprayMaterial;
        [SerializeField] private Material _stainMaterial;

        private readonly ParticleSystem[]          _sprays    = new ParticleSystem[SPRAY_SLOTS];
        private readonly Drop[]                    _drops     = new Drop[DROP_CAPACITY];
        private readonly BleedTrail[]              _trails    = new BleedTrail[TRAIL_CAPACITY];
        private readonly Stain[]                   _stains    = new Stain[STAIN_CAPACITY];
        private readonly ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[DROP_CAPACITY];
        private readonly RaycastHit[]              _hits      = new RaycastHit[GROUND_HIT_BUFFER];

        private ParticleSystem _dropParticles = null;
        private Coroutine      _tickRoutine   = null;
        private int            _dropCount     = 0;
        private int            _trailCount    = 0;
        private int            _spraySlot     = 0;
        private int            _stainSlot     = 0;
        private int            _dropSlot      = 0;
        private int            _trailId       = 0;
        private bool           _isReady       = false;

        private struct Drop
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float   Size;
            public float   Remaining;
            public uint    Seed;
        }

        private struct BleedTrail
        {
            public Transform Anchor;
            public Vector3   Previous;
            public float     Remaining;
            public float     NextDrop;
            public float     Still;
            public int       BeginFrame;
            public int       Id;
        }

        /****************************************
        *               Properties
        ****************************************/

        public int ActiveDrops => _dropCount;
        public int ActiveTrails => _trailCount;
        public int VisibleStains
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _stains.Length; ++i)
                {
                    if ((_stains[i] != null) && (_stains[i].Visible))
                    {
                        ++count;
                    }
                }
                return count;
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_sprayMaterial == null)
            {
                _sprayMaterial = Resources.Load<Material>(SPRAY_RESOURCE);
            }
            if (_stainMaterial == null)
            {
                _stainMaterial = Resources.Load<Material>(STAIN_RESOURCE);
            }
            if ((_sprayMaterial == null) || (_stainMaterial == null))
            {
                Debug.LogError("[DevBlood] 분사와 바닥 혈흔 재질이 필요하다", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < SPRAY_SLOTS; ++i)
            {
                _sprays[i] = CreateParticles("BloodSpray" + i, false);
            }
            _dropParticles = CreateParticles("BloodDrops", true);
            for (int i = 0; i < STAIN_CAPACITY; ++i)
            {
                _stains[i] = new Stain(transform, _stainMaterial, _stainColor, i, _stainSeconds, _fadeSeconds);
            }
            _isReady = true;
        }

        private void OnDisable() { StopAll(); }

        private void OnDestroy()
        {
            for (int i = 0; i < _stains.Length; ++i)
            {
                if (_stains[i] != null)
                {
                    _stains[i].Dispose();
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 확정된 타격의 접점에서 피를 터뜨린다. direction은 공격 진행 방향이다 </summary>
        public void Burst(Vector3 point, Vector3 direction, bool isKill, bool isCritical)
        {
            if ((!_isReady) || (!isActiveAndEnabled))
            {
                return;
            }

            Vector3 outward = -direction.normalized;
            Vector3 aim = outward + Vector3.up;
            if (aim.sqrMagnitude < MIN_DIRECTION_SQR)
            {
                aim = Vector3.up;
            }
            Vector3 up = Mathf.Abs(Vector3.Dot(aim.normalized, Vector3.up)) > (1f - MIN_DIRECTION_SQR)
                ? Vector3.forward : Vector3.up;
            ParticleSystem spray = _sprays[_spraySlot];
            _spraySlot = (_spraySlot + 1) % SPRAY_SLOTS;
            spray.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            spray.transform.SetPositionAndRotation(point, Quaternion.LookRotation(aim, up));
            spray.Play(false);
            spray.Emit(isKill ? _killSprayCount : _hitSprayCount);

            int count = isKill ? _hitDropCount * KILL_DROP_MULTIPLIER : _hitDropCount;
            for (int i = 0; i < count; ++i)
            {
                Vector3 velocity = (outward * Random.Range(_spraySpeed.x, _spraySpeed.y))
                    + (Random.insideUnitSphere * DROP_RANDOM_SPEED) + (Vector3.up * DROP_UP_SPEED);
                AddDrop(point, velocity);
            }
            float scale = (isKill ? KILL_STAIN_SCALE : 1f) * (isCritical ? CRIT_STAIN_SCALE : 1f);
            PlaceStain(point, Random.Range(_impactStainSize.x, _impactStainSize.y) * scale);
            EnsureTick();
        }

        /// <summary> 복제된 사체의 실제 골반 뼈를 따라 피를 떨어뜨린다. 정지 프레임이 끝난 뒤 시작한다 </summary>
        public int FollowCorpse(Transform anchor, int freezeFrames)
        {
            if ((!_isReady) || (!isActiveAndEnabled) || (anchor == null))
            {
                return 0;
            }
            int slot = 0;
            if (_trailCount < TRAIL_CAPACITY)
            {
                slot = _trailCount;
                ++_trailCount;
            }
            int id = ++_trailId;
            _trails[slot] = new BleedTrail
            {
                Anchor = anchor,
                Previous = anchor.position,
                Remaining = _bleedSeconds,
                BeginFrame = Time.frameCount + Mathf.Max(0, freezeFrames),
                Id = id
            };
            EnsureTick();
            return id;
        }

        public void StopFollowing(int id)
        {
            if (id == 0)
            {
                return;
            }
            for (int i = 0; i < _trailCount; ++i)
            {
                if (_trails[i].Id == id)
                {
                    RemoveTrail(i);
                    return;
                }
            }
        }

        /// <summary> 씬 종료나 표현 취소 때 방울, 사체 추적과 바닥 혈흔을 모두 지운다 </summary>
        public void StopAll()
        {
            if (!_isReady)
            {
                return;
            }
            if (_tickRoutine != null)
            {
                StopCoroutine(_tickRoutine);
                _tickRoutine = null;
            }
            _dropCount = 0;
            _trailCount = 0;
            System.Array.Clear(_trails, 0, _trails.Length);
            _dropParticles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            for (int i = 0; i < _sprays.Length; ++i)
            {
                _sprays[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            for (int i = 0; i < _stains.Length; ++i)
            {
                _stains[i].Hide();
            }
        }

        private void AddDrop(Vector3 point, Vector3 velocity)
        {
            int slot = _dropSlot;
            if (_dropCount < DROP_CAPACITY)
            {
                slot = _dropCount;
                ++_dropCount;
            }
            _dropSlot = (_dropSlot + 1) % DROP_CAPACITY;
            _drops[slot] = new Drop
            {
                Position = point,
                Velocity = velocity,
                Remaining = _dropSeconds,
                Size = Random.Range(_dropSize.x, _dropSize.y),
                Seed = (uint)Random.Range(1, int.MaxValue)
            };
        }

        private void EnsureTick()
        {
            if (_tickRoutine == null)
            {
                _dropParticles.Play(false);
                _tickRoutine = StartCoroutine(CoTick());
            }
        }

        private IEnumerator CoTick()
        {
            while ((_dropCount > 0) || (_trailCount > 0))
            {
                float dt = Time.deltaTime;
                TickTrails(dt);
                TickDrops(dt);
                yield return null;
            }
            _dropParticles.Clear(false);
            _tickRoutine = null;
        }

        private void TickTrails(float dt)
        {
            for (int i = _trailCount - 1; i >= 0; --i)
            {
                ref BleedTrail trail = ref _trails[i];
                if ((trail.Anchor == null) || (!trail.Anchor.gameObject.activeInHierarchy))
                {
                    RemoveTrail(i);
                    continue;
                }
                Vector3 point = trail.Anchor.position;
                if (Time.frameCount < trail.BeginFrame)
                {
                    trail.Previous = point;
                    continue;
                }
                Vector3 velocity = dt > MIN_DIRECTION_SQR ? (point - trail.Previous) / dt : Vector3.zero;
                trail.Previous = point;
                trail.Remaining -= dt;
                trail.Still = velocity.sqrMagnitude < STILL_SPEED_SQR ? trail.Still + dt : 0f;
                bool settled = ((_bleedSeconds - trail.Remaining) >= MIN_BLEED_SECONDS) && (trail.Still >= STILL_SECONDS);
                if ((trail.Remaining <= 0f) || (settled))
                {
                    PlaceStain(point, _impactStainSize.x);
                    RemoveTrail(i);
                    continue;
                }
                trail.NextDrop -= dt;
                if (trail.NextDrop <= 0f)
                {
                    trail.NextDrop = _dripInterval;
                    AddDrop(point, (velocity * TRAIL_VELOCITY_RATIO) + (Vector3.down * HALF));
                }
            }
        }

        private void RemoveTrail(int index)
        {
            --_trailCount;
            _trails[index] = _trails[_trailCount];
            _trails[_trailCount] = default;
        }

        private void TickDrops(float dt)
        {
            for (int i = _dropCount - 1; i >= 0; --i)
            {
                ref Drop drop = ref _drops[i];
                drop.Remaining -= dt;
                drop.Velocity += Physics.gravity * dt;
                Vector3 next = drop.Position + (drop.Velocity * dt);
                Vector3 step = next - drop.Position;
                bool landed = TryGround(drop.Position, step, step.magnitude, out RaycastHit ground);
                if (landed)
                {
                    PlaceStain(ground.point, Random.Range(_dripStainSize.x, _dripStainSize.y));
                }
                if ((drop.Remaining <= 0f) || (landed))
                {
                    --_dropCount;
                    _drops[i] = _drops[_dropCount];
                    continue;
                }
                drop.Position = next;
            }
            for (int i = 0; i < _dropCount; ++i)
            {
                _particles[i] = new ParticleSystem.Particle
                {
                    position = _drops[i].Position,
                    velocity = _drops[i].Velocity,
                    startSize = _drops[i].Size,
                    startColor = _freshColor,
                    startLifetime = CPU_PARTICLE_LIFETIME,
                    remainingLifetime = CPU_PARTICLE_LIFETIME,
                    randomSeed = _drops[i].Seed
                };
            }
            _dropParticles.SetParticles(_particles, _dropCount);
        }

        private bool TryGround(Vector3 origin, Vector3 direction, float distance, out RaycastHit ground)
        {
            ground = default;
            if (distance <= MIN_DIRECTION_SQR)
            {
                return false;
            }
            int count = Physics.RaycastNonAlloc(origin, direction.normalized, _hits, distance, _groundLayers,
                QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = _hits[i];
                // 살아 있는 캡슐과 래그돌은 바닥으로 취급하지 않는다. 프레임 루프에서 컴포넌트 탐색도 하지 않는다
                if ((hit.distance >= nearest) || (hit.normal.y < MIN_GROUND_UP) || (hit.collider.attachedRigidbody != null)
                    || (hit.collider is CapsuleCollider) || (hit.collider is CharacterController) || (hit.collider is SphereCollider))
                {
                    continue;
                }
                nearest = hit.distance;
                ground = hit;
                found = true;
            }
            return found;
        }

        private void PlaceStain(Vector3 point, float size)
        {
            if (!TryGround(point + (Vector3.up * PROBE_UP), Vector3.down, PROBE_DISTANCE, out RaycastHit center))
            {
                return;
            }
            Stain stain = _stains[_stainSlot];
            _stainSlot = (_stainSlot + 1) % STAIN_CAPACITY;
            float radians = Random.Range(0f, FULL_TURN) * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            int tile = Random.Range(0, ATLAS_TILES * ATLAS_TILES);
            for (int z = 0; z < GRID_SIZE; ++z)
            {
                for (int x = 0; x < GRID_SIZE; ++x)
                {
                    float u = x / (float)(GRID_SIZE - 1);
                    float v = z / (float)(GRID_SIZE - 1);
                    float dx = (u - HALF) * size;
                    float dz = (v - HALF) * size;
                    Vector3 vertex = center.point + new Vector3((dx * cosine) - (dz * sine), 0f, (dx * sine) + (dz * cosine));
                    // 중앙뿐 아니라 9개 꼭짓점을 같은 지형에 맞춰 경사면에서 카드가 뜨거나 파묻히는 것을 줄인다
                    if ((!TryGround(vertex + (Vector3.up * PROBE_UP), Vector3.down, VERTEX_PROBE_DISTANCE, out RaycastHit surface))
                        || (Mathf.Abs(surface.point.y - center.point.y) > MAX_SURFACE_STEP))
                    {
                        return;
                    }
                    int index = (z * GRID_SIZE) + x;
                    stain.Vertices[index] = stain.Space.InverseTransformPoint(surface.point + (surface.normal * SURFACE_OFFSET));
                    stain.Normals[index] = stain.Space.InverseTransformDirection(surface.normal);
                    stain.Uvs[index] = new Vector2(((tile % ATLAS_TILES) + Mathf.Lerp(ATLAS_INSET, 1f - ATLAS_INSET, u)) / ATLAS_TILES,
                        ((tile / ATLAS_TILES) + Mathf.Lerp(ATLAS_INSET, 1f - ATLAS_INSET, v)) / ATLAS_TILES);
                }
            }
            stain.Show();
        }

        private ParticleSystem CreateParticles(string name, bool cpuDrops)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = cpuDrops ? DROP_CAPACITY : Mathf.Max(_hitSprayCount, _killSprayCount);
            main.startLifetime = new ParticleSystem.MinMaxCurve(_sprayLifetime.x, _sprayLifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(_spraySpeed.x, _spraySpeed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(_spraySize.x, _spraySize.y);
            main.startColor = _freshColor;
            main.gravityModifier = cpuDrops ? 0f : SPRAY_GRAVITY;
            main.simulationSpeed = cpuDrops ? 0f : 1f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = !cpuDrops;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = SPRAY_CONE_ANGLE;
            shape.radius = SPRAY_CONE_RADIUS;
            var sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = ATLAS_TILES;
            sheet.numTilesY = ATLAS_TILES;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
            var size = system.sizeOverLifetime;
            size.enabled = !cpuDrops;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _sprayMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = cpuDrops ? DROP_STRETCH : SPRAY_STRETCH;
            renderer.lengthScale = STRETCH_LENGTH;
            renderer.cameraVelocityScale = 0f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private sealed class Stain
        {
            private readonly Mesh                  _mesh;
            private readonly MeshRenderer          _renderer;
            private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
            private readonly Color                 _color;
            private readonly Tween                 _fade;
            private float                          _alpha = 1f;

            public Vector3[] Vertices { get; } = new Vector3[GRID_SIZE * GRID_SIZE];
            public Vector3[] Normals { get; } = new Vector3[GRID_SIZE * GRID_SIZE];
            public Vector2[] Uvs { get; } = new Vector2[GRID_SIZE * GRID_SIZE];
            public bool Visible => _renderer.enabled;
            public Transform Space => _renderer.transform;

            public Stain(Transform parent, Material material, Color color, int index, float seconds, float fadeSeconds)
            {
                _color = color;
                var go = new GameObject("BloodStain" + index);
                go.transform.SetParent(parent, false);
                _mesh = new Mesh { name = "BloodSurface" + index };
                _mesh.MarkDynamic();
                _mesh.vertices = Vertices;
                var triangles = new int[(GRID_SIZE - 1) * (GRID_SIZE - 1) * QUAD_INDICES];
                int next = 0;
                for (int z = 0; z < GRID_SIZE - 1; ++z)
                {
                    for (int x = 0; x < GRID_SIZE - 1; ++x)
                    {
                        int a = (z * GRID_SIZE) + x;
                        triangles[next] = a;
                        triangles[next + 1] = a + GRID_SIZE;
                        triangles[next + 2] = a + 1;
                        triangles[next + 3] = a + 1;
                        triangles[next + 4] = a + GRID_SIZE;
                        triangles[next + 5] = a + GRID_SIZE + 1;
                        next += QUAD_INDICES;
                    }
                }
                _mesh.triangles = triangles;
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = go.AddComponent<MeshRenderer>();
                _renderer.sharedMaterial = material;
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = true;
                _renderer.enabled = false;
                float fade = Mathf.Clamp(fadeSeconds, MIN_FADE_SECONDS, Mathf.Max(MIN_FADE_SECONDS, seconds));
                _fade = DOTween.To(() => _alpha, ApplyAlpha, 0f, fade).SetDelay(Mathf.Max(0f, seconds - fade))
                    .OnComplete(HideRenderer).SetAutoKill(false).Pause().SetLink(parent.gameObject);
            }

            public void Show()
            {
                _mesh.vertices = Vertices;
                _mesh.normals = Normals;
                _mesh.uv = Uvs;
                _mesh.RecalculateBounds();
                ApplyAlpha(1f);
                _renderer.enabled = true;
                _fade.Restart();
            }

            public void Hide()
            {
                _fade.Pause();
                _renderer.enabled = false;
            }

            public void Dispose()
            {
                _fade.Kill();
                Object.Destroy(_mesh);
            }

            private void HideRenderer() { _renderer.enabled = false; }

            private void ApplyAlpha(float alpha)
            {
                _alpha = alpha;
                Color tint = _color;
                tint.a *= alpha;
                _block.SetColor(s_colorId, tint);
                _renderer.SetPropertyBlock(_block);
            }
        }
    }
}
