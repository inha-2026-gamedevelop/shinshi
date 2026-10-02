// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 동료 상태를 임시 도형으로 보여 준다, 최종 모델과 애니메이션이 정해지면 바꾼다 </summary>
    public sealed class CompanionView : ClientView
    {
        // 판정에 쓰는 값은 하나도 계산하지 않는다. 위치는 NetworkTransform, 방향과 종류, 활성은 CompanionCombatState가 정본이다

        /****************************************
        *                Fields
        ****************************************/

        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        [Header("참조")]
        [SerializeField] private CompanionCombatState _state;
        [SerializeField] private Transform            _visualPivot;
        [SerializeField] private Renderer             _bodyRenderer;

        [Header("임시 색")]
        [SerializeField] private Color _meleeColor  = new Color(0.62f, 0.58f, 0.52f, 1f); // 돌등, 근접
        [SerializeField] private Color _rangedColor = new Color(0.55f, 0.85f, 0.45f, 1f); // 솔빛, 원거리

        private MaterialPropertyBlock _properties  = null;
        private int                   _appliedSlot = -1;
        private bool                  _isShown     = true;
        private bool                  _hasWarned   = false;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _properties = new MaterialPropertyBlock();
        }

        // NetworkTransform이 Update에서 루트를 옮긴 뒤 시각 피벗만 돌린다. 루트 회전은 복제하지 않는다
        private void LateUpdate()
        {
            if ((_state == null) || (_visualPivot == null) || (_bodyRenderer == null))
            {
                if (!_hasWarned)
                {
                    _hasWarned = true;
                    Debug.LogError($"[Combat] {name}의 CompanionView 참조가 비어 있어 표시하지 않는다");
                }

                return;
            }

            SetShown(_state.IsActive);
            if (!_isShown)
            {
                return;
            }

            _visualPivot.rotation = Quaternion.LookRotation(FacingRules.DecodeYaw(_state.FacingYaw), Vector3.up);
            if (_state.Slot != _appliedSlot)
            {
                ApplyTint(_state.Slot);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void SetShown(bool shown)
        {
            if (shown == _isShown)
            {
                return;
            }

            _isShown = shown;
            _visualPivot.gameObject.SetActive(shown);
        }

        private void ApplyTint(int slot)
        {
            _appliedSlot = slot;
            _bodyRenderer.GetPropertyBlock(_properties);
            _properties.SetColor(_baseColorId, (slot == Constants.Combat.SKILL_SLOT_W) ? _rangedColor : _meleeColor);
            _bodyRenderer.SetPropertyBlock(_properties);
        }
    }
}
