// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary>내 캐릭터와 파티원 기술 이펙트의 투명도 설정을 저장값과 미리보기까지 함께 처리한다</summary>
    public static class VfxVisibility
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string OWN_PREFS_KEY   = "Game.VfxOwnOpacity";
        public const string PARTY_PREFS_KEY = "Game.VfxPartyOpacity";
        public const float  DEFAULT_OPACITY = 1f;

        // 이 값 이하는 완전히 투명한 것으로 보고 이펙트 재생 자체를 건너뛴다
        private const float HIDDEN_THRESHOLD = 0.01f;

        public static event Action Changed;

        /****************************************
        *              Properties
        ****************************************/

        public static float OwnOpacity { get; private set; } = DEFAULT_OPACITY;

        public static float PartyOpacity { get; private set; } = DEFAULT_OPACITY;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>이 플레이어의 이펙트에 적용할 투명도를 돌려준다. 0이면 숨김, 1이면 원본이다</summary>
        public static float Opacity(bool isOwned)
        {
            return isOwned ? OwnOpacity : PartyOpacity;
        }

        /// <summary>이 플레이어의 이펙트를 화면에 그려도 되는지 돌려준다</summary>
        public static bool Allows(bool isOwned)
        {
            return Opacity(isOwned) > HIDDEN_THRESHOLD;
        }

        /// <summary>저장된 내 이펙트 투명도를 읽고 손상된 값은 기본값으로 복구한다</summary>
        public static float LoadOwn()
        {
            return Valid(PlayerPrefs.GetFloat(OWN_PREFS_KEY, DEFAULT_OPACITY));
        }

        /// <summary>저장된 파티원 이펙트 투명도를 읽고 손상된 값은 기본값으로 복구한다</summary>
        public static float LoadParty()
        {
            return Valid(PlayerPrefs.GetFloat(PARTY_PREFS_KEY, DEFAULT_OPACITY));
        }

        /// <summary>값을 저장하며 디스크 반영은 설정 창의 적용 경로에서 수행한다</summary>
        public static void Save(float own, float party)
        {
            PlayerPrefs.SetFloat(OWN_PREFS_KEY, Valid(own));
            PlayerPrefs.SetFloat(PARTY_PREFS_KEY, Valid(party));
        }

        /// <summary>저장 없이 투명도를 바꿔 본다. 취소 시 마지막 적용값을 다시 전달한다</summary>
        public static void Preview(float own, float party)
        {
            own = Valid(own);
            party = Valid(party);
            if ((Mathf.Approximately(OwnOpacity, own)) && (Mathf.Approximately(PartyOpacity, party)))
            {
                return;
            }

            OwnOpacity = own;
            PartyOpacity = party;
            Changed?.Invoke();
        }

        private static float Valid(float value)
        {
            return float.IsNaN(value) ? DEFAULT_OPACITY : Mathf.Clamp01(value);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            Changed = null;
            OwnOpacity = DEFAULT_OPACITY;
            PartyOpacity = DEFAULT_OPACITY;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplySavedPreference()
        {
#if !UNITY_SERVER
            Preview(LoadOwn(), LoadParty());
#endif
        }
    }
}
