// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.Serialization;

namespace Shinshi.AI
{
    // 디졸브 컴포넌트는 외부 에셋(99.ThirdParty)이라 이 어셈블리가 직접 참조할 수 없다
    // 그래서 형식을 모르는 MonoBehaviour로 받고 SendMessage로 공개 메서드를 부른다
    /// <summary> 처치 모션을 재생한 뒤 마지막 자세에서 잠시 멈췄다가 디졸브를 시작하는 확인용 도구 </summary>
    public sealed class DeathSequencePreview : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string IDLE_STATE         = "Idle";
        private const string PLAY_EFFECT_METHOD = "PlayEffect";
        private const string SET_MASK_METHOD    = "UpdateMaskTransform";

        private const float  PANEL_WIDTH     = 250f;
        private const float  PANEL_MARGIN    = 10f;
        private const float  BUTTON_WIDTH    = 240f;
        private const float  BUTTON_HEIGHT   = 34f;
        private const float  SECOND_BUTTON_Y = 48f;

        private const float  CROSSFADE_SECONDS = 0.1f;
        private const float  ENTER_SECONDS     = 0.15f;

        /****************************************
        *                Fields
        ****************************************/

        [FormerlySerializedAs("animator")]
        [SerializeField] private Animator _animator;

        [FormerlySerializedAs("effect")]
        [SerializeField] private MonoBehaviour _effect;

        [FormerlySerializedAs("extraEffects")]
        [SerializeField] private MonoBehaviour[] _extraEffects;

        [FormerlySerializedAs("deathState")]
        [SerializeField] private string _deathState = "dead01_inplace";

        [FormerlySerializedAs("holdDelay")]
        [SerializeField] private float _holdDelay = 0.5f;

        private Coroutine _running;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnGUI()
        {
            if ((_animator == null) || (_effect == null))
            {
                return;
            }

            float x = Screen.width - PANEL_WIDTH;

            if (GUI.Button(new Rect(x, PANEL_MARGIN, BUTTON_WIDTH, BUTTON_HEIGHT), "처치 + 디졸브"))
            {
                PlayDeath();
            }

            if (GUI.Button(new Rect(x, SECOND_BUTTON_Y, BUTTON_WIDTH, BUTTON_HEIGHT), "되돌리기 (Idle)"))
            {
                StopRunning();
                SetMask(0f);
                _animator.CrossFade(IDLE_STATE, CROSSFADE_SECONDS, 0, 0f);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 처치 모션과 디졸브를 처음부터 재생한다, 기믹 성공 뒤 처치 연출로 이어갈 때 쓴다 </summary>
        public void PlayDeath()
        {
            StopRunning();
            _running = StartCoroutine(Play());
        }

        /// <summary> 진행 중인 처치 연출을 끊고 디졸브를 되돌린다, 모션은 부르는 쪽이 정한다 </summary>
        public void ResetDeath()
        {
            StopRunning();
            SetMask(0f);
        }

        private void StopRunning()
        {
            if (_running != null)
            {
                StopCoroutine(_running);
            }
        }

        private void SetMask(float lerp)
        {
            _effect.SendMessage(SET_MASK_METHOD, lerp);

            for (int i = 0; i < _extraEffects.Length; ++i)
            {
                _extraEffects[i].SendMessage(SET_MASK_METHOD, lerp);
            }
        }

        private IEnumerator Play()
        {
            SetMask(0f);
            _animator.CrossFade(_deathState, CROSSFADE_SECONDS, 0, 0f);

            // 전환이 끝나 처치 상태에 들어간 뒤, 정지 상태로 넘어갈 때까지 기다린다
            yield return new WaitForSeconds(ENTER_SECONDS);

            while (_animator.GetCurrentAnimatorStateInfo(0).IsName(_deathState))
            {
                yield return null;
            }

            yield return new WaitForSeconds(_holdDelay);

            _effect.SendMessage(PLAY_EFFECT_METHOD);

            for (int i = 0; i < _extraEffects.Length; ++i)
            {
                _extraEffects[i].SendMessage(PLAY_EFFECT_METHOD);
            }
        }
    }
}
