// System
using System;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Editor
{
    [CustomEditor(typeof(WolbaekSealDropPlayer))]
    public sealed class WolbaekSealDropPlayerEditor : WolbaekSealPreviewEditor
    {
        /****************************************
        *                Fields
        ****************************************/
        private const float DEFAULT_PREVIEW_RADIUS   = 2f;
        private const double PREVIEW_PREPARE_SECONDS = 1d;
        private const double PREVIEW_DROP_SECONDS    = 1.2d;
        private const float MIN_PREVIEW_RADIUS       = 0.1f;
        private const float MAX_PREVIEW_RADIUS       = 10f;
        private WolbaekSealDropPlayer.EPhase _phase  = WolbaekSealDropPlayer.EPhase.Tracking;
        private float _radius                        = DEFAULT_PREVIEW_RADIUS;

        /****************************************
        *              Properties
        ****************************************/
        private WolbaekSealDropPlayer Player => (WolbaekSealDropPlayer)target;
        protected override ScriptableObject Settings => Player.Settings;
        protected override bool Running => Player.IsRunning;
        protected override uint PlaybackId => Player.PlaybackId;
        protected override ulong LastSequence => Player.LastAcceptedSequence;
        protected override double LastNow => Player.LastSampleTime;
        protected override Transform Root => Player.transform;
        protected override string Description => "낙하 목표는 핵 좌표와 독립이다. Locked 이후 좌표는 고정되고 Resolved 외부 입력으로만 플래시를 재생한다.";

        /****************************************
        *                Methods
        ****************************************/
        protected override void DrawPreviewInput()
        {
            _phase = (WolbaekSealDropPlayer.EPhase)EditorGUILayout.EnumPopup("미리보기 상태", _phase);
            _radius = EditorGUILayout.Slider("미리보기 폭발 반경, 미터", _radius, MIN_PREVIEW_RADIUS, MAX_PREVIEW_RADIUS);
        }

        protected override uint BeginPreview(ulong castId, ulong sequence, Vector3 center, double now, float age)
        {
            double phaseAt = now - age;
            double appearedAt;
            double lockAt;
            double resolveAt;
            if (_phase == WolbaekSealDropPlayer.EPhase.Resolved)
            {
                resolveAt = phaseAt;
                lockAt = resolveAt - PREVIEW_DROP_SECONDS;
                appearedAt = lockAt - PREVIEW_PREPARE_SECONDS;
            }
            else if (_phase == WolbaekSealDropPlayer.EPhase.Locked)
            {
                lockAt = phaseAt;
                appearedAt = lockAt - PREVIEW_PREPARE_SECONDS;
                resolveAt = lockAt + PREVIEW_DROP_SECONDS;
            }
            else
            {
                appearedAt = phaseAt;
                lockAt = appearedAt + PREVIEW_PREPARE_SECONDS;
                resolveAt = lockAt + PREVIEW_DROP_SECONDS;
            }
            return Player.Begin(new WolbaekSealDropPlayer.Snapshot(castId, sequence, 0, 0, center, _radius,
                Math.Max(0d, appearedAt), lockAt, resolveAt, now, _phase, phaseAt), now);
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
}
