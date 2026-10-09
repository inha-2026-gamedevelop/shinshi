using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> Presentation 페이지를 샌드박스 카탈로그에 등록한다. 이미 같은 Id가 있으면 건너뛰고 경고만 남긴다 </summary>
    public static class PresentationRegistration
    {
        /****************************************
        *                Methods
        ****************************************/

        [InitializeOnLoadMethod]
        private static void RegisterOnLoad()
        {
            PresentationFixtureFactory.DestroyOrphans();
            RegisterAll();
        }

        /// <summary> 등록에 성공한 페이지 수를 돌려준다 </summary>
        public static int RegisterAll()
        {
            int registered = 0;
            IList<IUiSandboxPage> pages = CreatePages();
            for (int i = 0; i < pages.Count; ++i)
            {
                string error;
                if (UiSandboxCatalog.TryRegister(pages[i], out error))
                {
                    ++registered;
                }
                else
                {
                    Debug.LogWarning("[UiSandbox] Presentation 페이지 등록 건너뜀 (" + pages[i].Id + "): " + error);
                }
            }

            return registered;
        }

        /// <summary> 실제 View 페이지 12개와 계약 전용 페이지를 새로 만든다 </summary>
        public static IList<IUiSandboxPage> CreatePages()
        {
            List<IUiSandboxPage> pages = new List<IUiSandboxPage>
            {
                new HudStatusPage(),
                new HudCombatPage(),
                new HudResonanceLegacyPage(),
                new HudPartyPage(),
                new HudChatPage(),
                new HudMenuPage(),
                new HudCombatMeterPage(),
                new PopupPage(),
                new SortiePrepPage(),
                new SortieResultPage(),
                new CharacterSelectPage(),
                new CommonUiSandboxPage()
            };

            ContractOnlyPage[] contracts = PresentationContractCatalog.CreatePages();
            for (int i = 0; i < contracts.Length; ++i)
            {
                pages.Add(contracts[i]);
            }

            return pages;
        }
    }
}
