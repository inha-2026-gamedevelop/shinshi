// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 정해진 횟수만 맞으면 죽는 시험용 몬스터. 플레이어를 쫓아오며 서로 겹치지 않고, 맞으면 번쩍이고 밀리고 찌그러지며, 죽으면 물리로 날아간 뒤 줄어들며 사라진다 </summary>
    public sealed class DevHitFeedbackMonster : MonoBehaviour
    {
        // 판정과 경로 탐색은 없다. 플레이어 쪽으로 직선으로 걷고, 나무나 바위는 피하지 않는다.
        // 피격 연출 확인용 표적이라 체력 대신 맞은 횟수로 죽는다.
        // 렉프레임은 전역 timeScale을 건드리지 않고 이 몬스터의 Update만 건너뛰어 만든다. 멀티에서 남의 화면과 다른 몬스터는 그대로 흐른다

        /****************************************
        *                Fields
        ****************************************/

        private const string COLOR_PROPERTY = "_BaseColor";

        private const float FLASH_SECONDS = 0.12f;
        private const float DEATH_SECONDS = 0.25f;

        // 찌그러짐. 맞은 순간 가장 눌리고, 감쇠하면서 몇 번 되튄다
        private const float PUNCH_SECONDS   = 0.25f;
        private const float PUNCH_AMOUNT    = 0.25f;
        private const float PUNCH_CYCLES    = 2f;
        private const float PUNCH_XZ_RATIO  = 0.5f; // 높이가 눌린 만큼 옆으로 퍼지는 비율
        private const float FULL_TURN       = Mathf.PI * 2f;

        // 넉백. 맞은 순간의 속도가 지수로 줄어든다
        private const float KNOCKBACK_SPEED = 4f;  // m/s
        private const float KNOCKBACK_DRAG  = 12f; // 1/s
        private const float KNOCKBACK_STOP_SQR = 0.0001f;

        // 강공격에 맞으면 훨씬 멀리 밀려난다. 속도는 크게, 감속은 느리게 해서 3m 가까이 미끄러진다
        private const float HEAVY_KNOCKBACK_SPEED = 16f; // m/s
        private const float HEAVY_KNOCKBACK_DRAG  = 5f;  // 1/s
        private const float HEAVY_FLY_MULTIPLIER  = 1.8f;

        // 추격. 맞아 밀려나는 동안은 걷지 않고, 몬스터끼리는 몸통이 겹치지 않게 서로 민다
        private const float CHASE_STOP_DISTANCE   = 1.2f;  // m, 플레이어와 이만큼 남기고 선다
        private const float SEPARATION_DISTANCE   = 0.9f;  // m, 몬스터 중심끼리 이보다 가까우면 벌린다
        private const float PLAYER_MIN_DISTANCE   = 0.9f;  // m, 플레이어 몸통 반지름과 몬스터 반지름의 합에 여유를 더한 값
        private const float STUN_SPEED_SQR       = 1f;    // 넉백 속도가 이 값(제곱) 아래로 식어야 다시 걷는다
        private const float GROUND_PROBE_UP       = 0.5f;  // m, 몸 중심 위 이 높이에서 아래로 땅을 찾는다
        private const float GROUND_PROBE_DISTANCE = 4f;    // m
        private const float GROUND_FOLLOW_SPEED   = 20f;   // 1/s

        // 렉프레임 동안 맞은 몸이 떠는 크기
        private const float VIBRATE_AMOUNT = 0.05f; // m

        // 처치 시 날아가기
        private const float DEATH_FLY_SECONDS   = 0.8f;
        private const float DEATH_FLY_SPEED     = 8f;  // m/s, 수평
        private const float DEATH_FLY_UP_SPEED  = 5f;  // m/s
        private const float DEATH_SPIN_SPEED    = 14f; // rad/s
        private const float DEATH_BODY_DRAG     = 0.3f;
        private const float DEATH_FLASH_SECONDS = 0.2f;

        private const float MIN_DIRECTION_SQR = 0.0001f;
        private const float HALF              = 0.5f;

        private static readonly Color s_normalColor = new Color(0.55f, 0.2f, 0.2f, 1f);
        private static readonly Color s_flashColor  = Color.white;

        // 살아서 걷는 몬스터. 서로 겹침을 풀 때 훑는다
        private static readonly List<DevHitFeedbackMonster> s_alive = new List<DevHitFeedbackMonster>();

        private Transform             _target        = null;
        private Renderer              _renderer      = null;
        private Collider              _collider      = null;
        private Rigidbody             _body          = null;
        private MaterialPropertyBlock _block         = null;
        private Vector3               _baseScale     = Vector3.one;
        private Vector3               _knockVelocity = Vector3.zero;
        private Vector3               _vibrateOffset = Vector3.zero;
        private float                 _chaseSpeed    = 0f;
        private float                 _knockDrag     = KNOCKBACK_DRAG;
        private int                   _vibrateFrames = 0;
        private bool                  _launchPending = false;
        private Vector3               _launchAway    = Vector3.zero;
        private float                 _launchPower   = 1f;
        private int                   _hitsLeft      = 0;
        private float                 _flashLeft     = 0f;
        private float                 _flashSeconds  = FLASH_SECONDS;
        private float                 _punchLeft     = 0f;
        private float                 _deathElapsed  = 0f;
        private bool                  _isDying       = false;

        /// <summary> 죽는 연출이 끝나 사라지기 직전에 한 번 알린다 </summary>
        public event Action<DevHitFeedbackMonster> Died;

        /// <summary> 죽는 연출 중이거나 이미 죽었는지 </summary>
        public bool IsDead => _isDying;

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
            if (UpdateVibrate())
            {
                return;
            }

            if (_launchPending)
            {
                Launch();
            }

            UpdateFlash();

            if (_isDying)
            {
                UpdateDeath();
                return;
            }

            UpdateKnockback();
            UpdateChase();
            UpdatePunch();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 몬스터 렌더러를 잡고 죽기까지 맞아야 하는 횟수를 정한다. playerCollider를 쫓아가며, 죽어 날아갈 때는 부딪히지 않는다 </summary>
        public void Init(int hitsToDie, Collider playerCollider, float chaseSpeed)
        {
            _chaseSpeed = chaseSpeed;
            _target     = (playerCollider != null) ? playerCollider.transform : null;

            _renderer  = GetComponentInChildren<Renderer>();
            _collider  = GetComponent<Collider>();
            _block     = new MaterialPropertyBlock();
            _baseScale = transform.localScale;
            _hitsLeft  = hitsToDie;
            ApplyColor(s_normalColor);

            // 날아가는 시체가 플레이어를 밀거나 막지 않게 한다
            if ((playerCollider != null) && (_collider != null))
            {
                Physics.IgnoreCollision(_collider, playerCollider);
            }
        }

        /// <summary> 렉프레임 동안 몸을 떨게 한다. 화면이 멈춘 동안에도 프레임마다 흔들려 맞은 몸이 부들거려 보인다 </summary>
        public void Vibrate(int frames)
        {
            _vibrateFrames = Mathf.Max(_vibrateFrames, frames);
        }

        /// <summary> 공격자 위치 쪽에서 한 대 맞는다. 강공격이면 훨씬 멀리 날아간다. 맞은 횟수가 다 차면 날아가며 죽는다. 맞은 것으로 쳤으면 true, 이미 죽는 중이면 false </summary>
        public bool TakeHit(Vector3 attackerPosition, bool isHeavy, out bool isKill)
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
                StartDeath(away, isHeavy);
                return true;
            }

            _knockVelocity = away * (isHeavy ? HEAVY_KNOCKBACK_SPEED : KNOCKBACK_SPEED);
            _knockDrag     = isHeavy ? HEAVY_KNOCKBACK_DRAG : KNOCKBACK_DRAG;
            _punchLeft     = PUNCH_SECONDS;
            StartFlash(FLASH_SECONDS);
            return true;
        }

        private void StartDeath(Vector3 away, bool isHeavy)
        {
            _isDying = true;
            s_alive.Remove(this);
            transform.localScale = _baseScale;
            StartFlash(DEATH_FLASH_SECONDS);

            // 렉프레임이 풀린 뒤에 날아가도록 물리는 Update에서 붙인다
            _launchPending = true;
            _launchAway    = away;
            _launchPower   = isHeavy ? HEAVY_FLY_MULTIPLIER : 1f;
        }

        private void Launch()
        {
            _launchPending = false;

            _body               = gameObject.AddComponent<Rigidbody>();
            _body.linearDamping = DEATH_BODY_DRAG;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.AddForce(((_launchAway * DEATH_FLY_SPEED) + (Vector3.up * DEATH_FLY_UP_SPEED)) * _launchPower,
                ForceMode.VelocityChange);
            _body.AddTorque(UnityEngine.Random.onUnitSphere * DEATH_SPIN_SPEED, ForceMode.VelocityChange);
        }

        private void StartFlash(float seconds)
        {
            _flashSeconds = seconds;
            _flashLeft    = seconds;
            ApplyColor(s_flashColor);
        }

        // 흰색에서 원래 색으로 식어 가며 번쩍임이 남는 시간을 만든다
        private void UpdateFlash()
        {
            if (_flashLeft <= 0f)
            {
                return;
            }

            _flashLeft = Mathf.Max(0f, _flashLeft - Time.deltaTime);
            ApplyColor(Color.Lerp(s_normalColor, s_flashColor, _flashLeft / _flashSeconds));
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

        // 프레임마다 직전 떨림을 되돌리고 새로 떨어, 끝나면 원래 자리로 돌아온다
        // 이번 프레임이 렉프레임이라 떨었으면 true
        private bool UpdateVibrate()
        {
            transform.position -= _vibrateOffset;
            _vibrateOffset      = Vector3.zero;

            if (_vibrateFrames <= 0)
            {
                return false;
            }

            --_vibrateFrames;
            Vector2 shake = UnityEngine.Random.insideUnitCircle * VIBRATE_AMOUNT;
            _vibrateOffset      = new Vector3(shake.x, 0f, shake.y);
            transform.position += _vibrateOffset;
            return true;
        }

        // 높이가 눌렸다 되튀는 감쇠 진동. 눌린 만큼 옆으로 퍼져 부피가 비슷해 보이게 한다
        private void UpdatePunch()
        {
            if (_punchLeft <= 0f)
            {
                return;
            }

            _punchLeft = Mathf.Max(0f, _punchLeft - Time.deltaTime);

            float remain  = _punchLeft / PUNCH_SECONDS;
            float phase   = (1f - remain) * PUNCH_CYCLES * FULL_TURN;
            float squash  = PUNCH_AMOUNT * remain * Mathf.Cos(phase);
            float spread  = 1f + (squash * PUNCH_XZ_RATIO);

            transform.localScale = new Vector3(_baseScale.x * spread, _baseScale.y * (1f - squash), _baseScale.z * spread);
        }

        // 한참 날아가다 줄어들며 사라진다
        private void UpdateDeath()
        {
            _deathElapsed += Time.deltaTime;

            float shrink = Mathf.Clamp01(1f - ((_deathElapsed - DEATH_FLY_SECONDS) / DEATH_SECONDS));
            transform.localScale = _baseScale * shrink;

            if (shrink <= 0f)
            {
                Died?.Invoke(this);
                Destroy(gameObject);
            }
        }

        private void ApplyColor(Color color)
        {
            _block.SetColor(COLOR_PROPERTY, color);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
