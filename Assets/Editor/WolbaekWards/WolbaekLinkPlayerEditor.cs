// Unity
using UnityEditor;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Editor
{
    [CustomEditor(typeof(WolbaekLinkPlayer))]
    public sealed class WolbaekLinkPlayerEditor : UnityEditor.Editor
    {
        /****************************************
        *                Fields
        ****************************************/
        private UnityEditor.Editor _settingsEditor;
        private bool _showBindings;

        /****************************************
        *              Unity Event
        ****************************************/
        private void OnDisable()
        {
            if (_settingsEditor != null)
            {
                DestroyImmediate(_settingsEditor);
            }
        }

        /****************************************
        *                Methods
        ****************************************/
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_settings"));
            serializedObject.ApplyModifiedProperties();
            WolbaekLinkPlayer player = (WolbaekLinkPlayer)target;
            if (player.Settings != null)
            {
                EditorGUILayout.HelpBox("이 프리팹의 SO 값이다. 변경은 다음 Begin부터 적용된다. 위치와 상태는 외부 입력으로만 바뀐다.", MessageType.Info);
                CreateCachedEditor(player.Settings, null, ref _settingsEditor);
                _settingsEditor.OnInspectorGUI();
            }
            _showBindings = EditorGUILayout.Foldout(_showBindings, "생성된 메시와 리그 참조", true);
            if (_showBindings)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    DrawPropertiesExcluding(serializedObject, "m_Script", "_settings");
                }
            }
            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField("표현 재생", player.IsRunning ? "재생 중" : "정지");
                EditorGUILayout.LabelField("시전 ID", player.CastId.ToString());
            }
        }
    }
}
