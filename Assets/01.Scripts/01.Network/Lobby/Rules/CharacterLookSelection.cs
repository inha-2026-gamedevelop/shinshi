// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 커스터마이즈 탭의 현재 선택. 사용자가 직접 고른 부위는 직업을 바꿔도 유지한다 </summary>
    public sealed class CharacterLookSelection
    {
        /****************************************
        *                Fields
        ****************************************/

        // Cute 1종뿐이라 얼굴은 바뀌지 않는다
        private readonly int _faceId = CharacterAppearanceCatalog.FACE_CUTE;

        private int  _hairId;
        private int  _outfitId;
        private bool _hasHairChoice;
        private bool _hasOutfitChoice;

        /****************************************
        *              Properties
        ****************************************/

        public int FaceId   => _faceId;
        public int HairId   => _hairId;
        public int OutfitId => _outfitId;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterLookSelection(int classId)
        {
            ApplyClassDefaults(classId);
        }

        /// <summary> 직업을 바꿀 때 부른다. 아직 직접 고르지 않은 부위만 새 직업 기본값으로 다시 채운다 </summary>
        public void ApplyClassDefaults(int classId)
        {
            CharacterAppearance defaultAppearance = CharacterAppearanceCatalog.CreateDefaultV2(classId);
            if (!_hasHairChoice)
            {
                _hairId = defaultAppearance.HairId;
            }

            if (!_hasOutfitChoice)
            {
                _outfitId = defaultAppearance.OutfitId;
            }
        }

        public void SelectHair(int hairId)
        {
            _hairId        = hairId;
            _hasHairChoice = true;
        }

        public void SelectOutfit(int outfitId)
        {
            _outfitId        = outfitId;
            _hasOutfitChoice = true;
        }

        public CharacterLook ToLook() { return new CharacterLook(_faceId, _hairId, _outfitId); }
    }
}
