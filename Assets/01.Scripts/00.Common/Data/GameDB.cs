// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Data
{
    /// <summary> 읽기 전용 GameDB 정적 접근, 초기화는 GameDBBootstrap만 한다 </summary>
    public static class GameDB
    {
        /****************************************
        *                Fields
        ****************************************/

        private static GameDatabaseSO _root;

        public static bool IsInitialized => _root != null;

        public static string          ContentVersion => Root.ContentVersion;
        public static PlayerConfigSO  Player         => Root.Player;
        public static SessionConfigSO Session        => Root.Session;

        private static GameDatabaseSO Root
        {
            get
            {
                if (_root == null)
                {
                    throw new InvalidOperationException("GameDB가 초기화되지 않았다");
                }

                return _root;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 참조가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _root = null;
        }

        internal static void ResetForTests()
        {
            _root = null;
        }

        /// <summary> 루트를 검증하고 등록한다, 같은 루트로 다시 불러도 안전하다 </summary>
        public static void Initialize(GameDatabaseSO root)
        {
            if (root == null)
            {
                throw new InvalidOperationException("GameDB 루트 에셋이 없다");
            }

            if (_root == root)
            {
                return;
            }

            // 실행 중에 루트가 바뀌면 이미 읽은 값과 새 값이 섞인다
            if (_root != null)
            {
                throw new InvalidOperationException("GameDB가 이미 다른 루트로 초기화됐다");
            }

            var errors = new List<string>();
            root.Validate(errors);

            if (errors.Count > 0)
            {
                throw new InvalidOperationException($"GameDB 검증 실패 {errors.Count}건: {string.Join("; ", errors)}");
            }

            _root = root;
        }
    }
}
