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

        /****************************************
        *                Fields
        ****************************************/

        public const int CURRENT_VERSION   = 1;
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

        /****************************************
        *                Methods
        ****************************************/

        // 호출자가 넘긴 배열을 나중에 고쳐도 이 값이 바뀌지 않도록 복사한다
        public CharacterAppearance(int version, int bodyType, int[] face, int[] body, int skinTone,
            int eyeColor, int browColor, int lipColor, int hairId, int hairColor)
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
        }

        private CharacterAppearance() { }

        /// <summary> 현재 버전의 구조와 값 범위가 맞는지, 팔레트와 헤어 허용 여부는 보지 않는다 </summary>
        public bool IsWellFormed()
        {
            if (_version != CURRENT_VERSION)
            {
                return false;
            }

            if (_bodyType != BODY_TYPE_FEMALE)
            {
                return false;
            }

            if ((!AreSlidersValid(_face, FACE_SLIDER_COUNT)) || (!AreSlidersValid(_body, BODY_SLIDER_COUNT)))
            {
                return false;
            }

            if ((!IsByteValue(_skinTone)) || (!IsByteValue(_eyeColor)) || (!IsByteValue(_browColor))
                || (!IsByteValue(_lipColor)) || (!IsByteValue(_hairColor)))
            {
                return false;
            }

            return (_hairId >= VALUE_MIN) && (_hairId <= HAIR_ID_MAX);
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
