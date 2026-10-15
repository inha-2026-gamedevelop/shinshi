// System
using System;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Editor
{
    [CustomEditor(typeof(WolbaekSealCorePlayer))]
    public sealed class WolbaekSealCorePlayerEditor : WolbaekSealPreviewEditor
    {
        /****************************************
        *                Fields
        ****************************************/
        private WolbaekSealCorePlayer.EPhase _phase = WolbaekSealCorePlayer.EPhase.Dormant;

        /****************************************
        *              Properties
        ****************************************/
        private WolbaekSealCorePlayer Player => (WolbaekSealCorePlayer)target;
        protected override ScriptableObject Settings => Player.Settings;
        protected override bool Running => Player.IsRunning;
        protected override uint PlaybackId => Player.PlaybackId;
        protected override ulong LastSequence => Player.LastAcceptedSequence;
        protected override double LastNow => Player.LastSampleTime;
        protected override Transform Root => Player.transform;
        protected override string Description => "핵은 독립 표현이다. Opened는 시각 상태이고 체력이나 취약 판정이 없다. Destroyed는 개별 외부 확정 입력이다.";

        /****************************************
        *                Methods
        ****************************************/
        protected override void DrawPreviewInput()
        {
            _phase = (WolbaekSealCorePlayer.EPhase)EditorGUILayout.EnumPopup("미리보기 상태", _phase);
        }

        protected override uint BeginPreview(ulong castId, ulong sequence, Vector3 center, double now, float age)
        {
            double phaseAt = now - age;
            double appearedAt = _phase == WolbaekSealCorePlayer.EPhase.Dormant ? phaseAt
                : Math.Max(0d, phaseAt - Player.Settings.AppearSeconds);
            return Player.Begin(new WolbaekSealCorePlayer.Snapshot(castId, sequence, 0, 0, center,
                appearedAt, now, _phase, phaseAt), now);
        }

        protected override void TickPreview(uint playbackId, double now)
        {
            Player.Tick(playbackId, now);
        }

        protected override void StopPreview(uint playbackId)
        {
            Player.Stop(playbackId);
        }
    }

    /// <summary> PlayMode 인스턴스만 제어한다. 선택 해제 시 자신이 시작한 세대만 정리한다 </summary>
    public abstract class WolbaekSealPreviewEditor : UnityEditor.Editor
    {
        /****************************************
        *                Fields
        ****************************************/
        private const double PREVIEW_CLOCK_GAP  = 1d;
        private const float MAX_PREVIEW_AGE     = 5f;
        private const double MIN_PREVIEW_CLOCK  = 10d;
        private static ulong _nextPreviewCastId = unchecked((ulong)DateTime.UtcNow.Ticks);
        private UnityEditor.Editor _settingsEditor;
        private uint _previewId;
        private float _age;
        private float _startingAge;
        private double _clockOrigin;
        private double _editorOrigin;
        private Vector3 _center;
        private bool _showBindings;
        private bool _playing;

        /****************************************
        *              Properties
        ****************************************/
        protected abstract ScriptableObject Settings { get; }
        protected abstract bool Running { get; }
        protected abstract uint PlaybackId { get; }
        protected abstract ulong LastSequence { get; }
        protected abstract double LastNow { get; }
        protected abstract Transform Root { get; }
        protected abstract string Description { get; }
        private bool OwnsPlayback => (((target != null) && (_previewId != 0)) && (Running)) && (PlaybackId == _previewId);

        /****************************************
        *              Unity Event
        ****************************************/
        protected virtual void OnDisable()
        {
            EditorApplication.update -= AdvancePreview;
            if (OwnsPlayback)
            {
                StopPreview(_previewId);
            }
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
            EditorGUILayout.HelpBox(Description + " SO 변경은 다음 Begin부터 적용된다.", MessageType.Info);
            if (Settings != null)
            {
                CreateCachedEditor(Settings, null, ref _settingsEditor);
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
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Play, Stop, 시간 스크럽은 PlayMode의 씬 인스턴스에서 사용한다.", MessageType.Info);
                return;
            }
            bool externalPlayback = (Running) && (!OwnsPlayback);
            if (externalPlayback)
            {
                EditorGUILayout.HelpBox("외부 입력의 재생 중이다. Inspector 미리보기는 외부 재생을 제어하지 않는다.", MessageType.Info);
            }
            bool playableInstance = ((Settings != null) && (!EditorUtility.IsPersistent(target))) && (Root.gameObject.scene.IsValid());
            using (new EditorGUI.DisabledScope((externalPlayback) || (!playableInstance)))
            {
                EditorGUI.BeginChangeCheck();
                DrawPreviewInput();
                float age = EditorGUILayout.Slider("현재 상태 경과, 초", _age, 0f, MAX_PREVIEW_AGE);
                if (EditorGUI.EndChangeCheck())
                {
                    _age = age;
                    RebuildPreview(false);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Play"))
                    {
                        RebuildPreview(true);
                    }
                    using (new EditorGUI.DisabledScope(!OwnsPlayback))
                    {
                        if (GUILayout.Button("Stop"))
                        {
                            StopPreview(_previewId);
                            _previewId = 0;
                            _playing = false;
                            EditorApplication.update -= AdvancePreview;
                        }
                    }
                }
            }
            EditorGUILayout.LabelField("표현 재생", Running ? "재생 중" : "정지");
        }

        protected abstract void DrawPreviewInput();
        protected abstract uint BeginPreview(ulong castId, ulong sequence, Vector3 center, double now, float age);
        protected abstract void TickPreview(uint playbackId, double now);
        protected abstract void StopPreview(uint playbackId);

        private void RebuildPreview(bool play)
        {
            if ((LastSequence == ulong.MaxValue) || ((Running) && (!OwnsPlayback)))
            {
                return;
            }
            if (OwnsPlayback)
            {
                StopPreview(_previewId);
            }
            else
            {
                _center = Root.position;
            }
            // 스크럽은 더 이전 now를 전달하지 않고 새 미리보기 시전의 시작 시각을 바꿔 절대 포즈를 복원한다
            _clockOrigin = Math.Max(MIN_PREVIEW_CLOCK, Math.Max(LastNow, EditorApplication.timeSinceStartup));
            _editorOrigin = EditorApplication.timeSinceStartup;
            _startingAge = _age;
            // 시스템 시계는 연속 호출에서 같은 값을 줄 수 있으므로 스크럽마다 별도 시전 ID를 발급한다
            ulong castId = unchecked(++_nextPreviewCastId);
            if (castId == 0)
            {
                castId = unchecked(++_nextPreviewCastId);
            }
            _previewId = BeginPreview(castId, LastSequence + 1, _center, _clockOrigin, _age);
            _playing = (play) && (OwnsPlayback);
            EditorApplication.update -= AdvancePreview;
            if (_playing)
            {
                EditorApplication.update += AdvancePreview;
            }
            Repaint();
            SceneView.RepaintAll();
        }

        private void AdvancePreview()
        {
            if (((!EditorApplication.isPlaying) || (!_playing)) || (!OwnsPlayback))
            {
                _playing = false;
                EditorApplication.update -= AdvancePreview;
                return;
            }
            double elapsed = EditorApplication.timeSinceStartup - _editorOrigin;
            TickPreview(_previewId, _clockOrigin + elapsed);
            _age = (float)Math.Min(MAX_PREVIEW_AGE, _startingAge + elapsed);
            Repaint();
            SceneView.RepaintAll();
        }
    }
}
