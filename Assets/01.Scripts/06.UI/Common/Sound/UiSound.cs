// Unity
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinshi.UI
{
    /// <summary>
    /// UI 효과음을 재생하는 단일 창구. 그룹은 어느 UI인지, 동작은 무엇을 했는지이며
    /// 해당 칸이 비어 있으면 공통 그룹의 같은 동작을 쓴다. 네트워크로 보내지 않고 각 클라이언트가 직접 재생한다
    /// </summary>
    public static class UiSound
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PLAYER_NAME = "UiSoundPlayer";

        private static UiSoundPlayer _player        = null;
        private static bool          _isUnavailable = false;

        /****************************************
        *                Methods
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _player        = null;
            _isUnavailable = false;
        }

        public static void PlayWindowTransition(GameObject window, bool open)
        {
            if ((!Application.isPlaying) || (Time.frameCount <= 1) || (window == null) || (!window.activeSelf && open)
                || ((window.activeSelf) && (!open)))
            {
                return;
            }

            Play(EUiSoundGroup.Common, open ? EUiSoundAction.Open : EUiSoundAction.Close);
        }

        public static void Play(EUiSoundGroup group, EUiSoundAction action)
        {
            UiSoundPlayer player = GetPlayer();
            if (player == null)
            {
                return;
            }

            player.Play(group, action);
        }

        // 처음 부를 때 만들어 씬마다 배치하지 않아도 된다. 소리가 필요 없는 서버와 에디트 모드에서는 만들지 않는다.
        // PersistentSingleton이 아직 없어 ModernDarkRuntimeBootstrap처럼 정적 창구가 직접 만들어 들고 있는다
        // TODO(진욱): PersistentSingleton이 생기면 UiSoundPlayer를 그 위로 옮긴다
        private static UiSoundPlayer GetPlayer()
        {
            if (_player != null)
            {
                return _player;
            }

            if ((_isUnavailable) || (!Application.isPlaying))
            {
                return null;
            }

            if ((Application.isBatchMode) || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null))
            {
                _isUnavailable = true;
                return null;
            }

            UiSoundCatalogSO catalog = UiSoundCatalogSO.Load();
            if (catalog == null)
            {
                Debug.LogWarning($"[UiSound] Resources/{UiSoundCatalogSO.RESOURCE_NAME} 카탈로그가 없어 UI 효과음을 끕니다");
                _isUnavailable = true;
                return null;
            }

            GameObject playerObject = new GameObject(PLAYER_NAME);
            Object.DontDestroyOnLoad(playerObject);
            _player = playerObject.AddComponent<UiSoundPlayer>();
            _player.Initialize(catalog);
            return _player;
        }
    }
}
