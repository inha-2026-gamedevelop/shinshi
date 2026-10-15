// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Editor
{
    /// <summary> 무철 타격 VFX 재생기 인스펙터, 프로필을 인라인으로 보여주고 미리보기 버튼과 스크러빙 슬라이더를 더한다 </summary>
    [CustomEditor(typeof(MucheolImpactVfxPlayer))]
    public sealed class MucheolImpactVfxPlayerEditor : UnityEditor.Editor
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float SAMPLE_MARGIN_SECONDS = 0.5f;
        private const float FALLBACK_MAX_SECONDS   = 5f;

        private float _sampleSeconds;
        private bool _editorPlaying;
        private bool _showReferences;
        private double _editorStart;

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
            if ((_editorPlaying) && (target != null))
            {
                ((MucheolImpactVfxPlayer)target).Stop();
            }
        }

        private void UpdatePreview()
        {
            if ((!_editorPlaying) || (target == null) || (Application.isPlaying))
            {
                return;
            }
            MucheolImpactVfxPlayer player = (MucheolImpactVfxPlayer)target;
            serializedObject.Update();
            MucheolPatternVfxSettingsSO profile = serializedObject.FindProperty("_profile").objectReferenceValue as MucheolPatternVfxSettingsSO;
            double elapsed = EditorApplication.timeSinceStartup - _editorStart;
            float duration = MaxSampleSeconds(profile);
            if (elapsed >= duration)
            {
                if (serializedObject.FindProperty("_repeatPreview").boolValue)
                {
                    _editorStart = EditorApplication.timeSinceStartup;
                    elapsed = 0d;
                }
                else
                {
                    _editorPlaying = false;
                    player.Stop();
                    return;
                }
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
            SerializedProperty profileProperty = serializedObject.FindProperty("_profile");
            EditorGUILayout.PropertyField(profileProperty);

            MucheolPatternVfxSettingsSO profile = profileProperty.objectReferenceValue as MucheolPatternVfxSettingsSO;
            DrawInlineProfile(profile);

            EditorGUILayout.Space();
            DrawPreviewControls(profile);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_repeatPreview"), new GUIContent("반복 재생"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_playOnStart"), new GUIContent("Play 진입 시 재생"));
            EditorGUILayout.HelpBox("시간은 초 단위입니다. 전투 피해 판정은 변경하지 않습니다. 플레이 중 수정은 다음 재생부터 적용됩니다. 다른 패턴은 프로필을 복제해 연결하세요.", MessageType.Info);
            _showReferences = EditorGUILayout.Foldout(_showReferences, "렌더링 연결 (고급)");
            if (_showReferences)
            {
                DrawPropertiesExcluding(serializedObject, "m_Script", "_profile", "_repeatPreview", "_playOnStart");
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawInlineProfile(MucheolPatternVfxSettingsSO profile)
        {
            if (profile == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("타이밍 프로필 (인라인 편집)", EditorStyles.boldLabel);

            SerializedObject profileSerialized = new SerializedObject(profile);
            profileSerialized.Update();

            SerializedProperty iterator = profileSerialized.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script")
                {
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }

            profileSerialized.ApplyModifiedProperties();
        }

        private void DrawPreviewControls(MucheolPatternVfxSettingsSO profile)
        {
            MucheolImpactVfxPlayer player = (MucheolImpactVfxPlayer)target;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("재생"))
                {
                    if (Application.isPlaying)
                    {
                        player.Preview();
                    }
                    else
                    {
                        player.Stop();
                        _editorPlaying = true;
                        _editorStart = EditorApplication.timeSinceStartup;
                    }
                }

                if (GUILayout.Button("중지"))
                {
                    _editorPlaying = false;
                    player.Stop();
                    SceneView.RepaintAll();
                }
            }

            float maxSeconds = MaxSampleSeconds(profile);
            EditorGUI.BeginChangeCheck();
            _sampleSeconds = EditorGUILayout.Slider("시점 확인 (초)", _sampleSeconds, 0f, maxSeconds);
            if (EditorGUI.EndChangeCheck())
            {
                _editorPlaying = false;
                player.Stop();
                player.SamplePreview(_sampleSeconds);
                SceneView.RepaintAll();
            }
        }

        private static float MaxSampleSeconds(MucheolPatternVfxSettingsSO profile)
        {
            if (profile == null)
            {
                return FALLBACK_MAX_SECONDS;
            }

            float explosion = profile.ExpansionSeconds + profile.HoldSeconds + profile.FadeSeconds;
            float tail = Mathf.Max(explosion, profile.DebrisSeconds);
            return profile.TelegraphSeconds + tail + SAMPLE_MARGIN_SECONDS;
        }
    }
}
