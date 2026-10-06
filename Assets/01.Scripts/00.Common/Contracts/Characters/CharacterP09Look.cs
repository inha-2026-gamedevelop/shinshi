namespace Shinshi.Contracts
{
    /// <summary> P09 모듈 캐릭터 조립에 필요한 외형 식별자, 정규 v3 외형에서만 만든다 </summary>
    public readonly struct CharacterP09Look
    {
        /****************************************
        *              Properties
        ****************************************/

        public int BodyType   { get; }
        public int FaceId     { get; }
        public int HairId     { get; }
        public int HairColor  { get; }
        public int EyeColor   { get; }
        public int SkinTone   { get; }
        public int FacialHair { get; }
        public int BustSize   { get; }
        public int OutfitMask { get; }

        public bool IsMale => BodyType == CharacterAppearance.BODY_TYPE_MALE;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterP09Look(int bodyType, int faceId, int hairId, int hairColor, int eyeColor, int skinTone,
            int facialHair, int bustSize, int outfitMask)
        {
            BodyType   = bodyType;
            FaceId     = faceId;
            HairId     = hairId;
            HairColor  = hairColor;
            EyeColor   = eyeColor;
            SkinTone   = skinTone;
            FacialHair = facialHair;
            BustSize   = bustSize;
            OutfitMask = outfitMask;
        }

        /// <summary> 정규 v3 외형에서만 값을 뽑는다, 호출자가 먼저 TryResolve로 정규화해야 한다 </summary>
        public static bool TryFrom(CharacterAppearance appearance, out CharacterP09Look look)
        {
            if ((appearance == null) || (appearance.Version != CharacterAppearance.VERSION_3))
            {
                look = default;
                return false;
            }

            look = new CharacterP09Look(appearance.BodyType, appearance.FaceId, appearance.HairId, appearance.HairColor,
                appearance.EyeColor, appearance.SkinTone, appearance.FacialHair, appearance.BustSize, appearance.OutfitId);
            return true;
        }

        public CharacterAppearance ToAppearance()
        {
            return CharacterAppearanceCatalog.CreateP09(BodyType, FaceId, HairId, HairColor, EyeColor, SkinTone, FacialHair,
                BustSize, OutfitMask);
        }

        public bool HasOutfit(int outfitBit) { return (OutfitMask & outfitBit) != 0; }

        public bool Equals(CharacterP09Look other)
        {
            return (BodyType == other.BodyType) && (FaceId == other.FaceId) && (HairId == other.HairId)
                && (HairColor == other.HairColor) && (EyeColor == other.EyeColor) && (SkinTone == other.SkinTone)
                && (FacialHair == other.FacialHair) && (BustSize == other.BustSize) && (OutfitMask == other.OutfitMask);
        }

        public override bool Equals(object obj) { return (obj is CharacterP09Look other) && Equals(other); }

        // 모든 값이 1바이트 범위라 자리를 나눠 섞는다
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = BodyType;
                hash = (hash * 31) + FaceId;
                hash = (hash * 31) + HairId;
                hash = (hash * 31) + HairColor;
                hash = (hash * 31) + EyeColor;
                hash = (hash * 31) + SkinTone;
                hash = (hash * 31) + FacialHair;
                hash = (hash * 31) + BustSize;
                hash = (hash * 31) + OutfitMask;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"sex={BodyType} face={FaceId} hair={HairId} hairColor={HairColor} eye={EyeColor} skin={SkinTone} "
                + $"facialHair={FacialHair} bust={BustSize} outfit={OutfitMask}";
        }
    }
}
