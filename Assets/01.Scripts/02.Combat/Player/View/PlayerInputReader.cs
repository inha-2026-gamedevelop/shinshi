// System
using System;

// Unity
using UnityEngine;
using UnityEngine.EventSystems;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 소유 클라이언트의 마우스, 키 입력을 이동, 기본 공격, 회피, 기술 요청으로 바꾼다, 판정과 쿨다운 확정은 서버가 한다 </summary>
    [RequireComponent(typeof(PlayerInputState))]
    [RequireComponent(typeof(PlayerCombatState))]
    public sealed class PlayerInputReader : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PlayerInputState  _input;
        [SerializeField] private PlayerCombatState _combat;

        // 커서 레이캐스트가 자기 몸체에 맞으면 발밑을 누른 것으로 보고 건너뛴다
        [SerializeField] private Collider _bodyCollider;

        // 지점 기술 지정 중 범위를 그린다. 비어 있으면 표시 없이 지정만 한다
        [SerializeField] private SkillRangeIndicatorView _rangeIndicator;

        private readonly RaycastHit[] _hits = new RaycastHit[Constants.Player.CURSOR_RAYCAST_MAX_HITS];

        private Camera            _camera                = null;
        private CombatLineOfSight _lineOfSight           = null;
        private bool              _hasRequestedMove      = false;
        private Vector3           _lastRequestedMove     = Vector3.zero;
        private double            _nextAttackRequestTime = 0.0;

        // 지점 기술 지정 상태. 확정 전에는 서버가 모르므로 소유 클라이언트에만 있다
        private bool              _isTargeting  = false;
        private ESkillRequestKind _targetKind   = ESkillRequestKind.Regular;
        private byte              _targetSlot   = 0;
        private float             _targetRange  = 0f;
        private float             _targetRadius = 0f;

        // 확정, 취소에 쓴 클릭이 손을 뗄 때까지 공격, 이동으로 이어지지 않게 막는다
        private bool _suppressAttack = false;
        private bool _suppressMove   = false;

        // 마지막으로 보낸 기술 요청. 서버 결과의 순번이 같을 때만 어떤 기술이 거절됐는지 알린다
        private bool              _hasSentSkill      = false;
        private uint              _lastSkillSequence = 0;
        private ESkillRequestKind _lastSkillKind     = ESkillRequestKind.Regular;
        private byte              _lastSkillSlot     = 0;

        /****************************************
        *              Properties
        ****************************************/

        public bool              IsTargeting   => _isTargeting;
        public ESkillRequestKind TargetingKind => _targetKind;
        public byte              TargetingSlot => _targetSlot;

        // 서버가 마지막 기술 요청을 거절했을 때. HUD 피드백용이며 이전 요청의 결과는 오지 않는다
        public event Action<ESkillRequestKind, byte, ERejectReason> SkillRejected;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _lineOfSight = new CombatLineOfSight(CombatLineOfSight.ResolveLayerMask(Constants.Combat.OBSTACLE_LAYER_NAME),
                Constants.Combat.LINE_OF_SIGHT_HEIGHT);
        }

        private void OnEnable()
        {
            if (_input == null)
            {
                return;
            }

            _input.SkillSent   += OnSkillSent;
            _input.SkillResult += OnSkillResult;
        }

        private void OnDisable()
        {
            CancelTargeting();
            if (_input == null)
            {
                return;
            }

            _input.SkillSent   -= OnSkillSent;
            _input.SkillResult -= OnSkillResult;
        }

        private void Update()
        {
            if ((_input == null) || (_combat == null) || (!_input.isOwned))
            {
                CancelTargeting();
                return;
            }

            if (_combat.LifeState != EParticipantLifeState.Alive)
            {
                CancelTargeting();
                return;
            }

            ReleaseSuppressedButtons();

            // 창 위에 커서가 있어도 Esc는 지정을 취소한다
            if ((_isTargeting) && (Input.GetKeyDown(Constants.Player.KEY_CANCEL_TARGETING)))
            {
                CancelTargeting();
                return;
            }

            // UI를 누른 클릭은 전투 입력으로 넘기지 않는다
            if ((EventSystem.current != null) && (EventSystem.current.IsPointerOverGameObject()))
            {
                return;
            }

            // 상충 입력은 회피가 먼저다. 회피를 보낸 프레임에는 기술, 공격, 이동을 보내지 않고 진행 중인 지정도 끝낸다
            if (Input.GetKeyDown(Constants.Player.KEY_DODGE))
            {
                CancelTargeting();
                HandleDodge();
                return;
            }

            if (HandleSkillKey())
            {
                return;
            }

            if (_isTargeting)
            {
                UpdateTargeting();
                return;
            }

            bool attackDown = Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_ATTACK);
            bool attackHeld = (!_suppressAttack) && (Input.GetMouseButton(Constants.Player.MOUSE_BUTTON_ATTACK));
            bool moveDown   = Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_MOVE);
            bool moveHeld   = (!_suppressMove) && (Input.GetMouseButton(Constants.Player.MOUSE_BUTTON_MOVE));
            if ((!attackHeld) && (!moveHeld))
            {
                return;
            }

            if (!TryGetCursorPoint(out Vector3 point))
            {
                return;
            }

            if (attackHeld)
            {
                HandleAttack(point, attackDown);
                return;
            }

            HandleMove(point, moveDown);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 누르고 있는 동안 커서를 따라간다. 같은 지점을 매 프레임 다시 보내면 서버가 같은 목적지로 버리는 순번만 쌓인다
        private void HandleMove(Vector3 point, bool isNewPress)
        {
            if ((!isNewPress) && (_hasRequestedMove)
                && ((point - _lastRequestedMove).sqrMagnitude <= Constants.Player.MOVE_SAME_DESTINATION_EPSILON_SQR))
            {
                return;
            }

            _hasRequestedMove  = true;
            _lastRequestedMove = point;
            _input.RequestMove(point);
        }

        // 누르고 있으면 공격 간격마다 다시 보낸다. 매 프레임 보내면 쿨다운 거절만 오간다
        private void HandleAttack(Vector3 point, bool isNewPress)
        {
            double now = Time.unscaledTimeAsDouble;
            if ((!isNewPress) && (now < _nextAttackRequestTime))
            {
                return;
            }

            PlayerConfigSO config   = GameDB.Player;
            float          interval = config.GetBasicAttack(_combat.PlayerClass).Interval;

            // 서버는 남은 쿨다운이 보관 시간 안이면 받아 두므로 그만큼 일찍 보낸다
            _nextAttackRequestTime = now + Mathf.Max(0f, interval - config.InputBufferSeconds);
            _input.RequestAttack(point - transform.position);
        }

        // 커서 쪽으로 피한다. 커서가 지면을 가리키지 않으면 바라보는 쪽으로 피한다
        private void HandleDodge()
        {
            // 서버는 남은 쿨다운이 보관 시간 안일 때만 받아 둔다. 그보다 이르면 보내 봐야 거절되고 예측만 되돌린다
            if ((NetworkTime.time + GameDB.Player.InputBufferSeconds) < _input.DodgeReadyAt)
            {
                return;
            }

            Vector3 direction = TryGetCursorPoint(out Vector3 point) ? (point - transform.position) : transform.forward;
            _input.RequestDodge(direction);
        }

        // 이 프레임에 기술 키를 처리했으면 참이다. 직업에 없는 키나 준비되지 않은 기술은 다른 입력을 막지 않는다
        private bool HandleSkillKey()
        {
            ESkillInputKey key = ReadSkillKey();
            if (key == ESkillInputKey.None)
            {
                return false;
            }

            EPlayerClass playerClass = _combat.PlayerClass;
            if (!SkillInputRules.TryMapKey(playerClass, key, out ESkillRequestKind kind, out byte slot))
            {
                return false;
            }

            if (!SkillInputRules.TryGetAim(GameDB.Skills, playerClass, kind, slot, out ESkillAimMode aimMode,
                out float castRange, out float radius))
            {
                return false;
            }

            bool isReady = SkillInputRules.IsReady(GetReadyAt(kind, slot), NetworkTime.time, GameDB.Player.InputBufferSeconds);
            ESkillInputDecision decision = SkillInputRules.DecideKey(aimMode, isReady, _isTargeting, _targetKind, _targetSlot,
                kind, slot);
            switch (decision)
            {
                case ESkillInputDecision.CastNow:
                    CancelTargeting();
                    CastWithoutTargeting(kind, slot, aimMode);
                    return true;
                case ESkillInputDecision.BeginTargeting:
                    BeginTargeting(kind, slot, castRange, radius);
                    return true;
                default:
                    return false;
            }
        }

        // 한 프레임에 여러 키를 눌러도 하나만 처리한다. 순서는 Q, W, E, R, Z, X, C
        private static ESkillInputKey ReadSkillKey()
        {
            if (Input.GetKeyDown(Constants.Player.KEY_SKILL_Q))
            {
                return ESkillInputKey.Q;
            }

            if (Input.GetKeyDown(Constants.Player.KEY_SKILL_W))
            {
                return ESkillInputKey.W;
            }

            if (Input.GetKeyDown(Constants.Player.KEY_SKILL_E))
            {
                return ESkillInputKey.E;
            }

            if (Input.GetKeyDown(Constants.Player.KEY_SKILL_R))
            {
                return ESkillInputKey.R;
            }

            if (Input.GetKeyDown(Constants.Player.KEY_ASCENSION_Z))
            {
                return ESkillInputKey.Z;
            }

            if (Input.GetKeyDown(Constants.Player.KEY_ASCENSION_X))
            {
                return ESkillInputKey.X;
            }

            if (Input.GetKeyDown(Constants.Player.KEY_ASCENSION_C))
            {
                return ESkillInputKey.C;
            }

            return ESkillInputKey.None;
        }

        private double GetReadyAt(ESkillRequestKind kind, byte slot)
        {
            if (kind == ESkillRequestKind.Ascension)
            {
                return _input.AscensionReadyAt;
            }

            switch (slot)
            {
                case Constants.Combat.SKILL_SLOT_Q:
                    return _input.SkillQReadyAt;
                case Constants.Combat.SKILL_SLOT_W:
                    return _input.SkillWReadyAt;
                case Constants.Combat.SKILL_SLOT_E:
                    return _input.SkillEReadyAt;
                case Constants.Combat.SKILL_SLOT_R:
                    return _input.SkillRReadyAt;
                default:
                    return 0.0;
            }
        }

        // 방향 기술은 입력 순간의 커서 방향, 자기 위치 기술은 시전자 위치로 보낸다. 서버는 방향 기술의 지점을 쓰지 않는다
        private void CastWithoutTargeting(ESkillRequestKind kind, byte slot, ESkillAimMode aimMode)
        {
            Vector3 origin    = transform.position;
            Vector3 point     = origin;
            Vector3 direction = FacingRules.DecodeYaw(_combat.FacingYaw);
            if ((aimMode == ESkillAimMode.Direction) && (TryGetCursorPoint(out Vector3 cursor)))
            {
                point     = cursor;
                direction = cursor - origin;
            }

            _input.RequestSkill(kind, slot, point, direction);
        }

        private void BeginTargeting(ESkillRequestKind kind, byte slot, float castRange, float radius)
        {
            _isTargeting  = true;
            _targetKind   = kind;
            _targetSlot   = slot;
            _targetRange  = castRange;
            _targetRadius = radius;
            UpdateTargeting();
        }

        private void CancelTargeting()
        {
            if (!_isTargeting)
            {
                return;
            }

            _isTargeting = false;
            if (_rangeIndicator != null)
            {
                _rangeIndicator.Hide();
            }
        }

        // 지정 중에는 왼쪽이 확정, 오른쪽이 취소다. 이동하려면 먼저 취소해야 한다
        private void UpdateTargeting()
        {
            if (Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_MOVE))
            {
                CancelTargeting();
                _suppressMove = true;
                return;
            }

            if (!TryGetCursorPoint(out Vector3 cursor))
            {
                return;
            }

            Vector3 origin  = transform.position;
            bool    isValid = SkillInputRules.TryResolveTargetPoint(origin, cursor, _targetRange, _lineOfSight, out Vector3 point);
            if (_rangeIndicator != null)
            {
                float ringRadius = Mathf.Max(0f, _targetRange - Constants.Player.SKILL_POINT_RANGE_MARGIN);
                _rangeIndicator.Show(origin, ringRadius, point, _targetRadius, isValid);
            }

            // 벽 뒤, 이동 불가 지점은 붉게 보이고 확정하지 않는다
            if ((!Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_ATTACK)) || (!isValid))
            {
                return;
            }

            _input.RequestSkill(_targetKind, _targetSlot, point, point - origin);
            CancelTargeting();
            _suppressAttack = true;
        }

        private void ReleaseSuppressedButtons()
        {
            if ((_suppressAttack) && (!Input.GetMouseButton(Constants.Player.MOUSE_BUTTON_ATTACK)))
            {
                _suppressAttack = false;
            }

            if ((_suppressMove) && (!Input.GetMouseButton(Constants.Player.MOUSE_BUTTON_MOVE)))
            {
                _suppressMove = false;
            }
        }

        private void OnSkillSent(uint sequence, ESkillRequestKind kind, byte slot)
        {
            _hasSentSkill      = true;
            _lastSkillSequence = sequence;
            _lastSkillKind     = kind;
            _lastSkillSlot     = slot;
        }

        // 거절은 드물어서 이때만 문자열을 만든다
        private void OnSkillResult(uint sequence, int castInstanceId, ERejectReason reason)
        {
            if (reason == ERejectReason.None)
            {
                return;
            }

            if (SkillInputRules.IsRoutineRejection(reason))
            {
                Debug.Log($"[Skill] 기술 요청 거절 - 순번 {sequence}, 사유 {reason}");
            }
            else
            {
                Debug.LogWarning($"[Skill] 기술 요청 거절, 연결 상태 확인 필요 - 순번 {sequence}, 사유 {reason}");
            }

            if ((_hasSentSkill) && (sequence == _lastSkillSequence))
            {
                SkillRejected?.Invoke(_lastSkillKind, _lastSkillSlot, reason);
            }
        }

        private bool TryGetCursorPoint(out Vector3 point)
        {
            point = Vector3.zero;

            // 카메라는 씬에 있고 네트워크 프리팹은 직접 참조할 수 없다. 처음 한 번 찾아 두고 씬이 바뀌어 사라졌을 때만 다시 찾는다
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null)
            {
                return false;
            }

            Ray ray   = _camera.ScreenPointToRay(Input.mousePosition);
            int count = Physics.RaycastNonAlloc(ray, _hits, Constants.Player.CURSOR_RAYCAST_MAX_DISTANCE,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            bool  found   = false;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = _hits[i];
                if ((hit.collider == _bodyCollider) || (hit.distance >= nearest))
                {
                    continue;
                }

                // 투명하게 비워진 지붕, 나무를 눌러도 그 뒤에 보이는 땅을 누른 것으로 본다
                if (OcclusionFadeChannel.IsFaded(hit.point))
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
