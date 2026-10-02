// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 이동 경로의 모서리를 일정 속도로 따라간다, 서버 권위 이동과 소유 클라이언트 예측이 같이 쓴다 </summary>
    public sealed class PathFollower
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3[] _corners;

        private int _count = 0;
        private int _index = 0;

        /****************************************
        *              Properties
        ****************************************/

        // NavMeshPath.GetCornersNonAlloc에 그대로 넘기는 버퍼
        public Vector3[] CornerBuffer => _corners;

        public bool IsMoving => _index < _count;

        /****************************************
        *                Methods
        ****************************************/

        public PathFollower(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "경로 모서리 버퍼는 1 이상이다");
            }

            _corners = new Vector3[capacity];
        }

        /// <summary> 버퍼에 채운 모서리 수로 경로를 시작한다, 0번은 출발점이라 1번부터 향한다 </summary>
        public void Begin(int cornerCount)
        {
            _count = Mathf.Clamp(cornerCount, 0, _corners.Length);
            _index = (_count > 1) ? 1 : _count;
        }

        public void Stop()
        {
            _count = 0;
            _index = 0;
        }

        /// <summary> 이번 틱 이동 거리만큼 나아간 위치, 움직였으면 마지막 수평 진행 방향을 돌려준다 </summary>
        public Vector3 Step(Vector3 position, float distance, out Vector3 moveDirection, out bool moved)
        {
            moveDirection = Vector3.zero;
            moved         = false;

            while ((distance > 0f) && (_index < _count))
            {
                Vector3 target    = _corners[_index];
                Vector3 delta     = target - position;
                float   lengthSqr = delta.sqrMagnitude;
                float   length    = Mathf.Sqrt(lengthSqr);

                if (lengthSqr > Constants.Player.DIRECTION_EPSILON_SQR)
                {
                    // 수직으로만 오르내리는 구간은 바라보는 방향을 바꾸지 않는다
                    if (FacingRules.TryGetHorizontalDirection(delta, out Vector3 horizontal))
                    {
                        moveDirection = horizontal;
                    }

                    moved = true;
                }

                if (length <= distance)
                {
                    position = target;
                    distance -= length;
                    ++_index;
                    continue;
                }

                position += delta * (distance / length);
                distance  = 0f;
            }

            return position;
        }
    }
}
