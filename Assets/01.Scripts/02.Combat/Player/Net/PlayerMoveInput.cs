// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 클릭 이동 의도, 목적지만 보내고 경로와 위치는 서버가 정한다 </summary>
    public readonly struct PlayerMoveInput
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PlayerInputHeader _header;
        private readonly Vector3           _destination;

        /****************************************
        *              Properties
        ****************************************/

        public PlayerInputHeader Header => _header;

        // 클릭한 지면 좌표. 서버가 내비 메시 위로 다시 찾는다
        public Vector3 Destination => _destination;

        /****************************************
        *                Methods
        ****************************************/

        public PlayerMoveInput(in PlayerInputHeader header, Vector3 destination)
        {
            _header      = header;
            _destination = destination;
        }
    }
}
