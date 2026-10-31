#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.AI;

namespace Shinshi.AI
{
    /// <summary> Dev_PrologueMap 숲 지형에서 프롤로그 숲 구간을 눈으로 돌려 보게 하는 에디터 전용 시험 세계, 플레이어와 산귀는 도형 대역이다 </summary>
    public sealed class PrologueForestDevWorld : MonoBehaviour
    {
        // 서버 판정이 아니라 사건만 흉내 낸다. 이동은 NavMesh 위로만 하고, 공격은 가까운 도형 대역에 피해를 준다.
        // 위치는 씬의 PrologueMarkers 아래 이름 있는 빈 오브젝트에서 읽고 없으면 소환하지 않는다.
        // 산귀의 피해량과 속도는 체감을 보기 위한 임시 값이며 기획 수치가 아니다

        /****************************************
        *                Types
        ****************************************/

        private sealed class Enemy
        {
            public int                SpawnIndex;
            public EPrologueEnemyKind Kind;
            public Transform          Body;
            public float              Health;
            public bool               Telegraphing;
            public double             NextTelegraphAt;
            public double             TelegraphEndAt;
            public Transform          Zone;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const string MARKER_PLAYER_START   = "ForestPlayerStart";
        private const string MARKER_TALISMAN       = "Talisman";
        private const string MARKER_NORMAL_PREFIX  = "NormalEnemy_";
        private const string MARKER_TELEGRAPH_PREFIX = "TelegraphEnemy_";
        private const string HORIZONTAL_AXIS       = "Horizontal";
        private const string VERTICAL_AXIS         = "Vertical";

        private const float PLAYER_SPEED           = 5f;
        private const float PLAYER_MAX_HEALTH      = 100f;
        private const float PLAYER_HEIGHT_OFFSET   = 1f;
        private const float POTION_HEAL            = 60f;
        private const float ATTACK_RANGE           = 3.5f;
        private const float ATTACK_DAMAGE          = 34f;
        private const float ATTACK_COOLDOWN        = 0.4f;
        private const float SKILL_RANGE            = 6f;
        private const float SKILL_DAMAGE           = 60f;
        private const float SKILL_COOLDOWN         = 2f;
        private const float TALISMAN_ENTER_RADIUS  = 2.5f;
        private const float TALISMAN_LEAVE_RADIUS  = 5f;
        private const float NAV_SAMPLE_DISTANCE    = 2f;

        private const float ENEMY_HEALTH           = 100f;
        private const float ENEMY_SPEED            = 2.5f;
        private const float ENEMY_CONTACT_RANGE    = 1.5f;
        private const float ENEMY_CONTACT_DAMAGE   = 8f;
        private const float ENEMY_CONTACT_INTERVAL = 1f;
        private const float TELEGRAPH_FIRST_DELAY  = 2f;
        private const float TELEGRAPH_INTERVAL     = 5f;
        private const float TELEGRAPH_SECONDS      = 2f;
        private const float TELEGRAPH_RADIUS       = 4f;
        private const float TELEGRAPH_DAMAGE       = 30f;
        private const float ZONE_THICKNESS         = 0.05f;

        private static readonly Color PlayerColor    = Color.white;
        private static readonly Color HurtColor      = Color.red;
        private static readonly Color NormalColor    = new Color(0.45f, 0.35f, 0.25f, 1f);
        private static readonly Color TelegraphColor = new Color(0.55f, 0.2f, 0.6f, 1f);
        private static readonly Color ZoneColor      = new Color(1f, 0.15f, 0.1f, 1f);

        private const float HURT_FLASH_SECONDS     = 0.2f;
        private const float BEACON_LIFT            = 0.2f;
        private const float BEACON_LIFETIME        = 3f;
        private const float BEACON_RISE_SPEED      = 0.8f;
        private const float BEACON_SIZE_MIN        = 0.3f;
        private const float BEACON_SIZE_MAX        = 0.9f;
        private const int   BEACON_MAX_PARTICLES   = 80;
        private const float BEACON_RATE            = 25f;
        private const float BEACON_CONE_ANGLE      = 4f;
        private const float BEACON_FADE_IN         = 0.3f;
        private const float BEACON_LIGHT_HEIGHT    = 1.5f;
        private const float BEACON_LIGHT_RANGE     = 8f;
        private const float BEACON_LIGHT_INTENSITY = 4f;

