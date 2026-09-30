// Unity
using UnityEngine;

namespace Shinshi.Common
{
    /// <summary> 필수 설정이나 데이터가 없을 때 기본값으로 이어 가지 않고 실행을 멈춘다 </summary>
    public static class BootFailure
    {
        /****************************************
        *              Constants
        ****************************************/

        // 서버 관리 스크립트가 정상 종료와 구분할 수 있게 0이 아닌 값으로 끝낸다
        private const int EXIT_CODE = 1;

        /****************************************
        *                Methods
        ****************************************/

        public static void Stop(string reason)
        {
            Debug.LogError($"[Boot] 시작 중단: {reason}");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(EXIT_CODE);
#endif
        }
    }
}
