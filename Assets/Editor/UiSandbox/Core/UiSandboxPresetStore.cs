using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 시나리오 프리셋 에셋의 경로 검증 · 불러오기 · 저장. 명시적으로 허용하지 않으면 기존 파일을 덮어쓰지 않는다 </summary>
    public static class UiSandboxPresetStore
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ASSET_EXTENSION = ".asset";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 프리셋을 저장할 수 있는 경로인지 검사한다. Assets/ 아래 .asset이며 샌드박스 씬 폴더는 제외한다 </summary>
        public static bool ValidatePath(string assetPath, out string error)
        {
            string path = UiSandboxSafetyPolicy.NormalizePath(assetPath);
            if (path.Length == 0)
            {
                error = "프리셋 저장 경로가 비어 있다";
                return false;
            }

            if ((!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                || (!path.EndsWith(ASSET_EXTENSION, StringComparison.OrdinalIgnoreCase))
                || (path.Length <= "Assets/".Length + ASSET_EXTENSION.Length))
            {
                error = $"프리셋 경로가 올바르지 않다: {path}. Assets/ 아래 .asset 파일이어야 한다";
                return false;
            }

            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; ++i)
            {
                if ((segments[i].Length == 0) || (segments[i] == ".") || (segments[i] == "..") || (segments[i].IndexOf(':') >= 0))
                {
                    error = $"프리셋 경로에 허용되지 않는 구간이 있다: {path}";
                    return false;
                }
            }

            if (path.StartsWith(UiSandboxSafetyPolicy.SANDBOX_FOLDER + "/", StringComparison.OrdinalIgnoreCase))
            {
                error = $"샌드박스 씬 폴더({UiSandboxSafetyPolicy.SANDBOX_FOLDER})에는 프리셋을 저장하지 않는다";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary> 경로의 프리셋을 읽는다. 없거나 형식이 다르면 null </summary>
        public static UiSandboxScenarioSO Load(string assetPath)
        {
            if (!ValidatePath(assetPath, out _))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<UiSandboxScenarioSO>(UiSandboxSafetyPolicy.NormalizePath(assetPath));
        }

        /// <summary> 시나리오를 프리셋 에셋으로 저장한다. 같은 경로에 파일이 있으면 allowOverwrite가 true이고 프리셋 형식일 때만 값을 바꾼다 </summary>
        public static bool SaveAs(UiSandboxScenarioSO scenario, string assetPath, bool allowOverwrite, out string error)
        {
            if (scenario == null)
            {
                error = "저장할 시나리오가 없다";
                return false;
            }

            if (!ValidatePath(assetPath, out error))
            {
                return false;
            }

            string path = UiSandboxSafetyPolicy.NormalizePath(assetPath);
            string folder = path.Substring(0, path.LastIndexOf('/'));
            if (!AssetDatabase.IsValidFolder(folder))
            {
                error = $"저장할 폴더가 없다: {folder}";
                return false;
            }

            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            bool fileExists = (existing != null) || (File.Exists(path));

            if (!fileExists)
            {
                UiSandboxScenarioSO created = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
                created.CopyFrom(scenario);
                AssetDatabase.CreateAsset(created, path);
                AssetDatabase.SaveAssetIfDirty(created);
                error = null;
                return true;
            }

            UiSandboxScenarioSO existingPreset = existing as UiSandboxScenarioSO;
            if (existingPreset == null)
            {
                error = $"{path}는 시나리오 프리셋이 아니라 덮어쓰지 않는다";
                return false;
            }

            if (!allowOverwrite)
            {
                error = $"{path}에 이미 프리셋이 있다. 덮어쓰려면 저장 확인을 명시해야 한다";
                return false;
            }

            existingPreset.CopyFrom(scenario);
            EditorUtility.SetDirty(existingPreset);
            AssetDatabase.SaveAssetIfDirty(existingPreset);
            error = null;
            return true;
        }
    }
}
