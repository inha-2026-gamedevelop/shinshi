// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 값만 담는다. 클라이언트는 이 시각들로 전환과 자리 유지 단계를 재현하고 판정은 하지 않는다
    // 구조물 목록은 기믹이 시작할 때 한 번 만들어 지고 이후 바뀌지 않는다
    /// <summary> 사방 부적진 기믹의 시작 입력, 구조물 배치와 배정과 단계 시각 </summary>
    public readonly struct WolbaekTalismanStart
    {
        /****************************************
        *              Properties
        ****************************************/

        public int BossCombatId { get; }

        /// <summary> 서버가 발급한 시전 번호, 이 기믹의 모든 알림이 같은 번호를 싣는다 </summary>
        public int CastInstanceId { get; }

        /// <summary> 기믹에 진입한 서버 시각 </summary>
        public double StartServerTime { get; }

        /// <summary> 공격 없는 전환 시간이 끝나는 서버 시각, 이 시각부터 자리 유지를 잰다 </summary>
        public double TransitionEndServerTime { get; }

        /// <summary> 초, 필수 참가자 전원이 자기 자리에 연속으로 머물러야 하는 시간 </summary>
        public float HoldSeconds { get; }

        /// <summary> 구조물을 둔 기준점, 보스 공격의 중심이기도 하다 </summary>
        public Vector3 Center { get; }

        public IReadOnlyList<WolbaekTalismanStructure> Structures { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekTalismanStart(int bossCombatId, int castInstanceId, double startServerTime, double transitionEndServerTime,
            float holdSeconds, Vector3 center, IReadOnlyList<WolbaekTalismanStructure> structures)
        {
            BossCombatId            = bossCombatId;
            CastInstanceId          = castInstanceId;
            StartServerTime         = startServerTime;
            TransitionEndServerTime = transitionEndServerTime;
            HoldSeconds             = holdSeconds;
            Center                  = center;
            Structures              = structures;
        }
    }
}
