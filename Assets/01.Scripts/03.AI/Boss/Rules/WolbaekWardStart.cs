// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 값만 담는다. 클라이언트는 이 값으로 개인 색과 중앙 결계와 연결 띠 경고 거리를 재현하고 판정은 하지 않는다
    // 명단은 기믹이 시작할 때 한 번 만들어지고 이후 바뀌지 않는다
    /// <summary> 색 결계 기믹의 시작 입력, 배정 명단과 중앙 결계와 단계 시간 </summary>
    public readonly struct WolbaekWardStart
    {
        /****************************************
        *              Properties
        ****************************************/

        public int BossCombatId { get; }

        /// <summary> 서버가 발급한 시전 번호, 이 기믹의 모든 알림이 같은 번호를 싣는다 </summary>
        public int CastInstanceId { get; }

        /// <summary> 기믹에 진입한 서버 시각 </summary>
        public double StartServerTime { get; }

        /// <summary> 중앙 공동 결계의 중심, 전장 중심이다 </summary>
        public Vector3 GatherCenter { get; }

        /// <summary> m, 중앙 공동 결계의 반경 </summary>
        public float GatherRadius { get; }

        /// <summary> 초, 필수 참가자 전원이 중앙 결계 안에 연속으로 머물러야 하는 시간 </summary>
        public float GatherHoldSeconds { get; }

        /// <summary> m, 색 결계의 보호 반경 </summary>
        public float WardRadius { get; }

        /// <summary> m, 같은 색 두 명을 잇는 띠가 빨갛게 경고하기 시작하는 거리 </summary>
        public float LinkWarnDistance { get; }

        /// <summary> m, 같은 색 두 명 사이가 이 거리를 넘으면 거리 피해가 시작된다 </summary>
        public float LinkDamageDistance { get; }

        /// <summary> 시작 명단, 쓰러진 사람과 접속이 끊긴 사람을 포함한다 </summary>
        public IReadOnlyList<WolbaekWardMember> Members { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekWardStart(int bossCombatId, int castInstanceId, double startServerTime, Vector3 gatherCenter,
            float gatherRadius, float gatherHoldSeconds, float wardRadius, float linkWarnDistance, float linkDamageDistance,
            IReadOnlyList<WolbaekWardMember> members)
        {
            BossCombatId       = bossCombatId;
            CastInstanceId     = castInstanceId;
            StartServerTime    = startServerTime;
            GatherCenter       = gatherCenter;
            GatherRadius       = gatherRadius;
            GatherHoldSeconds  = gatherHoldSeconds;
            WardRadius         = wardRadius;
            LinkWarnDistance   = linkWarnDistance;
            LinkDamageDistance = linkDamageDistance;
            Members            = members;
        }
    }
}
