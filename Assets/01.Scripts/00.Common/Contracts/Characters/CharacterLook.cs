namespace Shinshi.Contracts
{
    /// <summary> 조립에 필요한 외형 식별자만 모은 값, appearance 전체를 들고 다니지 않아도 비교할 수 있다 </summary>
    public readonly struct CharacterLook
    {
        /****************************************
        *              Properties
        ****************************************/

        public int FaceId   { get; }
        public int HairId   { get; }
        public int OutfitId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public CharacterLook(int faceId, int hairId, int outfitId)
        {
            FaceId   = faceId;
            HairId   = hairId;
            OutfitId = outfitId;
        }

        /// <summary> 정규 v2 외형에서만 값을 뽑는다, 호출자가 먼저 TryResolve로 정규화해야 한다 </summary>
        public static bool TryFrom(CharacterAppearance appearance, out CharacterLook look)
        {
            if ((appearance == null) || (appearance.Version != CharacterAppearance.VERSION_2))
            {
                look = default;
                return false;
            }

            look = new CharacterLook(appearance.FaceId, appearance.HairId, appearance.OutfitId);
            return true;
        }

        public bool Equals(CharacterLook other)
        {
            return (FaceId == other.FaceId) && (HairId == other.HairId) && (OutfitId == other.OutfitId);
        }

        public override bool Equals(object obj) { return (obj is CharacterLook other) && Equals(other); }

        public override int GetHashCode()
        {
            return (FaceId * 397) ^ (HairId * 31) ^ OutfitId;
        }
    }
}
