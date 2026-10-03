// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 외부 모델 에셋의 텍스처를 최대 2048, 압축, 밉 스트리밍으로 가져온다. 원본 파일은 바꾸지 않는다 </summary>
    [InitializeOnLoad]
    public sealed class ThirdPartyModelTextureLimiter : AssetPostprocessor
    {
        // 외부 모델 팩은 4096 무압축(RGBA32)으로 들어오는 경우가 있다. 월백 외형이 Bootstrap의 네트워크 프리팹 목록에서
        // 참조돼 에디터를 열거나 게임을 시작할 때마다 1GB 넘게 메모리에 올라와서 프로젝트 쪽에서 상한을 건다.
        // 99.ThirdParty는 Git에서 제외되어 .meta를 고쳐도 팀원에게 가지 않으므로 이 규칙이 각자의 임포트에 적용한다.
        // 낮추기만 하고 올리지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const string ROOT_FOLDER      = "Assets/99.ThirdParty/Model";
        private const string ROOT_PREFIX      = ROOT_FOLDER + "/";
        private const string TEXTURE_FILTER   = "t:Texture2D";
        private const string STANDALONE       = "Standalone";
        private const int    MAX_TEXTURE_SIZE = 2048;

        /****************************************
        *                Methods
        ****************************************/

        static ThirdPartyModelTextureLimiter()
        {
            EditorApplication.delayCall += ApplyToImportedTextures;
        }

        // 새로 들어오거나 다시 가져오는 텍스처에 적용한다
        private void OnPreprocessTexture()
        {
            if ((!assetPath.StartsWith(ROOT_PREFIX, System.StringComparison.Ordinal))
                || (!(assetImporter is TextureImporter importer)))
            {
                return;
            }

            Limit(importer);
        }

        // 규칙을 넣기 전에 이미 가져온 텍스처는 에디터를 열 때 한 번 찾아 다시 가져온다. 이미 맞으면 아무것도 하지 않는다
        private static void ApplyToImportedTextures()
        {
            if ((EditorApplication.isPlayingOrWillChangePlaymode) || (!AssetDatabase.IsValidFolder(ROOT_FOLDER)))
            {
                return;
            }

            var targets = new List<TextureImporter>();
            string[] guids = AssetDatabase.FindAssets(TEXTURE_FILTER, new[] { ROOT_FOLDER });
            for (int i = 0; i < guids.Length; ++i)
            {
                if ((AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guids[i])) is TextureImporter importer)
                    && (Limit(importer)))
                {
                    targets.Add(importer);
                }
            }

            if (targets.Count == 0)
            {
                return;
            }

            Debug.Log($"[Texture] 외부 모델 텍스처 {targets.Count}개를 최대 {MAX_TEXTURE_SIZE}, 압축, 밉 스트리밍으로 다시 가져온다");
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < targets.Count; ++i)
                {
                    targets[i].SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        // 바꾼 값이 있으면 true
        private static bool Limit(TextureImporter importer)
        {
            bool changed = false;
            if (importer.maxTextureSize > MAX_TEXTURE_SIZE)
            {
                importer.maxTextureSize = MAX_TEXTURE_SIZE;
                changed = true;
            }

            if (importer.textureCompression == TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                changed = true;
            }

            if ((importer.mipmapEnabled) && (!importer.streamingMipmaps))
            {
                importer.streamingMipmaps = true;
                changed = true;
            }

            // 플랫폼별 덮어쓰기가 있으면 그 값이 기본값보다 우선하므로 같은 상한을 건다
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings(STANDALONE);
            if (standalone.overridden)
            {
                bool platformChanged = false;
                if (standalone.maxTextureSize > MAX_TEXTURE_SIZE)
                {
                    standalone.maxTextureSize = MAX_TEXTURE_SIZE;
                    platformChanged = true;
                }

                if (IsUncompressed(standalone.format))
                {
                    standalone.format             = TextureImporterFormat.Automatic;
                    standalone.textureCompression = TextureImporterCompression.CompressedHQ;
                    platformChanged = true;
                }

                if (platformChanged)
                {
                    importer.SetPlatformTextureSettings(standalone);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool IsUncompressed(TextureImporterFormat format)
        {
            return (format == TextureImporterFormat.RGBA32) || (format == TextureImporterFormat.ARGB32)
                || (format == TextureImporterFormat.RGB24) || (format == TextureImporterFormat.RGBAHalf)
                || (format == TextureImporterFormat.RGBAFloat);
        }
    }
}