        private const string GLOW_SHADER_NAME          = "Universal Render Pipeline/Particles/Unlit";
        private const string GLOW_FALLBACK_SHADER_NAME = "Sprites/Default";
        private const int    GLOW_TEXTURE_SIZE         = 32;
        private const float  GLOW_FALLOFF              = 2f;

        private static readonly Color BeaconColor  = new Color(1f, 0.85f, 0.2f, 1f);

        private const string MARKER_FOREST_CENTER = "PrologueForestCenter";
        private const float  FOREST_RADIUS        = 17f;
        private const float  FOREST_MARGIN        = 0.5f;

        private Transform _beacon       = null;
        private Transform _forestCenter = null;

        private readonly List<Enemy> _enemies = new List<Enemy>();

        private Func<bool>  _isInputBlocked = null;
        private Transform   _player         = null;
        private Renderer    _playerRenderer = null;
        private Transform   _talisman       = null;
        private float       _health         = PLAYER_MAX_HEALTH;
        private bool        _active         = false;
        private bool        _downReported   = false;
        private bool        _talismanLatched = false;
        private double      _attackReadyAt  = 0.0;
        private double      _skillReadyAt   = 0.0;
        private double      _invulnerableUntil = 0.0;
        private double      _contactReadyAt = 0.0;
        private double      _hurtUntil      = 0.0;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsDamaged => _health < PLAYER_MAX_HEALTH;

        public bool HasEnemies => _enemies.Count > 0;

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

        /****************************************
        *               Methods
        ****************************************/

        /// <summary> 시험 세계를 만든다. isInputBlocked가 참인 동안은 조작을 받지 않는다(조사 화면, 일러스트) </summary>
        public static PrologueForestDevWorld Create(Func<bool> isInputBlocked)
        {
            var root  = new GameObject("PrologueForestDevWorld");
            var world = root.AddComponent<PrologueForestDevWorld>();
            world._isInputBlocked = isInputBlocked;
            return world;
        }

        /// <summary> 숲 시작 위치에 플레이어 대역을 놓고 카메라를 묶는다 </summary>
        public void Begin()
        {
            Transform start = FindMarker(MARKER_PLAYER_START);
            _talisman       = FindMarker(MARKER_TALISMAN);
            _forestCenter   = FindMarker(MARKER_FOREST_CENTER);

            if (_player == null)
            {
                GameObject body = TestRigDisplay.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "ForestDevPlayer";
                Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(transform, false);
                _player         = body.transform;
                _playerRenderer = body.GetComponent<Renderer>();
            }

            CreateTalismanBeacon();

            Vector3 position = (start != null) ? start.position : Vector3.zero;
            _player.position = position + (Vector3.up * PLAYER_HEIGHT_OFFSET);
            _player.gameObject.SetActive(true);

            _health            = PLAYER_MAX_HEALTH;
            _downReported      = false;
            _talismanLatched   = false;
            _invulnerableUntil = 0.0;
            _active            = true;

            PrologueDevCamera.Follow(_player);
        }

        /// <summary> 월백 조우 등으로 숲 세계를 접는다. 산귀와 플레이어 대역을 치운다 </summary>
        public void Hide()
        {
            _active = false;
            ClearEnemies();

            if (_beacon != null)
            {
                Destroy(_beacon.gameObject);
                _beacon = null;
            }

            if (_player != null)
            {
                _player.gameObject.SetActive(false);
            }
        }

        /// <summary> 서버 소환 지시를 마커 위치의 도형 대역으로 옮긴다 </summary>
        public void SpawnEnemy(EPrologueEnemyKind kind, int spawnIndex)
        {
            string prefix = (kind == EPrologueEnemyKind.Normal) ? MARKER_NORMAL_PREFIX : MARKER_TELEGRAPH_PREFIX;
            Transform marker = FindMarker(prefix + spawnIndex);
            if (marker == null)
            {
                Debug.LogWarning($"[PrologueForestDevWorld] 스폰 마커를 찾지 못했다: {prefix}{spawnIndex}", this);
                return;
            }

            GameObject body = TestRigDisplay.CreatePrimitive(PrimitiveType.Capsule);
            body.name = $"ForestDevEnemy_{kind}_{spawnIndex}";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            body.transform.position = marker.position + (Vector3.up * PLAYER_HEIGHT_OFFSET);
            body.GetComponent<Renderer>().material.color = (kind == EPrologueEnemyKind.Normal) ? NormalColor : TelegraphColor;

            var enemy = new Enemy
            {
                SpawnIndex      = spawnIndex,
                Kind            = kind,
                Body            = body.transform,
                Health          = ENEMY_HEALTH,
                NextTelegraphAt = Time.timeAsDouble + TELEGRAPH_FIRST_DELAY
            };

            _enemies.Add(enemy);
        }

