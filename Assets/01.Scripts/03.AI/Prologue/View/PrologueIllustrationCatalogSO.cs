// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 프롤로그 일러스트와 자막 묶음, 로컬 표현 전용이라 GameDB에 넣지 않고 서버 판정과 무관하다 </summary>
    [CreateAssetMenu(fileName = "PrologueIllustrationCatalog", menuName = "Shinshi/PrologueIllustrationCatalogSO")]
    public sealed class PrologueIllustrationCatalogSO : ScriptableObject
    {
        // 도입과 마무리 문구는 아직 기획이 확정되지 않아 임시 문구가 들어 있다. 부적 조사와 월백 등장 자막만 확정 문구다

        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public sealed class PageEntry
        {
            [SerializeField] private Sprite _image;
            [SerializeField, TextArea] private string _text;

            public Sprite Image => _image;
            public string Text  => _text;

            public PageEntry(Sprite image, string text)
            {
                _image = image;
                _text  = text;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("세계관 도입 (4장)")]
        [SerializeField] private PageEntry[] _intro;

        [Header("부적 조사 (자막은 처음부터 전부 표시)")]
        [SerializeField] private PageEntry[] _talisman;

        [Header("월백 등장")]
        [SerializeField] private PageEntry[] _appearance;

        [Header("마무리 (2장)")]
        [SerializeField] private PageEntry[] _ending;

        private IllustrationPage[] _introPages      = null;
        private IllustrationPage[] _talismanPages   = null;
        private IllustrationPage[] _appearancePages = null;
        private IllustrationPage[] _endingPages     = null;

        /****************************************
        *              Properties
        ****************************************/

        public IllustrationPage[] IntroPages      => _introPages      ?? (_introPages      = Convert(_intro));
        public IllustrationPage[] TalismanPages   => _talismanPages   ?? (_talismanPages   = Convert(_talisman));
        public IllustrationPage[] AppearancePages => _appearancePages ?? (_appearancePages = Convert(_appearance));
        public IllustrationPage[] EndingPages     => _endingPages     ?? (_endingPages     = Convert(_ending));

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 에디터에서 에셋을 만들 때 쓰는 채우기, 런타임에서는 부르지 않는다 </summary>
        public void Fill(PageEntry[] intro, PageEntry[] talisman, PageEntry[] appearance, PageEntry[] ending)
        {
            _intro      = intro;
            _talisman   = talisman;
            _appearance = appearance;
            _ending     = ending;

            _introPages      = null;
            _talismanPages   = null;
            _appearancePages = null;
            _endingPages     = null;
        }

        private static IllustrationPage[] Convert(PageEntry[] entries)
        {
            if (entries == null)
            {
                return Array.Empty<IllustrationPage>();
            }

            var pages = new IllustrationPage[entries.Length];
            for (int i = 0; i < entries.Length; ++i)
            {
                pages[i] = new IllustrationPage(entries[i].Image, entries[i].Text);
            }

            return pages;
        }
    }
}
