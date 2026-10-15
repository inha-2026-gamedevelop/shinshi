// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 정해진 횟수만 맞으면 죽는 제자리 시험용 몬스터. 맞으면 번쩍이고 밀리고 찌그러지며, 죽으면 물리로 날아간 뒤 줄어들며 사라진다 </summary>
    public sealed class DevHitFeedbackMonster : MonoBehaviour
    {
        // 판정, 이동, AI는 없다. 피격 연출 확인용 표적이라 체력 대신 맞은 횟수로 죽는다.
        // 연출 시간은 모두 Time.deltaTime으로 센다. 히트스톱으로 timeScale이 내려가면 연출도 같이 멈칫해야 한다

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

        private Renderer              _renderer      = null;
        private Collider              _collider      = null;
        private Rigidbody             _body          = null;
        private MaterialPropertyBlock _block         = null;
        private Vector3               _baseScale     = Vector3.one;
        private Vector3               _knockVelocity = Vector3.zero;
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

        private void Update()
        {
            UpdateFlash();

            if (_isDying)
            {
                UpdateDeath();
                return;
            }

            UpdateKnockback();
            UpdatePunch();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 몬스터 렌더러를 잡고 죽기까지 맞아야 하는 횟수를 정한다. ignoredCollider와는 죽어 날아갈 때 부딪히지 않는다 </summary>
        public void Init(int hitsToDie, Collider ignoredCollider)
        {
            _renderer  = GetComponentInChildren<Renderer>();
            _collider  = GetComponent<Collider>();
            _block     = new MaterialPropertyBlock();
            _baseScale = transform.localScale;
            _hitsLeft  = hitsToDie;
            ApplyColor(s_normalColor);

            // 날아가는 시체가 플레이어를 밀거나 막지 않게 한다
            if ((ignoredCollider != null) && (_collider != null))
            {
                Physics.IgnoreCollision(_collider, ignoredCollider);
            }
        }

        /// <summary> 공격자 위치 쪽에서 한 대 맞는다. 맞은 횟수가 다 차면 날아가며 죽는다. 맞은 것으로 쳤으면 true, 이미 죽는 중이면 false </summary>
        public bool TakeHit(Vector3 attackerPosition, out bool isKill)
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
                StartDeath(away);
                return true;
            }

            _knockVelocity = away * KNOCKBACK_SPEED;
            _punchLeft     = PUNCH_SECONDS;
            StartFlash(FLASH_SECONDS);
            return true;
        }

        private void StartDeath(Vector3 away)
        {
            _isDying = true;
            transform.localScale = _baseScale;
            StartFlash(DEATH_FLASH_SECONDS);

            _body              = gameObject.AddComponent<Rigidbody>();
            _body.linearDamping = DEATH_BODY_DRAG;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.AddForce((away * DEATH_FLY_SPEED) + (Vector3.up * DEATH_FLY_UP_SPEED), ForceMode.VelocityChange);
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
            _knockVelocity     *= Mathf.Exp(-KNOCKBACK_DRAG * Time.deltaTime);
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
