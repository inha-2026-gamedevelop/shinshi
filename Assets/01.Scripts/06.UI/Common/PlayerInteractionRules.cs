namespace Shinshi.UI
{
    /// <summary> 눌린 대상이 내 캐릭터인지 다른 유저인지 가르는 순수 계산 </summary>
    public static class PlayerInteractionRules
    {
        /****************************************
        *                Types
        ****************************************/

        public enum ETarget
        {
            None,
            Self,
            Other,
        }

        /****************************************
        *                Methods
        ****************************************/

        // 씬에 고정으로 연결한 콜라이더(개발 씬)가 우선이고, 아니면 맞은 오브젝트 부모의 플레이어 상태로 가른다
        public static ETarget Classify(bool isFixedSelf, bool isFixedOther, bool hasPlayer, bool isLocalPlayer)
        {
            if (isFixedSelf)
            {
                return ETarget.Self;
            }

            if (isFixedOther)
            {
                return ETarget.Other;
            }

            if (!hasPlayer)
            {
                return ETarget.None;
            }

            return isLocalPlayer ? ETarget.Self : ETarget.Other;
        }
    }
}