        /// <summary> 체력 0 회복 처리, 체력을 가득 채우고 무적을 준다 </summary>
        public void Revive(float invulnerableSeconds)
        {
            _health            = PLAYER_MAX_HEALTH;
            _downReported      = false;
            _invulnerableUntil = Time.timeAsDouble + invulnerableSeconds;
        }

        /// <summary> 패널 버튼에서 체력을 0으로 만든다 </summary>
        public void ForceDown()
        {
            _health = 0f;
        }

        private void Update()
        {
            if ((!_active) || (_player == null))
            {
                return;
            }

            double now = Time.timeAsDouble;

            bool blocked = (_isInputBlocked != null) && (_isInputBlocked());
            if (!blocked)
            {
                Move();
                HandleActions(now);
            }

            UpdateTalisman();
            UpdateEnemies(now);
            ReportDown();

            _playerRenderer.material.color = (now < _hurtUntil) ? HurtColor : PlayerColor;
        }

        private void OnDestroy()
        {
            ClearEnemies();
        }

        private void Move()
        {
            Vector3 input = new Vector3(Input.GetAxisRaw(HORIZONTAL_AXIS), 0f, Input.GetAxisRaw(VERTICAL_AXIS));
            if (input.sqrMagnitude <= 0f)
            {
                return;
            }

            Vector3 direction = input.normalized;
            Vector3 next      = _player.position + (direction * (PLAYER_SPEED * Time.deltaTime));
            _player.rotation  = Quaternion.LookRotation(direction, Vector3.up);

            next = KeepInForest(next);

            // 걸을 수 없는 곳(절벽, 나무)으로는 가지 않는다
            if (NavMesh.SamplePosition(next - (Vector3.up * PLAYER_HEIGHT_OFFSET), out NavMeshHit hit, NAV_SAMPLE_DISTANCE, NavMesh.AllAreas))
            {
                _player.position = hit.position + (Vector3.up * PLAYER_HEIGHT_OFFSET);
            }
        }

        // 숲 구간 경계 원 밖으로 나가지 못하게 원 안으로 되돌린다. 씬에 보이지 않는 벽도 같은 반경으로 쳐 두었다
        private Vector3 KeepInForest(Vector3 position)
        {
            if (_forestCenter == null)
            {
                return position;
            }

            Vector3 offset = position - _forestCenter.position;
            offset.y       = 0f;

            float limit = FOREST_RADIUS - FOREST_MARGIN;
            if (offset.magnitude <= limit)
            {
                return position;
            }

            Vector3 clamped = _forestCenter.position + (offset.normalized * limit);
            return new Vector3(clamped.x, position.y, clamped.z);
        }

        private void HandleActions(double now)
        {
            if ((Input.GetKeyDown(KeyCode.J)) && (now >= _attackReadyAt))
            {
                _attackReadyAt = now + ATTACK_COOLDOWN;
                Strike(ATTACK_RANGE, ATTACK_DAMAGE, false);
                AttackUsed?.Invoke();
            }

            if ((Input.GetKeyDown(KeyCode.Q)) && (now >= _skillReadyAt))
            {
                _skillReadyAt = now + SKILL_COOLDOWN;
                Strike(SKILL_RANGE, SKILL_DAMAGE, true);
                SkillQUsed?.Invoke();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                _health = Mathf.Min(PLAYER_MAX_HEALTH, _health + POTION_HEAL);
                PotionUsed?.Invoke();
            }
        }

        // 범위 안 산귀에게 피해를 준다. area가 거짓이면 가장 가까운 한 마리만 맞는다
        private void Strike(float range, float damage, bool area)
        {
            Enemy nearest         = null;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < _enemies.Count; ++i)
            {
                float distance = FlatDistance(_player.position, _enemies[i].Body.position);
                if (distance > range)
                {
                    continue;
                }

                if (area)
                {
                    _enemies[i].Health -= damage;
                }
                else if (distance < nearestDistance)
                {
                    nearest         = _enemies[i];
                    nearestDistance = distance;
                }
            }

