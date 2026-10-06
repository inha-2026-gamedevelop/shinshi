// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 생성 화면의 P09 외형 선택. 사용자가 직접 고른 항목은 직업을 바꿔도 유지하고, 모든 변경 뒤 성별 조건을 맞춘다 </summary>
    public sealed class CharacterP09Selection
    {
        /****************************************
        *                Fields
        ****************************************/

        public enum EField
        {
            BodyType,
            Face,
            Hair,
            HairColor,
            EyeColor,
            Skin,
            FacialHair,
            BustSize,
            Outfit,
            Count
        }

        private readonly int[]  _values = new int[(int)EField.Count];
        private readonly bool[] _chosen = new bool[(int)EField.Count];

        /****************************************
        *              Properties
        ****************************************/

        public int  BodyType => _values[(int)EField.BodyType];
        public bool IsMale   => BodyType == CharacterAppearance.BODY_TYPE_MALE;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterP09Selection(int classId)
        {
            ApplyClassDefaults(classId);
        }

        public int Get(EField field) { return _values[(int)field]; }

        /// <summary> 직업을 바꿀 때 부른다. 아직 직접 고르지 않은 항목만 직업 기본값으로 채운 뒤 성별 조건을 맞춘다 </summary>
        public void ApplyClassDefaults(int classId)
        {
            CharacterAppearance defaults = CharacterAppearanceCatalog.CreateDefault(classId);
            if (defaults == null)
            {
                return;
            }

            FillIfNotChosen(EField.BodyType, defaults.BodyType);
            FillIfNotChosen(EField.Face, defaults.FaceId);
            FillIfNotChosen(EField.Hair, defaults.HairId);
            FillIfNotChosen(EField.HairColor, defaults.HairColor);
            FillIfNotChosen(EField.EyeColor, defaults.EyeColor);
            FillIfNotChosen(EField.Skin, defaults.SkinTone);
            FillIfNotChosen(EField.FacialHair, defaults.FacialHair);
            FillIfNotChosen(EField.BustSize, defaults.BustSize);
            FillIfNotChosen(EField.Outfit, defaults.OutfitId);
            Normalize();
        }

        /// <summary> 사용자가 한 항목을 고른다. 성별을 바꾸면 수염, 가슴 크기는 새 성별 기본값으로 돌아간다 </summary>
        public void Select(EField field, int value)
        {
            _values[(int)field] = value;
            _chosen[(int)field] = true;
            if (field == EField.BodyType)
            {
                _chosen[(int)EField.FacialHair] = false;
                _chosen[(int)EField.BustSize]   = false;
                _values[(int)EField.FacialHair] = CharacterAppearanceCatalog.P09_FACIAL_HAIR_NONE;
                _values[(int)EField.BustSize]   = CharacterAppearanceCatalog.P09_BUST_NONE;
            }

            Normalize();
        }

        public CharacterP09Look ToLook()
        {
            return new CharacterP09Look(Get(EField.BodyType), Get(EField.Face), Get(EField.Hair), Get(EField.HairColor),
                Get(EField.EyeColor), Get(EField.Skin), Get(EField.FacialHair), Get(EField.BustSize), Get(EField.Outfit));
        }

        public CharacterAppearance ToAppearance() { return ToLook().ToAppearance(); }

        private void FillIfNotChosen(EField field, int value)
        {
            if (!_chosen[(int)field])
            {
                _values[(int)field] = value;
            }
        }

        // 남성은 가슴 크기 0, 여성은 수염 0과 가슴 크기 1 ~ 3. 결과는 항상 CharacterAppearanceCatalog.IsAllowed를 만족한다
        private void Normalize()
        {
            int facialHair = _values[(int)EField.FacialHair];
            int bustSize   = _values[(int)EField.BustSize];
            CharacterAppearanceCatalog.NormalizeForBodyType(BodyType, ref facialHair, ref bustSize);
            _values[(int)EField.FacialHair] = facialHair;
            _values[(int)EField.BustSize]   = bustSize;
        }
    }
}
