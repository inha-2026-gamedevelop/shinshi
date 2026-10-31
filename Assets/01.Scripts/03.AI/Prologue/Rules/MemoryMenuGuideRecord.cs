// System
using System;
using System.Collections.Generic;

namespace Shinshi.AI
{
    /// <summary> 메모리에만 기억하는 완료 기록, 서버 저장이 연결되기 전의 시험과 개발 씬에서 쓴다 </summary>
    public sealed class MemoryMenuGuideRecord : IMenuGuideRecord
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly HashSet<string> _completed = new HashSet<string>(StringComparer.Ordinal);

        /****************************************
        *                Methods
        ****************************************/

        public bool IsCompleted(string menuId)
        {
            return (!string.IsNullOrEmpty(menuId)) && (_completed.Contains(menuId));
        }

        public void MarkCompleted(string menuId)
        {
            if (string.IsNullOrEmpty(menuId))
            {
                return;
            }

            _completed.Add(menuId);
        }

        public void Clear()
        {
            _completed.Clear();
        }
    }
}