            if (nearest != null)
            {
                nearest.Health -= damage;
            }
        }

        // 부적 가까이 가면 한 번 알리고, 멀어지면 다시 알릴 수 있게 한다
        private void UpdateTalisman()
        {
            if (_talisman == null)
            {
                return;
            }

            float distance = FlatDistance(_player.position, _talisman.position);
            if (distance <= TALISMAN_ENTER_RADIUS)
            {
                if (!_talismanLatched)
                {
                    _talismanLatched = true;
                    TalismanApproached?.Invoke();
                }
            }
            else if (distance >= TALISMAN_LEAVE_RADIUS)
            {
                _talismanLatched = false;
            }
        }

        private void UpdateEnemies(double now)
        {
            for (int i = _enemies.Count - 1; i >= 0; --i)
            {
                Enemy enemy = _enemies[i];

                if (enemy.Health <= 0f)
                {
                    RemoveEnemy(i);
                    EnemyKilled?.Invoke(enemy.SpawnIndex);
                    continue;
                }

                if (enemy.Kind == EPrologueEnemyKind.Normal)
                {
                    ChaseAndHit(enemy, now);
                }
                else
                {
                    RunTelegraph(enemy, now);
                }
            }
        }

        private void ChaseAndHit(Enemy enemy, double now)
        {
            float distance = FlatDistance(_player.position, enemy.Body.position);
            if (distance > ENEMY_CONTACT_RANGE)
            {
                Vector3 toPlayer = _player.position - enemy.Body.position;
                toPlayer.y       = 0f;
                enemy.Body.position += toPlayer.normalized * (ENEMY_SPEED * Time.deltaTime);
                return;
            }

            if (now >= _contactReadyAt)
            {
                _contactReadyAt = now + ENEMY_CONTACT_INTERVAL;
                Hurt(ENEMY_CONTACT_DAMAGE, now);
            }
        }

        // 예고형은 제자리에서 붉은 원을 띄우고 시간이 끝나면 그 안에 있던 플레이어에게 피해를 준다
        private void RunTelegraph(Enemy enemy, double now)
        {
            if (!enemy.Telegraphing)
            {
                if (now < enemy.NextTelegraphAt)
                {
                    return;
                }

                enemy.Telegraphing   = true;
                enemy.TelegraphEndAt = now + TELEGRAPH_SECONDS;
                enemy.Zone           = CreateZone(enemy.Body.position);
                TelegraphStarted?.Invoke();
                return;
            }

            if (now < enemy.TelegraphEndAt)
            {
                return;
            }

            enemy.Telegraphing    = false;
            enemy.NextTelegraphAt = now + TELEGRAPH_INTERVAL;

            if (FlatDistance(_player.position, enemy.Body.position) <= TELEGRAPH_RADIUS)
            {
                Hurt(TELEGRAPH_DAMAGE, now);
            }

            DestroyZone(enemy);
            TelegraphEnded?.Invoke();
        }

        private void Hurt(float damage, double now)
        {
            if ((now < _invulnerableUntil) || (_health <= 0f))
            {
                return;
            }

            _health   = Mathf.Max(0f, _health - damage);
            _hurtUntil = now + HURT_FLASH_SECONDS;
        }

        private void ReportDown()
        {
            if ((_health > 0f) || (_downReported))
            {
                return;
            }

            _downReported = true;
            PlayerDown?.Invoke();
        }

