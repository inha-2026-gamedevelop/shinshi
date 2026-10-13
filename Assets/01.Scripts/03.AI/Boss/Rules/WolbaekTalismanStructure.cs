// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 배치다. 클라이언트는 색과 문양과 배정된 사람만 표시하고 목적지 방향 화살표는 만들지 않는다
    /// <summary> 사방 부적진 구조물 하나, 위치와 색과 문양과 배정된 참가자 </summary>
    public readonly struct WolbaekTalismanStructure
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 방위 번호, 0 동, 1 북, 2 서, 3 남이다. 같은 번호는 반대편과 2 차이가 난다 </summary>
        public int DirectionIndex { get; }

        public EWolbaekTalismanColor Color { get; }

        /// <summary> 문양 번호, 색과 함께 구분하도록 색마다 다르다. 실제 그림은 아트 작업이 정한다 </summary>
        public int SymbolId { get; }

        public Vector3 Center { get; }

        /// <summary> m, 이 반경 안에 머물러야 한다 </summary>
        public float Radius { get; }

        /// <summary> 이 구조물에 배정된 필수 참가자의 전투 번호, 사망과 재접속 뒤에도 바뀌지 않는다 </summary>
        public int AssignedCombatId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekTalismanStructure(int directionIndex, EWolbaekTalismanColor color, int symbolId, Vector3 center,
            float radius, int assignedCombatId)
        {
            DirectionIndex   = directionIndex;
            Color            = color;
            SymbolId         = symbolId;
            Center           = center;
            Radius           = radius;
            AssignedCombatId = assignedCombatId;
        }
    }
}
