#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 프로젝트
using Shinshi.Art;

namespace Shinshi.Dev
{
    /// <summary> Dev_BossWolbaek에서 보스 아레나 붕괴 연출을 키로 시험한다. 키: 1 재생, 2 원래대로 </summary>
    public sealed class DevBossArenaShatterTrigger : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 본 게임에서 1은 소형 물약 키다. 플레이어가 없는 Dev 씬에서만 쓰므로 겹쳐도 된다
        private const KeyCode PLAY_KEY  = KeyCode.Alpha1;
        private const KeyCode RESET_KEY = KeyCode.Alpha2;

        [SerializeField] private BossArenaShatterView _view = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (_view == null)
            {
                return;
            }

            if (Input.GetKeyDown(PLAY_KEY))
            {
                _view.Play();
            }
            else if (Input.GetKeyDown(RESET_KEY))
            {
                _view.ResetToIntact();
            }
        }
    }
}
#endif
