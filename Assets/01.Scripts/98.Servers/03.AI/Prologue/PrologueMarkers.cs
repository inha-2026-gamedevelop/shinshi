// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 프롤로그 전장 씬(DungeonPrologue)에 이름으로 놓인 위치 마커를 서버가 세션 시작 때 한 번 읽어 둔다.
    // 마커는 PrologueMarkers 오브젝트 아래에 두는 것이 기본이고, 그 아래에서 못 찾으면 씬 전체에서 같은 이름을 찾는다.
    // 위치는 모두 NavMesh 위여야 한다(소환한 산귀와 순간 이동한 플레이어가 NavMesh 밖에 서지 않게)
    /// <summary> 프롤로그 숲 시작점, 부적, 산귀 소환점, 월백 전장 중심과 플레이어 시작점 </summary>
    public sealed class PrologueMarkers
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string ROOT_NAME               = "PrologueMarkers";
        public const string FOREST_PLAYER_START     = "ForestPlayerStart";
        public const string TALISMAN                = "Talisman";
        public const string WOLBAEK_ARENA_CENTER    = "PrologueWolbaekArenaCenter";
        public const string WOLBAEK_PLAYER_START    = "PrologueWolbaekPlayerStart";
        public const string NORMAL_ENEMY_FORMAT     = "NormalEnemy_{0}";
        public const string TELEGRAPH_ENEMY_FORMAT  = "TelegraphEnemy_{0}";

        // 일반 산귀 첫 전투의 마리 수, 예고형 산귀 마리 수. ForestProgress의 규칙과 같다
        public const int NORMAL_ENEMY_COUNT    = 2;
        public const int TELEGRAPH_ENEMY_COUNT = 1;

        // 플레이어가 부적에 닿았다고 보는 수평 반경(m)
        public const float TALISMAN_RADIUS = 2.5f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3   _forestPlayerStart;
        private readonly Vector3   _talisman;
        private readonly Vector3   _wolbaekCenter;
        private readonly Vector3   _wolbaekPlayerStart;
        private readonly Vector3[] _normalEnemies;
        private readonly Vector3[] _telegraphEnemies;

        /****************************************
        *              Properties
        ****************************************/

        public Vector3 ForestPlayerStart  => _forestPlayerStart;
        public Vector3 Talisman           => _talisman;
        public Vector3 WolbaekCenter      => _wolbaekCenter;
        public Vector3 WolbaekPlayerStart => _wolbaekPlayerStart;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueMarkers(Vector3 forestPlayerStart, Vector3 talisman, Vector3 wolbaekCenter, Vector3 wolbaekPlayerStart,
            Vector3[] normalEnemies, Vector3[] telegraphEnemies)
        {
            _forestPlayerStart  = forestPlayerStart;
            _talisman           = talisman;
            _wolbaekCenter      = wolbaekCenter;
            _wolbaekPlayerStart = wolbaekPlayerStart;
            _normalEnemies      = normalEnemies;
            _telegraphEnemies   = telegraphEnemies;
        }

        /// <summary> 산귀 종류와 번호에 맞는 소환 위치, 번호가 범위 밖이면 거짓이다 </summary>
        public bool TryGetEnemyPoint(EPrologueEnemyKind kind, int spawnIndex, out Vector3 point)
        {
            Vector3[] points = (kind == EPrologueEnemyKind.Telegraph) ? _telegraphEnemies : _normalEnemies;
            if ((points == null) || (spawnIndex < 0) || (spawnIndex >= points.Length))
            {
                point = default;
                return false;
            }

            point = points[spawnIndex];
            return true;
        }

        /// <summary> 열린 씬에서 마커를 모두 읽는다. 하나라도 없으면 거짓이고 빠진 이름을 사유에 적는다 </summary>
        public static bool TryLoad(out PrologueMarkers markers, out string error)
        {
            markers = null;
            error   = null;

            GameObject root = GameObject.Find(ROOT_NAME);
            var missing = new List<string>();

            bool ok = TryFind(root, FOREST_PLAYER_START, missing, out Vector3 forestStart);
            ok &= TryFind(root, TALISMAN, missing, out Vector3 talisman);
            ok &= TryFind(root, WOLBAEK_ARENA_CENTER, missing, out Vector3 center);
            ok &= TryFind(root, WOLBAEK_PLAYER_START, missing, out Vector3 wolbaekStart);

            var normal = new Vector3[NORMAL_ENEMY_COUNT];
            for (int i = 0; i < normal.Length; ++i)
            {
                ok &= TryFind(root, string.Format(NORMAL_ENEMY_FORMAT, i), missing, out normal[i]);
            }

            var telegraph = new Vector3[TELEGRAPH_ENEMY_COUNT];
            for (int i = 0; i < telegraph.Length; ++i)
            {
                ok &= TryFind(root, string.Format(TELEGRAPH_ENEMY_FORMAT, i), missing, out telegraph[i]);
            }

            if (!ok)
            {
                error = "프롤로그 마커를 찾지 못했다: " + string.Join(", ", missing);
                return false;
            }

            markers = new PrologueMarkers(forestStart, talisman, center, wolbaekStart, normal, telegraph);
            return true;
        }

        private static bool TryFind(GameObject root, string markerName, List<string> missing, out Vector3 position)
        {
            Transform found = null;
            if (root != null)
            {
                Transform[] children = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < children.Length; ++i)
                {
                    if (string.Equals(children[i].name, markerName, System.StringComparison.Ordinal))
                    {
                        found = children[i];
                        break;
                    }
                }
            }

            if (found == null)
            {
                GameObject byName = GameObject.Find(markerName);
                found = (byName != null) ? byName.transform : null;
            }

            if (found == null)
            {
                missing.Add(markerName);
                position = default;
                return false;
            }

            position = found.position;
            return true;
        }
    }
}
