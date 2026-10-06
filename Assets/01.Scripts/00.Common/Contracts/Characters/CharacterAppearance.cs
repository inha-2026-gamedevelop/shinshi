// System
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 캐릭터 기본 외형, 생성 때 확정되고 장비 외형은 담지 않는다 </summary>
    [DataContract]
    public sealed class CharacterAppearance
    {
        // 서버와 API는 이 값을 저장하고 검증만 한다. 메시 조립은 클라이언트가 슬라이더 카탈로그로 해석한다.
        // 슬라이더 배열의 인덱스 의미는 version마다 고정이며, 추가하거나 순서를 바꾸면 version을 올린다.
        // 팔레트 ID와 직업별 허용 hairId는 카탈로그 내용이라 IsWellFormed가 보지 않는다. API가 카탈로그로 따로 검사한다

        // v1은 faceId/outfitId를 모른다. 기존 request_hash를 보존하려면 v1 재직렬화에서 이 두 키가 항상 빠져야 하므로
        // EmitDefaultValue뿐 아니라 버전까지 보는 ShouldSerialize로 한 번 더 막는다(아래 메서드 참고)

        /****************************************
        *                Fields
        ****************************************/

        public const int VERSION_1         = 1;
        public const int VERSION_2         = 2;
        public const int CURRENT_VERSION   = VERSION_2;
        public const int BODY_TYPE_FEMALE  = 0;
        public const int FACE_SLIDER_COUNT = 22;
        public const int BODY_SLIDER_COUNT = 10;
        public const int VALUE_MIN         = 0;     // 슬라이더, 피부색, 팔레트 ID의 최솟값
        public const int VALUE_MAX         = 255;   // 와이어에서 1바이트로 보낸다
        public const int SLIDER_NEUTRAL    = 128;   // 양방향 슬라이더의 기본값
        public const int HAIR_ID_MAX       = 65535; // 와이어에서 2바이트로 보낸다

        [DataMember(Name = "version")]   private int   _version;
        [DataMember(Name = "bodyType")]  private int   _bodyType;
        [DataMember(Name = "face")]      private int[] _face;
        [DataMember(Name = "body")]      private int[] _body;

        // 아트가 정한 그라데이션 위치
        [DataMember(Name = "skinTone")]  private int   _skinTone;

        // 팔레트 ID
        [DataMember(Name = "eyeColor")]  private int   _eyeColor;
        [DataMember(Name = "browColor")] private int   _browColor;
        [DataMember(Name = "lipColor")]  private int   _lipColor;

        // 지금은 직업 기본 헤어와 기본 색만 허용한다. 선택 기능을 열 때 허용 목록만 넓힌다
        [DataMember(Name = "hairId")]    private int   _hairId;
        [DataMember(Name = "hairColor")] private int   _hairColor;

        // v2 전용. v1 JSON에는 이 두 키가 없었으므로 과거 request_hash를 보존하려면 v1 재직렬화에서 항상 빠져야 한다
        [DataMember(Name = "faceId",   EmitDefaultValue = false)] private int _faceId;
        [DataMember(Name = "outfitId", EmitDefaultValue = false)] private int _outfitId;

        /****************************************
        *              Properties
        ****************************************/

        public int                Version   => _version;
        public int                BodyType  => _bodyType;
        // 배열을 그대로 내주면 받는 쪽이 int[]로 바꿔 고칠 수 있다. 외형은 생성과 접속 때만 읽으므로 래퍼 할당은 문제없다
        public IReadOnlyList<int> Face      => (_face == null) ? null : Array.AsReadOnly(_face);
        public IReadOnlyList<int> Body      => (_body == null) ? null : Array.AsReadOnly(_body);
        public int                SkinTone  => _skinTone;
        public int                EyeColor  => _eyeColor;
        public int                BrowColor => _browColor;
        public int                LipColor  => _lipColor;
        public int                HairId    => _hairId;
        public int                HairColor => _hairColor;
        public int                FaceId    => _faceId;
        public int                OutfitId  => _outfitId;

        /****************************************
        *                Methods
        ****************************************/

        // 호출자가 넘긴 배열을 나중에 고쳐도 이 값이 바뀌지 않도록 복사한다. 기존 10인자 호출은 v2 필드를 0으로 둔다
        public CharacterAppearance(int version, int bodyType, int[] face, int[] body, int skinTone,
            int eyeColor, int browColor, int lipColor, int hairId, int hairColor)
            : this(version, bodyType, face, body, skinTone, eyeColor, browColor, lipColor, hairId, hairColor,
                VALUE_MIN, VALUE_MIN)
        {
        }

        public CharacterAppearance(int version, int bodyType, int[] face, int[] body, int skinTone,
            int eyeColor, int browColor, int lipColor, int hairId, int hairColor, int faceId, int outfitId)
        {
            _version   = version;
            _bodyType  = bodyType;
            _face      = (face == null) ? null : (int[])face.Clone();
            _body      = (body == null) ? null : (int[])body.Clone();
            _skinTone  = skinTone;
            _eyeColor  = eyeColor;
            _browColor = browColor;
            _lipColor  = lipColor;
            _hairId    = hairId;
            _hairColor = hairColor;
            _faceId    = faceId;
            _outfitId  = outfitId;
        }

        private CharacterAppearance() { }

        // Newtonsoft의 DefaultContractResolver는 Type.GetMethod(name, Type[]) 오버로드로 이 메서드를 찾는데,
        // 이 오버로드는 public만 찾는다 - private으로 두면 조용히 무시되고 EmitDefaultValue만으로는
        // 0이 아닌 값의 생략을 보장하지 않아 v1이 faceId/outfitId를 들고 있으면 과거 request_hash가 깨진다.
        // 그래서 이 두 메서드는 반드시 public이어야 한다
        public bool ShouldSerialize_faceId()   { return (_version == VERSION_2) && (_faceId != VALUE_MIN); }
        public bool ShouldSerialize_outfitId() { return (_version == VERSION_2) && (_outfitId != VALUE_MIN); }

        /// <summary> 현재 버전의 구조와 값 범위가 맞는지, 팔레트와 헤어 허용 여부는 보지 않는다 </summary>
        public bool IsWellFormed()
        {
            if (!IsCommonStructureValid())
            {
                return false;
            }

            // v1 API는 faceId/outfitId를 몰랐으므로 그 값이 무엇이든 기존 10필드 검증 범위만 본다.
            // 어떤 경로로 0이 아닌 값이 들어와도 거절하지 않는다 - 생략은 ShouldSerialize_* 가 버전으로 막는다
            if (_version == VERSION_1)
            {
                return true;
            }

            if (_version == VERSION_2)
            {
                return IsByteValue(_faceId) && IsByteValue(_outfitId);
            }

            return false;
        }

        /// <summary> 모든 필드와 배열 원소가 같은지 비교한다, 재시도 스냅샷 대조에 쓴다 </summary>
        public bool ContentEquals(CharacterAppearance other)
        {
            if (other == null)
            {
                return false;
            }

            if ((_version != other._version) || (_bodyType != other._bodyType) || (_skinTone != other._skinTone)
                || (_eyeColor != other._eyeColor) || (_browColor != other._browColor) || (_lipColor != other._lipColor)
                || (_hairId != other._hairId) || (_hairColor != other._hairColor) || (_faceId != other._faceId)
                || (_outfitId != other._outfitId))
            {
                return false;
            }

            return (AreArraysEqual(_face, other._face)) && (AreArraysEqual(_body, other._body));
        }

        private bool IsCommonStructureValid()
        {
            if ((_bodyType != BODY_TYPE_FEMALE)
                || (!AreSlidersValid(_face, FACE_SLIDER_COUNT)) || (!AreSlidersValid(_body, BODY_SLIDER_COUNT))
                || (!IsByteValue(_skinTone)) || (!IsByteValue(_eyeColor)) || (!IsByteValue(_browColor))
                || (!IsByteValue(_lipColor)) || (!IsByteValue(_hairColor)))
            {
                return false;
            }

            return (_hairId >= VALUE_MIN) && (_hairId <= HAIR_ID_MAX);
        }

        private static bool AreArraysEqual(int[] left, int[] right)
        {
            if (left == right)
            {
                return true;
            }

            if ((left == null) || (right == null) || (left.Length != right.Length))
            {
                return false;
            }

            for (int i = 0; i < left.Length; ++i)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreSlidersValid(int[] sliders, int expectedCount)
        {
            if ((sliders == null) || (sliders.Length != expectedCount))
            {
                return false;
            }

            for (int i = 0; i < sliders.Length; ++i)
            {
                if (!IsByteValue(sliders[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsByteValue(int value) { return (value >= VALUE_MIN) && (value <= VALUE_MAX); }
    }
}
