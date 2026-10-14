// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Editor
{
    /// <summary> 개별 무철 타격의 시간 프로필과 돌진 시간을 Inspector에서 편집하고 확인한다 </summary>
    [CustomEditor(typeof(MucheolBatchVfxPlayer))]
    public sealed class MucheolBatchVfxPlayerEditor : UnityEditor.Editor
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float SAMPLE_MARGIN_SECONDS = 0.5f;
        private const float FALLBACK_DURATION     = 5f;

        private bool   _editorPlaying;
        private bool   _ownsEditorPreview;
        private bool   _showReferences;
        private double _editorStart;
        private float  _sampleSeconds;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            EditorApplication.update += UpdatePreview;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePreview;
            if ((_ownsEditorPreview) && (!Application.isPlaying) && (target != null))
            {
                ((MucheolBatchVfxPlayer)target).Stop();
            }

            _editorPlaying = false;
            _ownsEditorPreview = false;
        }

        private void UpdatePreview()
        {
            if ((!_editorPlaying) || (target == null) || (Application.isPlaying))
            {
                return;
            }

            MucheolBatchVfxPlayer player = (MucheolBatchVfxPlayer)target;
            double elapsed = EditorApplication.timeSinceStartup - _editorStart;
            if (elapsed >= MaxSampleSeconds(player))
            {
                if (!player.RepeatPreview)
                {
                    _editorPlaying = false;
                    _ownsEditorPreview = false;
                    player.Stop();
                    return;
                }

                _editorStart = EditorApplication.timeSinceStartup;
                elapsed = 0d;
            }

            _sampleSeconds = (float)elapsed;
            player.SamplePreview(_sampleSeconds);
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
            Repaint();
        }

        /****************************************
        *                Methods
        ****************************************/

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MucheolBatchVfxPlayer player = (MucheolBatchVfxPlayer)target;
            SerializedProperty profile = serializedObject.FindProperty("_profile");
            EditorGUILayout.PropertyField(profile, new GUIContent("이 타격의 타이밍 프로필"));
            DrawInlineProfile(profile.objectReferenceValue as MucheolPatternVfxSettingsSO);

            if (serializedObject.FindProperty("_chargeHead").objectReferenceValue != null)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_chargeSeconds"), new GUIContent("돌진 이동 시간 (초)"));
                EditorGUILayout.HelpBox("예고 후 이 시간 동안 머리가 전진하고, 정지 타격부터 확산, 유지, 소멸이 진행됩니다.", MessageType.Info);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_repeatPreview"), new GUIContent("반복 재생"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_playOnStart"), new GUIContent("Play 진입 시 재생"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_previewGapSeconds"), new GUIContent("반복 간격 (초)"));
            serializedObject.ApplyModifiedProperties();
            SynchronizeProfile(player);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("재생"))
                {
                    player.Stop();
                    if (Application.isPlaying)
                    {
                        _editorPlaying = false;
                        _ownsEditorPreview = false;
                        player.Preview();
                    }
                    else
                    {
                        _editorPlaying = true;
                        _ownsEditorPreview = true;
                        _editorStart = EditorApplication.timeSinceStartup;
                    }
                }

                if (GUILayout.Button("중지"))
                {
                    _editorPlaying = false;
                    _ownsEditorPreview = false;
                    player.Stop();
                    SceneView.RepaintAll();
                }
            }

            EditorGUI.BeginChangeCheck();
            _sampleSeconds = EditorGUILayout.Slider("시점 확인 (초)", _sampleSeconds, 0f, MaxSampleSeconds(player));
            if (EditorGUI.EndChangeCheck())
            {
                _editorPlaying = false;
                _ownsEditorPreview = !Application.isPlaying;
                player.Stop();
                player.SamplePreview(_sampleSeconds);
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }

            EditorGUILayout.HelpBox("초 단위로 이 프리팹 하나를 재생합니다. 04와 05는 독립된 타격이며 05를 두 번 자동 재생하지 않습니다. 외부 시계에서는 ConfirmImpact를 받아야 타격이 시작됩니다.", MessageType.Info);
            serializedObject.Update();
            _showReferences = EditorGUILayout.Foldout(_showReferences, "렌더링 연결 (고급)");
            if (_showReferences)
            {
                DrawPropertiesExcluding(serializedObject, "m_Script", "_profile", "_chargeSeconds",
                    "_repeatPreview", "_playOnStart", "_previewGapSeconds");
            }

            serializedObject.ApplyModifiedProperties();
            SynchronizeProfile(player);
        }

        /// <summary> Inspector에서 교체한 프로필을 내부 타격 재생기에도 연결한다, 진행 중인 타격 스냅샷은 보존된다 </summary>
        public static bool SynchronizeProfile(MucheolBatchVfxPlayer player)
        {
            if (player == null)
            {
                return false;
            }

            SerializedObject wrapper = new SerializedObject(player);
            MucheolImpactVfxPlayer impact = wrapper.FindProperty("_player").objectReferenceValue as MucheolImpactVfxPlayer;
            if (impact == null)
            {
                return false;
            }

            UnityEngine.Object profile = wrapper.FindProperty("_profile").objectReferenceValue;
            SerializedObject child = new SerializedObject(impact);
            SerializedProperty childProfile = child.FindProperty("_profile");
            if (childProfile.objectReferenceValue == profile)
            {
                return false;
            }

            childProfile.objectReferenceValue = profile;
            child.ApplyModifiedProperties();
            return true;
        }

        private static void DrawInlineProfile(MucheolPatternVfxSettingsSO profile)
        {
            if (profile == null)
            {
                return;
            }

            SerializedObject settings = new SerializedObject(profile);
            settings.Update();
            SerializedProperty iterator = settings.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath != "m_Script")
                {
                    EditorGUILayout.PropertyField(iterator, true);
                }
            }

            settings.ApplyModifiedProperties();
        }

        private static float MaxSampleSeconds(MucheolBatchVfxPlayer player)
        {
            return (player.Profile == null) ? FALLBACK_DURATION : player.PreviewDuration + SAMPLE_MARGIN_SECONDS;
        }
    }
}
