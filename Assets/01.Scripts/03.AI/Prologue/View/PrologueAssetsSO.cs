// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 프롤로그 일러스트 카탈로그와 성장 메뉴 안내 카탈로그는 Resources 밖에 있어 코드가 바로 읽지 못한다.
    // 로비와 던전 클라이언트, 마을이 한 곳에서 읽도록 Resources에 이 참조 묶음 하나만 둔다(참조된 에셋은 빌드에 함께 들어간다)
    /// <summary> 프롤로그 화면이 쓰는 카탈로그 에셋 참조 묶음, Resources 경로 PrologueAssets </summary>
    [CreateAssetMenu(fileName = "PrologueAssets", menuName = "Shinshi/Prologue/Assets")]
    public sealed class PrologueAssetsSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string RESOURCE_PATH = "PrologueAssets";

        [SerializeField] private PrologueIllustrationCatalogSO _illustrations = null;
        [SerializeField] private MenuGuideCatalogSO            _menuGuides    = null;

        /****************************************
        *              Properties
        ****************************************/

        public PrologueIllustrationCatalogSO Illustrations => _illustrations;
        public MenuGuideCatalogSO            MenuGuides    => _menuGuides;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> Resources에서 읽는다, 없으면 null이다 </summary>
        public static PrologueAssetsSO Load()
        {
            return Resources.Load<PrologueAssetsSO>(RESOURCE_PATH);
        }
    }
}
