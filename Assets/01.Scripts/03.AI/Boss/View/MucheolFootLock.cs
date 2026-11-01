// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 클립은 몸 위치를 기준으로 발이 움직이게 만들어져 있어 Idle에서도 발이 계속 움직인다. 발이 지면에 닿는 순간의 월드 위치를 기억해 그 자리에 IK로 고정하고,
    // 클립에서 발이 들리거나 고정 위치에서 멀어지면 풀어 새 자리에 다시 고정한다. 몸 위치(서버 위치)와 클립 재생은 건드리지 않는다.
    // 고정과 해제가 짧게 반복되면 발이 떨리므로 상태를 바꾼 뒤 일정 시간은 유지하고, 발이 고정 위치와 클립 위치 사이를 오갈 때는 순간이동하지 않고 정해진 속도로 이어 간다.
    // OnAnimatorIK는 Animator가 붙은 오브젝트에서만 불리므로 Model에 붙이고, 컨트롤러 기본 레이어의 IK Pass를 켜 둬야 한다
    /// <summary> 무철 발 고정, 지면에 닿은 발을 월드에 박고 몸이 그 위에서 움직이게 한다 </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class MucheolFootLock : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   FOOT_COUNT = 2;
        private const float SETTLED_DISTANCE = 0.02f; // m, 핀과 클립의 발이 이 안이면 같은 자리로 보고 IK를 끈다
        private const float MIN_DELTA        = 0.0001f; // 초, 속도 계산에서 0으로 나누지 않게 하는 프레임 시간 하한

        /****************************************
        *               Types
        ****************************************/

        private struct FootState
        {
            public bool    IsLocked;
            public bool    WasAnchored;    // 지난 프레임에 기준 발이었다
            public bool    IsPinned;       // IK를 걸고 있다, 고정 중이거나 클립 위치로 돌아가는 중이다
            public Vector3 LockPosition;
            public Vector3 PinPosition;
            public Vector3 PreviousLocal;
            public float   StateSeconds;   // 마지막으로 고정 상태가 바뀐 뒤 흐른 시간
            public bool    HasPrevious;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private float _plantHeight    = 0.30f; // m, 이보다 낮으면 지면에 닿은 발로 본다
        [SerializeField] private float _releaseHeight  = 0.42f; // m, 고정 중 이보다 들리면 푼다
        [SerializeField] private float _plantSpeed     = 0.8f;  // m/s, 보스 기준 수평 속도가 이보다 느려야 고정한다
        [SerializeField] private float _maxStretch     = 0.45f; // m, 고정 위치에서 클립의 발이 이보다 멀어지면 풀고 새로 딛는다
        [SerializeField] private float _minLockSeconds = 0.25f; // 초, 고정한 뒤 이 시간은 늘어남 때문에 풀지 않는다, 발이 들리면 바로 푼다
        [SerializeField] private float _minFreeSeconds = 0.20f; // 초, 푼 뒤 이 시간은 다시 고정하지 않는다
        [SerializeField] private float _moveSpeed      = 4f;    // m/s, 핀이 새 목표(새 고정 위치, 클립 위치)로 옮겨 가는 속도

        private static readonly AvatarIKGoal[] G_Goals = { AvatarIKGoal.LeftFoot, AvatarIKGoal.RightFoot };

        private Animator             _animator;
        private Transform            _root;
        private MucheolMotionView    _motion;
        private readonly FootState[] _feet = new FootState[FOOT_COUNT];

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _root     = transform.root;
            _motion   = GetComponentInParent<MucheolMotionView>();
        }

        private void OnDisable()
        {
            for (int i = 0; i < FOOT_COUNT; ++i)
            {
                _feet[i] = default;
            }
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0)
            {
                return;
            }

            float  delta           = Mathf.Max(Time.deltaTime, MIN_DELTA);
            bool   hasMotion       = (_motion != null) && (_motion.Director != null);
            string clip            = hasMotion ? _motion.Director.PlayingClip : null;
            float  clipSeconds     = hasMotion ? _motion.Director.ClipSeconds : 0f;
            bool   isLeftAnchored  = hasMotion && MucheolClipBodyShiftCatalog.IsLeftFootAnchored(clip, clipSeconds);
            bool   isRightAnchored = hasMotion && MucheolClipBodyShiftCatalog.IsRightFootAnchored(clip, clipSeconds);
            bool   isReleased      = hasMotion && MucheolClipBodyShiftCatalog.IsFootLockReleased(clip, clipSeconds);
            for (int i = 0; i < FOOT_COUNT; ++i)
            {
                bool isAnchored = (isLeftAnchored && (G_Goals[i] == AvatarIKGoal.LeftFoot)) || (isRightAnchored && (G_Goals[i] == AvatarIKGoal.RightFoot));
                UpdateFoot(ref _feet[i], G_Goals[i], delta, isAnchored, isReleased);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // isAnchored이면 이 발은 클립이 끝날 때까지 풀지 않고, 클립에서 들려도 높이를 처음 지면 높이에 둔다. 몸 이동 경로가 이 발을 기준으로 잰 것이라 위치는 따라온다
        // isReleased이면 양발의 IK를 바로 끄고 새로 고정하지도 않는다
        private void UpdateFoot(ref FootState foot, AvatarIKGoal goal, float delta, bool isAnchored, bool isReleased)
        {
            Vector3 animated = _animator.GetIKPosition(goal);
            Vector3 local    = _root.InverseTransformPoint(animated);
            float   speed    = foot.HasPrevious ? (new Vector2(local.x - foot.PreviousLocal.x, local.z - foot.PreviousLocal.z).magnitude / delta) : float.MaxValue;
            foot.PreviousLocal = local;
            foot.HasPrevious   = true;
            foot.StateSeconds += delta;

            if (isAnchored)
            {
                // 고정 클립이 시작되는 순간의 발 위치를 새 고정 위치로 삼는다. 몸 이동 경로가 이 자세를 기준으로 잰 값이기 때문이다
                if (!foot.WasAnchored)
                {
                    foot.IsLocked     = true;
                    foot.LockPosition = animated;
                    foot.StateSeconds = 0f;
                    if (!foot.IsPinned)
                    {
                        foot.PinPosition = animated;
                    }
                }
            }
            else if (isReleased)
            {
                // 이어 가지 않고 IK 자체를 바로 끈다. 몸 이동 경로가 기준 발의 위치를 따라와 핀과 클립의 발 차이가 작다
                foot.IsLocked     = false;
                foot.IsPinned     = false;
                foot.PinPosition  = animated;
                foot.StateSeconds = 0f;
            }
            else if (!foot.IsLocked)
            {
                bool canPlant = (foot.StateSeconds >= _minFreeSeconds) && (local.y < _plantHeight) && (speed < _plantSpeed);
                if (canPlant)
                {
                    foot.IsLocked     = true;
                    foot.LockPosition = animated;
                    foot.StateSeconds = 0f;
                    if (!foot.IsPinned)
                    {
                        foot.PinPosition = animated;
                    }
                }
            }
            else
            {
                Vector2 away     = new Vector2(animated.x - foot.LockPosition.x, animated.z - foot.LockPosition.z);
                bool    isLifted = local.y > _releaseHeight;
                bool    isFar    = (away.magnitude > _maxStretch) && (foot.StateSeconds >= _minLockSeconds);
                if (isLifted || isFar)
                {
                    foot.IsLocked     = false;
                    foot.StateSeconds = 0f;
                }
            }

            // 핀은 고정 중이면 고정 위치로, 풀렸으면 클립의 발로 정해진 속도로 이어 간다. 다 닿으면 IK를 끈다
            if (foot.IsLocked || foot.IsPinned)
            {
                Vector3 target = foot.IsLocked ? foot.LockPosition : animated;
                Vector3 flat   = new Vector3(target.x, foot.PinPosition.y, target.z);
                foot.PinPosition = Vector3.MoveTowards(foot.PinPosition, flat, _moveSpeed * delta);
                foot.PinPosition.y = isAnchored ? foot.LockPosition.y : animated.y;
                foot.IsPinned = foot.IsLocked || (new Vector2(foot.PinPosition.x - animated.x, foot.PinPosition.z - animated.z).magnitude > SETTLED_DISTANCE);
            }

            foot.WasAnchored = isAnchored;
            if (!foot.IsPinned)
            {
                _animator.SetIKPositionWeight(goal, 0f);
                return;
            }

            // 수평만 월드에 박고 높이는 클립을 따라 발이 들리고 눌리는 모습을 남긴다
            _animator.SetIKPositionWeight(goal, 1f);
            _animator.SetIKPosition(goal, foot.PinPosition);
        }
    }
}
