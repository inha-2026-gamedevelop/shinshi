// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 숲 조작 안내, 월백 목표, 알림 문구 모음. 키와 마우스 버튼 이름은 Constants에서 읽어 입력이 바뀌어도 문구가 어긋나지 않게 한다 </summary>
    public static class PrologueHintTexts
    {
        // 문구는 안내 멘트 초안을 따른다. 위쪽 목표와 아래쪽 설명 두 줄이 한 단계를 이룬다.
        // 물약 안내에는 키 이름을 쓰지 않는다. 튜토리얼 임시 물약의 종류와 입력 연결이 확인되지 않아서, 키를 적으면
        // 확정된 조작으로 받아들여지기 때문이다. 연결이 확인되면 키 이름을 넣는다

        /****************************************
        *                Fields
        ****************************************/

        public const string WOLBAEK_OBJECTIVE   = "월백의 공격에 맞서기";
        public const string WOLBAEK_INSTRUCTION = "기본 공격과 기술을 사용하며 공격 예고에 대응하세요.";

        public const string POTION_INSTRUCTION = "물약으로 체력을 회복하세요.";
        public const string REVIVE_NOTICE      = "체력이 회복되었습니다. 잠시 공격을 받지 않습니다.";

        public const string MOVING_TO_TOWN     = "신시로 이동 중입니다.";
        public const string PROLOGUE_COMPLETED = "프롤로그를 마쳤습니다. 신시에서 모험을 이어가세요.";
        public const string RESUME_COMMON      = "중단한 구간의 처음부터 다시 시작합니다.";
        public const string RESUME_FOREST      = "튜토리얼 물약이 3개로 준비되었습니다.";
        public const string RESUME_WOLBAEK     = "체력과 물약이 교전 시작 상태로 복원되었습니다.";

        public const string SKIP_INTRO_LABEL      = "도입 이야기 건너뛰기";
        public const string SKIP_APPEARANCE_LABEL = "등장 이야기 건너뛰기";
        public const string SKIP_ENDING_LABEL     = "마무리 이야기 건너뛰기";

        private const string LEFT_BUTTON  = "왼쪽";
        private const string RIGHT_BUTTON = "오른쪽";
        private const string OTHER_BUTTON = "마우스";
        private const string ALPHA_PREFIX = "Alpha";

        private const string OBJECTIVE_MOVE           = "숲길 따라 이동하기";
        private const string OBJECTIVE_ATTACK         = "기본 공격 사용하기";
        private const string OBJECTIVE_SKILL_FORMAT   = "{0} 기술 사용하기";
        private const string OBJECTIVE_KILL_REMAINING = "남은 산귀 처치하기";
        private const string OBJECTIVE_KILL_BLOCKER   = "앞을 막은 산귀 처치하기";

        private const string MOVE_FORMAT            = "이동할 곳을 마우스 {0} 버튼으로 클릭하세요.";
        private const string ATTACK_FORMAT          = "적을 향해 마우스를 두고 {0} 버튼을 클릭하세요.";
        private const string SKILL_FORMAT           = "적을 향해 마우스를 두고 [{0}]를 눌러 기술을 사용하세요.";
        private const string KILL_REMAINING_TEXT    = "기본 공격과 기술로 남은 산귀를 처치하세요.";
        private const string USE_ATTACK_FORMAT      = "마우스 {0} 버튼을 클릭해 기본 공격을 한 번 사용하세요.";
        private const string USE_SKILL_FORMAT       = "[{0}] 기술을 한 번 사용하세요. 적에게 맞히지 않아도 됩니다.";
        private const string USE_BOTH_FORMAT        = "기본 공격과 [{0}] 기술을 한 번씩 사용하세요.";
        private const string FIGHT_TELEGRAPH_TEXT   = "적의 공격을 살피며 싸우세요.";
        private const string LEAVE_DANGER_TEXT      = "공격 예고가 표시된 곳에서 벗어나세요.";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 힌트의 위쪽 목표 문구, 목표 없이 설명만 보일 때는 빈 문자열이다 </summary>
        public static string ObjectiveFor(EForestHint hint)
        {
            switch (hint)
            {
                case EForestHint.Move:
                    return OBJECTIVE_MOVE;
                case EForestHint.BasicAttack:
                case EForestHint.UseAttackOnce:
                    return OBJECTIVE_ATTACK;
                case EForestHint.SkillQ:
                case EForestHint.UseSkillOnce:
                    return string.Format(OBJECTIVE_SKILL_FORMAT, KeyName(Constants.Player.KEY_SKILL_Q));
                case EForestHint.KillRemaining:
                    return OBJECTIVE_KILL_REMAINING;
                case EForestHint.FightTelegraphEnemy:
                case EForestHint.LeaveDangerZone:
                    return OBJECTIVE_KILL_BLOCKER;
                default:
                    return string.Empty;
            }
        }

        /// <summary> 힌트의 아래쪽 설명 문구, 보일 것이 없으면 빈 문자열이다 </summary>
        public static string InstructionFor(EForestHint hint)
        {
            switch (hint)
            {
                case EForestHint.Move:
                    return string.Format(MOVE_FORMAT, ButtonName(Constants.Player.MOUSE_BUTTON_MOVE));
                case EForestHint.BasicAttack:
                    return string.Format(ATTACK_FORMAT, ButtonName(Constants.Player.MOUSE_BUTTON_ATTACK));
                case EForestHint.SkillQ:
                    return string.Format(SKILL_FORMAT, KeyName(Constants.Player.KEY_SKILL_Q));
                case EForestHint.KillRemaining:
                    return KILL_REMAINING_TEXT;
                case EForestHint.UseAttackOnce:
                    return string.Format(USE_ATTACK_FORMAT, ButtonName(Constants.Player.MOUSE_BUTTON_ATTACK));
                case EForestHint.UseSkillOnce:
                    return string.Format(USE_SKILL_FORMAT, KeyName(Constants.Player.KEY_SKILL_Q));
                case EForestHint.UseBothOnce:
                    return string.Format(USE_BOTH_FORMAT, KeyName(Constants.Player.KEY_SKILL_Q));
                case EForestHint.FightTelegraphEnemy:
                    return FIGHT_TELEGRAPH_TEXT;
                case EForestHint.LeaveDangerZone:
                    return LEAVE_DANGER_TEXT;
                default:
                    return string.Empty;
            }
        }

        /// <summary> 중단 후 재개 때의 알림, 단계에 맞는 보조 안내를 아래 줄에 붙인다 </summary>
        public static string ResumeNotice(EPrologueStage stage)
        {
            switch (stage)
            {
                case EPrologueStage.Forest:
                    return RESUME_COMMON + "\n" + RESUME_FOREST;
                case EPrologueStage.Wolbaek:
                    return RESUME_COMMON + "\n" + RESUME_WOLBAEK;
                default:
                    return RESUME_COMMON;
            }
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
