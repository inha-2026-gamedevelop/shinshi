using System;
using System.Collections.Generic;

namespace Shinshi.Editor
{
    /// <summary> UI 샌드박스 화면 등록부. 각 모듈이 [InitializeOnLoadMethod]에서 자기 화면을 등록하고 코어는 구체 모듈을 모른다 </summary>
    public static class UiSandboxCatalog
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly List<IUiSandboxPage> _pages = new List<IUiSandboxPage>();

        /****************************************
        *              Properties
        ****************************************/

        public static IReadOnlyList<IUiSandboxPage> Pages
        {
            get { return _pages; }
        }

        /// <summary> 등록 · 해제로 화면 목록이 바뀔 때 불린다 </summary>
        public static event Action Changed;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 화면을 등록한다. 같은 Id(대소문자 무시)가 이미 있으면 어느 화면과 겹쳤는지 알리는 예외를 던진다 </summary>
        public static void Register(IUiSandboxPage page)
        {
            if (!TryRegister(page, out string error))
            {
                throw new ArgumentException(error, nameof(page));
            }
        }

        /// <summary> 예외 없이 등록을 시도한다. 실패하면 이유를 error에 담는다 </summary>
        public static bool TryRegister(IUiSandboxPage page, out string error)
        {
            if (page == null)
            {
                error = "UI 샌드박스 화면이 null이라 등록할 수 없다";
                return false;
            }

            string id = page.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                error = $"UI 샌드박스 화면 Id가 비어 있어 등록할 수 없다 ({page.GetType().FullName})";
                return false;
            }

            IUiSandboxPage existing = Find(id);
            if (existing != null)
            {
                error = $"UI 샌드박스 화면 Id '{id}'가 이미 등록되어 있다. 기존 {existing.GetType().FullName}, 새 {page.GetType().FullName}. Id를 서로 다르게 정한다";
                return false;
            }

            _pages.Add(page);
            error = null;
            Changed?.Invoke();
            return true;
        }

        /// <summary> Id로 화면을 찾는다. 없으면 null </summary>
        public static IUiSandboxPage Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            for (int i = 0; i < _pages.Count; ++i)
            {
                if (string.Equals(_pages[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return _pages[i];
                }
            }

            return null;
        }

        /// <summary> 화면을 등록에서 뺀다. 테스트와 모듈 정리용이다 </summary>
        public static bool Unregister(string id)
        {
            IUiSandboxPage page = Find(id);
            if (page == null)
            {
                return false;
            }

            _pages.Remove(page);
            Changed?.Invoke();
            return true;
        }
    }
}
