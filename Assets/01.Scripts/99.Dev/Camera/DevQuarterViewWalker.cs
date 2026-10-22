// Unity
using UnityEngine;
using UnityEngine.AI;

// 서드파티
using Unity.Cinemachine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Dev
{
    /// <summary> 네트워크 없이 환경 씬에서 시험용 캐릭터를 움직여 실제 쿼터뷰 카메라 화면을 확인한다 </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class DevQuarterViewWalker : MonoBehaviour
    {
        // 환경 씬에는 서버, 인증, 내비 메시가 없어 실제 플레이어 프리팹을 띄울 수 없다. 화면 확인이 목적이라
        // 이동 속도와 마우스 버튼 배치만 실제 값을 따르고, 경로 탐색 대신 직선으로 걸으며 충돌체에 막힌다

        /****************************************
        *                Fields
        ****************************************/

        // 개발 도구 전용 입력 이름. 실제 게임은 키보드 이동을 쓰지 않아 Constants에 두지 않는다
        private const string  AXIS_HORIZONTAL = "Horizontal";
        private const string  AXIS_VERTICAL   = "Vertical";
        private const KeyCode KEY_RESET       = KeyCode.R;

        private const string ANIM_PARAM_RUN = "Run";

        private const float GRAVITY            = -20f; // m/s^2
        private const float GROUND_STICK_SPEED = -2f;  // m/s, 땅에 붙어 있을 때 내리막에서 뜨지 않게 누르는 속도
        private const float TURN_SPEED_DEGREES = 720f; // 도/초
        private const float ARRIVE_DISTANCE    = 0.1f; // m
        private const float EDGE_PROBE_HEIGHT  = 1f;   // m, 다음 위치 위 이 높이에서 아래로 땅을 찾는다
        private const float EDGE_PROBE_DEPTH   = 3f;   // m, 이 깊이 안에 땅이 없으면 섬 끝으로 보고 멈춘다
        private const float FALL_RESET_DEPTH   = 30f;  // m, 시작 높이보다 이만큼 떨어지면 시작 지점으로 되돌린다
        private const float MIN_INPUT_SQR      = 0.01f;
        private const float NAV_SAMPLE_RADIUS  = 1f;   // m, 현재 위치에서 내비 메시를 찾는 반경

        // 실제로 움직인 거리가 의도한 거리의 이 비율(제곱)보다 작으면 막힌 것으로 본다
        private const float BLOCKED_MOVE_RATIO_SQR = 0.01f;
        private const int   RAYCAST_MAX_HITS       = 8;

        [SerializeField] private Camera            _outputCamera;
        [SerializeField] private CinemachineCamera _followCamera;
        [SerializeField] private Animator          _animator;

        // 숫자 키 1부터 순서대로 순간이동한다
        [SerializeField] private Transform[] _teleportPoints;

        // 시험 카메라와 겹쳐 그리는 기존 구도 확인용 카메라. 실행 중에만 끈다
        [SerializeField] private GameObject[] _disableOnPlay;

        private readonly RaycastHit[] _hits = new RaycastHit[RAYCAST_MAX_HITS];

        private CharacterController _controller     = null;
        private Vector3             _spawnPosition  = Vector3.zero;
        private Quaternion          _spawnRotation  = Quaternion.identity;
        private Vector3             _destination    = Vector3.zero;
        private bool                _hasDestination = false;
        private float               _verticalSpeed  = 0f;
        private float               _moveSpeed      = 0f;
        private int                 _runHash        = 0;
        private bool                _hasRunParam    = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _controller    = GetComponent<CharacterController>();
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        // GameDB는 GameDBBootstrap이 초기화하므로 Awake가 아니라 Start에서 읽는다
        private void Start()
        {
            if ((_outputCamera == null) || (_followCamera == null))
            {
                Debug.LogError("[DevWalker] 출력 카메라나 Cinemachine 카메라가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _moveSpeed = GameDB.Player.MoveSpeed;

            if (_animator != null)
            {
                _runHash     = Animator.StringToHash(ANIM_PARAM_RUN);
                _hasRunParam = HasParameter(_animator, _runHash);
            }

            for (int i = 0; i < _disableOnPlay.Length; ++i)
            {
                if (_disableOnPlay[i] != null)
                {
                    _disableOnPlay[i].SetActive(false);
                }
            }

            _followCamera.Target.TrackingTarget = transform;
            _followCamera.PreviousStateIsValid  = false;
        }

        private void Update()
        {
            HandleTeleportKeys();

            Vector3 direction = ReadMoveDirection();
            bool    isMoving  = direction.sqrMagnitude > MIN_INPUT_SQR;
            Vector3 step      = isMoving ? direction * (_moveSpeed * Time.deltaTime) : Vector3.zero;

            // 섬 밖으로 걸어 나가면 시험이 끊기므로 다음 위치 밑에 땅이 없으면 그 자리에 선다
            if ((isMoving) && (!HasGroundBelow(transform.position + step)))
            {
                step            = Vector3.zero;
                _hasDestination = false;
            }

            step = ClampToNavMesh(step);

            _verticalSpeed = _controller.isGrounded ? GROUND_STICK_SPEED : _verticalSpeed + (GRAVITY * Time.deltaTime);

            Vector3 before = transform.position;
            _controller.Move(step + (Vector3.up * (_verticalSpeed * Time.deltaTime)));

            // 경로 탐색이 없어 장애물을 정면으로 밀면 제자리걸음이 된다. 벽을 따라 미끄러지는 정도면 계속 걷는다
            Vector3 moved = transform.position - before;
            moved.y = 0f;
            bool isWalking = (step != Vector3.zero) && (moved.sqrMagnitude > (step.sqrMagnitude * BLOCKED_MOVE_RATIO_SQR));
            if (!isWalking)
            {
                _hasDestination = false;
            }

            // 섬 끝에 막혀도 누른 방향은 바라본다
            if (isMoving)
            {
                Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, TURN_SPEED_DEGREES * Time.deltaTime);
            }

            if (_hasRunParam)
            {
                _animator.SetBool(_runHash, isWalking);
            }

            if (transform.position.y < (_spawnPosition.y - FALL_RESET_DEPTH))
            {
                Teleport(_spawnPosition, _spawnRotation);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 키보드 입력이 있으면 그쪽을 우선하고, 없으면 이동 버튼으로 찍은 지점까지 걷는다
        private Vector3 ReadMoveDirection()
        {
            var keyboard = new Vector2(Input.GetAxisRaw(AXIS_HORIZONTAL), Input.GetAxisRaw(AXIS_VERTICAL));
            if (keyboard.sqrMagnitude > MIN_INPUT_SQR)
            {
                _hasDestination = false;

                // 카메라는 회전하지 않지만 방위를 설정값으로 바꿀 수 있어 화면 기준 방향을 매번 계산한다
                Vector3 forward = Vector3.ProjectOnPlane(_outputCamera.transform.forward, Vector3.up).normalized;
                Vector3 right   = Vector3.ProjectOnPlane(_outputCamera.transform.right, Vector3.up).normalized;
                return Vector3.ClampMagnitude((forward * keyboard.y) + (right * keyboard.x), 1f);
            }

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

            return toDestination.normalized;
        }

        private void HandleTeleportKeys()
        {
            if (Input.GetKeyDown(KEY_RESET))
            {
                Teleport(_spawnPosition, _spawnRotation);
                return;
            }

            for (int i = 0; i < _teleportPoints.Length; ++i)
            {
                if ((_teleportPoints[i] != null) && (Input.GetKeyDown(KeyCode.Alpha1 + i)))
                {
                    Teleport(_teleportPoints[i].position, _teleportPoints[i].rotation);
                    return;
                }
            }
        }

        // CharacterController가 켜져 있으면 위치 대입을 덮어쓰므로 잠깐 끈다
        private void Teleport(Vector3 position, Quaternion rotation)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;

            _hasDestination = false;
            _verticalSpeed  = 0f;

            // 순간이동한 자리에서 카메라가 끌려오지 않고 바로 붙게 한다
            _followCamera.PreviousStateIsValid = false;
        }

        // 실제 입력과 같이 가림 투명화로 비워진 지붕, 나무는 건너뛰고 그 뒤의 땅을 찍는다
        private bool TryGetCursorPoint(out Vector3 point)
        {
            Ray ray = _outputCamera.ScreenPointToRay(Input.mousePosition);
            return TryGetNearestHit(ray, Constants.Player.CURSOR_RAYCAST_MAX_DISTANCE, true, out point);
        }

        // 실제 플레이어는 서버 경로와 회피가 내비 메시 경계를 벽으로 보므로 같은 경계에서 막고 벽을 따라 미끄러진다.
        // 내비 메시가 없는 환경 씬에서는 막지 않는다
        private Vector3 ClampToNavMesh(Vector3 step)
        {
            if ((step == Vector3.zero) || (!NavMesh.SamplePosition(transform.position, out NavMeshHit origin, NAV_SAMPLE_RADIUS, NavMesh.AllAreas)))
            {
                return step;
            }

            Vector3 start = origin.position;
            if (!NavMesh.Raycast(start, start + step, out NavMeshHit wall, NavMesh.AllAreas))
            {
                return step;
            }

            Vector3 normal = wall.normal;
            normal.y = 0f;

            Vector3 slide = step - Vector3.Project(step, normal);
            return NavMesh.Raycast(start, start + slide, out _, NavMesh.AllAreas) ? Vector3.zero : slide;
        }

        private bool HasGroundBelow(Vector3 position)
        {
            var ray = new Ray(position + (Vector3.up * EDGE_PROBE_HEIGHT), Vector3.down);
            return TryGetNearestHit(ray, EDGE_PROBE_HEIGHT + EDGE_PROBE_DEPTH, false, out _);
        }

        // 자기 몸체 충돌체를 걸러 내야 해서 여러 개를 받는다
        private bool TryGetNearestHit(Ray ray, float maxDistance, bool skipFaded, out Vector3 point)
        {
            point = Vector3.zero;

            int count = Physics.RaycastNonAlloc(ray, _hits, maxDistance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            bool  found   = false;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = _hits[i];
                if ((hit.collider == _controller) || (hit.distance >= nearest))
                {
                    continue;
                }

                if ((skipFaded) && (OcclusionFadeChannel.IsFaded(hit.point)))
                {
                    continue;
                }

                nearest = hit.distance;
                point   = hit.point;
                found   = true;
            }

            return found;
        }

        private static bool HasParameter(Animator animator, int nameHash)
        {
            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; ++i)
            {
                if (parameters[i].nameHash == nameHash)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
