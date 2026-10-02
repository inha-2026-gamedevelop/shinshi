// Unity
using UnityEngine;

// 서드파티
using Unity.Cinemachine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 캐릭터 선택 화면에서 고른 칸 쪽으로 카메라를 살짝 기울인다. 칸마다 시점 카메라를 두고 우선순위만 바꾼다 </summary>
    public sealed class CharacterSelectCameraFocus : ClientView
    {
        // 위치를 직접 트윈하지 않고 시점 카메라를 바꾸는 이유: 전환 곡선과 시간은 CinemachineBrain의 기본 블렌드가 맡고,
        // 칸별 시점은 씬 뷰에서 카메라를 옮겨 바로 조정할 수 있다

        /****************************************
        *                Fields
        ****************************************/

        // 기본 시점은 두 값 사이에 둔다. 고른 칸만 기본보다 높아야 나머지 칸이 기본 시점을 가리지 않는다
        private const int FOCUSED_PRIORITY = 20;
        private const int BASE_PRIORITY    = 10;
        private const int IDLE_PRIORITY    = 0;

        [SerializeField] private CharacterSelectSelection _selection   = null;
        [SerializeField] private CinemachineCamera        _baseCamera  = null;
        [SerializeField] private CinemachineCamera[]      _slotCameras = null;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_selection == null) || (!HasCameras()))
            {
                Debug.LogError("[CharacterSelect] 선택 상태나 시점 카메라가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _baseCamera.Priority = BASE_PRIORITY;
        }

        private void OnEnable()
        {
            _selection.SelectionChanged += ApplyPriorities;
            ApplyPriorities(_selection.SelectedSlot);
        }

        private void OnDisable() { _selection.SelectionChanged -= ApplyPriorities; }

        /****************************************
        *                Methods
        ****************************************/

        // 범위 밖 번호는 어느 칸도 올리지 않아 기본 시점이 남는다
        private void ApplyPriorities(int selectedSlot)
        {
            for (int i = 0; i < _slotCameras.Length; ++i)
            {
                _slotCameras[i].Priority = (i == selectedSlot) ? FOCUSED_PRIORITY : IDLE_PRIORITY;
            }
        }

        private bool HasCameras()
        {
            if ((_baseCamera == null) || (_slotCameras == null) || (_slotCameras.Length == 0))
            {
                return false;
            }

            for (int i = 0; i < _slotCameras.Length; ++i)
            {
                if (_slotCameras[i] == null)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
