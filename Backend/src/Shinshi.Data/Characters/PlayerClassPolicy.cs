// System
using System;
using System.Collections.Generic;
using System.Linq;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Characters
{
    /// <summary> 저장된 직업 ID 0~5를 해석하되 신규 생성은 검사, 마검사, 궁수 3~5만 허용한다.
    /// 기존 직업 캐릭터의 선택은 생성 허용과 구분해 유지한다 </summary>
    public sealed class PlayerClassPolicy
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MIN_CLASS_ID    = 0;
        public const int MAX_CLASS_ID    = 5;
        public const int LEGACY_MAX_ID   = 2;
        public const string DEFAULT_ENABLED_CLASS_IDS = "3,4,5";

        private const char SEPARATOR = ',';

        private static readonly PlayerClassPolicy G_Default = new PlayerClassPolicy(new[]
        {
            ContractCodes.PlayerClass.SWORDSMAN,
            ContractCodes.PlayerClass.MAGEKNIGHT,
            ContractCodes.PlayerClass.ARCHER
        });

        private readonly HashSet<int> _enabled;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 기본 생성 정책은 3, 4, 5만 열린다 </summary>
        public static PlayerClassPolicy Default => G_Default;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 설정 목록의 새 직업만 생성 허용 집합에 넣는다. 구직업 ID는 재개방하지 않고 범위 밖 ID는 거절한다 </summary>
        public PlayerClassPolicy(IEnumerable<int> enabledClassIds)
        {
            ArgumentNullException.ThrowIfNull(enabledClassIds);
            _enabled = new HashSet<int>();
            foreach (int classId in enabledClassIds)
            {
                if (!IsKnown(classId))
                {
                    throw new ArgumentOutOfRangeException(nameof(enabledClassIds),
                        "Characters:EnabledClassIds must contain only class IDs 0 to 5.");
                }

                if (classId > LEGACY_MAX_ID)
                {
                    _enabled.Add(classId);
                }
            }
        }

        /// <summary> 쉼표 목록에서 생성할 새 직업을 읽는다. 비어 있으면 3,4,5이고 형식이 틀리면 시작 시점에 실패시킨다 </summary>
        public static PlayerClassPolicy Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Default;
            }

            List<int> ids = new List<int>();
            foreach (string part in text.Split(SEPARATOR, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, out int classId) || !IsKnown(classId))
                {
                    throw new InvalidOperationException("Characters:EnabledClassIds must be a comma separated list of class IDs 0 to 5.");
                }

                ids.Add(classId);
            }

            return new PlayerClassPolicy(ids);
        }

        /// <summary> 스키마와 콘텐츠가 아는 직업 범위인지 본다(0~5). 열려 있는지는 IsEnabled가 본다 </summary>
        public static bool IsKnown(int classId)
        {
            return (classId >= MIN_CLASS_ID) && (classId <= MAX_CLASS_ID);
        }

        /// <summary> 새 캐릭터 생성이 허용되는 직업인가. 구직업은 설정에 들어 있어도 생성하지 않는다 </summary>
        public bool IsEnabled(int classId)
        {
            return _enabled.Contains(classId);
        }

        /// <summary> 저장된 구직업은 선택할 수 있고 새 직업은 생성 허용 설정을 함께 따른다 </summary>
        public bool IsSelectable(int classId)
        {
            return IsKnown(classId) && ((classId <= LEGACY_MAX_ID) || IsEnabled(classId));
        }

        /// <summary> 열린 직업 ID를 오름차순으로 돌려준다(로그, 진단용) </summary>
        public int[] EnabledClassIds()
        {
            return _enabled.OrderBy(classId => classId).ToArray();
        }
    }
}
