// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 맞은 몸에 보이는 연출(번쩍임, 찌그러짐, 렉프레임 떨림, 처치 시 날아가기)을 맡는 순수 표현 클래스 </summary>
    public sealed class HitReactor : IHitLagTarget
    {
        // 위치나 체력 같은 판정은 건드리지 않고 렌더러와 이 오브젝트의 표현만 바꾼다. 소유자가 매 프레임 Tick을 부른다.
        // 렉프레임 떨림은 모델(visual)만 움직여 충돌체와 판정 위치를 그대로 둔다. 실제 적은 서버가 루트 위치를
        // 권위로 가지므로 이 방식이어야 옮겨 쓸 수 있다. 모델 없는 시험용 캡슐만 루트를 떤다.
        // 찌그러짐은 아직 루트 크기를 바꾼다

        /****************************************
        *                Fields
        ****************************************/

        private const string COLOR_PROPERTY = "_BaseColor";

        // 찌그러짐. 맞은 순간 가장 눌리고, 감쇠하면서 몇 번 되튄다
        private const float PUNCH_SECONDS  = 0.25f;
        private const float PUNCH_CYCLES   = 2f;
        private const float PUNCH_XZ_RATIO = 0.5f; // 높이가 눌린 만큼 옆으로 퍼지는 비율
        private const float FULL_TURN      = Mathf.PI * 2f;

        private const float MIN_AXIS_SQR = 0.0001f;

        // 처치 시 날아가기
        private const float DEATH_FLASH_SECONDS = 0.2f;
        private const float DEATH_FLY_SPEED     = 10f; // m/s, 수평
        private const float DEATH_FLY_UP_SPEED  = 6f;  // m/s
        private const float DEATH_SPIN_SPEED    = 22f; // rad/s
        private const float DEATH_BODY_DRAG     = 0.2f;
        private const float DEATH_BOUNCINESS    = 0.45f; // 땅에 떨어지면 몇 번 튄다
        private const float RAGDOLL_SPIN_SPEED  = 6f;    // rad/s, 래그돌 뼈마다 주는 회전

        private static readonly Color s_normalFlashColor = Color.white;
        private static readonly Color s_critFlashColor   = new Color(1f, 0.8f, 0.2f, 1f);

        private readonly GameObject            _owner;
        private readonly Transform             _transform;
        private readonly Transform             _shakeTarget;
        private readonly Renderer              _renderer;
        private readonly Collider              _collider;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly Color                 _normalColor;
        private readonly Color                 _corpseColor;
        private readonly Vector3               _baseScale;
        private readonly Rigidbody[]           _ragdollBodies;
        private readonly Collider[]            _ragdollColliders;

        private Rigidbody _body          = null;
        private Color     _flashColor    = Color.white;
        private Vector3   _vibrateOffset = Vector3.zero; // _shakeTarget의 부모 기준
        private Vector3   _vibrateAxis   = Vector3.right; // 월드 수평, 맞은 방향
        private Vector3   _launchAway    = Vector3.zero;
        private float     _launchPower   = 1f;
        private float     _vibrateAmount = 0f;
        private float     _flashLeft     = 0f;
        private float     _flashSeconds  = 1f;
        private float     _punchLeft     = 0f;
        private float     _punchAmount   = 0f;
        private int       _vibrateFrames = 0;
        private int       _vibrateTotal  = 0;
        private bool      _launchPending = false;
        private bool      _isDead        = false;

        /// <summary> 처치 뒤 날아가는 동안 붙는 리지드바디. 날아가기 전에는 null이다. 래그돌이면 항상 null이다 </summary>
        public Rigidbody Body => _body;

        /// <summary> 모델에 뼈마다 리지드바디와 충돌체(래그돌)가 달려 있는지 </summary>
        public bool HasRagdoll => _ragdollBodies.Length > 0;

        /// <summary> 래그돌 뼈의 충돌체. 날아가기 시작해야 켜진다 </summary>
        public Collider[] RagdollColliders => _ragdollColliders;

        /// <summary> 처치된 몸이 실제로 날아가기 시작한 순간 한 번 알린다 </summary>
        public event System.Action Launched;

        /****************************************
        *               Constructor
        ****************************************/

        /// <summary> owner의 렌더러에 색과 찌그러짐을 입힌다. visual은 렉프레임에 떨 모델이고 비우면 owner를 떤다. normalColor는 평소 색, corpseColor는 죽은 뒤 색이다 </summary>
        public HitReactor(GameObject owner, Transform visual, Color normalColor, Color corpseColor)
        {
            _owner       = owner;
            _transform   = owner.transform;
            _shakeTarget = (visual != null) ? visual : _transform;
            _renderer    = owner.GetComponentInChildren<Renderer>();
            _collider    = owner.GetComponent<Collider>();
            _normalColor = normalColor;
            _corpseColor = corpseColor;
            _baseScale   = _transform.localScale;

            // 래그돌 뼈는 평소에 물리가 꺼져 있다(키네마틱, 충돌체 꺼짐). 루트의 충돌체는 래그돌이 아니므로 뺀다
            _ragdollBodies = owner.GetComponentsInChildren<Rigidbody>(true);
            var colliders  = new System.Collections.Generic.List<Collider>();
            foreach (Collider found in owner.GetComponentsInChildren<Collider>(true))
            {
                if (found.gameObject != owner)
                {
                    colliders.Add(found);
                }
            }

            _ragdollColliders = colliders.ToArray();

            ApplyColor(_normalColor);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <inheritdoc/>
        public void Freeze(int frames)
        {
            // 더 긴 멈춤이 오면 감쇠도 처음부터 다시 센다
            if (frames > _vibrateFrames)
            {
                _vibrateFrames = frames;
                _vibrateTotal  = frames;
            }
        }

        /// <summary> 렉프레임 동안 떠는 크기(m)를 정한다. 카메라 거리로 환산한 값을 React 뒤에 덮어쓸 때 쓴다 </summary>
        public void SetVibrateAmount(float meters) { _vibrateAmount = meters; }

        /// <summary> 맞은 순간의 번쩍임과 찌그러짐을 시작한다. away는 공격자에서 맞은 쪽으로 향하는 수평 방향이고 떨림 축이 된다. 치명타면 금색으로 번쩍이고 더 크게 찌그러진다 </summary>
        public void React(HitFeedbackProfile profile, Vector3 away, bool isCritical)
        {
            _vibrateAmount = profile.VibrateAmount;
            SetVibrateAxis(away);
            _punchAmount   = profile.PunchAmount * (isCritical ? profile.CritPunchMultiplier : 1f);
            _punchLeft   = PUNCH_SECONDS;
            StartFlash(profile.FlashSeconds, isCritical ? s_critFlashColor : s_normalFlashColor);
        }

        /// <summary> 처치된 몸을 away 쪽으로 날릴 준비를 한다. 렉프레임이 풀린 뒤에 실제로 날아간다 </summary>
        public void Die(Vector3 away, HitFeedbackProfile profile)
        {
            _isDead        = true;
            _vibrateAmount = profile.VibrateAmount;
            SetVibrateAxis(away);
            _transform.localScale = _baseScale;
            StartFlash(DEATH_FLASH_SECONDS, s_normalFlashColor);

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

        private void StartFlash(float seconds, Color color)
        {
            _flashSeconds = seconds;
            _flashLeft    = seconds;
            _flashColor   = color;
            ApplyColor(color);
        }

        // 번쩍임 색에서 평소 색(죽었으면 시체 색)으로 식어 가며 번쩍임이 남는 시간을 만든다
        private void UpdateFlash()
        {
            if (_flashLeft <= 0f)
            {
                return;
            }

            _flashLeft = Mathf.Max(0f, _flashLeft - Time.deltaTime);

            Color settled = _isDead ? _corpseColor : _normalColor;
            ApplyColor(Color.Lerp(settled, _flashColor, _flashLeft / _flashSeconds));
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

        private void SetVibrateAxis(Vector3 away)
        {
            away.y = 0f;
            if (away.sqrMagnitude > MIN_AXIS_SQR)
            {
                _vibrateAxis = away.normalized;
            }
        }

        // 프레임마다 직전 떨림을 되돌리고 새로 떨어, 끝나면 원래 자리로 돌아온다. 이번 프레임이 렉프레임이면 true.
        // 맞은 방향 축으로 앞뒤를 번갈아 떨고 멈춤이 끝나 갈수록 줄여, 무작위 떨림보다 어느 쪽에서 맞았는지 읽힌다
        private bool UpdateVibrate()
        {
            _shakeTarget.localPosition -= _vibrateOffset;
            _vibrateOffset              = Vector3.zero;

            if (_vibrateFrames <= 0)
            {
                return false;
            }

            float decay = _vibrateFrames / (float)Mathf.Max(1, _vibrateTotal);
            float side  = ((_vibrateFrames % 2) == 0) ? 1f : -1f;
            --_vibrateFrames;

            Vector3   world  = _vibrateAxis * (_vibrateAmount * decay * side);
            Transform parent = _shakeTarget.parent;
            _vibrateOffset              = (parent != null) ? parent.InverseTransformVector(world) : world;
            _shakeTarget.localPosition += _vibrateOffset;
            return true;
        }

        private void Launch()
        {
            _launchPending = false;

            if (HasRagdoll)
            {
                LaunchRagdoll();
                return;
            }

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
            Launched?.Invoke();
        }

        // 루트 충돌체를 끄고 뼈마다 물리를 켜서, 처치 방향으로 날려 팔다리가 따로 늘어지게 한다
        private void LaunchRagdoll()
        {
            if (_collider != null)
            {
                _collider.enabled = false;
            }

            foreach (Collider bone in _ragdollColliders)
            {
                bone.enabled = true;
            }

            Vector3 velocity = ((_launchAway * DEATH_FLY_SPEED) + (Vector3.up * DEATH_FLY_UP_SPEED)) * _launchPower;
            foreach (Rigidbody bone in _ragdollBodies)
            {
                bone.isKinematic     = false;
                bone.linearDamping   = DEATH_BODY_DRAG;
                bone.linearVelocity  = velocity;
                bone.angularVelocity = Random.onUnitSphere * RAGDOLL_SPIN_SPEED;
            }

            Launched?.Invoke();
        }

        private void ApplyColor(Color color)
        {
            _block.SetColor(COLOR_PROPERTY, color);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
