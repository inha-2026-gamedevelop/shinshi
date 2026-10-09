namespace Shinshi.Contracts
{
    /// <summary> 외형 ID 카탈로그, 엔진 비의존. API 생성 검증, 서버 입장 판정, 로비 선택 화면이 같은 함수로 허용 여부를 정한다 </summary>
    public static class CharacterAppearanceCatalog
    {
        // v2는 Tripo 3종 조합, v3는 P09 모듈 캐릭터다. 새 캐릭터는 v3로 만들고, API는 기존 v2 요청의 재시도를 위해 v2도 계속 받는다.
        // 표시는 항상 v3로 한다. TryResolve가 null, v1, v2를 직업 기본 v3로 바꾸므로 기존 캐릭터는 DB 값과 무관하게 직업 기본 P09로 보인다

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

        // v3(P09) 허용 범위. 0이 의미 있는 값(없음, 끔)인 항목만 최솟값이 0이다
        public const int P09_FACE_MIN         = 1;
        public const int P09_FACE_MAX         = 3;
        public const int P09_HAIR_01          = 1;
        public const int P09_HAIR_02          = 2;
        public const int P09_HAIR_SKINHEAD    = 3;
        public const int P09_HAIR_COLOR_MIN   = 1;
        public const int P09_HAIR_COLOR_MAX   = 9;
        public const int P09_EYE_COLOR_MIN    = 1;
        public const int P09_EYE_COLOR_MAX    = 5;
        public const int P09_SKIN_MIN         = 1;
        public const int P09_SKIN_MAX         = 3;
        public const int P09_FACIAL_HAIR_NONE = 0;
        public const int P09_FACIAL_HAIR_MAX  = 8;
        public const int P09_BUST_NONE        = 0;
        public const int P09_BUST_MIN         = 1;
        public const int P09_BUST_DEFAULT     = 2;
        public const int P09_BUST_MAX         = 3;

        // outfitId 비트, 1이면 그 부위에 Armor_003을 입는다
        public const int P09_OUTFIT_HEAD  = 1;
        public const int P09_OUTFIT_CHEST = 2;
        public const int P09_OUTFIT_ARM   = 4;
        public const int P09_OUTFIT_WAIST = 8;
        public const int P09_OUTFIT_LEG   = 16;
        public const int P09_OUTFIT_ALL   = P09_OUTFIT_HEAD | P09_OUTFIT_CHEST | P09_OUTFIT_ARM | P09_OUTFIT_WAIST | P09_OUTFIT_LEG;

        // 직업 기본 v3. 순서는 bodyType, face, hair, hairColor, eye, skin, facialHair, bust, outfit
        private static readonly int[][] G_ClassDefaults = {
            new[] { CharacterAppearance.BODY_TYPE_FEMALE, 1, P09_HAIR_01, 1, 1, 1, P09_FACIAL_HAIR_NONE, P09_BUST_DEFAULT, P09_OUTFIT_ALL },
            new[] { CharacterAppearance.BODY_TYPE_FEMALE, 2, P09_HAIR_02, 5, 4, 2, P09_FACIAL_HAIR_NONE, P09_BUST_DEFAULT, P09_OUTFIT_ALL },
            new[] { CharacterAppearance.BODY_TYPE_FEMALE, 3, P09_HAIR_02, 8, 3, 1, P09_FACIAL_HAIR_NONE, P09_BUST_DEFAULT, P09_OUTFIT_ALL },
            new[] { CharacterAppearance.BODY_TYPE_FEMALE, 1, P09_HAIR_01, 3, 2, 1, P09_FACIAL_HAIR_NONE, P09_BUST_DEFAULT, P09_OUTFIT_ALL },
            new[] { CharacterAppearance.BODY_TYPE_FEMALE, 2, P09_HAIR_02, 6, 5, 2, P09_FACIAL_HAIR_NONE, P09_BUST_DEFAULT, P09_OUTFIT_ALL },
            new[] { CharacterAppearance.BODY_TYPE_FEMALE, 3, P09_HAIR_01, 7, 3, 3, P09_FACIAL_HAIR_NONE, P09_BUST_DEFAULT, P09_OUTFIT_ALL } };

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

        /// <summary> 슬라이더가 중립이고 쓰지 않는 색이 0인 정규 v3(P09) 외형을 만든다. 값 검사는 IsAllowed가 한다 </summary>
        public static CharacterAppearance CreateP09(int bodyType, int faceId, int hairId, int hairColor, int eyeColor,
            int skinTone, int facialHair, int bustSize, int outfitMask)
        {
            int[] face = NewSliders(CharacterAppearance.FACE_SLIDER_COUNT);
            int[] body = NewSliders(CharacterAppearance.BODY_SLIDER_COUNT);
            return new CharacterAppearance(CharacterAppearance.VERSION_3, bodyType, face, body, skinTone, eyeColor,
                CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN, hairId, hairColor, faceId, outfitMask,
                facialHair, bustSize);
        }

        /// <summary> 직업 기본 v2(Tripo) 외형, 헤어와 의상 ID는 직업 ID + 1이다. 기존 Tripo 선택 상태가 쓴다 </summary>
        public static CharacterAppearance CreateDefaultV2(int classId)
        {
            int defaultPartId = classId + 1;
            return Create(FACE_CUTE, defaultPartId, defaultPartId);
        }

        /// <summary> 직업 기본 v3 외형, 모르는 직업이면 null이다 </summary>
        public static CharacterAppearance CreateDefault(int classId)
        {
            if (!IsKnownClass(classId))
            {
                return null;
            }

            int[] values = G_ClassDefaults[classId];
            return CreateP09(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8]);
        }

        /// <summary> 알려진 직업의 허용된 외형인지. v2는 Tripo 9조합 규칙, v3는 P09 범위와 성별 조건을 본다. 직업은 외형을 제한하지 않는다 </summary>
        public static bool IsAllowed(int classId, CharacterAppearance appearance)
        {
            if ((!IsKnownClass(classId)) || (appearance == null) || (!appearance.IsWellFormed()))
            {
                return false;
            }

            if (appearance.Version == CharacterAppearance.VERSION_2)
            {
                return (appearance.FaceId == FACE_CUTE) && (IsPartId(appearance.HairId)) && (IsPartId(appearance.OutfitId))
                    && (IsNeutralV2(appearance));
            }

            return (appearance.Version == CharacterAppearance.VERSION_3) && (IsAllowedP09(appearance));
        }

        /// <summary> 서버 입장과 선택 화면이 같은 함수로 저장된 외형을 해석한다. 결과는 항상 정규 v3이며, null, v1, 허용된 v2는 직업 기본 v3다 </summary>
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

            if (!IsAllowed(classId, stored))
            {
                canonical = null;
                return false;
            }

            canonical = (stored.Version == CharacterAppearance.VERSION_3)
                ? CreateP09(stored.BodyType, stored.FaceId, stored.HairId, stored.HairColor, stored.EyeColor, stored.SkinTone,
                    stored.FacialHair, stored.BustSize, stored.OutfitId)
                : CreateDefault(classId);
            return true;
        }

        /// <summary> 성별에 따라 수염과 가슴 크기를 허용 범위로 맞춘다. 생성 화면이 모든 선택 변경 뒤에 부른다 </summary>
        public static void NormalizeForBodyType(int bodyType, ref int facialHair, ref int bustSize)
        {
            if (bodyType == CharacterAppearance.BODY_TYPE_MALE)
            {
                bustSize = P09_BUST_NONE;
                return;
            }

            facialHair = P09_FACIAL_HAIR_NONE;
            if (!IsInRange(bustSize, P09_BUST_MIN, P09_BUST_MAX))
            {
                bustSize = P09_BUST_DEFAULT;
            }
        }

        private static bool IsAllowedP09(CharacterAppearance appearance)
        {
            if ((!IsInRange(appearance.FaceId, P09_FACE_MIN, P09_FACE_MAX))
                || (!IsInRange(appearance.HairId, P09_HAIR_01, P09_HAIR_SKINHEAD))
                || (!IsInRange(appearance.HairColor, P09_HAIR_COLOR_MIN, P09_HAIR_COLOR_MAX))
                || (!IsInRange(appearance.EyeColor, P09_EYE_COLOR_MIN, P09_EYE_COLOR_MAX))
                || (!IsInRange(appearance.SkinTone, P09_SKIN_MIN, P09_SKIN_MAX))
                || (!IsInRange(appearance.OutfitId, 0, P09_OUTFIT_ALL)))
            {
                return false;
            }

            // 수염은 남성만, 가슴 크기는 여성만 0이 아니다
            bool isMale            = appearance.BodyType == CharacterAppearance.BODY_TYPE_MALE;
            bool isBodyDetailValid = isMale
                ? ((IsInRange(appearance.FacialHair, P09_FACIAL_HAIR_NONE, P09_FACIAL_HAIR_MAX)) && (appearance.BustSize == P09_BUST_NONE))
                : ((appearance.FacialHair == P09_FACIAL_HAIR_NONE) && (IsInRange(appearance.BustSize, P09_BUST_MIN, P09_BUST_MAX)));
            if (!isBodyDetailValid)
            {
                return false;
            }

            return (appearance.BrowColor == CharacterAppearance.VALUE_MIN) && (appearance.LipColor == CharacterAppearance.VALUE_MIN)
                && (AreAllNeutral(appearance.Face)) && (AreAllNeutral(appearance.Body));
        }

        private static bool IsNeutralV2(CharacterAppearance appearance)
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

        private static bool IsInRange(int value, int min, int max) { return (value >= min) && (value <= max); }

        private static bool IsKnownClass(int classId)
        {
            return (classId == ContractCodes.PlayerClass.WARRIOR) || (classId == ContractCodes.PlayerClass.SORCERER)
                || (classId == ContractCodes.PlayerClass.SUMMONER) || (classId == ContractCodes.PlayerClass.SWORDSMAN)
                || (classId == ContractCodes.PlayerClass.MAGEKNIGHT) || (classId == ContractCodes.PlayerClass.ARCHER);
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
