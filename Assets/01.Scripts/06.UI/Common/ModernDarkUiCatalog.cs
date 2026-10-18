// Unity
using UnityEngine;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> References the prefab and font that the runtime bootstrap needs, loaded from Resources by name </summary>
    [CreateAssetMenu(fileName = RESOURCE_NAME, menuName = "Shinshi/UI/Modern Dark Catalog")]
    public sealed class ModernDarkUiCatalog : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string RESOURCE_NAME = "ModernDarkUiCatalog";

        [SerializeField] private GameObject    _trainingPrefab = null;
        [SerializeField] private TMP_FontAsset _font           = null;
        [SerializeField] private Sprite        _background     = null;

        /****************************************
        *              Properties
        ****************************************/

        public GameObject    TrainingPrefab => _trainingPrefab;
        public TMP_FontAsset Font           => _font;

        /// <summary> Shared moonlit shrine backdrop for screens whose own art is empty, null when the catalog has none </summary>
        public Sprite Background => _background;

        /****************************************
        *                Methods
        ****************************************/

        public static ModernDarkUiCatalog Load()
        {
            return Resources.Load<ModernDarkUiCatalog>(RESOURCE_NAME);
        }

#if UNITY_EDITOR
        public void Assign(GameObject trainingPrefab, TMP_FontAsset font, Sprite background)
        {
            _trainingPrefab = trainingPrefab;
            _font           = font;
            _background     = background;
        }
#endif
    }
}
