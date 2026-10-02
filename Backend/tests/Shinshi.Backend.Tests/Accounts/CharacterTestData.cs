// System
using System.Linq;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> 현재 버전 구조를 통과하는 기본 외형 </summary>
    internal static class CharacterTestData
    {
        /****************************************
        *                Methods
        ****************************************/

        public static CharacterAppearance Appearance(int skinTone = 10)
        {
            int[] face = Enumerable.Repeat(CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.FACE_SLIDER_COUNT).ToArray();
            int[] body = Enumerable.Repeat(CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.BODY_SLIDER_COUNT).ToArray();
            return new CharacterAppearance(CharacterAppearance.CURRENT_VERSION, CharacterAppearance.BODY_TYPE_FEMALE, face,
                body, skinTone, 1, 2, 3, 4, 5);
        }
    }
}
