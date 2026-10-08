// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.UI
{
    /// <summary>볼륨의 작은 조절 버튼이 슬라이더와 동일한 변경 이벤트를 발생시킨다</summary>
    public sealed class SettingsVolumeStep : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Slider _slider = null;
        [SerializeField] private float  _step   = 0.05f;

        /****************************************
        *                Methods
        ****************************************/

        public void Step()
        {
            if ((_slider == null) || (!_slider.IsInteractable()))
            {
                return;
            }
            _slider.value = Mathf.Clamp01(_slider.value + _step);
        }
    }
}
