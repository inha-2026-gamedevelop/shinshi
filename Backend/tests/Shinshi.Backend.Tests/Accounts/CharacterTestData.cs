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

        // v1 고정. CURRENT_VERSION을 쓰면 버전이 올라갈 때 이 기본값이 조용히 바뀌어 v1 회귀를 검사할 수 없게 된다
        public static CharacterAppearance Appearance(int skinTone = 10)
        {
            int[] face = Enumerable.Repeat(CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.FACE_SLIDER_COUNT).ToArray();
            int[] body = Enumerable.Repeat(CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.BODY_SLIDER_COUNT).ToArray();
            return new CharacterAppearance(CharacterAppearance.VERSION_1, CharacterAppearance.BODY_TYPE_FEMALE, face,
                body, skinTone, 1, 2, 3, 4, 5);
        }

        /// <summary> 카탈로그가 허용하는 v2 외형, classId와 무관하게 face/hair/outfit을 그대로 쓴다 </summary>
        public static CharacterAppearance AppearanceV2(int faceId, int hairId, int outfitId)
        {
            return CharacterAppearanceCatalog.Create(faceId, hairId, outfitId);
        }
    }
}
