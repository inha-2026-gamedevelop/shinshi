// System
using System.IO;
using System.Text.RegularExpressions;

// Unity
using UnityEditor;

namespace Shinshi.Editor
{
    /// <summary>
    /// 스크립트 템플릿의 #NAMESPACE#를 생성 위치의 도메인 네임스페이스로 바꾼다.
    /// </summary>
    public class ScriptTemplateNamespaceProcessor : AssetModificationProcessor
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string NAMESPACE_KEYWORD = "#NAMESPACE#";
        private const string ROOT_NAMESPACE    = "Shinshi";
        private const string META_EXTENSION    = ".meta";
        private const string SCRIPT_EXTENSION  = ".cs";
        private const string SCRIPTS_ROOT      = "Assets/01.Scripts/";
        private const string SERVERS_FOLDER    = "98.Servers/";
        private const string DATA_FOLDER       = "00.Common/Data/";
        private const string EDITOR_ROOT       = "Assets/Editor/";
        private const string DATA_DOMAIN       = "Data";
        private const string EDITOR_DOMAIN     = "Editor";

        // "02.Combat/..." 에서 번호를 뗀 도메인 이름만 잡는다.
        private static readonly Regex G_DomainFolder = new Regex(@"^\d+\.([A-Za-z]+)/");

        /****************************************
        *                Methods
        ****************************************/

        private static void OnWillCreateAsset(string metaPath)
        {
            string assetPath = metaPath.Replace('\\', '/');
            if (assetPath.EndsWith(META_EXTENSION))
            {
                assetPath = assetPath.Substring(0, assetPath.Length - META_EXTENSION.Length);
            }

            if ((!assetPath.EndsWith(SCRIPT_EXTENSION)) || (!File.Exists(assetPath)))
            {
                return;
            }

            string content = File.ReadAllText(assetPath);
            if (!content.Contains(NAMESPACE_KEYWORD))
            {
                return;
            }

            File.WriteAllText(assetPath, content.Replace(NAMESPACE_KEYWORD, ResolveNamespace(assetPath)));
        }

        private static string ResolveNamespace(string assetPath)
        {
            if (assetPath.StartsWith(EDITOR_ROOT))
            {
                return $"{ROOT_NAMESPACE}.{EDITOR_DOMAIN}";
            }

            if (!assetPath.StartsWith(SCRIPTS_ROOT))
            {
                return ROOT_NAMESPACE;
            }

            string relativePath = assetPath.Substring(SCRIPTS_ROOT.Length);
            if (relativePath.StartsWith(DATA_FOLDER))
            {
                return $"{ROOT_NAMESPACE}.{DATA_DOMAIN}";
            }

            // 98.Servers 아래 서버 클래스는 원래 도메인 경로를 그대로 비추므로 그 도메인의 네임스페이스를 쓴다.
            if (relativePath.StartsWith(SERVERS_FOLDER))
            {
                relativePath = relativePath.Substring(SERVERS_FOLDER.Length);
            }

            Match match = G_DomainFolder.Match(relativePath);
            if (!match.Success)
            {
                return ROOT_NAMESPACE;
            }

            return $"{ROOT_NAMESPACE}.{match.Groups[1].Value}";
        }
    }
}
