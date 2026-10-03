// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 실제 렌더러에 연결된 추가 본만 기존 보조본 계산기에 넘긴다 </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(PlayerFemaleCharacterSecondaryMotionBinding.EXECUTION_ORDER)]
    public sealed class PlayerFemaleCharacterSecondaryMotionBinding : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int EXECUTION_ORDER = -5000;

        [SerializeField] private Animator                     _animator;
        [SerializeField] private Transform                    _motionRoot;
        [SerializeField] private CharacterSecondaryMotionView _simulation;
        [SerializeField] private SkinnedMeshRenderer[]        _renderers = Array.Empty<SkinnedMeshRenderer>();
        [SerializeField] private SecondaryMotionChain[]       _chains    = Array.Empty<SecondaryMotionChain>();
        [SerializeField] private SecondaryMotionCapsule[]     _capsules  = Array.Empty<SecondaryMotionCapsule>();

        private bool   _isBound         = false;
        private string _validationError = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsBound            => _isBound;
        public string ValidationError  => _validationError;
        public int SimulatedBoneCount  => (_isBound) ? _simulation.SimulatedBoneCount : 0;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            TryBind(out _validationError);
        }

        private void OnEnable()
        {
            if ((IsServerBuild) || (!_isBound) || (_simulation == null))
            {
                return;
            }

            _simulation.enabled = true;
            _simulation.ResetSimulation();
        }

        private void OnDisable()
        {
            if (_simulation != null)
            {
                _simulation.enabled = false;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> Animator의 최초 평가 전에 authored 추가 본과 렌더러 참조를 연결한다 </summary>
        public bool Configure(Animator animator, Transform motionRoot, CharacterSecondaryMotionView simulation,
            SkinnedMeshRenderer[] renderers, SecondaryMotionChain[] chains, SecondaryMotionCapsule[] capsules)
        {
            if (_simulation != null)
            {
                _simulation.enabled = false;
            }

            _animator   = animator;
            _motionRoot = motionRoot;
            _simulation = simulation;
            _renderers  = renderers;
            _chains     = chains;
            _capsules   = capsules;
            return TryBind(out _validationError);
        }

        /// <summary> Humanoid 본 구동과 다른 캐릭터의 참조를 거절하고 초기화 결과를 돌려준다 </summary>
        public bool TryBind(out string error)
        {
            _isBound = false;
            if (_simulation != null)
            {
                _simulation.enabled = false;
            }

            if (!ValidateReferences(out error))
            {
                _validationError = error;
                return false;
            }

            _isBound = _simulation.Configure(_motionRoot, _chains, _capsules);
            error = (_isBound) ? string.Empty : _simulation.ValidationError;
            _validationError = error;
            _simulation.enabled = (_isBound) && (isActiveAndEnabled);
            return _isBound;
        }

        private bool ValidateReferences(out string error)
        {
            if (IsServerBuild)
            {
                error = "전용 서버에서는 캐릭터 보조본을 연결하지 않는다";
                return false;
            }

            if ((_animator == null) || (_motionRoot == null) || (_simulation == null)
                || (_animator.avatar == null) || (!_animator.avatar.isValid) || (!_animator.avatar.isHuman)
                || (_animator.updateMode != AnimatorUpdateMode.Normal))
            {
                error = "Normal 모드의 유효한 Humanoid Animator와 시각 루트, 보조본 계산기가 필요하다";
                return false;
            }

            if ((!IsWithinRoot(_animator.transform)) || (!IsWithinRoot(_simulation.transform))
                || (_chains == null) || (_chains.Length == 0) || (_renderers == null) || (_renderers.Length == 0))
            {
                error = "보조본과 Animator, 계산기, 렌더러를 같은 시각 루트에 연결해야 한다";
                return false;
            }

            var humanoidBones = new HashSet<Transform>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; ++i)
            {
                Transform bone = _animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null)
                {
                    humanoidBones.Add(bone);
                }
            }

            var renderedBones = new HashSet<Transform>();
            for (int i = 0; i < _renderers.Length; ++i)
            {
                SkinnedMeshRenderer renderer = _renderers[i];
                if ((renderer == null) || (renderer.sharedMesh == null) || (!IsWithinRoot(renderer.transform)))
                {
                    error = "보조본 렌더러에 메시가 없거나 다른 캐릭터에 속한다";
                    return false;
                }

                Transform[] bones = renderer.bones;
                for (int boneIndex = 0; boneIndex < bones.Length; ++boneIndex)
                {
                    if (bones[boneIndex] != null)
                    {
                        renderedBones.Add(bones[boneIndex]);
                    }
                }
            }

            for (int chainIndex = 0; chainIndex < _chains.Length; ++chainIndex)
            {
                SecondaryMotionChain chain = _chains[chainIndex];
                if ((chain == null) || (chain.Root == null) || (!IsWithinRoot(chain.Root)) || (chain.BoneCount == 0))
                {
                    error = "캐릭터 안의 고정 부착점과 실제 추가 본 체인이 필요하다";
                    return false;
                }

                for (int boneIndex = 0; boneIndex < chain.BoneCount; ++boneIndex)
                {
                    Transform bone = chain.GetBone(boneIndex);
                    if ((bone == null) || (!IsWithinRoot(bone)) || (humanoidBones.Contains(bone))
                        || (!renderedBones.Contains(bone)))
                    {
                        error = "구동 본은 Humanoid 본이 아닌 같은 캐릭터의 스킨 참조여야 한다";
                        return false;
                    }
                }
            }

            // bones 배열의 참조는 양수 skin weight의 증거가 아니므로 제작 메시의 가중치 검사는 별도로 한다
            error = string.Empty;
            return true;
        }

        private bool IsWithinRoot(Transform value)
        {
            return (value == _motionRoot) || (value.IsChildOf(_motionRoot));
        }
    }
}
