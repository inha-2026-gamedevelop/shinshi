// Unity
using UnityEditor;
using UnityEditor.Build;

namespace Shinshi.Editor
{
    /// <summary> 로컬 다인 기능 시험용 Mono 클라이언트를 만들고 원래 빌드 설정을 복원한다 </summary>
    public static class ForestM2Build
    {
        public static void BuildClient()
        {
            ScriptingImplementation original = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            bool succeeded = false;
            try
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                succeeded = ShinshiBuild.Build("windows-client", true);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, original);
            }

            EditorApplication.Exit(succeeded ? 0 : 1);
        }
    }
}
