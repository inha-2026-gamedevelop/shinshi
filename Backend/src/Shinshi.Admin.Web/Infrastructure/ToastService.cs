// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Shinshi.Admin.Web.Infrastructure
{
    public enum EToastKind { Success, Error, Info }

    public sealed record ToastItem(int Id, EToastKind Kind, string Text);

    /// <summary> 화면 오른쪽 아래 알림. 회로(circuit)마다 하나이며 성공·안내는 잠시 뒤 사라지고 오류는 닫을 때까지 남는다 </summary>
    public sealed class ToastService
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_VISIBLE = 4;
        private const int AUTO_CLOSE_MS = 5000;

        private readonly object _lock = new();
        private readonly List<ToastItem> _items = new();
        private int _nextId;

        /****************************************
        *              Properties
        ****************************************/

        public event Action? Changed;

        /****************************************
        *                Methods
        ****************************************/

        public IReadOnlyList<ToastItem> Snapshot()
        {
            lock (_lock)
            {
                return _items.ToArray();
            }
        }

        public void Show(EToastKind kind, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            int id;
            lock (_lock)
            {
                id = ++_nextId;
                _items.Add(new ToastItem(id, kind, text));
                while (_items.Count > MAX_VISIBLE)
                {
                    _items.RemoveAt(0);
                }
            }
            Changed?.Invoke();
            if (kind != EToastKind.Error)
            {
                _ = CloseLaterAsync(id);
            }
        }

        public void Dismiss(int id)
        {
            bool removed;
            lock (_lock)
            {
                removed = _items.RemoveAll(item => item.Id == id) > 0;
            }
            if (removed)
            {
                Changed?.Invoke();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _items.Clear();
            }
            Changed?.Invoke();
        }

        private async Task CloseLaterAsync(int id)
        {
            await Task.Delay(AUTO_CLOSE_MS);
            Dismiss(id);
        }
    }
}
