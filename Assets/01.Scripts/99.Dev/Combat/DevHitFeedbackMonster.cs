// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 정해진 횟수만 맞으면 죽는 시험용 몬스터. 플레이어를 쫓아오며 서로 겹치지 않고, 맞은 연출은 HitReactor가 맡는다 </summary>
    public sealed class DevHitFeedbackMonster : MonoBehaviour
    {
        // 판정과 경로 탐색은 없다. 플레이어 쪽으로 직선으로 걷고, 나무나 바위는 피하지 않는다.
        // 피격 연출 확인용 표적이라 체력 대신 맞은 횟수로 죽는다. 서버가 하게 될 일(맞은 횟수, 넉백 이동, 사망)은 여기서 하고,
        // 보이는 연출은 HitReactor에 맡겨 둔다. 렉프레임은 전역 timeScale을 건드리지 않고 이 몬스터의 Update만 건너뛰어 만든다

        /****************************************
        *                Fields
        ****************************************/

        private static readonly Color s_normalColor = new Color(0.55f, 0.2f, 0.2f, 1f);
        private static readonly Color s_corpseColor = new Color(0.32f, 0.12f, 0.09f, 1f); // 원래 색에 검정을 섞은 적갈색

        private const float DEATH_SECONDS = 0.25f;
        private const float CORPSE_SECONDS = 10f; // 날아가 쓰러진 채 남아 있는 시간

        // 넉백. 맞은 순간의 속도가 지수로 줄어든다
        private const float KNOCKBACK_STOP_SQR = 0.0001f;
        private const float SHOVE_DRAG         = 5f; // 1/s, 충격파나 시체에 밀릴 때의 감속

        // 추격. 맞아 밀려나는 동안은 걷지 않고, 몬스터끼리는 몸통이 겹치지 않게 서로 민다
        private const float CHASE_STOP_DISTANCE   = 1.2f;  // m, 플레이어와 이만큼 남기고 선다
        private const float SEPARATION_DISTANCE   = 0.9f;  // m, 몬스터 중심끼리 이보다 가까우면 벌린다
        private const float PLAYER_MIN_DISTANCE   = 0.9f;  // m, 플레이어 몸통 반지름과 몬스터 반지름의 합에 여유를 더한 값
        private const float STUN_SPEED_SQR        = 1f;    // 넉백 속도가 이 값(제곱) 아래로 식어야 다시 걷는다
        private const float GROUND_PROBE_UP       = 0.5f;  // m, 몸 중심 위 이 높이에서 아래로 땅을 찾는다
        private const float GROUND_PROBE_DISTANCE = 4f;    // m
        private const float GROUND_FOLLOW_SPEED   = 20f;   // 1/s

        // 날아가는 시체에 부딪힌 산 몬스터는 같이 튕겨 나간다
        private const float SHOVE_MIN_SPEED = 4f;   // m/s, 이 속도 이상으로 부딪혀야 민다
        private const float SHOVE_RATIO     = 0.8f; // 부딪힌 속도 대비 밀려나는 속도
        private const float SHOVE_MAX_SPEED = 14f;  // m/s

        private const float MIN_DIRECTION_SQR = 0.0001f;
        private const float HALF              = 0.5f;

        // 살아서 걷는 몬스터. 서로 겹침을 풀 때 훑는다
        private static readonly List<DevHitFeedbackMonster> s_alive = new List<DevHitFeedbackMonster>();

        private HitReactor _reactor       = null;
        private Transform  _target        = null;
        private Vector3    _baseScale     = Vector3.one;
        private Vector3    _knockVelocity = Vector3.zero;
        private float      _chaseSpeed    = 0f;
        private float      _knockDrag     = 0f;
        private int        _hitsLeft      = 0;
        private float      _deathElapsed  = 0f;
        private bool       _isDying       = false;

        /// <summary> 죽는 순간 한 번 알린다. 사체는 이 뒤로도 한동안 남아 있다 </summary>
        public event Action<DevHitFeedbackMonster> Died;

        /// <summary> 죽는 연출 중이거나 이미 죽었는지 </summary>
        public bool IsDead => _isDying;

        /// <summary> 맞은 몸의 연출. 렉프레임을 걸 때 대상으로 넘긴다 </summary>
        public IHitLagTarget LagTarget => _reactor;

        /****************************************
        *              Unity Event
        ****************************************/

        // 플레이 모드 진입 때 도메인 리로드를 끄면 지난 판의 목록이 남는다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_alive.Clear();
        }

        private void OnEnable()
        {
            s_alive.Add(this);
        }

        private void OnDisable()
        {
            s_alive.Remove(this);
        }

        private void Update()
        {
            // 렉프레임 동안은 떨기만 하고 나머지 시간은 흐르지 않는다. 처치한 몬스터도 멈춤이 풀린 뒤에 날아간다
            if (_reactor.Tick())
            {
                return;
            }

            if (_isDying)
            {
                UpdateDeath();
                return;
            }

            UpdateKnockback();
            UpdateChase();
        }

        // 날아가는 시체가 산 몬스터에 부딪히면 부딪힌 방향으로 같이 튕겨 보낸다
        private void OnCollisionEnter(Collision collision)
        {
            Rigidbody body = _reactor?.Body;
            if ((body == null) || (!collision.collider.TryGetComponent(out DevHitFeedbackMonster other)) || (other._isDying))
            {
                return;
            }

            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;

            float speed = velocity.magnitude;
            if (speed >= SHOVE_MIN_SPEED)
            {
                other.Shove(velocity / speed, Mathf.Min(speed * SHOVE_RATIO, SHOVE_MAX_SPEED));
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 죽기까지 맞아야 하는 횟수를 정하고 연출을 준비한다. playerCollider를 쫓아가며, 죽어 날아갈 때는 부딪히지 않는다 </summary>
        public void Init(int hitsToDie, Collider playerCollider, float chaseSpeed)
        {
            _chaseSpeed = chaseSpeed;
            _target     = (playerCollider != null) ? playerCollider.transform : null;
            _baseScale  = transform.localScale;
            _hitsLeft   = hitsToDie;
            _reactor    = new HitReactor(gameObject, s_normalColor, s_corpseColor);

            // 날아가는 시체가 플레이어를 밀거나 막지 않게 한다
            Collider ownCollider = GetComponent<Collider>();
            if ((playerCollider != null) && (ownCollider != null))
            {
                Physics.IgnoreCollision(ownCollider, playerCollider);
            }
        }

        /// <summary> 공격자 위치 쪽에서 profile의 타격으로 한 대 맞는다. 맞은 횟수가 다 차면 날아가며 죽는다. 맞은 것으로 쳤으면 true, 이미 죽는 중이면 false </summary>
        public bool TakeHit(Vector3 attackerPosition, HitFeedbackProfile profile, out bool isKill)
        {
            isKill = false;
            if (_isDying)
            {
                return false;
            }

            Vector3 away = transform.position - attackerPosition;
            away.y = 0f;
            away = (away.sqrMagnitude > MIN_DIRECTION_SQR) ? away.normalized : Vector3.forward;

            --_hitsLeft;
            if (_hitsLeft <= 0)
            {
                isKill = true;
                StartDeath(away, profile);
                return true;
            }

            _knockVelocity = away * profile.KnockbackSpeed;
            _knockDrag     = profile.KnockbackDrag;
            _reactor.React(profile);
            return true;
        }

        /// <summary> center에서 radius 안의 산 몬스터를 바깥쪽으로 민다. 가까울수록 세게 밀리고, 이미 더 세게 밀려나는 중이면 건드리지 않는다 </summary>
        public static void ShoveAround(Vector3 center, float radius, float speed)
        {
            for (int i = 0; i < s_alive.Count; ++i)
            {
                DevHitFeedbackMonster monster = s_alive[i];

                Vector3 away = monster.transform.position - center;
                away.y = 0f;

                float distance = away.magnitude;
                if (distance >= radius)
                {
                    continue;
                }

                Vector3 direction = (distance > MIN_DIRECTION_SQR) ? (away / distance) : Vector3.forward;
                monster.Shove(direction, speed * (1f - (distance / radius)));
            }
        }

        private void StartDeath(Vector3 away, HitFeedbackProfile profile)
        {
            _isDying = true;
            s_alive.Remove(this);
            _reactor.Die(away, profile);
            IgnoreOtherMonsters();

            // 사체가 남아 있는 동안에도 다음 무리가 나올 수 있게 죽는 순간 알린다
            Died?.Invoke(this);
        }

        // 죽는 순간부터 다른 몬스터(산 것과 사체 모두)와는 부딪히지 않는다. 바닥과는 계속 부딪혀 튄다
        private void IgnoreOtherMonsters()
        {
            Collider ownCollider = GetComponent<Collider>();
            if (ownCollider == null)
            {
                return;
            }

            DevHitFeedbackMonster[] monsters = FindObjectsByType<DevHitFeedbackMonster>(FindObjectsSortMode.None);
            for (int i = 0; i < monsters.Length; ++i)
            {
                if (monsters[i] == this)
                {
                    continue;
                }

                Collider other = monsters[i].GetComponent<Collider>();
                if (other != null)
                {
                    Physics.IgnoreCollision(ownCollider, other);
                }
            }
        }

        private void Shove(Vector3 direction, float speed)
        {
            if (_knockVelocity.sqrMagnitude >= (speed * speed))
            {
                return;
            }

            _knockVelocity = direction * speed;
            _knockDrag     = SHOVE_DRAG;
        }

        private void UpdateKnockback()
        {
            if (_knockVelocity.sqrMagnitude <= KNOCKBACK_STOP_SQR)
            {
                return;
            }

            transform.position += _knockVelocity * Time.deltaTime;
            _knockVelocity     *= Mathf.Exp(-_knockDrag * Time.deltaTime);
        }

        // 플레이어 쪽으로 걷고, 겹친 몬스터를 밀어내고, 땅 높이에 맞춘다
        private void UpdateChase()
        {
            Vector3 position = transform.position;

            if ((_target != null) && (_knockVelocity.sqrMagnitude <= STUN_SPEED_SQR))
            {
                Vector3 toTarget = _target.position - position;
                toTarget.y = 0f;

                float distance = toTarget.magnitude;
                if (distance > CHASE_STOP_DISTANCE)
                {
                    float step = Mathf.Min(_chaseSpeed * Time.deltaTime, distance - CHASE_STOP_DISTANCE);
                    position  += (toTarget / distance) * step;
                }
            }

            position += ReadSeparation(position);

            // 뒤에서 밀려도 플레이어 몸속으로는 못 들어온다. 마지막에 적용해 이 거리가 우선한다
            if (_target != null)
            {
                Vector3 fromTarget = position - _target.position;
                fromTarget.y = 0f;

                float sqr = fromTarget.sqrMagnitude;
                if ((sqr < (PLAYER_MIN_DISTANCE * PLAYER_MIN_DISTANCE)) && (sqr > MIN_DIRECTION_SQR))
                {
                    position += fromTarget.normalized * (PLAYER_MIN_DISTANCE - Mathf.Sqrt(sqr));
                }
            }

            position.y = FollowGround(position);
            transform.position = position;
        }

        // 가까운 몬스터마다 겹친 길이의 절반씩 밀려난다. 상대도 같은 계산을 해서 한 프레임에 겹침이 풀린다
        private Vector3 ReadSeparation(Vector3 position)
        {
            Vector3 push = Vector3.zero;

            for (int i = 0; i < s_alive.Count; ++i)
            {
                DevHitFeedbackMonster other = s_alive[i];
                if (other == this)
                {
                    continue;
                }

                Vector3 away = position - other.transform.position;
                away.y = 0f;

                float sqr = away.sqrMagnitude;
                if (sqr >= (SEPARATION_DISTANCE * SEPARATION_DISTANCE))
                {
                    continue;
                }

                // 정확히 겹쳐 방향이 없으면 아무 쪽으로나 민다
                if (sqr <= MIN_DIRECTION_SQR)
                {
                    Vector2 random = UnityEngine.Random.insideUnitCircle.normalized;
                    push += new Vector3(random.x, 0f, random.y) * (SEPARATION_DISTANCE * HALF);
                    continue;
                }

                float distance = Mathf.Sqrt(sqr);
                push += (away / distance) * ((SEPARATION_DISTANCE - distance) * HALF);
            }

            return push;
        }

        // 몸 안에서 아래로 쏘면 자기 충돌체는 맞지 않는다. 캡슐 높이의 절반이 곧 스케일 y다
        private float FollowGround(Vector3 position)
        {
            Vector3 origin = position + (Vector3.up * GROUND_PROBE_UP);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, GROUND_PROBE_DISTANCE,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return position.y;
            }

            float targetY = hit.point.y + _baseScale.y;
            return Mathf.Lerp(position.y, targetY, Mathf.Clamp01(GROUND_FOLLOW_SPEED * Time.deltaTime));
        }

        // 날아가 쓰러진 채 한동안 남아 있다가 줄어들며 사라진다
        private void UpdateDeath()
        {
            _deathElapsed += Time.deltaTime;

            float shrink = Mathf.Clamp01(1f - ((_deathElapsed - CORPSE_SECONDS) / DEATH_SECONDS));
            transform.localScale = _baseScale * shrink;

            if (shrink <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }
}
