// System
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shinshi.Data.Characters
{
    /// <summary> 직업 ID 범위(0~5)와 서버가 생성, 선택을 허용하는 직업 집합. 기존 직업 0, 1, 2는 항상 열려 있고
    /// 새 직업 3, 4, 5(검사, 마검사, 궁수)는 설정으로 열기 전까지 닫혀 있다 </summary>
    public sealed class PlayerClassPolicy
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MIN_CLASS_ID    = 0;
        public const int MAX_CLASS_ID    = 5;
        public const int LEGACY_MAX_ID   = 2;

        private const char SEPARATOR = ',';

        private static readonly PlayerClassPolicy G_Default = new PlayerClassPolicy(Array.Empty<int>());

        private readonly HashSet<int> _enabled;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 기본 정책: 0, 1, 2만 열린다 </summary>
        public static PlayerClassPolicy Default => G_Default;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 기존 직업은 항상 열고 추가로 열 직업 ID를 받는다. 범위 밖 ID는 잘못된 배포 설정이다 </summary>
        public PlayerClassPolicy(IEnumerable<int> enabledClassIds)
        {
            ArgumentNullException.ThrowIfNull(enabledClassIds);
            _enabled = new HashSet<int>();
            for (int classId = MIN_CLASS_ID; classId <= LEGACY_MAX_ID; ++classId)
            {
                _enabled.Add(classId);
            }

            foreach (int classId in enabledClassIds)
            {
                if (!IsKnown(classId))
                {
                    throw new ArgumentOutOfRangeException(nameof(enabledClassIds),
                        "Characters:EnabledClassIds must contain only class IDs 0 to 5.");
                }

                _enabled.Add(classId);
            }
        }

        /// <summary> "0,1,2,3" 같은 쉼표 목록을 읽는다. 비어 있으면 기본 정책이다. 형식이 틀리면 시작 시점에 실패시킨다 </summary>
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

        /// <summary> 새 캐릭터 생성과 마을 입장 선택이 허용되는 직업인가 </summary>
        public bool IsEnabled(int classId)
        {
            return _enabled.Contains(classId);
        }

        /// <summary> 열린 직업 ID를 오름차순으로 돌려준다(로그, 진단용) </summary>
        public int[] EnabledClassIds()
        {
            return _enabled.OrderBy(classId => classId).ToArray();
        }
    }
}
