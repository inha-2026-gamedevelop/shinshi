// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Shinshi.AI
{
    /// <summary> 처치된 일반 적의 모델 복제본을 날려 보내고 쓰러뜨린 뒤 줄여 지우는 로컬 시체. 어떤 판정에도 참여하지 않는다 </summary>
    public sealed class EnemyCorpse : MonoBehaviour
    {
        // 서버 개체는 처치 즉시 풀로 돌아가므로 시체는 모델 복제본을 월드 루트에 따로 둔다.
        // 네트워크 동기화와 공격 판정에서 분리하고, 시체 충돌은 플레이어와 살아 있는 적을 무시한다.
        // 모델에 준비된 뼈 물리를 사용하고, 없으면 스킨 뼈에서 만든다. 뼈가 없는 모델은 포물선 비행으로 대신 보인다.
        // 동시에 남는 시체는 MAX_CORPSES개로 막고 넘으면 가장 오래된 것부터 재사용한다

        /****************************************
        *                Fields
        ****************************************/

        private const float FLY_SPEED       = 9f;   // m/s, 수평
        private const float FLY_UP_SPEED    = 5f;   // m/s
        private const float GRAVITY         = 20f;  // m/s^2
        private const float SPIN_SPEED      = 10f;  // rad/s
        private const float BOUNCE          = 0.35f;
        private const float GROUND_DRAG     = 6f;   // 1/s, 바닥에서 미끄러지는 감속
        private const float SETTLE_SPEED    = 1.2f; // m/s, 이 아래로 튀면 멈춘다
        private const float GROUND_UP       = 0.1f; // m, 모델 원점이 바닥에서 뜨는 높이
        private const float PROBE_UP        = 1f;
        private const float PROBE_DISTANCE  = 6f;
        private const float CORPSE_SECONDS  = 4f;
        private const float SHRINK_SECONDS  = 0.5f;
        private const float ASH_SECONDS     = 1.6f; // 재가 되어 사라지는 데 걸리는 시간
        private const float ASH_LINGER      = 1.5f; // 몸이 다 사라진 뒤 재 입자가 흩어지길 기다리는 시간
        private const float MIN_DIRECTION_SQR = 0.0001f;
        private const int   MAX_CORPSES     = 16;
        private const int   HIT_BUFFER      = 8;

        // 처치 순간의 타격감. 날아가기 전에 몸이 하얗게 번쩍이며 잠깐 굳어 떨고, 풀리면 날아간다
        private const int   DEATH_FREEZE_FRAMES = 6;     // 프레임, 타격 멈춤 3 + 처치 추가 3
        private const float DEATH_VIBRATE       = 0.05f; // m, 굳어 있는 동안 떠는 크기
        private const float DEATH_FLASH_SECONDS = 0.2f;

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

        private static readonly List<EnemyCorpse>  s_active = new List<EnemyCorpse>(MAX_CORPSES);
        private static readonly RaycastHit[]       s_hits   = new RaycastHit[HIT_BUFFER];
        private static readonly MaterialPropertyBlock s_block = new MaterialPropertyBlock();

        /// <summary> 시체의 뼈 물리가 켜진 순간 한 번 알린다. 값은 뼈 충돌체다. 개체 쪽에서 이 뼈와의 충돌을 끄는 데 쓴다 </summary>
        public static event System.Action<Collider[]> RagdollLaunched;

        // 번쩍임이 식은 뒤 시체가 머무는 색. 원래 색에 검정을 섞은 적갈색이다
        private static readonly Color CORPSE_COLOR = new Color(0.32f, 0.12f, 0.09f, 1f);

        private Renderer[] _flashRenderers;
        private bool    _prebuiltRagdoll;
        private float   _flyMultiplier = 1f;
        private Vector3 _vibrateOffset;
        private Vector3 _pendingAway;
        private float   _flashLeft;
        private int     _freezeFrames;
        private bool    _ragdollPending;

        private Vector3 _velocity;
        private Vector3 _spin;
        private Vector3 _baseScale;
        private float   _elapsed;
        private bool    _settled;
        private bool    _ashing;
        private bool    _shrinking;
        private bool    _ragdolled;

        /****************************************
        *              Unity Event
        ****************************************/

        // 도메인 리로드를 끈 플레이 모드 진입 때 지난 판의 목록이 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_active.Clear();
        }

        private void Update()
        {
            // 굳어 있는 동안은 시체 시간도 흐르지 않고 날아가지도 않는다
            if (UpdateFreeze())
            {
                return;
            }

            if (_ragdollPending)
            {
                _ragdollPending = false;

                // 사체 개수 상한과 래그돌의 자동 물리 종료로 비용을 제한한다. 새 사체를 경직된 자세로 대체하지 않는다
                _ragdolled = _prebuiltRagdoll
                    ? EnemyCorpseRagdoll.Adopt(gameObject, _pendingAway, _flyMultiplier)
                    : EnemyCorpseRagdoll.Build(gameObject, _pendingAway, _flyMultiplier);

                if (_ragdolled)
                {
                    IgnoreCharacters(GetComponentsInChildren<Collider>());
                }
            }

            UpdateFlash();
            _elapsed += Time.deltaTime;

            if ((!_settled) && (!_ragdolled))
            {
                Fly(Time.deltaTime);
            }

            if (_elapsed >= CORPSE_SECONDS)
            {
                UpdateVanish();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 시체 시간이 끝나면 재가 되어 사라지고, 셰이더를 못 찾으면 줄어들며 사라진다
        private void UpdateVanish()
        {
            if ((!_ashing) && (!_shrinking))
            {
                _ashing    = EnemyCorpseDissolve.Begin(gameObject, ASH_SECONDS);
                _shrinking = !_ashing;
            }

            if (_ashing)
            {
                if (_elapsed >= (CORPSE_SECONDS + ASH_SECONDS + ASH_LINGER))
                {
                    Recycle(this);
                }

                return;
            }

            float shrink = Mathf.Clamp01(1f - ((_elapsed - CORPSE_SECONDS) / SHRINK_SECONDS));
            transform.localScale = _baseScale * shrink;
            if (shrink <= 0f)
            {
                Recycle(this);
            }
        }

        /// <summary> source 모델의 현재 자세를 복제해 away(수평) 쪽으로 날린다. 방향이 없으면 source의 뒤쪽으로 날린다. 복제할 수 없으면 false.
        /// flyMultiplier는 날아가는 세기 배율이고, freezeFrames는 날기 전에 굳는 프레임 수다(음수면 기본값) </summary>
        public static bool Launch(GameObject source, Vector3 away, float flyMultiplier = 1f, int freezeFrames = -1)
        {
            if (source == null)
            {
                return false;
            }

            away.y = 0f;
            away   = (away.sqrMagnitude > MIN_DIRECTION_SQR) ? away.normalized : (-source.transform.forward);

            EnemyCorpse corpse = Acquire(source);
            if (corpse == null)
            {
                return false;
            }

            Transform sourceTransform = source.transform;
            corpse.transform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
            corpse.transform.localScale = sourceTransform.lossyScale;
            corpse.Begin(away);
            corpse._velocity *= flyMultiplier;

            // 래그돌은 굳음이 풀린 뒤 Update에서 만들어 날린다
            corpse._pendingAway    = away;
            corpse._flyMultiplier  = flyMultiplier;
            corpse._ragdollPending = true;
            corpse.BeginImpact((freezeFrames >= 0) ? freezeFrames : DEATH_FREEZE_FRAMES);

            return true;
        }

        // 하얗게 번쩍이며 몇 프레임 굳는다. 번쩍임은 시체 색으로 식는다
        private void BeginImpact(int freezeFrames)
        {
            _freezeFrames = freezeFrames;
            _flashLeft    = DEATH_FLASH_SECONDS;

            var renderers = new List<Renderer>();
            foreach (Renderer found in GetComponentsInChildren<Renderer>(true))
            {
                Material material = found.sharedMaterial;
                if ((material != null) && (material.HasProperty(BASE_COLOR_ID)))
                {
                    renderers.Add(found);
                }
            }

            _flashRenderers = renderers.ToArray();
            ApplyFlash(1f);
        }

        // 프레임마다 직전 떨림을 되돌리고 새로 떤다. 굳어 있는 프레임이면 true
        private bool UpdateFreeze()
        {
            transform.position -= _vibrateOffset;
            _vibrateOffset      = Vector3.zero;

            if (_freezeFrames <= 0)
            {
                return false;
            }

            --_freezeFrames;
            Vector2 shake = Random.insideUnitCircle * DEATH_VIBRATE;
            _vibrateOffset      = new Vector3(shake.x, 0f, shake.y);
            transform.position += _vibrateOffset;
            return true;
        }

        private void UpdateFlash()
        {
            if (_flashLeft <= 0f)
            {
                return;
            }

            _flashLeft = Mathf.Max(0f, _flashLeft - Time.deltaTime);
            ApplyFlash(_flashLeft / DEATH_FLASH_SECONDS);
        }

        // weight 1이면 흰색, 0이면 시체 색. 번쩍임이 끝나도 시체 색은 그대로 남는다
        private void ApplyFlash(float weight)
        {
            Color color = Color.Lerp(CORPSE_COLOR, Color.white, weight);
            for (int i = 0; i < _flashRenderers.Length; ++i)
            {
                if (_flashRenderers[i] == null)
                {
                    continue;
                }

                _flashRenderers[i].GetPropertyBlock(s_block);
                s_block.SetColor(BASE_COLOR_ID, color);
                _flashRenderers[i].SetPropertyBlock(s_block);
            }
        }

        // 뼈 물리가 켜지는 순간, 플레이어와 적(산 것)과는 부딪히지 않게 한다. 바닥과 장애물에만 부딪혀 튄다.
        // 레이어만으로는 지면과 개체를 가를 수 없어(둘 다 Default) 켜지는 순간 있는 개체와 짝으로 무시한다
        private static void IgnoreCharacters(Collider[] bones)
        {
            foreach (CharacterController character in FindObjectsByType<CharacterController>())
            {
                IgnoreAll(bones, character);
            }

            foreach (NetworkIdentity identity in FindObjectsByType<NetworkIdentity>())
            {
                foreach (Collider other in identity.GetComponentsInChildren<Collider>())
                {
                    IgnoreAll(bones, other);
                }
            }

            RagdollLaunched?.Invoke(bones);
        }

        private static void IgnoreAll(Collider[] bones, Collider other)
        {
            for (int i = 0; i < bones.Length; ++i)
            {
                if ((bones[i] != other) && (other.enabled))
                {
                    Physics.IgnoreCollision(bones[i], other);
                }
            }
        }

        /// <summary> 남아 있는 시체를 모두 지운다. 던전 종료와 씬 전환에서 부른다 </summary>
        public static void ClearAll()
        {
            for (int i = s_active.Count - 1; i >= 0; --i)
            {
                Recycle(s_active[i]);
            }
        }

        private static EnemyCorpse Acquire(GameObject source)
        {
            // 씬 전환으로 이미 파괴된 시체는 목록에서 먼저 뺀다
            for (int i = s_active.Count - 1; i >= 0; --i)
            {
                if (s_active[i] == null)
                {
                    s_active.RemoveAt(i);
                }
            }

            // 상한이 차면 가장 오래된 시체를 지우고 새로 만든다
            if (s_active.Count >= MAX_CORPSES)
            {
                Recycle(s_active[0]);
            }

            // 뼈마다 리지드바디가 미리 달린 모델은 그 래그돌을 살려 쓴다
            bool prebuilt = source.GetComponentInChildren<Rigidbody>(true) != null;

            GameObject copy = Instantiate(source);
            copy.name = source.name + "_Corpse";
            copy.transform.SetParent(null, true);
            Strip(copy, prebuilt);

            EnemyCorpse corpse = copy.AddComponent<EnemyCorpse>();
            corpse._prebuiltRagdoll = prebuilt;
            s_active.Add(corpse);
            return corpse;
        }

        // 복제본에서 판정과 동기화에 닿는 것을 모두 걷어 내고, 애니메이션은 현재 자세로 굳힌다
        private static void Strip(GameObject copy, bool keepRagdoll)
        {
            // 미리 만든 래그돌은 날아가기 전까지 물리를 끈 채로 둔다
            if (keepRagdoll)
            {
                foreach (Rigidbody body in copy.GetComponentsInChildren<Rigidbody>(true))
                {
                    body.isKinematic = true;
                }

                foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                }
            }
            else
            {
                // 리지드바디에 의존하는 관절이 남아 있으면 리지드바디를 못 지우므로 복제본의 관절을 먼저 즉시 지운다
                foreach (Joint joint in copy.GetComponentsInChildren<Joint>(true))
                {
                    DestroyImmediate(joint);
                }

                foreach (Rigidbody body in copy.GetComponentsInChildren<Rigidbody>(true))
                {
                    DestroyImmediate(body);
                }

                foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true))
                {
                    DestroyImmediate(collider);
                }
            }

            foreach (NetworkBehaviour behaviour in copy.GetComponentsInChildren<NetworkBehaviour>(true))
            {
                Destroy(behaviour);
            }

            foreach (Animator animator in copy.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false;
            }

            copy.SetActive(true);
        }

        private void Begin(Vector3 away)
        {
            _baseScale = transform.localScale;
            _elapsed   = 0f;
            _settled   = false;
            _ashing    = false;
            _shrinking = false;
            _ragdolled = false;
            _velocity  = (away * FLY_SPEED) + (Vector3.up * FLY_UP_SPEED);
            _spin      = Vector3.Cross(Vector3.up, away) * SPIN_SPEED;
            gameObject.SetActive(true);
        }

        private void Fly(float dt)
        {
            _velocity.y -= GRAVITY * dt;

            Vector3 position = transform.position + (_velocity * dt);
            transform.Rotate(_spin * (dt * Mathf.Rad2Deg), Space.World);

            if (TryFindGround(position, out float groundY) && (position.y < (groundY + GROUND_UP)) && (_velocity.y < 0f))
            {
                position.y = groundY + GROUND_UP;

                if (Mathf.Abs(_velocity.y) < SETTLE_SPEED)
                {
                    _velocity = Vector3.zero;
                    _spin     = Vector3.zero;
                    _settled  = true;
                }
                else
                {
                    _velocity.y = -_velocity.y * BOUNCE;
                    _velocity.x *= Mathf.Exp(-GROUND_DRAG * dt);
                    _velocity.z *= Mathf.Exp(-GROUND_DRAG * dt);
                    _spin       *= BOUNCE;
                }
            }

            transform.position = position;
        }

        // 개체(플레이어, 살아 있는 적)는 땅이 아니므로 건너뛴다
        private static bool TryFindGround(Vector3 position, out float groundY)
        {
            Ray ray   = new Ray(position + (Vector3.up * PROBE_UP), Vector3.down);
            int count = Physics.RaycastNonAlloc(ray, s_hits, PROBE_DISTANCE + PROBE_UP, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            groundY = 0f;
            bool found = false;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = s_hits[i];
                if ((hit.distance >= nearest) || (hit.collider.GetComponentInParent<NetworkIdentity>() != null))
                {
                    continue;
                }

                nearest = hit.distance;
                groundY = hit.point.y;
                found   = true;
            }

            return found;
        }

        private static void Recycle(EnemyCorpse corpse)
        {
            s_active.Remove(corpse);
            if (corpse != null)
            {
                Destroy(corpse.gameObject);
            }
        }
    }
}
