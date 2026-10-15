// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Dev
{
    /// <summary> 네트워크 없이 캡슐 하나를 마우스로 움직이고 좌클릭으로 공격해 피격 연출을 확인하는 시험용 플레이어 </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class DevHitFeedbackPlayer : MonoBehaviour
    {
        // 서버, 인증, 내비 메시가 없는 시험 씬용이다. 마우스 버튼 배치는 실제 값(오른쪽 이동, 왼쪽 공격)을 따르고,
        // 이동은 경로 탐색 없이 커서 지면 지점으로 직선으로 걷는다. 공격은 바라보는 부채꼴 안의 충돌체를 모아 Hit 이벤트로 알린다

        /****************************************
        *                Fields
        ****************************************/

        private const string COLOR_PROPERTY = "_BaseColor";

        private const float GRAVITY            = -20f; // m/s^2
        private const float GROUND_STICK_SPEED = -2f;  // m/s, 땅에 붙어 있을 때 내리막에서 뜨지 않게 누르는 속도
        private const float TURN_SPEED_DEGREES = 1080f; // 도/초
        private const float ARRIVE_DISTANCE    = 0.1f;  // m
        private const float FALL_RESET_DEPTH   = 30f;   // m, 시작 높이보다 이만큼 떨어지면 시작 지점으로 되돌린다
        private const float MIN_DIRECTION_SQR  = 0.0001f;
        private const float HALF               = 0.5f;

        private const int RAYCAST_MAX_HITS = 8;
        private const int HIT_BUFFER_SIZE  = 32;

        private const float SLASH_THICKNESS = 0.05f; // m
        private const float SLASH_HEIGHT    = 0.9f;  // m, 몸통 높이에 띄운다

        // 정면 화살표. 바닥 위에 얹은 막대 하나와 양옆으로 꺾인 머리 막대 둘이다
        private const string ARROW_NAME           = "DevFacingArrow";
        private const float  ARROW_HEIGHT         = 0.05f; // m, 바닥에 파묻히지 않게 띄운다
        private const float  ARROW_SHAFT_START    = 0.8f;  // m, 캡슐 앞쪽 시작 거리
        private const float  ARROW_SHAFT_LENGTH   = 0.8f;  // m
        private const float  ARROW_WIDTH          = 0.14f; // m
        private const float  ARROW_HEAD_LENGTH    = 0.45f; // m
        private const float  ARROW_HEAD_ANGLE     = 40f;   // 도, 진행 방향 축에서 벌어지는 각

        [Header("참조")]
        [SerializeField] private Camera _outputCamera;

        [Header("이동")]
        [SerializeField] private float _moveSpeed      = 6f;  // m/s
        [SerializeField] private float _cameraDistance = 40f; // m, 카메라 방향 그대로 캐릭터에서 이만큼 떨어져 따라간다

        [Header("공격")]
        [SerializeField] private float     _attackRange     = 2.5f;  // m
        [SerializeField] private float     _attackHalfAngle = 60f;   // 도
        [SerializeField] private float     _attackCooldown  = 0.4f;  // 초
        [SerializeField] private float     _attackLockTime  = 0.2f;  // 초, 공격 중에는 걷지 않고 휘두르는 방향을 유지한다
        [SerializeField] private LayerMask _hitLayers       = ~0;
        [SerializeField] private Color     _slashColor      = new Color(1f, 0.85f, 0.4f, 0.6f);

        [Header("표시")]
        [SerializeField] private Color _arrowColor = new Color(1f, 0.9f, 0.2f, 1f);

        private readonly RaycastHit[] _rayHits    = new RaycastHit[RAYCAST_MAX_HITS];
        private readonly Collider[]   _overlapHits = new Collider[HIT_BUFFER_SIZE];

        private CharacterController _controller     = null;
        private GameObject          _slash          = null;
        private Vector3             _spawnPosition  = Vector3.zero;
        private Quaternion          _spawnRotation  = Quaternion.identity;
        private Vector3             _destination    = Vector3.zero;
        private Vector3             _cameraOffset   = Vector3.zero;
        private bool                _hasDestination = false;
        private float               _verticalSpeed  = 0f;
        private float               _nextAttackTime = 0f;
        private float               _attackEndTime  = 0f;

        /// <summary> 공격이 맞힌 충돌체마다 한 번씩 알린다 </summary>
        public event Action<Collider> Hit;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _controller    = GetComponent<CharacterController>();
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _slash         = CreateSlashVisual();
            CreateFacingArrow();
        }

        private void Start()
        {
            if (_outputCamera == null)
            {
                Debug.LogError("[DevHitPlayer] 출력 카메라가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _cameraOffset = -_outputCamera.transform.forward * _cameraDistance;
        }

        private void Update()
        {
            bool isAttacking = Time.time < _attackEndTime;

            if ((!isAttacking) && (Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_ATTACK)))
            {
                TryAttack();
                isAttacking = Time.time < _attackEndTime;
            }

            Vector3 step = isAttacking ? Vector3.zero : ReadMoveStep();

            _verticalSpeed = _controller.isGrounded ? GROUND_STICK_SPEED : _verticalSpeed + (GRAVITY * Time.deltaTime);
            _controller.Move(step + (Vector3.up * (_verticalSpeed * Time.deltaTime)));

            if (transform.position.y < (_spawnPosition.y - FALL_RESET_DEPTH))
            {
                Teleport();
            }

            if ((_slash.activeSelf) && (!isAttacking))
            {
                _slash.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            _outputCamera.transform.position = transform.position + _cameraOffset;
        }

        /****************************************
        *                Methods
        ****************************************/

        // 이동 버튼을 누르는 동안 커서 지점을 목적지로 갱신하고, 도착하면 선다
        private Vector3 ReadMoveStep()
        {
            if ((Input.GetMouseButton(Constants.Player.MOUSE_BUTTON_MOVE)) && (TryGetCursorPoint(out Vector3 point)))
            {
                _destination    = point;
                _hasDestination = true;
            }

            if (!_hasDestination)
            {
                return Vector3.zero;
            }

            Vector3 toDestination = _destination - transform.position;
            toDestination.y = 0f;
            if (toDestination.magnitude <= ARRIVE_DISTANCE)
            {
                _hasDestination = false;
                return Vector3.zero;
            }

            Vector3 direction = toDestination.normalized;
            FaceTowards(direction);

            // 한 프레임에 목적지를 넘어서 떨지 않게 남은 거리까지만 걷는다
            return direction * Mathf.Min(_moveSpeed * Time.deltaTime, toDestination.magnitude);
        }

        private void TryAttack()
        {
            if ((Time.time < _nextAttackTime) || (!TryGetCursorPoint(out Vector3 point)))
            {
                return;
            }

            Vector3 aim = point - transform.position;
            aim.y = 0f;
            if (aim.sqrMagnitude > MIN_DIRECTION_SQR)
            {
                transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);
            }

            _hasDestination = false;
            _nextAttackTime = Time.time + _attackCooldown;
            _attackEndTime  = Time.time + _attackLockTime;

            ShowSlash();
            DealDamageInCone();
        }

        // 몸 중심 구 안에서 바라보는 방향과의 각도가 반각 이하인 충돌체만 맞힌 것으로 본다
        private void DealDamageInCone()
        {
            Vector3 origin  = transform.position;
            Vector3 forward = transform.forward;
            int     count   = Physics.OverlapSphereNonAlloc(origin, _attackRange, _overlapHits, _hitLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; ++i)
            {
                Collider target = _overlapHits[i];
                if ((target == _controller) || (target.transform.IsChildOf(transform)))
                {
                    continue;
                }

                Vector3 toTarget = target.ClosestPoint(origin) - origin;
                toTarget.y = 0f;
                if ((toTarget.sqrMagnitude > MIN_DIRECTION_SQR) && (Vector3.Angle(forward, toTarget) > _attackHalfAngle))
                {
                    continue;
                }

                Hit?.Invoke(target);
            }
        }

        private void ShowSlash()
        {
            // 공격 범위 길이만큼 앞으로 뻗은 얇은 판. 부채꼴 대신 사거리와 방향을 읽을 수 있는 정도로만 보여 준다
            float width = 2f * _attackRange * Mathf.Sin(_attackHalfAngle * Mathf.Deg2Rad);
            _slash.transform.localScale    = new Vector3(Mathf.Max(width, SLASH_THICKNESS), SLASH_THICKNESS, _attackRange);
            _slash.transform.localPosition = new Vector3(0f, SLASH_HEIGHT, _attackRange * HALF);
            _slash.SetActive(true);
        }

        private GameObject CreateSlashVisual()
        {
            GameObject slash = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slash.name = "DevSlash";

            // 시험용 표시가 판정이나 커서 레이캐스트를 가로막지 않게 충돌체를 없앤다
            Destroy(slash.GetComponent<Collider>());
            slash.transform.SetParent(transform, false);

            var block = new MaterialPropertyBlock();
            block.SetColor(COLOR_PROPERTY, _slashColor);
            slash.GetComponent<Renderer>().SetPropertyBlock(block);

            slash.SetActive(false);
            return slash;
        }

        // 시험용 표시가 판정이나 커서 레이캐스트를 가로막지 않게 충돌체를 없앤 막대를 만든다
        private Transform CreateBar(Transform parent, string barName, Vector3 localPosition, Quaternion localRotation, Vector3 scale)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = barName;
            Destroy(bar.GetComponent<Collider>());

            Transform barTransform = bar.transform;
            barTransform.SetParent(parent, false);
            barTransform.SetLocalPositionAndRotation(localPosition, localRotation);
            barTransform.localScale = scale;

            var block = new MaterialPropertyBlock();
            block.SetColor(COLOR_PROPERTY, _arrowColor);
            bar.GetComponent<Renderer>().SetPropertyBlock(block);
            return barTransform;
        }

        // 몸이 둥글어 정면이 안 보이므로 앞쪽 바닥에 화살표를 놓는다. 머리 막대 끝이 화살촉 끝점에 모인다
        private void CreateFacingArrow()
        {
            var root = new GameObject(ARROW_NAME).transform;
            root.SetParent(transform, false);

            float tipZ = ARROW_SHAFT_START + ARROW_SHAFT_LENGTH;
            CreateBar(root, "Shaft", new Vector3(0f, ARROW_HEIGHT, ARROW_SHAFT_START + (ARROW_SHAFT_LENGTH * HALF)),
                Quaternion.identity, new Vector3(ARROW_WIDTH, SLASH_THICKNESS, ARROW_SHAFT_LENGTH));

            float   rad  = ARROW_HEAD_ANGLE * Mathf.Deg2Rad;
            Vector3 back = new Vector3(Mathf.Sin(rad), 0f, -Mathf.Cos(rad));
            Vector3 headScale = new Vector3(ARROW_WIDTH, SLASH_THICKNESS, ARROW_HEAD_LENGTH);
            Vector3 tip = new Vector3(0f, ARROW_HEIGHT, tipZ);

            // 오른쪽 머리는 뒤로 오른쪽, 왼쪽 머리는 뒤로 왼쪽으로 뻗는다
            CreateBar(root, "HeadRight", tip + (back * (ARROW_HEAD_LENGTH * HALF)),
                Quaternion.LookRotation(back, Vector3.up), headScale);
            Vector3 backLeft = new Vector3(-back.x, 0f, back.z);
            CreateBar(root, "HeadLeft", tip + (backLeft * (ARROW_HEAD_LENGTH * HALF)),
                Quaternion.LookRotation(backLeft, Vector3.up), headScale);
        }

        private void FaceTowards(Vector3 direction)
        {
            Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, TURN_SPEED_DEGREES * Time.deltaTime);
        }

        // CharacterController가 켜져 있으면 위치 대입을 덮어쓰므로 잠깐 끈다
        private void Teleport()
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _controller.enabled = true;

            _hasDestination = false;
            _verticalSpeed  = 0f;
        }

        // 자기 몸체 충돌체를 걸러 내야 해서 여러 개를 받아 가장 가까운 것을 쓴다
        private bool TryGetCursorPoint(out Vector3 point)
        {
            point = Vector3.zero;

            Ray ray   = _outputCamera.ScreenPointToRay(Input.mousePosition);
            int count = Physics.RaycastNonAlloc(ray, _rayHits, Constants.Player.CURSOR_RAYCAST_MAX_DISTANCE,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            bool  found   = false;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = _rayHits[i];
                if ((hit.collider == _controller) || (hit.collider.transform.IsChildOf(transform)) || (hit.distance >= nearest))
                {
                    continue;
                }

                nearest = hit.distance;
                point   = hit.point;
                found   = true;
            }

            return found;
        }
    }
}
