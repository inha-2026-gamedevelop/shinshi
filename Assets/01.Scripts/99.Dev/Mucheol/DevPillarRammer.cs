#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Dev
{
    /// <summary>
    /// 무철 보스 시험 씬에서 캡슐을 직접 몰아 석주에 부딪히면 부서지는 연출을 확인한다.
    /// 키: WASD 이동, Left Shift 가속, R 남은 석주 복구와 제자리 복귀
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class DevPillarRammer : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string AXIS_HORIZONTAL = "Horizontal";
        private const string AXIS_VERTICAL   = "Vertical";
        private const KeyCode KEY_SPRINT     = KeyCode.LeftShift;
        private const KeyCode KEY_RESET      = KeyCode.R;

        private const float GRAVITY            = -20f; // m/s^2
        private const float GROUND_STICK_SPEED = -2f;  // m/s
        private const float TURN_SPEED_DEGREES = 720f; // 도/초
        private const float FALL_RESET_DEPTH   = 30f;  // m
        private const float MIN_INPUT_SQR      = 0.01f;

        [Header("이동")]
        [SerializeField] private float _walkSpeed    = 6f;
        [SerializeField] private float _sprintSpeed  = 16f;
        [SerializeField] private float _acceleration = 30f;

        [Header("연결")]
        [SerializeField] private Transform             _viewCamera;
        [SerializeField] private MucheolPillarShatter[] _pillars;

        private CharacterController _controller;
        private Collider            _lastHitCollider;
        private Vector3             _spawnPosition;
        private Quaternion          _spawnRotation;
        private float               _speed;
        private float               _verticalSpeed;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _controller    = GetComponent<CharacterController>();
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KEY_RESET))
            {
                ResetAll();
            }

            Vector3 direction = ReadMoveDirection();
            bool    isMoving  = direction.sqrMagnitude > MIN_INPUT_SQR;
            float   target    = !isMoving ? 0f : (Input.GetKey(KEY_SPRINT) ? _sprintSpeed : _walkSpeed);
            _speed = Mathf.MoveTowards(_speed, target, _acceleration * Time.deltaTime);

            _verticalSpeed = _controller.isGrounded ? GROUND_STICK_SPEED : _verticalSpeed + (GRAVITY * Time.deltaTime);
            _controller.Move((direction * (_speed * Time.deltaTime)) + (Vector3.up * (_verticalSpeed * Time.deltaTime)));

            if (isMoving)
            {
                Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, TURN_SPEED_DEGREES * Time.deltaTime);
            }

            if (transform.position.y < _spawnPosition.y - FALL_RESET_DEPTH)
            {
                Respawn();
            }
        }

        // 석주의 온전한 충돌체에 닿는 순간 한 번만 부순다. 부서진 뒤에는 충돌체가 꺼져 더는 호출되지 않는다
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.collider == _lastHitCollider)
            {
                return;
            }

            _lastHitCollider = hit.collider;

            MucheolPillarShatter pillar = hit.collider.GetComponentInParent<MucheolPillarShatter>();
            if (pillar != null)
            {
                pillar.Shatter(hit.point);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private Vector3 ReadMoveDirection()
        {
            Vector3 input = new Vector3(Input.GetAxisRaw(AXIS_HORIZONTAL), 0f, Input.GetAxisRaw(AXIS_VERTICAL));
            if (input.sqrMagnitude < MIN_INPUT_SQR)
            {
                return Vector3.zero;
            }

            // 카메라가 보는 방향을 기준으로 위가 앞이 되게 한다
            Quaternion yaw = _viewCamera != null ? Quaternion.Euler(0f, _viewCamera.eulerAngles.y, 0f) : Quaternion.identity;
            return (yaw * input).normalized;
        }

        private void ResetAll()
        {
            // 다 사라져 지워진 석주는 되살릴 수 없으므로 남아 있는 것만 복구한다
            for (int i = 0; i < _pillars.Length; ++i)
            {
                if (_pillars[i] != null)
                {
                    _pillars[i].ResetPillar();
                }
            }

            _lastHitCollider = null;
            Respawn();
        }

        private void Respawn()
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _controller.enabled = true;
            _speed         = 0f;
            _verticalSpeed = 0f;
        }
    }
}
#endif
