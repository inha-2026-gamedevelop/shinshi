// Project
using Shinshi.Data.Characters;

namespace Shinshi.Backend.Characters
{
    /// <summary> 캐릭터 설정. Characters:EnabledClassIds(환경 변수 Characters__EnabledClassIds)는 생성할 새 직업 ID의
    /// 쉼표 목록이다. 비우면 "3,4,5"이고 구직업 0, 1, 2는 설정에 넣어도 새로 생성하지 않는다.
    /// 배열이 아니라 문자열인 이유는 .NET 설정 바인더가 배열 기본값 뒤에 값을 덧붙이기 때문이다 </summary>
    public sealed class CharacterOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SECTION_NAME = "Characters";
        public const string DEFAULT_ENABLED_CLASS_IDS = PlayerClassPolicy.DEFAULT_ENABLED_CLASS_IDS;

        /****************************************
        *              Properties
        ****************************************/

        public string EnabledClassIds { get; set; } = DEFAULT_ENABLED_CLASS_IDS;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 잘못된 값은 잘못된 배포 설정이라 시작 시점에 실패시킨다 </summary>
        public PlayerClassPolicy ResolvePolicy()
        {
            return PlayerClassPolicy.Parse(EnabledClassIds);
        }
    }
}
