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
    /// <summary> 소유 클라이언트의 마우스, 키 입력을 이동, 기본 공격, 회피 요청으로 바꾼다, 판정과 쿨다운 확정은 서버가 한다 </summary>
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

        private readonly RaycastHit[] _hits = new RaycastHit[Constants.Player.CURSOR_RAYCAST_MAX_HITS];

        private Camera  _camera                = null;
        private bool    _hasRequestedMove      = false;
        private Vector3 _lastRequestedMove     = Vector3.zero;
        private double  _nextAttackRequestTime = 0.0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if ((_input == null) || (_combat == null) || (!_input.isOwned))
            {
                return;
            }

            if (_combat.LifeState != EParticipantLifeState.Alive)
            {
                return;
            }

            // UI를 누른 클릭은 전투 입력으로 넘기지 않는다
            if ((EventSystem.current != null) && (EventSystem.current.IsPointerOverGameObject()))
            {
                return;
            }

            // 상충 입력은 회피가 먼저다. 회피를 보낸 프레임에는 공격과 이동을 보내지 않는다
            if (Input.GetKeyDown(Constants.Player.KEY_DODGE))
            {
                HandleDodge();
                return;
            }

            bool attackDown = Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_ATTACK);
            bool attackHeld = Input.GetMouseButton(Constants.Player.MOUSE_BUTTON_ATTACK);
            bool moveDown   = Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_MOVE);
            bool moveHeld   = Input.GetMouseButton(Constants.Player.MOUSE_BUTTON_MOVE);
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
