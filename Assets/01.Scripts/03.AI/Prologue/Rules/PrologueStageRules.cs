// System
using System;

namespace Shinshi.AI
{
    /// <summary> 프롤로그 단계 전이와 단계별 규칙, 엔진 비의존 순수 규칙이라 서버와 클라이언트가 같이 쓴다 </summary>
    public static class PrologueStageRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 일반 전투 중 체력 0 회복 직후 무적 시간
        public const float REVIVE_INVULNERABLE_SECONDS = 3f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 구간을 끝냈을 때 이어지는 단계, 완료는 그대로 완료다 </summary>
        public static EPrologueStage Next(EPrologueStage current)
        {
            switch (current)
            {
                case EPrologueStage.Intro:
                    return EPrologueStage.Forest;
                case EPrologueStage.Forest:
                    return EPrologueStage.Wolbaek;
                case EPrologueStage.Wolbaek:
                    return EPrologueStage.Ending;
                case EPrologueStage.Ending:
                    return EPrologueStage.Done;
                default:
                    return current;
            }
        }

        /// <summary> 저장된 번호를 단계로 되살린다, 모르는 번호면 거짓이다 </summary>
        public static bool TryFromStored(int stored, out EPrologueStage stage)
        {
            stage = EPrologueStage.Intro;
            if (!Enum.IsDefined(typeof(EPrologueStage), stored))
            {
                return false;
            }

            stage = (EPrologueStage)stored;
            return true;
        }

        /// <summary> 체력 0일 때 제자리에서 최대 체력으로 회복시키는 단계인가, 월백 교전은 회복 없이 마무리로 간다 </summary>
        public static bool RevivesOnDown(EPrologueStage stage)
        {
            return stage == EPrologueStage.Forest;
        }
    }
}
