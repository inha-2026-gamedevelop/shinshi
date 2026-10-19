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
    public sealed class DevHitFeedbackPlayer : MonoBehaviour, IHitLagTarget
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
        private const float DEAD_TILT_DEGREES  = 90f;  // 도, 죽으면 옆으로 눕는다

        private const int RAYCAST_MAX_HITS = 8;
        private const int HIT_BUFFER_SIZE  = 32;

        private const float SLASH_THICKNESS = 0.05f; // m
        private const float SLASH_HEIGHT    = 0.9f;  // m, 몸통 높이에 띄운다

        // 휘두르는 호. 뒤로 갈수록 투명해지는 띠가 부채꼴을 왼쪽에서 오른쪽으로 훑는다
        private const string SLASH_SHADER_NAME   = "Sprites/Default";
        private const int    SLASH_SEGMENTS      = 14;
        private const float  SLASH_TRAIL_ANGLE   = 60f;  // 도, 띠가 뒤로 늘어진 각
        private const float  SLASH_INNER_RATIO   = 0.55f; // 사거리 대비 띠 안쪽 반지름

        // 콤보. 마지막 단계가 강공격이다
        private const int HEAVY_COMBO_STEP = 3;

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
        [SerializeField] private float     _attackCooldown  = 0.2f;  // 초
        [SerializeField] private float     _attackLockTime  = 0.1f;  // 초, 공격 중에는 걷지 않고 휘두르는 방향을 유지한다
        [SerializeField] private LayerMask _hitLayers       = ~0;
        [SerializeField] private Color     _slashColor      = new Color(1f, 0.85f, 0.4f, 0.6f);

        [Header("강공격 (3타)")]
        [SerializeField] private float _comboWindow     = 1f;    // 초, 이 안에 다음 공격을 눌러야 콤보가 이어진다
        [SerializeField] private float _heavyLockTime   = 0.175f; // 초
        [SerializeField] private Color _heavySlashTint  = new Color(1f, 0.45f, 0.25f, 1f);
        [SerializeField] private float _slamReach       = 1.75f; // m, 충격 중심이 캐릭터 앞으로 떨어진 거리

        [Header("체력 (몬스터에게 이만큼 맞으면 죽는다)")]
        [SerializeField, Min(1)] private int   _maxHits          = 3;
        [SerializeField, Min(0f)] private float _invincibleSeconds = 0.6f; // 초, 맞은 직후 연달아 맞지 않게 막는 시간

        [Header("표시")]
        [SerializeField] private Color _arrowColor = new Color(1f, 0.9f, 0.2f, 1f);

        private readonly RaycastHit[] _rayHits    = new RaycastHit[RAYCAST_MAX_HITS];
        private readonly Collider[]   _overlapHits = new Collider[HIT_BUFFER_SIZE];

        private CharacterController _controller     = null;
        private GameObject          _slash          = null;
        private Material            _slashMaterial  = null;
        private HitFeedbackCamera   _feedbackCamera = null;
        private int                 _hitLagFramesLeft = 0;
        private bool                _attackBuffered = false;
        private int                 _comboStep      = 0;
        private float               _comboEndTime   = 0f;
        private float               _slashStartTime = 0f;
        private float               _slashSeconds   = 0f;
        private Vector3             _spawnPosition  = Vector3.zero;
        private Quaternion          _spawnRotation  = Quaternion.identity;
        private Vector3             _destination    = Vector3.zero;
        private Vector3             _cameraOffset   = Vector3.zero;
        private bool                _hasDestination = false;
        private float               _verticalSpeed  = 0f;
        private float               _nextAttackTime = 0f;
        private float               _attackEndTime  = 0f;
        private int                 _hitsLeft       = 0;
        private float               _invincibleEnd  = 0f;
        private bool                _isDead         = false;
        private bool                _showSlash      = true;
        private bool                _skipPlayerFreeze   = false;
        private bool                _attackInputEnabled = true;
        private bool                _movementBlocked    = false;

        /// <summary> 공격이 맞힌 충돌체마다 한 번씩 알린다. 값은 맞은 충돌체, 몸 중심에서 가장 가까운 맞은 지점, 강공격 여부다 </summary>
        public event Action<Collider, Vector3, bool> Hit;

        /// <summary> 강공격이 바닥을 내려칠 때 한 번 알린다. 값은 충격 중심이다 </summary>
        public event Action<Vector3> Slam;

        /// <summary> 이동 버튼을 새로 누를 때마다 커서가 가리킨 지면 지점과 함께 알린다. 누르고 있는 동안 따라가는 이동에는 알리지 않는다 </summary>
        public event Action<Vector3> MoveClicked;

        /// <summary> 몬스터에게 맞을 때마다 알린다. 값은 남은 체력이다 </summary>
        public event Action<int> Damaged;

        /// <summary> 체력이 다해 죽을 때 한 번 알린다 </summary>
        public event Action Died;

        /// <summary> 공격을 시작할 때 한 번 알린다. 값은 0부터 센 콤보 단계와 강공격 여부다 </summary>
        public event Action<int, bool> Attacked;

        public bool IsDead => _isDead;

        /// <summary> 직전 프레임의 이동 속도(m/s) </summary>
        public Vector3 Velocity => _controller.velocity;

        public int HitsLeft => _hitsLeft;

        public int MaxHits => _maxHits;

        /// <summary> 이 플레이어의 카메라 흔들림과 줌을 맡는 도우미 </summary>
        public HitFeedbackCamera FeedbackCamera => _feedbackCamera;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _controller    = GetComponent<CharacterController>();
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _hitsLeft      = _maxHits;
            _slash         = CreateSlashVisual();
            CreateFacingArrow();

            _feedbackCamera = gameObject.AddComponent<HitFeedbackCamera>();
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
            _feedbackCamera.Init(_outputCamera);
        }

        private void Update()
        {
            if (_isDead)
            {
                return;
            }

            bool attackPressed = (_attackInputEnabled) && (Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_ATTACK));

            // 렉프레임 동안은 나만 굳는다. 전역 timeScale은 건드리지 않아 다른 몬스터와 다른 플레이어는 그대로 흐른다
            if (_hitLagFramesLeft > 0)
            {
                _attackBuffered |= attackPressed;
                UpdateHitLag();
                return;
            }

            attackPressed |= _attackBuffered;
            _attackBuffered = false;

            if ((Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_MOVE)) && (TryGetCursorPoint(out Vector3 clickPoint)))
            {
                MoveClicked?.Invoke(clickPoint);
            }

            bool isAttacking = Time.time < _attackEndTime;

            if ((!isAttacking) && (attackPressed))
            {
                TryAttack();
                isAttacking = Time.time < _attackEndTime;
            }

            if (_movementBlocked)
            {
                _hasDestination = false;
            }

            Vector3 step = ((isAttacking) || (_movementBlocked)) ? Vector3.zero : ReadMoveStep();

            _verticalSpeed = _controller.isGrounded ? GROUND_STICK_SPEED : _verticalSpeed + (GRAVITY * Time.deltaTime);
            _controller.Move(step + (Vector3.up * (_verticalSpeed * Time.deltaTime)));

            if (transform.position.y < (_spawnPosition.y - FALL_RESET_DEPTH))
            {
                Teleport();
            }

            UpdateSlash(isAttacking);
        }

        private void LateUpdate()
        {
            _outputCamera.transform.position = transform.position + _cameraOffset + _feedbackCamera.ReadShakeOffset();
            _feedbackCamera.ApplyZoom();
        }

        private void OnDestroy()
        {
            if (_slashMaterial != null)
            {
                Destroy(_slashMaterial);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 몬스터에게 한 대 맞는다. 맞은 직후 잠깐은 무적이고, 체력이 다하면 쓰러져 조작할 수 없다 </summary>
        public void TakeHit(Vector3 attackerPosition)
        {
            if ((_isDead) || (Time.time < _invincibleEnd))
            {
                return;
            }

            --_hitsLeft;
            _invincibleEnd = Time.time + _invincibleSeconds;
            _hasDestination = false;
            Damaged?.Invoke(_hitsLeft);

            if (_hitsLeft > 0)
            {
                return;
            }

            _isDead = true;
            transform.rotation = Quaternion.Euler(DEAD_TILT_DEGREES, transform.eulerAngles.y, 0f);
            Died?.Invoke();
        }

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

            // 콤보 시간 안에 이어 누르면 단계가 오르고, 강공격 뒤나 시간이 지나면 처음으로 돌아간다
            _comboStep = (Time.time < _comboEndTime) ? (_comboStep + 1) : 1;
            bool isHeavy = _comboStep >= HEAVY_COMBO_STEP;
            int  attackStep = _comboStep - 1;
            if (isHeavy)
            {
                _comboStep = 0;
            }

            _comboEndTime = Time.time + _comboWindow;

            float lockTime = isHeavy ? _heavyLockTime : _attackLockTime;
            _hasDestination = false;
            _nextAttackTime = Time.time + _attackCooldown;
            _attackEndTime  = Time.time + lockTime;

            Attacked?.Invoke(attackStep, isHeavy);
            ShowSlash(isHeavy, lockTime);
            DealDamageInCone(isHeavy);

            if (isHeavy)
            {
                PlaySlam();
            }
        }

        // 캐릭터 앞 발밑 높이에서 내려친다
        private void PlaySlam()
        {
            Vector3 center = transform.position + (transform.forward * _slamReach);
            center.y = _controller.bounds.min.y;

            Slam?.Invoke(center);
        }

        // 몸 중심 구 안에서 바라보는 방향과의 각도가 반각 이하인 충돌체만 맞힌 것으로 본다
        private void DealDamageInCone(bool isHeavy)
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

                Vector3 hitPoint = target.ClosestPoint(origin);
                Hit?.Invoke(target, hitPoint, isHeavy);
            }
        }

        /// <inheritdoc/>
        public void Freeze(int frames)
        {
            if (_skipPlayerFreeze)
            {
                return;
            }

            // 한 번 휘두르다 여럿을 맞혀도 멈춤이 겹쳐 길어지지 않고 가장 긴 것만 쓴다
            _hitLagFramesLeft = Mathf.Max(_hitLagFramesLeft, frames);
        }

        // 렉프레임은 프레임 수로 센다. 굳은 만큼 공격, 콤보, 슬래시 시각을 뒤로 밀어 멈춘 시간이 소모되지 않게 한다
        private void UpdateHitLag()
        {
            --_hitLagFramesLeft;

            float frozen = Time.deltaTime;
            _attackEndTime  += frozen;
            _nextAttackTime += frozen;
            _comboEndTime   += frozen;
            _slashStartTime += frozen;
        }

        /// <summary> 기본 공격 판정을 다른 곳(서버 판정 대역)이 맡을 때 이 플레이어의 자체 공격 입력을 끈다 </summary>
        public void SetAttackInputEnabled(bool isEnabled) { _attackInputEnabled = isEnabled; }

        /// <summary> 기술 시전 중처럼 걷지 못하는 동안 참으로 둔다 </summary>
        public void SetMovementBlocked(bool isBlocked) { _movementBlocked = isBlocked; }

        /// <summary> 외부 판정이 맞힌 충돌체를 같은 Hit 경로로 알린다 </summary>
        public void ReportHit(Collider target, Vector3 hitPoint, bool isHeavy, bool freezesPlayer = true)
        {
            // 렉프레임(Freeze)은 Hit 이벤트 처리 중에 동기로 불린다. 지속 피해처럼 플레이어가 굳으면 안 되는 타격은 그 동안만 무시한다
            _skipPlayerFreeze = !freezesPlayer;
            Hit?.Invoke(target, hitPoint, isHeavy);
            _skipPlayerFreeze = false;
        }

        /// <summary> 커서가 가리킨 지면 지점 </summary>
        public bool TryReadCursorPoint(out Vector3 point) { return TryGetCursorPoint(out point); }

        /// <summary> 직업별 공격 모양을 코드에서 덮어쓴다. 휘두르는 띠는 근접 직업만 보인다 </summary>
        public void ConfigureAttack(float range, float halfAngle, float cooldown, float lockTime, float heavyLockTime, bool showSlash)
        {
            _attackRange     = range;
            _attackHalfAngle = halfAngle;
            _attackCooldown  = cooldown;
            _attackLockTime  = lockTime;
            _heavyLockTime   = heavyLockTime;
            _showSlash       = showSlash;
        }

        private void ShowSlash(bool isHeavy, float seconds)
        {
            if ((!_showSlash) || (_slashMaterial == null))
            {
                return;
            }

            _slashStartTime = Time.time;
            _slashSeconds   = seconds;
            _slashMaterial.color = isHeavy ? _heavySlashTint : Color.white;
            _slash.transform.localPosition = new Vector3(0f, SLASH_HEIGHT, 0f);
            _slash.SetActive(true);
            UpdateSlash(true);
        }

        // 띠의 앞끝이 부채꼴 왼쪽 끝에서 오른쪽 끝까지 공격 잠금 시간 동안 지나간다
        private void UpdateSlash(bool isAttacking)
        {
            if (!_slash.activeSelf)
            {
                return;
            }

            if (!isAttacking)
            {
                _slash.SetActive(false);
                return;
            }

            float t = Mathf.Clamp01((Time.time - _slashStartTime) / _slashSeconds);
            _slash.transform.localRotation = Quaternion.Euler(0f, Mathf.Lerp(-_attackHalfAngle, _attackHalfAngle, t), 0f);
        }

        // 앞끝(로컬 각 0)은 불투명하고 뒤로 갈수록 투명해지는 부채꼴 띠. 각은 정면에서 오른쪽이 양수다
        private GameObject CreateSlashVisual()
        {
            var slash = new GameObject("DevSlash");
            slash.transform.SetParent(transform, false);

            // 시험용 씬이라 머티리얼 에셋을 따로 두지 않고 Awake에서 한 번만 이름으로 찾는다
            Shader shader = Shader.Find(SLASH_SHADER_NAME);
            if (shader == null)
            {
                Debug.LogError($"[DevHitPlayer] 셰이더를 찾지 못했다: {SLASH_SHADER_NAME}", this);
                slash.SetActive(false);
                return slash;
            }

            _slashMaterial = new Material(shader);

            int vertexCount = (SLASH_SEGMENTS + 1) * 2;
            var vertices    = new Vector3[vertexCount];
            var colors      = new Color[vertexCount];
            var triangles   = new int[SLASH_SEGMENTS * 6];

            float inner = _attackRange * SLASH_INNER_RATIO;
            for (int i = 0; i <= SLASH_SEGMENTS; ++i)
            {
                float   ratio = (float)i / SLASH_SEGMENTS;
                float   rad   = -SLASH_TRAIL_ANGLE * (1f - ratio) * Mathf.Deg2Rad;
                Vector3 dir   = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Color   color = new Color(_slashColor.r, _slashColor.g, _slashColor.b, _slashColor.a * ratio * ratio);

                vertices[i * 2]     = dir * inner;
                vertices[(i * 2) + 1] = dir * _attackRange;
                colors[i * 2]       = color;
                colors[(i * 2) + 1] = color;
            }

            for (int i = 0; i < SLASH_SEGMENTS; ++i)
            {
                int v = i * 2;
                int t = i * 6;
                triangles[t]     = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = "DevSlashArc", vertices = vertices, colors = colors, triangles = triangles };
            mesh.RecalculateBounds();

            slash.AddComponent<MeshFilter>().sharedMesh = mesh;
            slash.AddComponent<MeshRenderer>().sharedMaterial = _slashMaterial;

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
