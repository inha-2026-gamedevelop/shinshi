// Unity
using UnityEngine;
using UnityEngine.Animations.Rigging;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 치마 보정 중에는 특정 모션에서만 필요한 것이 있어서, 애니메이터가 그 모션 상태에 있는 동안만 지정한 제약들의 가중치를 올린다
    // 모션 이름 대신 클립 상태를 기준으로 삼아서 같은 클립을 쓰는 다른 사건 모션에도 같이 적용된다
    // 상태가 바뀌는 크로스페이드 중에도 목표 상태를 보고 가중치를 부드럽게 올리고 내린다
    /// <summary> 지정한 모션 상태일 때만 지정한 리그 제약들을 켜는 표현 전용 컴포넌트 </summary>
    public sealed class SkirtMotionGate : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Animator        _animator;
        [SerializeField] private MonoBehaviour[] _constraints;
        [SerializeField] private string          _stateName;
        [SerializeField] private float           _blendSeconds;

        private IRigConstraint[] _targets;
        private float            _blend;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _targets = new IRigConstraint[_constraints.Length];

            for (int i = 0; i < _constraints.Length; i++)
            {
                _targets[i] = (IRigConstraint)_constraints[i];
            }

            Apply(0f);
        }

        private void Update()
        {
            bool isActive = _animator.GetCurrentAnimatorStateInfo(0).IsName(_stateName)
                || _animator.GetNextAnimatorStateInfo(0).IsName(_stateName);

            float step = (_blendSeconds > 0f) ? (Time.deltaTime / _blendSeconds) : 1f;
            float next = Mathf.MoveTowards(_blend, isActive ? 1f : 0f, step);

            if (!Mathf.Approximately(next, _blend))
            {
                Apply(next);
            }
        }

        private void OnDisable()
        {
            if (_targets != null)
            {
                Apply(0f);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Apply(float blend)
        {
            _blend = blend;

            for (int i = 0; i < _targets.Length; i++)
            {
                _targets[i].weight = blend;
            }
        }
    }
}
