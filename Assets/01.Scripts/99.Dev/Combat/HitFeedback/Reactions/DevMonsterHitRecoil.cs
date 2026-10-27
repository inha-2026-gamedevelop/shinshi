// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// Third Party
using DG.Tweening;

namespace Shinshi.Dev
{
    /// <summary> 애니메이션 평가 뒤 상체에 피격 방향 반동을 더한다 </summary>
    [DefaultExecutionOrder(100)]
    public sealed class DevMonsterHitRecoil : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MAX_DELAY_RATIO = 0.95f;
        private const float HEAD_TORSO_COMPENSATION = 0.65f;
        private const float ARM_TORSO_COMPENSATION = 0.4f;
        private const int ARM_PART = 2;

        [SerializeField, Min(0.1f)] private float _seconds = 0.34f;
        [SerializeField] private float _torsoDegrees = 18f;
        [SerializeField] private float _headDegrees = 10f;
        [SerializeField] private float _armDegrees = 13f;
        [SerializeField, Min(0f)] private float _headDelay = 0.035f;
        [SerializeField, Min(0f)] private float _armDelay = 0.055f;
        [SerializeField] private float _criticalScale = 1.35f;
        [SerializeField] private AnimationCurve _response = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.16f, 1f),
            new Keyframe(0.62f, -0.12f), new Keyframe(1f, 0f));

        private readonly List<Transform> _bones = new List<Transform>();
        private readonly List<int> _parts = new List<int>();
        private Quaternion[] _baseRotations;
        private Tweener _tween;
        private Vector3 _axis;
        private float _phase = 1f;
        private float _strength = 1f;
        private bool _hasPose = false;
        private bool _isLagged = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update() { RestorePose(); }

        private void LateUpdate()
        {
            RestorePose();
            if (_phase >= 1f)
            {
                return;
            }
            float torso = _response.Evaluate(_phase) * _torsoDegrees;
            for (int i = 0; i < _bones.Count; ++i)
            {
                Transform bone = _bones[i];
                if (bone == null)
                {
                    continue;
                }
                _baseRotations[i] = bone.localRotation;
                float angle = torso;
                if (_parts[i] != 0)
                {
                    bool head = _parts[i] == 1;
                    float delay = Mathf.Clamp((head ? _headDelay : _armDelay) / _seconds, 0f, MAX_DELAY_RATIO);
                    float delayedPhase = Mathf.Clamp01((_phase - delay) / (1f - delay));
                    // 부모인 몸통의 회전을 일부 상쇄해 머리와 팔이 뒤늦게 따라오게 한다.
                    angle = (_response.Evaluate(delayedPhase) * (head ? _headDegrees : _armDegrees))
                        - (torso * (head ? HEAD_TORSO_COMPENSATION : ARM_TORSO_COMPENSATION));
                }
                Vector3 localAxis = bone.parent != null ? bone.parent.InverseTransformDirection(_axis) : _axis;
                bone.localRotation = Quaternion.AngleAxis(angle * _strength, localAxis) * _baseRotations[i];
            }
            _hasPose = true;
        }

        private void OnDisable() { StopAndRestore(); }

        private void OnDestroy()
        {
            _tween?.Kill();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Init(Animator animator)
        {
            StopAndRestore();
            _tween?.Kill();
            _bones.Clear();
            _parts.Clear();
            if (animator != null)
            {
                Transform[] candidates = animator.GetComponentsInChildren<Transform>(true);
                AddBone(candidates, 0, "CATRigSpine", "Spine", "spine_01", "mixamorig:Spine");
                AddBone(candidates, 1, "CATRigRibcage003", "Head", "head", "mixamorig:Head");
                AddBone(candidates, ARM_PART, "CATRigLArm1", "LeftArm", "upperarm_l", "mixamorig:LeftArm");
                AddBone(candidates, ARM_PART, "CATRigRArm1", "RightArm", "upperarm_r", "mixamorig:RightArm");
            }
            _baseRotations = new Quaternion[_bones.Count];
            _tween = DOTween.To(() => _phase, value => _phase = value, 1f, _seconds)
                .SetEase(Ease.Linear).SetAutoKill(false).Pause().SetLink(gameObject);
        }

        public void Play(Vector3 away, bool critical)
        {
            if ((!isActiveAndEnabled) || (_tween == null) || (_bones.Count == 0))
            {
                return;
            }
            RestorePose();
            _axis = Vector3.Cross(Vector3.up, away.normalized);
            _strength = critical ? _criticalScale : 1f;
            _phase = 0f;
            _tween.ChangeStartValue(0f);
            _tween.Restart();
            if (_isLagged)
            {
                _tween.Pause();
            }
        }

        public void SetLagged(bool lagged)
        {
            if (_isLagged == lagged)
            {
                return;
            }
            _isLagged = lagged;
            if ((_tween == null) || (_phase >= 1f))
            {
                return;
            }
            if (lagged)
            {
                _tween.Pause();
            }
            else
            {
                _tween.Play();
            }
        }

        public void StopAndRestore()
        {
            RestorePose();
            _tween?.Pause();
            _phase = 1f;
            _isLagged = false;
        }

        private void RestorePose()
        {
            if (!_hasPose)
            {
                return;
            }
            for (int i = 0; i < _bones.Count; ++i)
            {
                if (_bones[i] != null)
                {
                    _bones[i].localRotation = _baseRotations[i];
                }
            }
            _hasPose = false;
        }

        private void AddBone(Transform[] candidates, int part, params string[] names)
        {
            foreach (string name in names)
            {
                foreach (Transform bone in candidates)
                {
                    if (string.Equals(bone.name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        _bones.Add(bone);
                        _parts.Add(part);
                        return;
                    }
                }
            }
        }
    }
}
