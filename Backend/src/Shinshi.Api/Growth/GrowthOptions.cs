// System
using System;

// Project
using Shinshi.Data.Growth;

namespace Shinshi.Backend.Growth
{
    /// <summary> 성장 모듈 설정. Growth:ContentProfile은 Trial 또는 Standard이고 비우면 Trial이다 </summary>
    public sealed class GrowthOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SECTION_NAME = "Growth";

        /****************************************
        *              Properties
        ****************************************/

        public string ContentProfile { get; set; } = nameof(EGrowthContentProfile.Trial);

        // 출시 프로필 설정(Growth__RejectUnknownBattles=true). 성장 콘텐츠(growth-content.json progression)에 없는 battleId의
        // 출정 예약을 INVALID_TARGET으로 거절한다. 테스트용 지도는 콘텐츠에 없으므로 출시 빌드에서 빠진다. 기본 false(개발, 테스트)
        public bool RejectUnknownBattles { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 알 수 없는 값은 잘못된 배포 설정이라 시작 시점에 실패시킨다 </summary>
        public EGrowthContentProfile ResolveProfile()
        {
            if (string.IsNullOrWhiteSpace(ContentProfile))
            {
                return EGrowthContentProfile.Trial;
            }
            if (!Enum.TryParse(ContentProfile, ignoreCase: true, out EGrowthContentProfile profile)
                || !Enum.IsDefined(profile))
            {
                throw new InvalidOperationException("Growth:ContentProfile must be Trial or Standard.");
            }

            return profile;
        }
    }
}
