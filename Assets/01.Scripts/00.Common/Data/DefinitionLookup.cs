// System
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 카탈로그 배열을 ID로 찾는 지연 색인, 카탈로그의 OnValidate에서 Invalidate한다 </summary>
    public sealed class DefinitionLookup<T> where T : class, IDefinition
    {
        /****************************************
        *                Fields
        ****************************************/

        private Dictionary<string, T> _map;

        /****************************************
        *                Methods
        ****************************************/

        public bool TryGet(IReadOnlyList<T> source, string definitionId, out T definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(definitionId))
            {
                return false;
            }

            if (_map == null)
            {
                Build(source);
            }

            return _map.TryGetValue(definitionId, out definition);
        }

        public bool Contains(IReadOnlyList<T> source, string definitionId)
        {
            return TryGet(source, definitionId, out _);
        }

        public void Invalidate()
        {
            _map = null;
        }

        // 중복 ID는 GameDB 검증이 시작을 막으므로 여기서는 먼저 들어온 항목을 남긴다
        private void Build(IReadOnlyList<T> source)
        {
            int count = (source == null) ? 0 : source.Count;
            _map = new Dictionary<string, T>(count);

            for (int i = 0; i < count; ++i)
            {
                T item = source[i];
                if ((item == null) || string.IsNullOrEmpty(item.DefinitionId) || _map.ContainsKey(item.DefinitionId))
                {
                    continue;
                }

                _map.Add(item.DefinitionId, item);
            }
        }
    }
}
