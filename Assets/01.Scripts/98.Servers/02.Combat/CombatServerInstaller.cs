// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Combat
{
    /// <summary> 서버 전투 판정을 서버 프로세스에 설치한다, 서버 부트스트랩이 서버를 열기 전에 한 번 부른다 </summary>
    public static class CombatServerInstaller
    {
        /****************************************
        *                Fields
        ****************************************/

        // FNV-1a 32비트 해시 상수, 출정 ID 문자열을 전투 번호로 접는다
        private const uint FNV_OFFSET_BASIS = 2166136261u;
        private const uint FNV_PRIME        = 16777619u;
        private const int  POSITIVE_MASK    = int.MaxValue;

        private static CombatServerRuntime _runtime;

        /****************************************
        *              Properties
        ****************************************/

        public static bool                IsInstalled => _runtime != null;
        public static CombatServerRuntime Runtime     => _runtime;

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 런타임 참조가 남지 않게 한다. ServerLinks도 같은 시점에 비워진다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _runtime = null;
        }

        /// <summary> GameDB 값으로 설치한다. 여러 번 불러도 한 번만 설치하며 실패하면 예외를 던지고 미설치로 남는다 </summary>
        public static void Install(IParticipantRoster roster)
        {
            if (!GameDB.IsInitialized)
            {
                throw new InvalidOperationException("GameDB 초기화 전에는 서버 전투를 설치하지 않는다");
            }

            Install(GameDB.Player, GameDB.Session, roster);
        }

        /// <summary> 설정을 받아 설치한다, 실패하면 만든 개체를 지우고 아무것도 등록하지 않은 채 예외를 다시 던진다 </summary>
        public static void Install(PlayerConfigSO playerConfig, SessionConfigSO sessionConfig, IParticipantRoster roster)
        {
            if (_runtime != null)
            {
                return;
            }

            if (sessionConfig == null)
            {
                throw new ArgumentNullException(nameof(sessionConfig));
            }

            // 지역 변수로 초기화와 등록을 모두 끝낸 뒤에만 정적 필드에 넣는다. 중간에 실패하면 반쯤 만든 런타임이
            // 설치된 것처럼 남아 다음 Install이 건너뛰고, 처리기 없는 전투가 열린다
            var host = new GameObject(nameof(CombatServerRuntime));
            CombatServerRuntime runtime;
            try
            {
                runtime = host.AddComponent<CombatServerRuntime>();
                runtime.Initialize(playerConfig, sessionConfig.ReconnectGraceSeconds, NewSessionEpoch(), roster);
                if (Application.isPlaying)
                {
                    Object.DontDestroyOnLoad(host);
                }

                // 마지막에 등록한다. 등록 뒤에는 실패할 단계가 없어 처리기만 남는 일이 없다
                ServerLinks.Register<PlayerInputState>(OnPlayerStarted, OnPlayerStopped);
            }
            catch
            {
                DestroyHost(host);
                throw;
            }

            _runtime = runtime;
        }

        private static void DestroyHost(GameObject host)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(host);
                return;
            }

            Object.DestroyImmediate(host);
        }

        private static void OnPlayerStarted(PlayerInputState state)
        {
            if (_runtime == null)
            {
                return;
            }

            _runtime.AddPlayer(state);
        }

        private static void OnPlayerStopped(PlayerInputState state)
        {
            if (_runtime == null)
            {
                return;
            }

            _runtime.RemovePlayer(state);
        }

        // 이전 판의 늦은 입력이 섞이지 않도록 프로세스가 열릴 때마다 다른 번호를 쓴다. 0은 클라이언트의 미배정 값이라 피한다.
        // 던전은 첫 전투 개체를 만들기 전에 런타임이 출정 ID에서 만든 번호(CreateSessionEpoch)로 바꾼다. 마을은 이 번호를 그대로 쓴다
        private static int NewSessionEpoch()
        {
            return new System.Random().Next(CombatIds.NONE + 1, int.MaxValue);
        }

        // 같은 출정이면 같은 번호라 로그와 보스 난수 씨앗을 출정으로 다시 찾을 수 있다. 출정마다 프로세스가 새로 뜨므로 이전 판과도 다르다
        /// <summary> 출정 ID에서 입력 머리의 세션 번호를 만든다, 0(미배정)이 아닌 양수다 </summary>
        public static int CreateSessionEpoch(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentException("출정 ID가 비어 있다", nameof(sessionId));
            }

            uint hash = FNV_OFFSET_BASIS;
            for (int i = 0; i < sessionId.Length; ++i)
            {
                hash ^= sessionId[i];
                hash *= FNV_PRIME;
            }

            int epoch = (int)(hash & POSITIVE_MASK);
            return (epoch > CombatIds.NONE) ? epoch : CombatIds.NONE + 1;
        }
    }
}
