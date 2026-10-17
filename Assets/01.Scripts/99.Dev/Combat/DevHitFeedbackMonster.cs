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
        private static readonly Color s_modelColor  = Color.white; // 모델은 원래 색을 그대로 보여 준다
        private static readonly int   s_movingHash  = Animator.StringToHash("Moving");
        private static readonly int   s_attackHash  = Animator.StringToHash("Attack");
        private static readonly int   s_dashingHash = Animator.StringToHash("Dashing");
        private static readonly Color s_corpseColor = new Color(0.32f, 0.12f, 0.09f, 1f); // 원래 색에 검정을 섞은 적갈색

        private const float DEATH_SECONDS = 0.25f;
        private const float CORPSE_SECONDS = 10f; // 날아가 쓰러진 채 남아 있는 시간
        private const float ASH_SECONDS        = 1.6f; // 래그돌 사체가 재가 되어 사라지는 데 걸리는 시간
        private const float ASH_LINGER_SECONDS = 1.5f; // 몸이 다 사라진 뒤 재 입자가 흩어지길 기다리는 시간

        // 넉백. 맞은 순간의 속도가 지수로 줄어든다
        private const float KNOCKBACK_STOP_SQR = 0.0001f;
        private const float SHOVE_DRAG         = 5f; // 1/s, 충격파나 시체에 밀릴 때의 감속

        // 추격. 맞아 밀려나는 동안은 걷지 않고, 몬스터끼리는 몸통이 겹치지 않게 서로 민다
        // 거리는 몬스터 크기에 맞춰 Init에서 정한다. 크기를 키웠는데 거리를 그대로 두면 몸통이 서로, 또 플레이어와 겹쳐 올라탄 것처럼 보인다
        private const float BODY_GAP              = 0.1f;  // m, 몸통 표면 사이에 두는 여유
        private const float CHASE_STOP_EXTRA      = 0.3f;  // m, 플레이어 몸통에 닿을 거리에서 더 물러서 서는 거리
        private const float STUN_SPEED_SQR        = 1f;    // 넉백 속도가 이 값(제곱) 아래로 식어야 다시 걷는다
        private const float GROUND_PROBE_UP       = 0.5f;  // m, 몸 중심 위 이 높이에서 아래로 땅을 찾는다
        private const float GROUND_PROBE_DISTANCE = 4f;    // m
        private const float GROUND_FOLLOW_SPEED   = 20f;   // 1/s

        // 날아가는 시체에 부딪힌 산 몬스터는 같이 튕겨 나간다
        private const float SHOVE_MIN_SPEED = 4f;   // m/s, 이 속도 이상으로 부딪혀야 민다
        private const float SHOVE_RATIO     = 0.8f; // 부딪힌 속도 대비 밀려나는 속도
        private const float SHOVE_MAX_SPEED = 14f;  // m/s

        // 공격. 걷다가 플레이어 몸에 닿을 거리에 서면 애니메이션을 틀고, 선딜이 지난 순간에도 닿아 있으면 한 대 맞힌다
        private const float ATTACK_REACH_EXTRA  = 0.6f; // m, 멈춰 서는 거리에서 더 뻗어 닿는 거리
        private const float ATTACK_DASH_SECONDS = 0.4f; // 초, 돌진형 모델이 공격 뒤 달리는 자세를 유지하는 시간

        private const float TURN_SPEED        = 10f;   // 1/s, 플레이어 쪽으로 몸을 돌리는 속도
        private const float MIN_DIRECTION_SQR = 0.0001f;
        private const float HALF              = 0.5f;
        // 살아서 걷는 몬스터. 서로 겹침을 풀 때 훑는다
        private static readonly List<DevHitFeedbackMonster> s_alive = new List<DevHitFeedbackMonster>();

        private const int GROUND_HIT_BUFFER = 8;
        private static readonly RaycastHit[] s_groundHits = new RaycastHit[GROUND_HIT_BUFFER];

        private HitReactor _reactor       = null;
        private Animator   _animator      = null;
        private Collider   _playerCollider = null;
        private Transform  _target        = null;
        private Vector3    _baseScale     = Vector3.one;
        private Vector3    _knockVelocity = Vector3.zero;
        private float      _chaseSpeed    = 0f;
        private float      _chaseStopDistance  = 0f; // m, 플레이어와 이만큼 남기고 선다
        private float      _separationDistance = 0f; // m, 몬스터 중심끼리 이보다 가까우면 벌린다
        private float      _playerMinDistance  = 0f; // m, 플레이어 중심과 이보다 가까워지지 않는다
        private float      _knockDrag     = 0f;
        private Vector3    _leashCenter   = Vector3.zero; // 활동 반경의 중심
        private float      _leashRadius   = 0f;           // m, 플레이어가 이 안에 들어서면 쫓기 시작한다. 0이면 처음부터 쫓는다
        private bool       _isChasing     = false;        // 한 번 쫓기 시작하면 계속 쫓는다
        private int        _hitsLeft      = 0;
        private float      _deathElapsed  = 0f;
        private bool       _isDying       = false;
        private bool       _isAshing      = false;

        private DevHitFeedbackPlayer _player = null;
        private float _attackWindup   = 0f;
        private float _attackCooldown = 0f;
        private float _nextAttackTime = 0f;
        private float _attackHitTime  = -1f; // 선딜이 끝나 맞히는 시각, 공격 중이 아니면 음수
        private float _dashEndTime    = -1f;
        private bool  _hasAttackTrigger = false;
        private bool  _hasDashingBool   = false;

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
            bool isLagged = _reactor.Tick();
            if (_animator != null)
            {
                _animator.speed = isLagged ? 0f : 1f;
            }

            if (isLagged)
            {
                return;
            }

            if (_isDying)
            {
                UpdateDeath();
                return;
            }

            UpdateDash();
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

        /// <summary> 죽기까지 맞아야 하는 횟수를 정하고 연출을 준비한다. animator가 있으면 걷는 동안 이동 애니메이션을 재생한다. playerCollider를 쫓아가며, 죽어 날아갈 때는 부딪히지 않는다 </summary>
        public void Init(int hitsToDie, Collider playerCollider, float chaseSpeed, Animator animator, float attackWindup, float attackCooldown)
        {
            _attackWindup   = attackWindup;
            _attackCooldown = attackCooldown;
            _nextAttackTime = Time.time + attackCooldown;

            if (playerCollider != null)
            {
                playerCollider.TryGetComponent(out _player);
            }

            if (animator != null)
            {
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    _hasAttackTrigger |= (parameter.nameHash == s_attackHash);
                    _hasDashingBool   |= (parameter.nameHash == s_dashingHash);
                }
            }

            _animator   = animator;
            _chaseSpeed = chaseSpeed;
            _target     = (playerCollider != null) ? playerCollider.transform : null;
            _baseScale  = transform.localScale;
            _hitsLeft   = hitsToDie;
            _playerCollider = playerCollider;

            // 캡슐 기본 반지름 0.5m에 루트 스케일이 곱해진 것이 몬스터의 실제 몸통 반지름이다
            float bodyRadius   = transform.localScale.x * HALF;
            float playerRadius = (playerCollider != null) ? playerCollider.bounds.extents.x : 0f;

            _separationDistance = (bodyRadius * 2f) + BODY_GAP;
            _playerMinDistance  = bodyRadius + playerRadius + BODY_GAP;
            _chaseStopDistance  = _playerMinDistance + CHASE_STOP_EXTRA;

            _reactor    = new HitReactor(gameObject, (animator != null) ? s_modelColor : s_normalColor, s_corpseColor);

            _reactor.Launched += IgnoreRagdollCollisions;

            // 날아가는 시체가 플레이어를 밀거나 막지 않게 한다
            Collider ownCollider = GetComponent<Collider>();
            if ((playerCollider != null) && (ownCollider != null))
            {
                Physics.IgnoreCollision(ownCollider, playerCollider);
            }
        }

        /// <summary> center에서 수평 radius 안에 플레이어가 들어서면 쫓기 시작한다. 한 번 쫓기 시작하면 플레이어가 밖으로 나가도 계속 쫓는다. radius가 0이면 처음부터 쫓는다 </summary>
        public void SetLeash(Vector3 center, float radius)
        {
            _leashCenter = center;
            _leashRadius = radius;
        }

        // 플레이어가 반경 안에 들어선 순간 추적을 시작하고, 이후에는 반경과 상관없이 쫓는다
        private bool IsChasing()
        {
            if (_isChasing)
            {
                return true;
            }

            if (_leashRadius > 0f)
            {
                Vector3 fromCenter = _target.position - _leashCenter;
                fromCenter.y = 0f;
                if (fromCenter.sqrMagnitude > (_leashRadius * _leashRadius))
                {
                    return false;
                }
            }

            _isChasing = true;
            return true;
        }

        /// <summary> 공격자 위치 쪽에서 profile의 타격으로 한 대 맞는다(치명타면 더 세게 밀리고 찌그러진다). 맞은 횟수가 다 차면 날아가며 죽는다. 맞은 것으로 쳤으면 true, 이미 죽는 중이면 false </summary>
        public bool TakeHit(Vector3 attackerPosition, HitFeedbackProfile profile, bool isCritical, out bool isKill)
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

            _attackHitTime = -1f; // 맞으면 휘두르던 공격은 끊긴다
            _knockVelocity = away * (profile.KnockbackSpeed * (isCritical ? profile.CritKnockbackMultiplier : 1f));
            _knockDrag     = profile.KnockbackDrag;
            _reactor.React(profile, isCritical);
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

            // 날아가는 동안 걷는 자세로 굳어 있게 애니메이션을 멈춘다
            if (_animator != null)
            {
                _animator.enabled = false;
            }

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

            DevHitFeedbackMonster[] monsters = FindObjectsByType<DevHitFeedbackMonster>();
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

        // 래그돌 뼈가 켜지는 순간, 플레이어와 다른 몬스터(산 것, 이미 날아간 사체의 뼈)와는 부딪히지 않게 한다. 바닥과는 부딪힌다
        private void IgnoreRagdollCollisions()
        {
            DevHitFeedbackMonster[] monsters = FindObjectsByType<DevHitFeedbackMonster>();
            foreach (Collider bone in _reactor.RagdollColliders)
            {
                if (_playerCollider != null)
                {
                    Physics.IgnoreCollision(bone, _playerCollider);
                }

                for (int i = 0; i < monsters.Length; ++i)
                {
                    DevHitFeedbackMonster other = monsters[i];
                    if (other == this)
                    {
                        continue;
                    }

                    Collider otherRoot = other.GetComponent<Collider>();
                    if ((otherRoot != null) && (otherRoot.enabled))
                    {
                        Physics.IgnoreCollision(bone, otherRoot);
                    }

                    foreach (Collider otherBone in other._reactor.RagdollColliders)
                    {
                        if (otherBone.enabled)
                        {
                            Physics.IgnoreCollision(bone, otherBone);
                        }
                    }
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
            bool    isWalking = false;
            bool    playerAlive = (_player == null) || (!_player.IsDead);

            if ((_target != null) && (playerAlive) && (IsChasing()) && (_knockVelocity.sqrMagnitude <= STUN_SPEED_SQR))
            {
                Vector3 toTarget = _target.position - position;
                toTarget.y = 0f;

                float distance = toTarget.magnitude;
                bool  isAttacking = _attackHitTime >= 0f;
                if ((distance > _chaseStopDistance) && (!isAttacking))
                {
                    float step = Mathf.Min(_chaseSpeed * Time.deltaTime, distance - _chaseStopDistance);
                    position  += (toTarget / distance) * step;
                    isWalking  = true;
                }

                UpdateAttack(distance);

                if ((distance > MIN_DIRECTION_SQR) && (_attackHitTime < 0f))
                {
                    Quaternion look = Quaternion.LookRotation(toTarget / distance, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, look, TURN_SPEED * Time.deltaTime);
                }
            }

            if (_animator != null)
            {
                _animator.SetBool(s_movingHash, isWalking);
            }

            position += ReadSeparation(position);

            // 뒤에서 밀려도 플레이어 몸속으로는 못 들어온다. 마지막에 적용해 이 거리가 우선한다
            if (_target != null)
            {
                Vector3 fromTarget = position - _target.position;
                fromTarget.y = 0f;

                float sqr = fromTarget.sqrMagnitude;
                if ((sqr < (_playerMinDistance * _playerMinDistance)) && (sqr > MIN_DIRECTION_SQR))
                {
                    position += fromTarget.normalized * (_playerMinDistance - Mathf.Sqrt(sqr));
                }
            }

            position.y = FollowGround(position);
            transform.position = position;
        }

        // 닿는 거리 안에 섰고 쉴 시간이 지났으면 공격을 시작하고, 선딜이 끝난 순간 아직 닿아 있으면 플레이어를 맞힌다
        private void UpdateAttack(float distance)
        {
            float reach = _chaseStopDistance + ATTACK_REACH_EXTRA;

            if (_attackHitTime < 0f)
            {
                if ((distance <= reach) && (Time.time >= _nextAttackTime))
                {
                    _attackHitTime = Time.time + _attackWindup;
                    if ((_animator != null) && (_hasAttackTrigger))
                    {
                        _animator.SetTrigger(s_attackHash);
                    }
                }

                return;
            }

            if (Time.time < _attackHitTime)
            {
                return;
            }

            _attackHitTime  = -1f;
            _nextAttackTime = Time.time + _attackCooldown;

            if ((_player != null) && (distance <= reach))
            {
                _player.TakeHit(transform.position);
            }

            // 돌진형 모델은 공격 자세 뒤 달리기 자세로 넘어가야 공격 상태에서 빠져나온다
            if ((_animator != null) && (_hasDashingBool))
            {
                _animator.SetBool(s_dashingHash, true);
                _dashEndTime = Time.time + ATTACK_DASH_SECONDS;
            }
        }

        private void UpdateDash()
        {
            if ((_dashEndTime < 0f) || (Time.time < _dashEndTime))
            {
                return;
            }

            _dashEndTime = -1f;
            if (_animator != null)
            {
                _animator.SetBool(s_dashingHash, false);
            }
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
                if (sqr >= (_separationDistance * _separationDistance))
                {
                    continue;
                }

                // 정확히 겹쳐 방향이 없으면 아무 쪽으로나 민다
                if (sqr <= MIN_DIRECTION_SQR)
                {
                    Vector2 random = UnityEngine.Random.insideUnitCircle.normalized;
                    push += new Vector3(random.x, 0f, random.y) * (_separationDistance * HALF);
                    continue;
                }

                float distance = Mathf.Sqrt(sqr);
                push += (away / distance) * ((_separationDistance - distance) * HALF);
            }

            return push;
        }

        // 몸 안에서 아래로 쏘면 자기 충돌체는 맞지 않는다. 캡슐 높이의 절반이 곧 스케일 y다.
        // 다른 몬스터(산 것과 사체)와 플레이어는 땅이 아니므로 건너뛴다. 안 그러면 머리 위로 올라타 쌓인다
        private float FollowGround(Vector3 position)
        {
            Vector3 origin = position + (Vector3.up * GROUND_PROBE_UP);
            int     count  = Physics.RaycastNonAlloc(new Ray(origin, Vector3.down), s_groundHits, GROUND_PROBE_DISTANCE,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            float groundY = 0f;
            bool  found   = false;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = s_groundHits[i];
                if ((hit.distance >= nearest) || (hit.collider.GetComponentInParent<DevHitFeedbackMonster>() != null)
                    || (hit.collider.GetComponentInParent<DevHitFeedbackPlayer>() != null))
                {
                    continue;
                }

                nearest = hit.distance;
                groundY = hit.point.y;
                found   = true;
            }

            if (!found)
            {
                return position.y;
            }

            float targetY = groundY + _baseScale.y;
            return Mathf.Lerp(position.y, targetY, Mathf.Clamp01(GROUND_FOLLOW_SPEED * Time.deltaTime));
        }

        // 날아가 쓰러진 채 한동안 남아 있다가 줄어들며 사라진다
        private void UpdateDeath()
        {
            _deathElapsed += Time.deltaTime;

            // 래그돌은 뼈가 월드에서 움직이므로 루트 스케일로 줄이지 않는다. 시체 시간이 끝나면 재처럼 타들어 가며 사라지고, 입자가 흩어진 뒤 지운다
            if (_reactor.HasRagdoll)
            {
                if ((!_isAshing) && (_deathElapsed >= CORPSE_SECONDS))
                {
                    _isAshing = true;
                    HitAshDissolve.Begin(gameObject, ASH_SECONDS);
                }

                if (_deathElapsed >= (CORPSE_SECONDS + ASH_SECONDS + ASH_LINGER_SECONDS))
                {
                    Destroy(gameObject);
                }

                return;
            }

            float shrink = Mathf.Clamp01(1f - ((_deathElapsed - CORPSE_SECONDS) / DEATH_SECONDS));
            transform.localScale = _baseScale * shrink;

            if (shrink <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }
}
