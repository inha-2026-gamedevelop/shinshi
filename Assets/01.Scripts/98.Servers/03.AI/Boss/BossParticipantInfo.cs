// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 값은 호출한 시점의 서버 상태다. 참조를 보관하지 않고 틱마다 다시 묻는다
    /// <summary> 보스 기믹이 묻는 참가자 한 명의 현재 상태 </summary>
    public readonly struct BossParticipantInfo
    {
        /****************************************
        *              Properties
        ****************************************/

        public EParticipantLifeState LifeState { get; }

        /// <summary> 지금 접속 중이다, 재접속 유예 중인 생존자는 거짓이다 </summary>
        public bool IsConnected { get; }

        /// <summary> 쓰러졌어도 공용 부활 횟수가 남아 다시 돌아올 수 있다, 생존자에게는 의미가 없다 </summary>
        public bool IsRevivable { get; }

        public Vector3 Position { get; }

        /****************************************
        *                Methods
        ****************************************/

        public BossParticipantInfo(EParticipantLifeState lifeState, bool isConnected, bool isRevivable, Vector3 position)
        {
            LifeState   = lifeState;
            IsConnected = isConnected;
            IsRevivable = isRevivable;
            Position    = position;
        }
    }
}
