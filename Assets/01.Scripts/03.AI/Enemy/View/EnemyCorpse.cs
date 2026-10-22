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
        // 서버 개체는 처치 즉시 풀로 돌아가므로 시체는 모델 복제본을 월드 루트에 따로 둔다. 충돌체와 리지드바디가 없어
        // 플레이어, 살아 있는 적, 길찾기, 공격 판정에 닿지 않고 NetworkTransform과도 겹치지 않는다.
        // 현재 모델(Beast, Vendigo)에는 래그돌이 없어 포물선 비행, 회전, 바닥 튕김으로 대신 보인다.
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

        private static readonly List<EnemyCorpse>  s_active = new List<EnemyCorpse>(MAX_CORPSES);
        private static readonly RaycastHit[]       s_hits   = new RaycastHit[HIT_BUFFER];

        private Vector3 _velocity;
        private Vector3 _spin;
        private Vector3 _baseScale;
        private float   _elapsed;
        private bool    _settled;
        private bool    _ashing;
        private bool    _shrinking;

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
            _elapsed += Time.deltaTime;

            if (!_settled)
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

        /// <summary> source 모델의 현재 자세를 복제해 away(수평) 쪽으로 날린다. 방향이 없으면 source의 뒤쪽으로 날린다. 복제할 수 없으면 false </summary>
        public static bool Launch(GameObject source, Vector3 away)
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
            return true;
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

            GameObject copy = Instantiate(source);
            copy.name = source.name + "_Corpse";
            copy.transform.SetParent(null, true);
            Strip(copy);

            EnemyCorpse corpse = copy.AddComponent<EnemyCorpse>();
            s_active.Add(corpse);
            return corpse;
        }

        // 복제본에서 판정과 동기화에 닿는 것을 모두 걷어 내고, 애니메이션은 현재 자세로 굳힌다
        private static void Strip(GameObject copy)
        {
            foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true))
            {
                Destroy(collider);
            }

            foreach (Rigidbody body in copy.GetComponentsInChildren<Rigidbody>(true))
            {
                Destroy(body);
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
