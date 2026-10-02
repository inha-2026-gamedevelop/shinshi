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

        // 이전 판의 늦은 입력이 섞이지 않도록 프로세스가 열릴 때마다 다른 번호를 쓴다. 0은 클라이언트의 미배정 값이라 피한다
        // TODO(민성): 출정 연결이 생기면 출정 sessionId에서 만든 번호로 바꾼다
        private static int NewSessionEpoch()
        {
            return new System.Random().Next(CombatIds.NONE + 1, int.MaxValue);
        }
    }
}
