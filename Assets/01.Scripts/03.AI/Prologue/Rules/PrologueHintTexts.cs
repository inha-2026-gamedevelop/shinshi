// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 숲 조작 안내와 월백 목표 문구, 키와 마우스 버튼 이름은 Constants에서 읽어 입력이 바뀌어도 문구가 어긋나지 않게 한다 </summary>
    public static class PrologueHintTexts
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string WOLBAEK_OBJECTIVE = "월백의 공격에 맞서기";

        private const string LEFT_BUTTON         = "왼쪽";
        private const string RIGHT_BUTTON        = "오른쪽";
        private const string OTHER_BUTTON        = "마우스";
        private const string ALPHA_PREFIX        = "Alpha";
        private const string MOVE_FORMAT         = "마우스 {0} 버튼으로 이동";
        private const string ATTACK_FORMAT       = "마우스 {0} 버튼으로 공격";
        private const string SKILL_FORMAT        = "{0} 키로 기술 사용";
        private const string POTION_FORMAT       = "{0} 키로 물약 사용";
        private const string DANGER_TEXT         = "공격 예고 범위 밖으로 벗어나세요";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 힌트에 맞는 화면 문구, 보일 것이 없으면 빈 문자열이다 </summary>
        public static string ForHint(EForestHint hint)
        {
            switch (hint)
            {
                case EForestHint.Move:
                    return string.Format(MOVE_FORMAT, ButtonName(Constants.Player.MOUSE_BUTTON_MOVE));
                case EForestHint.BasicAttack:
                    return string.Format(ATTACK_FORMAT, ButtonName(Constants.Player.MOUSE_BUTTON_ATTACK));
                case EForestHint.SkillQ:
                    return string.Format(SKILL_FORMAT, KeyName(Constants.Player.KEY_SKILL_Q));
                case EForestHint.LeaveDangerZone:
                    return DANGER_TEXT;
                default:
                    return string.Empty;
            }
        }

        /// <summary> 체력이 줄었고 물약이 남았을 때 띄우는 물약 안내 </summary>
        public static string PotionHint()
        {
            return string.Format(POTION_FORMAT, KeyName(Constants.Player.KEY_POTION_SMALL));
        }

        private static string ButtonName(int button)
        {
            if (button == 0)
            {
                return LEFT_BUTTON;
            }

            return (button == 1) ? RIGHT_BUTTON : OTHER_BUTTON;
        }

        // 숫자 키는 Alpha1처럼 불려서 앞의 이름을 떼어 화면에는 1로 보인다
        private static string KeyName(KeyCode key)
        {
            string name = key.ToString();
            return name.StartsWith(ALPHA_PREFIX) ? name.Substring(ALPHA_PREFIX.Length) : name;
        }
    }
}
