namespace Shinshi.Contracts
{
    /// <summary> v2 외형 ID 카탈로그, 엔진 비의존. 서버 입장 판정과 로비 선택 화면이 같은 함수로 허용 여부를 정한다 </summary>
    public static class CharacterAppearanceCatalog
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int FACE_CUTE = 1;

        public const int HAIR_WARRIOR  = 1;
        public const int HAIR_SORCERER = 2;
        public const int HAIR_SUMMONER = 3;

        public const int OUTFIT_WARRIOR  = 1;
        public const int OUTFIT_SORCERER = 2;
        public const int OUTFIT_SUMMONER = 3;

        private const int PART_MIN = 1;
        private const int PART_MAX = 3;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 모든 슬라이더와 색이 중립인 정규 v2 외형을 만든다. 직업과 무관하게 어떤 face/hair/outfit도 받는다 </summary>
        public static CharacterAppearance Create(int faceId, int hairId, int outfitId)
        {
            int[] face = NewSliders(CharacterAppearance.FACE_SLIDER_COUNT);
            int[] body = NewSliders(CharacterAppearance.BODY_SLIDER_COUNT);
            return new CharacterAppearance(CharacterAppearance.VERSION_2, CharacterAppearance.BODY_TYPE_FEMALE,
                face, body, CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.VALUE_MIN,
                CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN, hairId, CharacterAppearance.VALUE_MIN,
                faceId, outfitId);
        }

        /// <summary> 직업 기본 외형, 헤어와 의상 ID는 직업 ID + 1이다 </summary>
        public static CharacterAppearance CreateDefault(int classId)
        {
            int defaultPartId = classId + 1;
            return Create(FACE_CUTE, defaultPartId, defaultPartId);
        }

        /// <summary> 알려진 직업의 등록된 v2 외형인지, 직업과 헤어/의상 선택은 서로 독립이라 9조합 모두 허용한다 </summary>
        public static bool IsAllowed(int classId, CharacterAppearance appearance)
        {
            if ((!IsKnownClass(classId)) || (appearance == null) || (!appearance.IsWellFormed())
                || (appearance.Version != CharacterAppearance.VERSION_2))
            {
                return false;
            }

            if ((appearance.FaceId != FACE_CUTE) || (!IsPartId(appearance.HairId)) || (!IsPartId(appearance.OutfitId)))
            {
                return false;
            }

            return IsNeutral(appearance);
        }

        /// <summary> 서버 입장과 선택 화면이 같은 함수로 저장된 외형을 해석한다. v1/null은 직업 기본값, 유효한 v2는 그대로 유지한다 </summary>
        public static bool TryResolve(int classId, CharacterAppearance stored, out CharacterAppearance canonical)
        {
            if (!IsKnownClass(classId))
            {
                canonical = null;
                return false;
            }

            if ((stored == null) || ((stored.IsWellFormed()) && (stored.Version == CharacterAppearance.VERSION_1)))
            {
                canonical = CreateDefault(classId);
                return true;
            }

            if (IsAllowed(classId, stored))
            {
                canonical = Create(stored.FaceId, stored.HairId, stored.OutfitId);
                return true;
            }

            canonical = null;
            return false;
        }

        private static bool IsNeutral(CharacterAppearance appearance)
        {
            if ((appearance.SkinTone != CharacterAppearance.SLIDER_NEUTRAL) || (appearance.EyeColor != CharacterAppearance.VALUE_MIN)
                || (appearance.BrowColor != CharacterAppearance.VALUE_MIN) || (appearance.LipColor != CharacterAppearance.VALUE_MIN)
                || (appearance.HairColor != CharacterAppearance.VALUE_MIN))
            {
                return false;
            }

            return (AreAllNeutral(appearance.Face)) && (AreAllNeutral(appearance.Body));
        }

        private static bool AreAllNeutral(System.Collections.Generic.IReadOnlyList<int> sliders)
        {
            for (int i = 0; i < sliders.Count; ++i)
            {
                if (sliders[i] != CharacterAppearance.SLIDER_NEUTRAL)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPartId(int id) { return (id >= PART_MIN) && (id <= PART_MAX); }

        private static bool IsKnownClass(int classId)
        {
            return (classId == ContractCodes.PlayerClass.WARRIOR) || (classId == ContractCodes.PlayerClass.SORCERER)
                || (classId == ContractCodes.PlayerClass.SUMMONER);
        }

        private static int[] NewSliders(int count)
        {
            int[] sliders = new int[count];
            for (int i = 0; i < count; ++i)
            {
                sliders[i] = CharacterAppearance.SLIDER_NEUTRAL;
            }

            return sliders;
        }
    }
}
