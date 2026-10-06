#if UNITY_EDITOR
// System
using System.Collections.Generic;
using System.IO;
using System.Text;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinshi.Editor
{
    // P09 원본 재질은 프로젝트에 없는 lilToon 셰이더를 써서 분홍으로 보인다. 원본은 그대로 두고, 저장된 lilToon 값을 읽어
    // 같은 텍스처를 쓰는 URP Lit 사본을 PlayerP09 폴더에 만든다. HDRP 마스크(R 금속, G AO, A 매끄러움)는 원본이 sRGB로
    // 임포트돼 있어 그대로 쓰면 값이 틀어지므로, 텍스처를 복사해 사본만 선형으로 임포트해서 금속·AO 맵에 넣는다
    /// <summary> P09 lilToon 재질을 URP Lit 사본으로 변환한다 </summary>
    public static class P09MaterialConverter
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string OUTPUT_MATERIAL_FOLDER = "Assets/01.Scripts/05.Art/Characters/PlayerP09/Materials";
        public const string OUTPUT_TEXTURE_FOLDER  = "Assets/01.Scripts/05.Art/Characters/PlayerP09/Textures";
        public const string MATERIAL_PREFIX        = "M_";
        public const string SOURCE_PREFIX          = "P09_";

        private const string SOURCE_ROOT          = "Assets/99.ThirdParty/Model/P09_Modular_Humanoid/Model_DATA";
        private const string SOURCE_MATERIAL_ROOT = SOURCE_ROOT + "/Materials";
        private const string SOURCE_TEXTURE_ROOT  = SOURCE_ROOT + "/Textures";
        private const string LIT_SHADER_NAME      = "Universal Render Pipeline/Lit";
        private const string MENU_PATH            = "Shinshi/Art/P09/Convert Materials";
        private const string REPORT_FILE_NAME     = "README.md";
        private const float  CUTOFF_DEFAULT       = 0.5f;
        private const float  CULL_BACK            = 2f;
        private const float  SMOOTHNESS_DEFAULT   = 0.5f;

        // 프리팹 렌더러와 데모 커스터마이징 데이터가 실제로 쓰는 재질만 변환한다. 없는 텍스처를 가리키는 Armor_001 ~ 012, 무기는 뺀다
        private static readonly string[] G_SourceMaterials = {
            "Common/Eye/P09_Eye_Yellow", "Common/Eye/P09_Eye_Bright", "Common/Eye/P09_Eye_Purple", "Common/Eye/P09_Eye_Cyan",
            "Common/Eye/P09_Eye_Red",
            "Common/Hair/P09_Hair_Brown", "Common/Hair/P09_Hair_Blue", "Common/Hair/P09_Hair_Gold", "Common/Hair/P09_Hair_Green",
            "Common/Hair/P09_Hair_Ivory", "Common/Hair/P09_Hair_Orange", "Common/Hair/P09_Hair_Pink", "Common/Hair/P09_Hair_Purple",
            "Common/Hair/P09_Hair_Red",
            "Female_Body/P09_Female_Skin", "Female_Body/P09_Female_Skin_Bright", "Female_Body/P09_Female_Skin_Dark",
            "Male_Body/P09_Male_Skin", "Male_Body/P09_Male_Skin_Bright", "Male_Body/P09_Male_Skin_Dark",
            "Male_Body/FacialHair/P09_Male_FacialHair",
            "Armor/P09_Female_Armor_003", "Armor/P09_Male_Armor_003", "Armor/P09_Female_UnderWear", "Armor/P09_Male_UnderWear" };

        // 원본 재질 이름의 앞부분 → 마스크 텍스처(Textures 기준 경로)
        private static readonly string[,] G_MaskMaps = {
            { "P09_Female_Skin",      "Female_Body/Extra/P09_Female_Body_MaskMap.png" },
            { "P09_Male_Skin",        "Male_Body/Extra/P09_Male_Body_MaskMap.png" },
            { "P09_Female_Armor_003", "Armors/Fem_Armor_003/Extra/P09_Female_Armor_003_MaskMap.png" },
            { "P09_Male_Armor_003",   "Armors/Male_Armor_003/Extra/P09_Male_Armor_003_MaskMap.png" },
            { "P09_Female_UnderWear", "Armors/Fem_UnderWear/Extra/P09_Female_UnderWear_MaskMap.png" },
            { "P09_Male_UnderWear",   "Armors/Male_UnderWear/Extra/P09_Male_UnderWear_MaskMap.png" } };

        // 털처럼 가장자리가 잘리는 메시는 알파 클립으로 그린다
        private static readonly string[] G_AlphaClipPrefixes = { "P09_Male_FacialHair" };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_PATH)]
        private static void ConvertMenu()
        {
            Convert();
        }

        /// <summary> 모든 대상 재질을 변환하고 결과 요약을 돌려준다. 이미 있는 사본은 값을 다시 채운다 </summary>
        public static string Convert()
        {
            Shader lit = Shader.Find(LIT_SHADER_NAME);
            if (lit == null)
            {
                return "[P09] URP Lit 셰이더를 찾지 못했다";
            }

            EnsureFolder(OUTPUT_MATERIAL_FOLDER);
            EnsureFolder(OUTPUT_TEXTURE_FOLDER);

            var report   = new StringBuilder();
            var failures = new List<string>();
            report.AppendLine("# P09 URP Lit 재질 변환 기록");
            report.AppendLine();
            report.AppendLine("`Assets/Editor/Art/P09MaterialConverter.cs`(메뉴 Shinshi/Art/P09/Convert Materials)가 만든다. 원본 lilToon 재질과 텍스처는 바꾸지 않는다.");
            report.AppendLine("마스크가 없는 재질은 원본에 금속·AO 데이터가 없어 lilToon 값만 옮겼다.");
            report.AppendLine();
            report.AppendLine("| 사본 | 원본 | 금속·AO 마스크 | 비고 |");
            report.AppendLine("|---|---|---|---|");

            for (int i = 0; i < G_SourceMaterials.Length; ++i)
            {
                string sourcePath = SOURCE_MATERIAL_ROOT + "/" + G_SourceMaterials[i] + ".mat";
                var    source     = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
                if (source == null)
                {
                    failures.Add(sourcePath);
                    continue;
                }

                Texture2D mask   = FindMask(source.name);
                Material  result = ConvertOne(source, lit, mask, out bool alphaClip);
                report.Append("| ").Append(result.name).Append(" | ").Append(source.name).Append(" | ")
                    .Append((mask != null) ? mask.name : "없음").Append(" | ").Append(alphaClip ? "알파 클립" : string.Empty).AppendLine(" |");
            }

            if (failures.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("변환하지 못한 원본: " + string.Join(", ", failures));
            }

            File.WriteAllText(Path.Combine(OUTPUT_MATERIAL_FOLDER, REPORT_FILE_NAME), report.ToString());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary = $"[P09] 재질 {G_SourceMaterials.Length - failures.Count}개 변환, 실패 {failures.Count}개";
            Debug.Log(summary);
            return summary;
        }

        /// <summary> 원본 재질 이름에 대응하는 사본 경로 </summary>
        public static string GetConvertedPath(string sourceName)
        {
            return OUTPUT_MATERIAL_FOLDER + "/" + GetConvertedName(sourceName) + ".mat";
        }

        /// <summary> P09_Female_Skin → M_P09_Female_Skin </summary>
        public static string GetConvertedName(string sourceName)
        {
            return MATERIAL_PREFIX + sourceName;
        }

        private static Material ConvertOne(Material source, Shader lit, Texture2D mask, out bool alphaClip)
        {
            string path   = GetConvertedPath(source.name);
            var    target = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (target == null)
            {
                target = new Material(lit) { name = GetConvertedName(source.name) };
                AssetDatabase.CreateAsset(target, path);
            }
            else
            {
                target.shader = lit;
            }

            // 셰이더가 없는 원본은 Material API로 값을 못 읽으므로 저장된 속성을 직접 읽는다
            SerializedProperty saved = new SerializedObject(source).FindProperty("m_SavedProperties");

            Texture baseMap   = ReadTexture(saved, "_MainTex");
            Texture normalMap = ReadTexture(saved, "_BumpMap");
            Color   color     = ReadColor(saved, "_Color", Color.white);
            float   metallic  = ReadFloat(saved, "_Metallic", 0f);
            float   smooth    = ReadFloat(saved, "_Smoothness", SMOOTHNESS_DEFAULT);
            float   cull      = ReadFloat(saved, "_Cull", CULL_BACK);
            float   cutoff    = ReadFloat(saved, "_Cutoff", CUTOFF_DEFAULT);
            float   transMode = ReadFloat(saved, "_TransparentMode", 0f);

            target.SetTexture("_BaseMap", baseMap);
            target.SetColor("_BaseColor", color);

            target.SetTexture("_BumpMap", normalMap);
            SetKeyword(target, "_NORMALMAP", normalMap != null);

            // 마스크 알파가 _Smoothness와 곱해지므로 마스크가 있으면 1로 둔다
            bool hasMask = mask != null;
            target.SetTexture("_MetallicGlossMap", mask);
            target.SetTexture("_OcclusionMap", mask);
            SetKeyword(target, "_METALLICSPECGLOSSMAP", hasMask);
            SetKeyword(target, "_OCCLUSIONMAP", hasMask);
            target.SetFloat("_SmoothnessTextureChannel", 0f);
            target.SetFloat("_Smoothness", hasMask ? 1f : smooth);
            target.SetFloat("_Metallic", hasMask ? 1f : metallic);
            target.SetFloat("_OcclusionStrength", 1f);

            alphaClip = (transMode != 0f) || StartsWithAny(source.name, G_AlphaClipPrefixes);
            target.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            target.SetFloat("_Cutoff", cutoff);
            SetKeyword(target, "_ALPHATEST_ON", alphaClip);
            target.SetFloat("_Cull", cull);
            target.renderQueue = alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;

            EditorUtility.SetDirty(target);
            return target;
        }

        // 마스크는 원본을 복사해 사본만 선형으로 임포트한다. 원본 import 설정은 sRGB 그대로 둔다
        private static Texture2D FindMask(string sourceName)
        {
            for (int i = 0; i < G_MaskMaps.GetLength(0); ++i)
            {
                if (!sourceName.StartsWith(G_MaskMaps[i, 0]))
                {
                    continue;
                }

                string sourcePath = SOURCE_TEXTURE_ROOT + "/" + G_MaskMaps[i, 1];
                string copyPath   = OUTPUT_TEXTURE_FOLDER + "/" + Path.GetFileName(sourcePath);
                if ((AssetDatabase.LoadAssetAtPath<Texture2D>(copyPath) == null) && (!AssetDatabase.CopyAsset(sourcePath, copyPath)))
                {
                    Debug.LogError("[P09] 마스크 텍스처를 복사하지 못했다: " + sourcePath);
                    return null;
                }

                var importer = AssetImporter.GetAtPath(copyPath) as TextureImporter;
                if ((importer != null) && importer.sRGBTexture)
                {
                    importer.sRGBTexture = false;
                    importer.SaveAndReimport();
                }

                return AssetDatabase.LoadAssetAtPath<Texture2D>(copyPath);
            }

            return null;
        }

        private static Texture ReadTexture(SerializedProperty saved, string name)
        {
            SerializedProperty entry = FindEntry(saved.FindPropertyRelative("m_TexEnvs"), name);
            return (entry != null) ? (entry.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture) : null;
        }

        private static float ReadFloat(SerializedProperty saved, string name, float fallback)
        {
            SerializedProperty entry = FindEntry(saved.FindPropertyRelative("m_Floats"), name);
            return (entry != null) ? entry.FindPropertyRelative("second").floatValue : fallback;
        }

        private static Color ReadColor(SerializedProperty saved, string name, Color fallback)
        {
            SerializedProperty entry = FindEntry(saved.FindPropertyRelative("m_Colors"), name);
            return (entry != null) ? entry.FindPropertyRelative("second").colorValue : fallback;
        }

        private static SerializedProperty FindEntry(SerializedProperty list, string name)
        {
            for (int i = 0; i < list.arraySize; ++i)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == name)
                {
                    return entry;
                }
            }

            return null;
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }

        private static bool StartsWithAny(string value, string[] prefixes)
        {
            for (int i = 0; i < prefixes.Length; ++i)
            {
                if (value.StartsWith(prefixes[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
