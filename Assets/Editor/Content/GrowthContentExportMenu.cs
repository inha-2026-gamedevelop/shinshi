// System
using System.IO;
using System.Text;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Editor
{
    /// <summary> GameDB 성장 SO 값으로 백엔드 growth-content.json을 다시 쓴다 </summary>
    public static class GrowthContentExportMenu
    {
        // SO 값을 바꾼 뒤 이 메뉴로 파일을 다시 쓰고 백엔드 테스트를 돌린다. 파일이 낡으면 EditMode 일치 테스트가 실패한다.
        // 콘텐츠 버전(GameDatabaseSO)도 함께 적힌다. 수치를 바꾸면 버전도 올린다

        /****************************************
        *              Constants
        ****************************************/

        private const string MENU_PATH = "Shinshi/Export Growth Content";

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem(MENU_PATH)]
        private static void Export()
        {
            GameDatabaseSO root = Resources.Load<GameDatabaseSO>(Constants.Resource.GAME_DB_PATH);
            if (root == null)
            {
                Debug.LogError($"[GrowthContent] GameDB 에셋을 찾지 못했다, Resources/{Constants.Resource.GAME_DB_PATH}");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path        = GrowthContentExport.GetPath(projectRoot);
            string json        = GrowthContentExport.Serialize(root);
            File.WriteAllText(path, json, new UTF8Encoding(false));
            Debug.Log($"[GrowthContent] 내보냈다, {path}, contentVersion={root.ContentVersion}");
        }
    }
}
