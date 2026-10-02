// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary>
    /// 배치 편집기 JSON에서 만든 UI 오브젝트에 원본 id, 설명, 표시 조건을 남긴다
    ///
    /// 다시 적용할 때 이름이 바뀌어도 같은 오브젝트를 찾는 열쇠가 id다.
    /// 런타임 동작은 없다. 사람과 Unity MCP가 인스펙터에서 이 요소가 무엇인지 읽는 용도다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiLayoutNote : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary>배치 편집기의 표시 조건. 직렬화되므로 값을 재배열하지 않는다</summary>
        public enum ECondition
        {
            Always = 0,
            Field  = 1,
            Boss   = 2,
        }

        /****************************************
        *                Fields
        ****************************************/

        private const int DESCRIPTION_MIN_LINES = 2;
        private const int DESCRIPTION_MAX_LINES = 6;

        [SerializeField] private string     _layoutId  = string.Empty;
        [SerializeField] private ECondition _condition = ECondition.Always;
        [SerializeField, TextArea(DESCRIPTION_MIN_LINES, DESCRIPTION_MAX_LINES)] private string _description = string.Empty;

        public string     LayoutId    => _layoutId;
        public ECondition Condition   => _condition;
        public string     Description => _description;

        /****************************************
        *                Methods
        ****************************************/

        public void Apply(string layoutId, string description, ECondition condition)
        {
            _layoutId    = layoutId ?? string.Empty;
            _description = description ?? string.Empty;
            _condition   = condition;
        }
    }
}
