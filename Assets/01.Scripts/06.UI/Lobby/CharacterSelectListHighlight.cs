// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 캐릭터 선택 리스트에서 고른 칸은 선택 테두리와 빛 알갱이를 켜고, 마우스를 올린 칸은 테두리만 옅게 미리 보인다 </summary>
    public sealed class CharacterSelectListHighlight : ClientView
    {
        // 카드 스프라이트를 선택용으로 바꾸지 않고 카드 위에 테두리를 덮는 이유: 선택 텍스처는 바깥 그림자 여백이 넓고
        // 좌우, 위아래 여백도 달라 같은 크기로 바꿔 끼우면 테두리 선이 안쪽으로 줄고 한쪽으로 쏠려 보였다.
        // 덮개는 칸별 RectTransform을 바깥으로 넓혀 두 텍스처의 선을 겹치므로 텍스처를 다시 자르지 않아도 된다

        /****************************************
        *                Fields
        ****************************************/

        private const float HIDDEN_ALPHA   = 0f;
        private const float SELECTED_ALPHA = 1f;

        [SerializeField] private CharacterSelectSelection _selection    = null;
        [SerializeField] private Image[]                  _frames       = null;
        [SerializeField] private float                    _hoverAlpha   = 0.35f;
        [SerializeField] private float                    _fadeDuration = 0.12f;

        // 칸 순서는 _frames와 같고, 비어 있는 칸은 알갱이 없이 테두리만 켠다
        [SerializeField] private UiParticleEmitter[] _selectedParticles = null;

        private Tweener[] _fadeTweens = null;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_selection == null) || (!HasFrames()))
            {
                Debug.LogError("[CharacterSelect] 리스트 하이라이트에 선택 상태나 선택 테두리가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            CreateFadeTweens();
        }

        private void OnEnable()
        {
            _selection.SelectionChanged += OnSelectionChanged;
            _selection.HoverChanged     += OnHoverChanged;
            ApplyHighlight(true);
            ApplyParticles();
        }

        private void OnDisable()
        {
            _selection.SelectionChanged -= OnSelectionChanged;
            _selection.HoverChanged     -= OnHoverChanged;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnSelectionChanged(int slot)
        {
            ApplyHighlight(false);
            ApplyParticles();
        }

        private void OnHoverChanged(int slot) { ApplyHighlight(false); }

        private void ApplyHighlight(bool isImmediate)
        {
            for (int i = 0; i < _frames.Length; ++i)
            {
                float target = GetTargetAlpha(i);
                if (isImmediate)
                {
                    _fadeTweens[i].Pause();
                    SetFrameAlpha(i, target);
                    continue;
                }

                if (Mathf.Approximately(GetFrameAlpha(i), target))
                {
                    continue;
                }

                // 지금 보이는 알파에서 출발해야 페이드 도중 다른 칸으로 옮겨도 튀지 않는다
                _fadeTweens[i].ChangeEndValue(target, true);
                _fadeTweens[i].Play();
            }
        }

        // 떠난 칸은 새로 뿌리지만 않고 떠 있는 알갱이는 수명대로 사라지게 둔다
        private void ApplyParticles()
        {
            if (_selectedParticles == null)
            {
                return;
            }

            for (int i = 0; i < _selectedParticles.Length; ++i)
            {
                UiParticleEmitter particles = _selectedParticles[i];
                if (particles == null)
                {
                    continue;
                }

                bool isSelected = (i == _selection.SelectedSlot);
                if (isSelected == particles.IsEmitting)
                {
                    continue;
                }

                if (isSelected)
                {
                    particles.Play();
                }
                else
                {
                    particles.Stop();
                }
            }
        }

        private float GetTargetAlpha(int slot)
        {
            if (slot == _selection.SelectedSlot)
            {
                return SELECTED_ALPHA;
            }

            return (slot == _selection.HoveredSlot) ? _hoverAlpha : HIDDEN_ALPHA;
        }

        // 칸을 옮길 때마다 페이드를 다시 쓰므로 칸별로 한 번 만들어 재사용한다.
        // Image.DOFade는 Shinshi.Runtime이 참조하지 못하는 DOTween 모듈에 있어 코어 DOTween.To를 쓴다
        private void CreateFadeTweens()
        {
            _fadeTweens = new Tweener[_frames.Length];
            for (int i = 0; i < _frames.Length; ++i)
            {
                int slot = i;
                _fadeTweens[i] = DOTween.To(() => GetFrameAlpha(slot), alpha => SetFrameAlpha(slot, alpha), HIDDEN_ALPHA, _fadeDuration)
                    .SetUpdate(true)
                    .SetAutoKill(false)
                    .Pause()
                    .SetLink(gameObject);
            }
        }

        private float GetFrameAlpha(int slot) { return _frames[slot].color.a; }

        private void SetFrameAlpha(int slot, float alpha)
        {
            Color color = _frames[slot].color;
            color.a = alpha;
            _frames[slot].color = color;
        }

        private bool HasFrames()
        {
            if ((_frames == null) || (_frames.Length == 0))
            {
                return false;
            }

            for (int i = 0; i < _frames.Length; ++i)
            {
                if (_frames[i] == null)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
