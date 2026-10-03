// System
using System;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 로비, 캐릭터 선택, 캐릭터 생성, 마을로 이어지는 클라이언트 진입 흐름의 씬 이동과 화면 사이 공유 값 </summary>
    public static class LobbyFlow
    {
        /****************************************
        *                Fields
        ****************************************/

        // 개발 로그인 기본값. 로컬 API를 Backend/README.md의 예시 주소로 띄운 경우다
        public const string DEV_API_BASE_URL = "http://127.0.0.1:5080";

        // 개발 로그인이 게임 세션을 받을 때 보내는 빌드 ID. API의 허용 buildId 목록에 같은 값을 넣어야 한다
        public const string DEV_BUILD_ID = "dev-local";

        // 개발 로그인의 기본 API 주소와 빌드 ID를 실행 인자로 바꾼다
        public const string ARG_API_BASE_URL = "-api-base-url";
        public const string ARG_BUILD_ID     = "-build-id";

        private static string _preferredCharacterId = null;
        private static string _notice               = null;
        private static string _contentHash          = null;

        /****************************************
        *              Properties
        ****************************************/

        // 캐릭터 생성 직후처럼 선택 화면이 먼저 고를 캐릭터
        public static string PreferredCharacterId
        {
            get => _preferredCharacterId;
            set => _preferredCharacterId = value;
        }

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 값이 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _preferredCharacterId = null;
            _notice               = null;
            _contentHash          = null;
        }

        /// <summary> 네트워크 매니저가 없으면 Bootstrap부터 다시 시작하고 false를 돌려준다. 에디터에서 흐름 씬을 바로 플레이할 때 쓴다 </summary>
        public static bool EnsureBootstrapped()
        {
            if (NetworkManager.singleton != null)
            {
                return true;
            }

            SceneManager.LoadScene(Constants.Scene.BOOTSTRAP);
            return false;
        }

        public static void GoToLobby()           { SceneManager.LoadScene(Constants.Scene.LOBBY); }
        public static void GoToCharacterSelect() { SceneManager.LoadScene(Constants.Scene.CHARACTER_SELECT); }
        public static void GoToCharacterCreate() { SceneManager.LoadScene(Constants.Scene.CHARACTER_CREATE); }

        /// <summary> 마을 접속이 끊겼을 때 선택 화면으로 돌아가며 안내를 남긴다 </summary>
        public static void ReturnToCharacterSelect(string notice)
        {
            _notice = notice;
            GoToCharacterSelect();
        }

        /// <summary> 남긴 안내를 한 번만 꺼낸다 </summary>
        public static string TakeNotice()
        {
            string notice = _notice;
            _notice = null;
            return notice;
        }

        /// <summary> 게임 세션 발급의 빌드 검사에 보낼 값. GameDB가 준비되지 않았으면 null이다 </summary>
        public static ClientBuildInfo CreateBuildInfo()
        {
            if (string.IsNullOrEmpty(_contentHash))
            {
                try
                {
                    _contentHash = GameDBContentHash.Compute();
                }
                catch (Exception exception)
                {
                    // 데이터 값이 예외 메시지에 들어갈 가능성이 있어 타입만 기록한다
                    Debug.LogError($"[Lobby] contentHash 계산 실패 ({exception.GetType().Name})");
                    return null;
                }
            }

            string buildId = ReadArgument(ARG_BUILD_ID) ?? DEV_BUILD_ID;
            return new ClientBuildInfo(buildId, Constants.Network.PROTOCOL_VERSION, _contentHash);
        }

        public static string ReadDevApiBaseUrl()
        {
            return ReadArgument(ARG_API_BASE_URL) ?? DEV_API_BASE_URL;
        }

        private static string ReadArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < (args.Length - 1); ++i)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
