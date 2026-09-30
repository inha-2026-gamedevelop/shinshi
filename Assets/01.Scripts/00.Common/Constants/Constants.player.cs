namespace Shinshi.Common
{
    public static partial class Constants
    {
        /// <summary> 입력 이름과 조작 슬롯 개수 </summary>
        public static class Player
        {
            /****************************************
            *                Fields
            ****************************************/

            // 입력은 구 Input Manager(UnityEngine.Input)를 쓴다. 축, 버튼 이름은 ProjectSettings/InputManager.asset에 있는 것만 둔다
            // TODO(민성): 이동 클릭, 기술 Q/W/E/R, 회피, 물약 1/2, 강림 Z/X/C 이름은 U04에서 InputManager 설정과 같이 넣는다

            public const int SKILL_SLOT_COUNT  = 4;
            public const int SUMMON_SLOT_COUNT = 3;
            public const int POTION_SLOT_COUNT = 2;
        }
    }
}
