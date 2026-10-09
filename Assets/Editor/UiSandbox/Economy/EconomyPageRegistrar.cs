// System
using System.Collections.Generic;

// Unity
using UnityEditor;

namespace Shinshi.Editor
{
    /// <summary> 경제 화면 페이지 13개를 에디터가 로드될 때 카탈로그에 올린다. 도메인 리로드 뒤 중복 등록은 건너뛴다 </summary>
    public static class EconomyPageRegistrar
    {
        /****************************************
        *                Methods
        ****************************************/

        [InitializeOnLoadMethod]
        private static void RegisterAll()
        {
            RegisterMissing();
        }

        /// <summary> 새 페이지 목록, 실제 View 5개와 계약 전용 8개 </summary>
        public static List<IUiSandboxPage> CreatePages()
        {
            List<IUiSandboxPage> pages = new List<IUiSandboxPage>
            {
                new InventorySandboxPage(),
                new CharDetailSandboxPage(),
                new MailBoxSandboxPage(),
                new AuctionSandboxPage(),
                new TradeSandboxPage()
            };

            ContractOnlyDefinition[] definitions = ContractOnlyDefinitions.All();
            for (int i = 0; i < definitions.Length; ++i)
            {
                pages.Add(new ContractOnlySandboxPage(definitions[i]));
            }

            return pages;
        }

        /// <summary> 아직 없는 id만 등록하고 새로 올린 개수를 돌려준다 </summary>
        public static int RegisterMissing()
        {
            int registered = 0;
            List<IUiSandboxPage> pages = CreatePages();
            for (int i = 0; i < pages.Count; ++i)
            {
                if (UiSandboxCatalog.Find(pages[i].Id) != null)
                {
                    continue;
                }

                UiSandboxCatalog.Register(pages[i]);
                ++registered;
            }

            return registered;
        }
    }
}
