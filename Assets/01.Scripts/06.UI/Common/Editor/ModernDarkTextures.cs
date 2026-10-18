#if UNITY_EDITOR
// System
using System;
using System.IO;

// Unity
using UnityEditor;
using UnityEngine;

namespace Shinshi.UI.Editor
{
    /// <summary> 공통 어두운 창 스타일의 스프라이트를 PNG 에셋으로 구워 프리팹에 연결할 수 있게 한다 </summary>
    public static class ModernDarkTextures
    {
        // 픽셀은 런타임과 같은 UiModernDarkSprites가 만든다. 이미 있는 PNG는 건드리지 않고, 다시 굽고 싶으면 force를 쓴다

        /****************************************
        *                Fields
        ****************************************/

        private const float PIXELS_PER_UNIT = 100f;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/ModernDark/스타일 텍스처 만들기")]
        public static void BuildMissing()
        {
            EnsureAll(false);
        }

        public static void EnsureAll(bool force)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), UiModernDark.TEXTURE_FOLDER);
            Directory.CreateDirectory(folder);

            for (int i = 0; i < UiModernDarkSprites.G_AllNames.Length; ++i)
            {
                Write(UiModernDarkSprites.G_AllNames[i], force);
            }

            AssetDatabase.Refresh();
        }

        public static Sprite LoadSprite(string name)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiModernDark.TEXTURE_FOLDER + name + ".png");
            if (sprite == null)
            {
                throw new InvalidOperationException("공통 스타일 스프라이트가 없습니다. 메뉴 Shinshi/UI/ModernDark/스타일 텍스처 만들기를 먼저 실행하세요: " + name);
            }

            return sprite;
        }

        private static void Write(string name, bool force)
        {
            string assetPath = UiModernDark.TEXTURE_FOLDER + name + ".png";
            string filePath  = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            if ((!force) && (File.Exists(filePath)))
            {
                return;
            }

            Color32[] pixels = UiModernDarkSprites.BuildPixels(name, out int width, out int height, out int border);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(filePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PIXELS_PER_UNIT;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled       = false;
            importer.filterMode          = FilterMode.Bilinear;
            importer.wrapMode            = TextureWrapMode.Clamp;
            importer.textureCompression  = TextureImporterCompression.Uncompressed;
            importer.spriteBorder        = new Vector4(border, border, border, border);
            importer.SaveAndReimport();
        }
    }
}
#endif
