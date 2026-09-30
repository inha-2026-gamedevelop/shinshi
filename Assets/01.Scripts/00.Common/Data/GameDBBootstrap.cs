// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> GameDB를 초기화하는 유일한 경로 </summary>
    [DisallowMultipleComponent]
    public class GameDBBootstrap : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // BeforeSceneLoad에서 실패하면 컴포넌트의 Awake가 같은 실패를 다시 기록하지 않게 한다
        private static bool _hasFailed;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _hasFailed = false;
        }

        // Mirror가 서버 시작 전에 매니저를 비활성화할 수 있어 Awake만으로는 부트 순서를 보장하지 못한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeBeforeSceneLoad()
        {
            TryInitialize();
        }

        private void Awake()
        {
            TryInitialize();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 아직 초기화 전이면 Resources의 루트를 읽어 초기화한다. 실패하면 예외를 던진다 </summary>
        public static void EnsureInitialized()
        {
            if (GameDB.IsInitialized)
            {
                return;
            }

            GameDatabaseSO root = Resources.Load<GameDatabaseSO>(Constants.Resource.GAME_DB_PATH);
            GameDB.Initialize(root);
        }

        // 필수 데이터가 없으면 기본값으로 전투를 열지 않고 실행을 멈춘다
        private static void TryInitialize()
        {
            if (_hasFailed)
            {
                return;
            }

            try
            {
                EnsureInitialized();
            }
            catch (InvalidOperationException e)
            {
                _hasFailed = true;
                BootFailure.Stop($"GameDB 초기화 실패 - {e.Message}");
            }
        }
    }
}
