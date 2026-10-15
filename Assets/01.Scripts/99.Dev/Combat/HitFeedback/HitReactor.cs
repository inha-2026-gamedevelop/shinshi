// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 맞은 몸에 보이는 연출(번쩍임, 찌그러짐, 렉프레임 떨림, 처치 시 날아가기)을 맡는 순수 표현 클래스 </summary>
    public sealed class HitReactor : IHitLagTarget
    {
        // 위치나 체력 같은 판정은 건드리지 않고 렌더러와 이 오브젝트의 표현만 바꾼다. 소유자가 매 프레임 Tick을 부른다.
        // 시험 씬은 몬스터 루트가 곧 몸이라 루트를 직접 다루지만, 실제 적은 서버가 위치를 권위로 가지므로
        // 이 클래스를 모델 자식 오브젝트에 연결해야 서버 위치와 어긋나지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const string COLOR_PROPERTY = "_BaseColor";

        // 찌그러짐. 맞은 순간 가장 눌리고, 감쇠하면서 몇 번 되튄다
        private const float PUNCH_SECONDS  = 0.25f;
        private const float PUNCH_CYCLES   = 2f;
        private const float PUNCH_XZ_RATIO = 0.5f; // 높이가 눌린 만큼 옆으로 퍼지는 비율
        private const float FULL_TURN      = Mathf.PI * 2f;

        // 처치 시 날아가기
        private const float DEATH_FLASH_SECONDS = 0.2f;
        private const float DEATH_FLY_SPEED     = 10f; // m/s, 수평
        private const float DEATH_FLY_UP_SPEED  = 6f;  // m/s
        private const float DEATH_SPIN_SPEED    = 22f; // rad/s
        private const float DEATH_BODY_DRAG     = 0.2f;
        private const float DEATH_BOUNCINESS    = 0.45f; // 땅에 떨어지면 몇 번 튄다

        private static readonly Color s_flashColor = Color.white;

        private readonly GameObject            _owner;
        private readonly Transform             _transform;
        private readonly Renderer              _renderer;
        private readonly Collider              _collider;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly Color                 _normalColor;
        private readonly Color                 _corpseColor;
        private readonly Vector3               _baseScale;

        private Rigidbody _body          = null;
        private Vector3   _vibrateOffset = Vector3.zero;
        private Vector3   _launchAway    = Vector3.zero;
        private float     _launchPower   = 1f;
        private float     _vibrateAmount = 0f;
        private float     _flashLeft     = 0f;
        private float     _flashSeconds  = 1f;
        private float     _punchLeft     = 0f;
        private float     _punchAmount   = 0f;
        private int       _vibrateFrames = 0;
        private bool      _launchPending = false;
        private bool      _isDead        = false;

        /// <summary> 처치 뒤 날아가는 동안 붙는 리지드바디. 날아가기 전에는 null이다 </summary>
        public Rigidbody Body => _body;

        /****************************************
        *               Constructor
        ****************************************/

        /// <summary> owner의 렌더러에 색과 찌그러짐을 입힌다. normalColor는 평소 색, corpseColor는 죽은 뒤 색이다 </summary>
        public HitReactor(GameObject owner, Color normalColor, Color corpseColor)
        {
            _owner       = owner;
            _transform   = owner.transform;
            _renderer    = owner.GetComponentInChildren<Renderer>();
            _collider    = owner.GetComponent<Collider>();
            _normalColor = normalColor;
            _corpseColor = corpseColor;
            _baseScale   = _transform.localScale;

            ApplyColor(_normalColor);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <inheritdoc/>
        public void Freeze(int frames)
        {
            _vibrateFrames = Mathf.Max(_vibrateFrames, frames);
        }

        /// <summary> 맞은 순간의 번쩍임과 찌그러짐을 시작한다. 렉프레임 동안 떠는 크기도 이때 프로필에서 받아 둔다 </summary>
        public void React(HitFeedbackProfile profile)
        {
            _vibrateAmount = profile.VibrateAmount;
            _punchAmount   = profile.PunchAmount;
            _punchLeft   = PUNCH_SECONDS;
            StartFlash(profile.FlashSeconds);
        }

        /// <summary> 처치된 몸을 away 쪽으로 날릴 준비를 한다. 렉프레임이 풀린 뒤에 실제로 날아간다 </summary>
        public void Die(Vector3 away, HitFeedbackProfile profile)
        {
            _isDead        = true;
            _vibrateAmount = profile.VibrateAmount;
            _transform.localScale = _baseScale;
            StartFlash(DEATH_FLASH_SECONDS);

            _launchPending = true;
            _launchAway    = away;
            _launchPower   = profile.FlyMultiplier;
        }

        /// <summary> 매 프레임 소유자 Update 맨 앞에서 부른다. 이번 프레임이 렉프레임이라 소유자가 나머지를 건너뛰어야 하면 true </summary>
        public bool Tick()
        {
            if (UpdateVibrate())
            {
                return true;
            }

            if (_launchPending)
            {
                Launch();
            }

            UpdateFlash();
            UpdatePunch();
            return false;
        }

        private void StartFlash(float seconds)
        {
            _flashSeconds = seconds;
            _flashLeft    = seconds;
            ApplyColor(s_flashColor);
        }

        // 흰색에서 평소 색(죽었으면 시체 색)으로 식어 가며 번쩍임이 남는 시간을 만든다
        private void UpdateFlash()
        {
            if (_flashLeft <= 0f)
            {
                return;
            }

            _flashLeft = Mathf.Max(0f, _flashLeft - Time.deltaTime);

            Color settled = _isDead ? _corpseColor : _normalColor;
            ApplyColor(Color.Lerp(settled, s_flashColor, _flashLeft / _flashSeconds));
        }

        // 높이가 눌렸다 되튀는 감쇠 진동. 눌린 만큼 옆으로 퍼져 부피가 비슷해 보이게 한다
        private void UpdatePunch()
        {
            if ((_punchLeft <= 0f) || (_isDead))
            {
                return;
            }

            _punchLeft = Mathf.Max(0f, _punchLeft - Time.deltaTime);

            float remain = _punchLeft / PUNCH_SECONDS;
            float phase  = (1f - remain) * PUNCH_CYCLES * FULL_TURN;
            float squash = _punchAmount * remain * Mathf.Cos(phase);
            float spread = 1f + (squash * PUNCH_XZ_RATIO);

            _transform.localScale = new Vector3(_baseScale.x * spread, _baseScale.y * (1f - squash), _baseScale.z * spread);
        }

        // 프레임마다 직전 떨림을 되돌리고 새로 떨어, 끝나면 원래 자리로 돌아온다. 이번 프레임이 렉프레임이면 true
        private bool UpdateVibrate()
        {
            _transform.position -= _vibrateOffset;
            _vibrateOffset       = Vector3.zero;

            if (_vibrateFrames <= 0)
            {
                return false;
            }

            --_vibrateFrames;
            Vector2 shake = Random.insideUnitCircle * _vibrateAmount;
            _vibrateOffset       = new Vector3(shake.x, 0f, shake.y);
            _transform.position += _vibrateOffset;
            return true;
        }

        private void Launch()
        {
            _launchPending = false;

            // 튀어 오르도록 탄성이 있는 재질을 입힌다. 시험용이라 몬스터마다 만들어 쓴다
            if (_collider != null)
            {
                _collider.sharedMaterial = new PhysicsMaterial
                {
                    bounciness    = DEATH_BOUNCINESS,
                    bounceCombine = PhysicsMaterialCombine.Maximum
                };
            }

            _body               = _owner.AddComponent<Rigidbody>();
            _body.linearDamping = DEATH_BODY_DRAG;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.AddForce(((_launchAway * DEATH_FLY_SPEED) + (Vector3.up * DEATH_FLY_UP_SPEED)) * _launchPower,
                ForceMode.VelocityChange);
            _body.AddTorque(Random.onUnitSphere * DEATH_SPIN_SPEED, ForceMode.VelocityChange);
        }

        private void ApplyColor(Color color)
        {
            _block.SetColor(COLOR_PROPERTY, color);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
