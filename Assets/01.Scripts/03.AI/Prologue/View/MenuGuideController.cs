// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 성장 메뉴 안내를 언제 보일지 정하는 진행기, 메뉴 창은 첫 진입과 도움말 버튼에서 한 줄만 부르면 된다 </summary>
    public sealed class MenuGuideController : IDisposable
    {
        // 메뉴 창 코드에는 아무것도 넣지 않는다. 창이 열릴 때 TryShowFirstTime, 도움말 버튼이 눌릴 때 Replay를 부르는 것이 연결의 전부다.
        // 완료 기록은 끝까지 보거나 건너뛰었을 때만 남기고, 안내 도중에 닫히거나 게임이 끝나면 남기지 않아 다음 진입에 다시 안내한다

        /****************************************
        *                Fields
        ****************************************/

        private readonly MenuGuideCatalogSO _catalog;
        private readonly IMenuGuideRecord   _record;
        private readonly MenuGuideView      _view;

        private string _activeMenuId = null;
        private bool   _disposed     = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsShowing => _view.IsShowing;

        /****************************************
        *                Methods
        ****************************************/

        public MenuGuideController(MenuGuideCatalogSO catalog, IMenuGuideRecord record, MenuGuideView view)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            _catalog = catalog;
            _record  = record;
            _view    = view;

            _view.Finished += OnFinished;
        }

        /// <summary> 메뉴에 처음 들어왔을 때 부른다. 계정에서 이미 마친 안내이거나 등록되지 않은 메뉴면 아무것도 하지 않고 거짓을 돌려준다 </summary>
        public bool TryShowFirstTime(string menuId, Func<string, RectTransform> resolver)
        {
            if (_record.IsCompleted(menuId))
            {
                return false;
            }

            return Begin(menuId, resolver);
        }

        /// <summary> 도움말 버튼이 눌렸을 때 부른다, 이미 마쳤어도 다시 보여 준다 </summary>
        public bool Replay(string menuId, Func<string, RectTransform> resolver)
        {
            return Begin(menuId, resolver);
        }

        /// <summary> 메뉴가 닫히는 등으로 안내를 끝내지 않고 내린다, 완료 기록은 남기지 않는다 </summary>
        public void Abort()
        {
            _activeMenuId = null;
            _view.Abort();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _view.Finished -= OnFinished;
        }

        private bool Begin(string menuId, Func<string, RectTransform> resolver)
        {
            if (!_catalog.TryGetSteps(menuId, out MenuGuideStepData[] steps))
            {
                return false;
            }

            // 다른 메뉴 안내가 떠 있으면 먼저 내린다, 완료로 치지 않는다
            if (_view.IsShowing)
            {
                Abort();
            }

            _activeMenuId = menuId;
            _view.Show(steps, resolver);
            return true;
        }

        private void OnFinished(bool skipped)
        {
            if (string.IsNullOrEmpty(_activeMenuId))
            {
                return;
            }

            _record.MarkCompleted(_activeMenuId);
            _activeMenuId = null;
        }
    }
}
