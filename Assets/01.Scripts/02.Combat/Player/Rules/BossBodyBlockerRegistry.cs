// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    // 소유 클라이언트 예측(PlayerPrediction)이 보스 복제 상태를 찾으려고 매 프레임 탐색하지 않도록 스폰된 보스가 스스로 등록한다.
    // 클라이언트 전용이고 서버 판정은 이 목록을 보지 않는다(서버는 틱 순서에서 보스 몸체를 직접 넘긴다)
    /// <summary> 클라이언트에 스폰된 보스 몸체 목록 </summary>
    public static class BossBodyBlockerRegistry
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int INITIAL_CAPACITY = 2;

        private static readonly List<IBossBodyBlocker> _blockers = new List<IBossBodyBlocker>(INITIAL_CAPACITY);

        /****************************************
        *              Properties
        ****************************************/

        public static int Count => _blockers.Count;

        /****************************************
        *                Methods
        ****************************************/

        public static IBossBodyBlocker Get(int index)
        {
            return _blockers[index];
        }

        /// <summary> 같은 몸체는 한 번만 담는다 </summary>
        public static void Register(IBossBodyBlocker blocker)
        {
            if ((blocker == null) || (_blockers.Contains(blocker)))
            {
                return;
            }

            _blockers.Add(blocker);
        }

        public static void Unregister(IBossBodyBlocker blocker)
        {
            if (blocker == null)
            {
                return;
            }

            _blockers.Remove(blocker);
        }

        // 도메인 리로드를 끈 에디터에서 지난 플레이의 보스가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _blockers.Clear();
        }
    }
}