        // 부적 위치가 눈에 보이게 노란 빛 입자와 점광을 놓는다. 이미 있으면 다시 만들지 않는다
        private void CreateTalismanBeacon()
        {
            if ((_talisman == null) || (_beacon != null))
            {
                return;
            }

            var root = new GameObject("ForestDevTalismanBeacon").transform;
            root.SetParent(transform, false);
            root.position = _talisman.position;
            _beacon = root;

            // 위로 천천히 떠오르는 노란 빛 입자
            var glowObject = new GameObject("Glow");
            glowObject.transform.SetParent(root, false);
            glowObject.transform.localPosition = Vector3.up * BEACON_LIFT;
            glowObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var glow = glowObject.AddComponent<ParticleSystem>();
            var main = glow.main;
            main.loop            = true;
            main.startLifetime   = BEACON_LIFETIME;
            main.startSpeed      = BEACON_RISE_SPEED;
            main.startSize       = new ParticleSystem.MinMaxCurve(BEACON_SIZE_MIN, BEACON_SIZE_MAX);
            main.startColor      = BeaconColor;
            main.maxParticles    = BEACON_MAX_PARTICLES;

            var emission = glow.emission;
            emission.rateOverTime = BEACON_RATE;

            var shape = glow.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle     = BEACON_CONE_ANGLE;
            shape.radius    = TALISMAN_ENTER_RADIUS * 0.5f;

            // 생겼다가 사라질 때 부드럽게 보이도록 투명도를 0 -> 1 -> 0으로 바꾼다
            var fade     = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, BEACON_FADE_IN), new GradientAlphaKey(0f, 1f) });
            var colorOverLifetime     = glow.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color   = fade;

            glowObject.GetComponent<ParticleSystemRenderer>().material = CreateGlowMaterial();

            // 주변 지면을 은은하게 물들이는 점광
            var lightObject = new GameObject("GlowLight");
            lightObject.transform.SetParent(root, false);
            lightObject.transform.localPosition = Vector3.up * BEACON_LIGHT_HEIGHT;

            var glowLight       = lightObject.AddComponent<Light>();
            glowLight.type      = LightType.Point;
            glowLight.color     = BeaconColor;
            glowLight.range     = BEACON_LIGHT_RANGE;
            glowLight.intensity = BEACON_LIGHT_INTENSITY;
        }

        // 가운데가 밝고 가장자리가 투명한 원형 점을 더해 그리는(Additive) 재질을 만든다
        private static Material CreateGlowMaterial()
        {
            Shader shader = Shader.Find(GLOW_SHADER_NAME);
            var material  = new Material((shader != null) ? shader : Shader.Find(GLOW_FALLBACK_SHADER_NAME));

            var texture = new Texture2D(GLOW_TEXTURE_SIZE, GLOW_TEXTURE_SIZE, TextureFormat.RGBA32, false);
            float half  = (GLOW_TEXTURE_SIZE - 1) * 0.5f;
            for (int y = 0; y < GLOW_TEXTURE_SIZE; ++y)
            {
                for (int x = 0; x < GLOW_TEXTURE_SIZE; ++x)
                {
                    float distance = Mathf.Sqrt(((x - half) * (x - half)) + ((y - half) * (y - half))) / half;
                    float alpha    = Mathf.Pow(Mathf.Clamp01(1f - distance), GLOW_FALLOFF);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            texture.wrapMode = TextureWrapMode.Clamp;

            material.mainTexture = texture;
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        private Transform CreateZone(Vector3 center)
        {
            GameObject zone = TestRigDisplay.CreatePrimitive(PrimitiveType.Cylinder);
            zone.name = "ForestDevTelegraphZone";
            Destroy(zone.GetComponent<Collider>());
            zone.transform.SetParent(transform, false);
            zone.transform.position   = new Vector3(center.x, center.y - PLAYER_HEIGHT_OFFSET + ZONE_THICKNESS, center.z);
            zone.transform.localScale = new Vector3(TELEGRAPH_RADIUS * 2f, ZONE_THICKNESS, TELEGRAPH_RADIUS * 2f);
            zone.GetComponent<Renderer>().material.color = ZoneColor;
            return zone.transform;
        }

        private static void DestroyZone(Enemy enemy)
        {
            if (enemy.Zone != null)
            {
                Destroy(enemy.Zone.gameObject);
                enemy.Zone = null;
            }
        }

        private void RemoveEnemy(int index)
        {
            Enemy enemy = _enemies[index];
            DestroyZone(enemy);

            if (enemy.Body != null)
            {
                Destroy(enemy.Body.gameObject);
            }

            _enemies.RemoveAt(index);
        }

        private void ClearEnemies()
        {
            for (int i = _enemies.Count - 1; i >= 0; --i)
            {
                RemoveEnemy(i);
            }
        }

        // 위치 표시용 오브젝트는 에디터 전용 코드라 이름으로 찾는다
        private static Transform FindMarker(string markerName)
        {
            GameObject found = GameObject.Find(markerName);
            return (found != null) ? found.transform : null;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
#endif
