// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 공격 애니메이터 상태의 정해진 시각에 넓게 베기 연출을 시작한다 </summary>
    public sealed class SlashVfxStateBehaviour : StateMachineBehaviour
    {
        // 유료 클립 FBX의 임포트 설정에 애니메이션 이벤트를 넣지 않고 이 상태 동작을 쓴다. 유료 에셋은
        // Git에서 빠져 팀원마다 새로 임포트하므로 메타에 넣은 이벤트는 다른 PC에서 사라진다.
        // 애니메이터는 상태 동작을 애니메이터마다 따로 만들므로 연출 캐시를 필드에 둬도 섞이지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // 기술 시작(연출 시간표의 0)에 해당하는 상태 진행률
        [SerializeField, Range(0f, 1f)] private float _startNormalizedTime = 0f;

        private SlashVfxEmitter _emitter;
        private bool            _fired  = false;
        private bool            _warned = false;

        /****************************************
        *              Unity Event
        ****************************************/

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            _fired = false;
            TryFire(animator, stateInfo.normalizedTime);
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            TryFire(animator, stateInfo.normalizedTime);
        }

        // 회피나 피격으로 공격 상태를 일찍 벗어나면 준비 중 연출을 거둔다. 휘두른 뒤라면 연출 쪽이 무시한다
        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if ((!_fired) || (_emitter == null))
            {
                return;
            }

            _emitter.CancelWideSlash();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void TryFire(Animator animator, float normalizedTime)
        {
            if ((_fired) || (normalizedTime < _startNormalizedTime))
            {
                return;
            }

            _fired = true;

            if ((_emitter == null) && (!animator.TryGetComponent(out _emitter)))
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning("[VFX] 애니메이터와 같은 오브젝트에 SlashVfxEmitter가 없다", animator);
                }
                return;
            }

            _emitter.PlayWideSlash();
        }
    }
}
