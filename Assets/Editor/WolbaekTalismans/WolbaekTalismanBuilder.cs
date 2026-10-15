// System
using System;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Dev;

namespace Shinshi.Editor
{
    /// <summary> 승인된 Blender 부적 메시와 PBR 받침을 독립 프리팹, 전용 검수 씬으로 만든다 </summary>
    public static class WolbaekTalismanBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ROOT     = "Assets/01.Scripts/05.Art/VFX/Wolbaek/Talismans";
        public const string PREFABS  = "Assets/02.Prefab/VFX/Wolbaek/Talismans";
        public const string SCENE    = "Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_Talismans.unity";
        public const string EVIDENCE = ".claude/docs/2026-10-14/wolbaek-talismans-blender";

        private const string PARTICLE_SHADER = "Shinshi/VFX/Particle";
        private const string LIT_SHADER      = "Universal Render Pipeline/Lit";
        private const string PEDESTAL_MODEL  = ROOT + "/Models/SM_Wolbaek_TalismanPedestal.fbx";
        private const string STONE_FLOOR      = "Assets/02.Prefab/Environment/StoneCourtyard/PF_StoneCourtyardTile.prefab";
        private const string STONE_CENTRE     = "Assets/02.Prefab/Environment/DungeonAssemblies/WolbaekCentre16m/PF_WolbaekCentre16m.prefab";
        private const string BEAM_TEXTURES   = "Assets/01.Scripts/05.Art/VFX/Wolbaek/Beams/Textures/";
        private const string MOTE_TEXTURE    = "Assets/01.Scripts/05.Art/VFX/Wolbaek/Warp/Materials/T_Warp_MoteGlow.png";
        private const string PROGRESS_TEXTURE = ROOT + "/Textures/T_Talisman_ProgressMask.png";
        private const string GEOMETRY_FILE   = "talisman_geometry_v04.json";
        private const string PAPER_VEIL_TEXTURE = ROOT + "/Textures/T_Talisman_PaperVeil.png";
        private const string WISP_VEIL_TEXTURE  = ROOT + "/Textures/T_Talisman_WispVeil.png";

        private const int SIGN_COUNT         = 4;
        private const int PAPER_COUNT        = 6;
        private const int FLOW_STATIONS      = 9;
        private const int AIR_WISP_PIECES    = 6;
        private const int SUCCESS_WISP_PIECES = 3;
        private const int POSITION_CHANNELS  = 3;
        private const int COLOR_CHANNELS     = 4;
        private const int UV_CHANNELS        = 2;
        private const int MAX_PIECES         = 4096;
        private const int RING_SEGMENTS      = 192;
        private const int PROGRESS_WIDTH     = 256;
        private const int PROGRESS_HEIGHT    = 16;
        private const int FLOOR_HALF_COUNT   = 5;
        private const float HALF             = 0.5f;
        private const float TAU              = Mathf.PI * 2f;
        private const float GOLDEN_PHASE     = 2.399963f;
        private const float MIN_SUM          = 0.0001f;
        private const float BOUNDARY_WIDTH   = 0.0045f;
        private const float PROGRESS_RADIUS  = 0.93f;
        private const float PROGRESS_HALF_WIDTH = 0.012f;
        private const float RING_HEIGHT      = 0.035f;
        private const float GROUND_OFFSET    = -0.025f;
        private const float CENTRE_OFFSET    = -0.015f;
        private const float FLOOR_SPACING    = 6f;
        private const float STATION_DISTANCE = 10f;
        private const float PREVIEW_RADIUS   = 3f;
        private const float FLOW_AMPLITUDE   = 1f;
        private const float GROUND_AMPLITUDE = 0.25f;
        private const float AIR_MIN_HEIGHT  = 0.25f;
        private const float AIR_MAX_HEIGHT  = 2.2f;
        private const float SUCCESS_MAX_HEIGHT = 4.35f;
        private const float SKY_MAX_RADIUS  = 1.2f;
        private const float GEOMETRY_EPSILON = 0.001f;
        private const float FOOT_LIGHT_RANGE = 2.3f;
        private const float FOOT_LIGHT_HEIGHT = 0.55f;
        private const float APPEAR_SECONDS   = 0.8f;
        private const float RESET_SECONDS    = 0.8f;
        private const float SUCCESS_SECONDS  = 1.6f;
        private const float FADE_SECONDS     = 1.1f;
        private const float DATA_BOUND       = 1000f;
        private const float SOFT_FADE        = 0.001f;
        private const float MASK_EDGE       = 0.2f;
        private const float BLOOM_INTENSITY  = 0.8f;
        private const float BLOOM_THRESHOLD  = 1.1f;
        private const float BLOOM_SCATTER    = 0.65f;
        private const float LOD_NEAR         = 0.28f;
        private const float LOD_MIDDLE       = 0.12f;
        private const float LOD_FAR          = 0.025f;

        private const int PEDESTAL_TEXTURE_SIZE   = 1024;
        private const float FLOW_STATION_PHASE    = 0.23f;
        private const float PREVIEW_KEY_INTENSITY = 0.9f;
        private const float PREVIEW_KEY_PITCH     = 48f;
        private const float PREVIEW_KEY_YAW       = -32f;
        private const float PREVIEW_CAMERA_FOV    = 40f;
        private const float PREVIEW_CAMERA_NEAR   = 0.1f;
        private const float PREVIEW_CAMERA_FAR    = 150f;
        private static readonly Color _previewKeyColor         = new Color(0.86f, 0.92f, 1f);
        private static readonly Color _previewSkyColor         = new Color(0.22f, 0.27f, 0.36f);
        private static readonly Color _previewEquatorColor     = new Color(0.13f, 0.17f, 0.23f);
        private static readonly Color _previewGroundColor      = new Color(0.08f, 0.1f, 0.14f);
        private static readonly Vector3 _previewCameraPosition = new Vector3(12f, 24f, -30f);
        private static readonly Color _previewBackgroundColor  = new Color(0.008f, 0.014f, 0.024f);
        private static readonly Color _previewFloorTint        = new Color(0.28f, 0.3f, 0.34f, 1f);

        private static readonly string[] G_Names = { "Moon", "Flower", "Star", "Diamond" };
        private static readonly string[] G_GlyphRoles = { "GlyphMoon", "GlyphFlower", "GlyphStar", "GlyphDiamond" };
        private static readonly string[] G_TextureNames = { "BaseColor", "Normal", "AO", "MetallicSmoothness" };
        private static readonly string[] G_RequiredRoles =
        {
            "Paper", "PaperRunes", "Wisps", "Petals", "GroundPetals", "Motes", "FootCore",
            "GlyphMoon", "GlyphFlower", "GlyphStar", "GlyphDiamond", "AirWisps", "SuccessWisps"
        };
        private static readonly string[] G_VeilTextures = { PAPER_VEIL_TEXTURE, WISP_VEIL_TEXTURE };
        private static readonly Vector3 _boundsPadding        = new Vector3(2f, 4f, 2f);
        private static readonly Vector3 _successBoundsPadding = new Vector3(4f, 8f, 4f);
        private static readonly Color[] G_Colors =
        {
            new Color(0.68f, 1.18f, 1.5f, 1f),
            new Color(1.5f, 0.75f, 0.95f, 1f),
            new Color(1.55f, 1.1f, 0.62f, 1f),
            new Color(1.1f, 0.7f, 1.55f, 1f)
        };

        [Serializable]
        private sealed class Export
        {
            [SerializeField] private Part[] _meshes = null;
            [SerializeField] private float[] _paperCenters = null;

            public Part[] Meshes => _meshes;
            public float[] PaperCenters => _paperCenters;
        }

        [Serializable]
        private sealed class Part
        {
            [SerializeField] private string _name = null;
            [SerializeField] private string _role = null;
            [SerializeField] private float[] _vertices = null;
            [SerializeField] private float[] _normals = null;
            [SerializeField] private float[] _uv = null;
            [SerializeField] private float[] _colors = null;
            [SerializeField] private int[] _pieceIds = null;
            [SerializeField] private int[] _triangles = null;

            public string Name => _name;
            public string Role => _role;
            public float[] Vertices => _vertices;
            public float[] Normals => _normals;
            public float[] Uv => _uv;
            public float[] Colors => _colors;
            public int[] PieceIds => _pieceIds;
            public int[] Triangles => _triangles;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 전체 입력을 검증한 뒤 에셋을 갱신한다. 기존 SO의 Inspector 조정은 보존한다 </summary>
        [MenuItem("Shinshi/VFX/Wolbaek/Build Talismans")]
        public static void Build()
        {
            GuardEditor();
            Export export = ReadValidatedExport();
            ValidateDependencies();
            EnsureFolders();
            ConfigureImports();

            Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>(export.Meshes.Length);
            foreach (Part part in export.Meshes)
            {
                meshes.Add(part.Name, MeshAsset(part));
            }

            Dictionary<string, Material> materials = CreateMaterials();
            Mesh boundary = RingAsset("SM_Talisman_ExactBoundary", 1f - BOUNDARY_WIDTH, 1f, false);
            Mesh progress = RingAsset("SM_Talisman_HoldProgress", PROGRESS_RADIUS - PROGRESS_HALF_WIDTH,
                PROGRESS_RADIUS + PROGRESS_HALF_WIDTH, true);
            Scene original = SceneManager.GetActiveScene();
            Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            try
            {
                for (int sign = 0; sign < SIGN_COUNT; ++sign)
                {
                    BuildPrefab(sign, export, meshes, materials, boundary, progress);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(temporary, true);
                RestoreActiveScene(original);
            }

            AssetDatabase.SaveAssets();
            BuildScene();
        }

        /// <summary> 부적 문양 순서에 대응하는 배치용 프리팹 경로를 반환한다 </summary>
        public static string PrefabPath(int sign)
        {
            if ((sign < 0) || (sign >= SIGN_COUNT))
            {
                throw new ArgumentOutOfRangeException(nameof(sign));
            }

            return PREFABS + "/PF_WolbaekTalisman_" + G_Names[sign] + ".prefab";
        }

        private static void GuardEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("PlayMode 중에는 부적 에셋을 생성하지 않습니다.");
            }

            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if ((scene.path == SCENE) && (scene.isDirty))
                {
                    throw new InvalidOperationException("부적 검수 씬의 미저장 변경을 먼저 보존하세요.");
                }
            }
        }

        private static Export ReadValidatedExport()
        {
            string path = Path.Combine(EVIDENCE, GEOMETRY_FILE);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Blender 부적 메시 export가 필요합니다.", path);
            }

            Export export = JsonUtility.FromJson<Export>(File.ReadAllText(path));
            if ((export == null) || (export.Meshes == null) || (export.Meshes.Length == 0)
                || (export.PaperCenters == null) || (export.PaperCenters.Length != PAPER_COUNT * POSITION_CHANNELS))
            {
                throw new InvalidDataException("부적 메시 또는 종이 중심점 데이터가 비어 있습니다.");
            }

            ValidateFinite(export.PaperCenters, "paperCenters");
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> roles = new HashSet<string>(StringComparer.Ordinal);
            foreach (Part part in export.Meshes)
            {
                ValidatePart(part);
                if (!names.Add(part.Name))
                {
                    throw new InvalidDataException("중복 메시 이름: " + part.Name);
                }
                roles.Add(part.Role);
            }

            foreach (string role in G_RequiredRoles)
            {
                if (!roles.Contains(role))
                {
                    throw new InvalidDataException("필수 부적 레이어 없음: " + role);
                }
            }
            return export;
        }

        private static void ValidatePart(Part part)
        {
            if ((part == null) || (string.IsNullOrEmpty(part.Name)) || (string.IsNullOrEmpty(part.Role))
                || (part.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                || (part.Name.Contains("/")) || (part.Name.Contains("\\")))
            {
                throw new InvalidDataException("메시 이름 또는 역할이 잘못되었습니다.");
            }

            if ((part.Role != "Mist") && (Array.IndexOf(G_RequiredRoles, part.Role) < 0))
            {
                throw new InvalidDataException("알 수 없는 부적 역할: " + part.Role);
            }

            if ((part.Vertices == null) || (part.Vertices.Length == 0)
                || (part.Vertices.Length % POSITION_CHANNELS != 0))
            {
                throw new InvalidDataException("잘못된 정점 배열: " + part.Name);
            }

            int count = part.Vertices.Length / POSITION_CHANNELS;
            if ((part.Normals == null) || (part.Normals.Length != part.Vertices.Length)
                || (part.Uv == null) || (part.Uv.Length != count * UV_CHANNELS)
                || (part.Colors == null) || (part.Colors.Length != count * COLOR_CHANNELS)
                || (part.PieceIds == null) || (part.PieceIds.Length != count)
                || (part.Triangles == null) || (part.Triangles.Length == 0)
                || (part.Triangles.Length % POSITION_CHANNELS != 0))
            {
                throw new InvalidDataException("메시 채널 길이가 다릅니다: " + part.Name);
            }

            ValidateFinite(part.Vertices, part.Name + ".vertices");
            ValidateFinite(part.Normals, part.Name + ".normals");
            ValidateFinite(part.Uv, part.Name + ".uv");
            ValidateFinite(part.Colors, part.Name + ".colors");
            int maxPiece = -1;
            HashSet<int> pieces = new HashSet<int>();
            for (int i = 0; i < count; ++i)
            {
                int piece = part.PieceIds[i];
                if ((piece < 0) || (piece >= MAX_PIECES)
                    || (((part.Role == "Paper") || (part.Role == "PaperRunes")) && (piece >= PAPER_COUNT)))
                {
                    throw new InvalidDataException("잘못된 조각 ID: " + part.Name);
                }
                pieces.Add(piece);
                maxPiece = Math.Max(maxPiece, piece);
                if ((IsWispRole(part.Role)) && ((part.Uv[i * UV_CHANNELS + 1] < 0f)
                    || (part.Uv[i * UV_CHANNELS + 1] > 1f)))
                {
                    throw new InvalidDataException("리본 길이 UV는 0부터 1 사이여야 합니다: " + part.Name);
                }

                if ((part.Role == "AirWisps") || (part.Role == "SuccessWisps"))
                {
                    int vertex = i * POSITION_CHANNELS;
                    float x = part.Vertices[vertex];
                    float height = part.Vertices[vertex + 1];
                    float z = part.Vertices[vertex + 2];
                    float radius = Mathf.Sqrt(x * x + z * z);
                    float minHeight = part.Role == "AirWisps" ? AIR_MIN_HEIGHT : 0f;
                    float maxHeight = part.Role == "AirWisps" ? AIR_MAX_HEIGHT : SUCCESS_MAX_HEIGHT;
                    if ((radius > SKY_MAX_RADIUS + GEOMETRY_EPSILON)
                        || (height < minHeight - GEOMETRY_EPSILON) || (height > maxHeight + GEOMETRY_EPSILON))
                    {
                        throw new InvalidDataException("공중 리본의 실제 미터 범위가 다릅니다: " + part.Name);
                    }
                }
            }

            if ((pieces.Count != maxPiece + 1)
                || (((part.Role == "Paper") || (part.Role == "PaperRunes")) && (pieces.Count != PAPER_COUNT)))
            {
                throw new InvalidDataException("메시 조각 ID에 빈 구간이 있습니다: " + part.Name);
            }

            if (((part.Role == "AirWisps") && (pieces.Count != AIR_WISP_PIECES))
                || ((part.Role == "SuccessWisps") && (pieces.Count != SUCCESS_WISP_PIECES)))
            {
                throw new InvalidDataException("공중 리본 조각 수가 다릅니다: " + part.Name);
            }

            foreach (int triangle in part.Triangles)
            {
                if ((triangle < 0) || (triangle >= count))
                {
                    throw new InvalidDataException("삼각형 정점 참조 범위 오류: " + part.Name);
                }
            }
        }

        private static void ValidateFinite(float[] values, string channel)
        {
            foreach (float value in values)
            {
                if ((float.IsNaN(value)) || (float.IsInfinity(value)) || (Mathf.Abs(value) > DATA_BOUND))
                {
                    throw new InvalidDataException("유한한 범위의 메시 값이 필요합니다: " + channel);
                }
            }
        }

        private static void ValidateDependencies()
        {
            if ((Shader.Find(PARTICLE_SHADER) == null) || (Shader.Find(LIT_SHADER) == null))
            {
                throw new InvalidOperationException("프로젝트 VFX 셰이더와 URP Lit이 필요합니다.");
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(STONE_FLOOR) == null)
            {
                throw new FileNotFoundException("프로젝트 석재 바닥 프리팹이 필요합니다.", STONE_FLOOR);
            }

            RequireFile(PEDESTAL_MODEL);
            RequireFile(BEAM_TEXTURES + "M_Beam_Boundary.png");
            RequireFile(BEAM_TEXTURES + "M_Beam_VeilCloud.png");
            RequireFile(MOTE_TEXTURE);
            foreach (string texture in G_VeilTextures)
            {
                RequireFile(texture);
            }
            foreach (string name in G_TextureNames)
            {
                RequireFile(PedestalTexturePath(name));
            }
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("부적 제작 의존 파일이 필요합니다.", path);
            }
        }

        private static string PedestalTexturePath(string suffix)
        {
            return ROOT + "/Textures/T_Wolbaek_TalismanPedestal_" + suffix + ".png";
        }

        private static void EnsureFolders()
        {
            Directory.CreateDirectory(ROOT + "/Models");
            Directory.CreateDirectory(ROOT + "/Materials");
            Directory.CreateDirectory(ROOT + "/Textures");
            Directory.CreateDirectory(PREFABS);
            Directory.CreateDirectory(Path.GetDirectoryName(SCENE));
            AssetDatabase.Refresh();
        }

        private static void ConfigureImports()
        {
            foreach (string path in G_VeilTextures)
            {
                TextureImporter veil = AssetImporter.GetAtPath(path) as TextureImporter;
                if (veil == null)
                {
                    throw new InvalidOperationException("반투명 마스크 임포터가 없습니다: " + path);
                }
                veil.textureType = TextureImporterType.Default;
                veil.sRGBTexture = false;
                veil.alphaSource = TextureImporterAlphaSource.FromInput;
                veil.alphaIsTransparency = true;
                veil.mipmapEnabled = true;
                veil.isReadable = false;
                veil.wrapMode = TextureWrapMode.Clamp;
                veil.filterMode = FilterMode.Bilinear;
                veil.textureCompression = TextureImporterCompression.Uncompressed;
                veil.SaveAndReimport();
            }

            foreach (string name in G_TextureNames)
            {
                string path = PedestalTexturePath(name);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    throw new InvalidOperationException("텍스처 임포터가 없습니다: " + path);
                }
                importer.textureType = name == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = name == "BaseColor";
                importer.mipmapEnabled = true;
                importer.isReadable = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.maxTextureSize = PEDESTAL_TEXTURE_SIZE;
                importer.SaveAndReimport();
            }

            ModelImporter model = AssetImporter.GetAtPath(PEDESTAL_MODEL) as ModelImporter;
            if (model == null)
            {
                throw new InvalidOperationException("받침 FBX 임포터가 없습니다.");
            }
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.importNormals = ModelImporterNormals.Import;
            model.importTangents = ModelImporterTangents.CalculateMikk;
            model.isReadable = false;
            model.SaveAndReimport();
        }

        private static Dictionary<string, Material> CreateMaterials()
        {
            Texture2D mist = AssetDatabase.LoadAssetAtPath<Texture2D>(BEAM_TEXTURES + "M_Beam_VeilCloud.png");
            Texture2D mote = AssetDatabase.LoadAssetAtPath<Texture2D>(MOTE_TEXTURE);
            Texture2D paperVeil = AssetDatabase.LoadAssetAtPath<Texture2D>(PAPER_VEIL_TEXTURE);
            Texture2D wispVeil = AssetDatabase.LoadAssetAtPath<Texture2D>(WISP_VEIL_TEXTURE);
            Dictionary<string, Material> materials = new Dictionary<string, Material>();
            materials.Add("Paper", ParticleMaterial("M_Talisman_Paper", paperVeil, false));
            materials.Add("PaperRunes", ParticleMaterial("M_Talisman_PaperRunes", Texture2D.whiteTexture, true));
            materials.Add("Wisps", ParticleMaterial("M_Talisman_LowWisps", wispVeil, true));
            materials.Add("AirWisps", ParticleMaterial("M_Talisman_AirWisps", wispVeil, true));
            materials.Add("SuccessWisps", ParticleMaterial("M_Talisman_SuccessWisps", wispVeil, true));
            materials.Add("Mist", ParticleMaterial("M_Talisman_Mist", mist, false));
            materials.Add("Petals", ParticleMaterial("M_Talisman_Petals", Texture2D.whiteTexture, true));
            materials.Add("GroundPetals", ParticleMaterial("M_Talisman_GroundPetals", Texture2D.whiteTexture, true));
            materials.Add("Motes", ParticleMaterial("M_Talisman_Motes", mote, true));
            materials.Add("FootCore", ParticleMaterial("M_Talisman_FootCore", mote, true));
            materials.Add("Glyph", ParticleMaterial("M_Talisman_Glyph", Texture2D.whiteTexture, true));
            materials.Add("Boundary", ParticleMaterial("M_Talisman_ExactBoundary", Texture2D.whiteTexture, true));
            materials.Add("Progress", ParticleMaterial("M_Talisman_HoldProgress", ProgressMask(), true));
            materials.Add("Pedestal", PedestalMaterial());
            return materials;
        }

        private static Material ParticleMaterial(string name, Texture2D texture, bool additive)
        {
            string path = ROOT + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = material == null;
            if (fresh)
            {
                material = new Material(Shader.Find(PARTICLE_SHADER));
            }

            material.name = name;
            material.shader = Shader.Find(PARTICLE_SHADER);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_SoftFade", SOFT_FADE);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.renderQueue = (int)RenderQueue.Transparent;
            StoreAsset(material, path, fresh);
            return material;
        }

        private static Material PedestalMaterial()
        {
            string path = ROOT + "/Materials/M_Wolbaek_TalismanPedestal.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = material == null;
            if (fresh)
            {
                material = new Material(Shader.Find(LIT_SHADER));
            }

            material.name = "M_Wolbaek_TalismanPedestal";
            material.shader = Shader.Find(LIT_SHADER);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PedestalTexturePath("BaseColor")));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PedestalTexturePath("Normal")));
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PedestalTexturePath("AO")));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PedestalTexturePath("MetallicSmoothness")));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_OcclusionStrength", 1f);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            StoreAsset(material, path, fresh);
            return material;
        }

        private static Texture2D ProgressMask()
        {
            Texture2D texture = new Texture2D(PROGRESS_WIDTH, PROGRESS_HEIGHT, TextureFormat.RGBA32, false);
            for (int y = 0; y < PROGRESS_HEIGHT; ++y)
            {
                float v = y / (PROGRESS_HEIGHT - 1f);
                float edge = Mathf.Clamp01(Mathf.Min(v, 1f - v) / MASK_EDGE);
                for (int x = 0; x < PROGRESS_WIDTH; ++x)
                {
                    float alpha = x < PROGRESS_WIDTH / UV_CHANNELS ? edge : 0f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            File.WriteAllBytes(PROGRESS_TEXTURE, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(PROGRESS_TEXTURE, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(PROGRESS_TEXTURE) as TextureImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(PROGRESS_TEXTURE);
        }

        private static WolbaekTalismanSettingsSO SettingsAsset(int sign)
        {
            string path = ROOT + "/SO_WolbaekTalisman_" + G_Names[sign] + ".asset";
            WolbaekTalismanSettingsSO settings = AssetDatabase.LoadAssetAtPath<WolbaekTalismanSettingsSO>(path);
            if (settings != null)
            {
                return settings;
            }

            settings = ScriptableObject.CreateInstance<WolbaekTalismanSettingsSO>();
            SerializedObject serialized = new SerializedObject(settings);
            serialized.FindProperty("_defaultSign").enumValueIndex = sign;
            serialized.FindProperty("_defaultColor").colorValue = G_Colors[sign];
            serialized.FindProperty("_appearSeconds").floatValue = APPEAR_SECONDS;
            serialized.FindProperty("_resetSeconds").floatValue = RESET_SECONDS;
            serialized.FindProperty("_successSeconds").floatValue = SUCCESS_SECONDS;
            serialized.FindProperty("_fadeSeconds").floatValue = FADE_SECONDS;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(settings, path);
            return settings;
        }

        private static Mesh MeshAsset(Part part)
        {
            string path = ROOT + "/Models/" + part.Name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = mesh == null;
            if (fresh)
            {
                mesh = new Mesh();
            }

            mesh.Clear();
            mesh.name = part.Name;
            mesh.indexFormat = IndexFormat.UInt32;
            int count = part.Vertices.Length / POSITION_CHANNELS;
            Vector3[] vertices = new Vector3[count];
            Vector3[] normals = new Vector3[count];
            Vector2[] uv = new Vector2[count];
            Color[] colors = new Color[count];
            for (int i = 0; i < count; ++i)
            {
                int vertex = i * POSITION_CHANNELS;
                int color = i * COLOR_CHANNELS;
                vertices[i] = new Vector3(part.Vertices[vertex], part.Vertices[vertex + 1], part.Vertices[vertex + 2]);
                normals[i] = new Vector3(part.Normals[vertex], part.Normals[vertex + 1], part.Normals[vertex + 2]);
                uv[i] = new Vector2(part.Uv[i * UV_CHANNELS], part.Uv[i * UV_CHANNELS + 1]);
                colors[i] = new Color(part.Colors[color], part.Colors[color + 1], part.Colors[color + 2], part.Colors[color + 3]);
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = part.Triangles;
            mesh.RecalculateBounds();
            StoreAsset(mesh, path, fresh);
            return mesh;
        }

        private static void StoreAsset(UnityEngine.Object asset, string path, bool fresh)
        {
            if (fresh)
            {
                AssetDatabase.CreateAsset(asset, path);
                return;
            }
            EditorUtility.SetDirty(asset);
        }

        private static void BuildPrefab(int sign, Export export, Dictionary<string, Mesh> meshes,
            Dictionary<string, Material> materials, Mesh boundaryMesh, Mesh progressMesh)
        {
            GameObject root = new GameObject("PF_WolbaekTalisman_" + G_Names[sign]);
            WolbaekTalismanPlayer player = root.AddComponent<WolbaekTalismanPlayer>();
            Transform radiusRoot = Child(root.transform, "Authoritative_Radius");
            Transform glyphRoot = Child(root.transform, "FloatingGlyphs");
            List<Renderer> layers = new List<Renderer>();
            List<int> layerKinds = new List<int>();
            List<Transform> bones = new List<Transform>();
            List<Vector3> rest = new List<Vector3>();
            List<Vector4> motion = new List<Vector4>();
            Renderer[] glyphs = new Renderer[SIGN_COUNT];
            foreach (Part part in export.Meshes)
            {
                int glyph = Array.IndexOf(G_GlyphRoles, part.Role);
                if (glyph >= 0)
                {
                    glyphs[glyph] = MeshRendererAsset(glyphRoot, part.Name, meshes[part.Name], materials["Glyph"], false);
                    continue;
                }

                Transform parent = IsRadiusRole(part.Role) ? radiusRoot : root.transform;
                GameObject layer = Child(parent, part.Name).gameObject;
                SkinnedMeshRenderer renderer = Skin(layer, meshes[part.Name], part,
                    export.PaperCenters, bones, rest, motion);
                renderer.sharedMaterial = materials[part.Role];
                ConfigureRenderer(renderer, false);
                layers.Add(renderer);
                layerKinds.Add(LayerKind(part.Role));
            }

            Renderer boundary = MeshRendererAsset(radiusRoot, "Exact_Occupancy_Boundary",
                boundaryMesh, materials["Boundary"], false);
            Renderer progress = MeshRendererAsset(radiusRoot, "Authoritative_Hold_Progress",
                progressMesh, materials["Progress"], false);
            Renderer[] pillar = BuildPedestal(root.transform, materials["Pedestal"]);
            Light footLight = Child(root.transform, "Talisman_FootLight").gameObject.AddComponent<Light>();
            footLight.type = LightType.Point;
            footLight.enabled = false;
            footLight.intensity = 0f;
            footLight.shadows = LightShadows.None;
            footLight.range = FOOT_LIGHT_RANGE;
            footLight.transform.localPosition = Vector3.up * FOOT_LIGHT_HEIGHT;

            SerializedObject serialized = new SerializedObject(player);
            serialized.FindProperty("_settings").objectReferenceValue = SettingsAsset(sign);
            serialized.FindProperty("_radiusRoot").objectReferenceValue = radiusRoot;
            serialized.FindProperty("_glyphRoot").objectReferenceValue = glyphRoot;
            serialized.FindProperty("_boundary").objectReferenceValue = boundary;
            serialized.FindProperty("_progress").objectReferenceValue = progress;
            serialized.FindProperty("_footLight").objectReferenceValue = footLight;
            SetReferences(serialized.FindProperty("_layers"), layers);
            SetReferences(serialized.FindProperty("_glyphs"), glyphs);
            SetReferences(serialized.FindProperty("_pillar"), pillar);
            SetReferences(serialized.FindProperty("_bones"), bones);
            SerializedProperty kinds = serialized.FindProperty("_layerKinds");
            SerializedProperty poses = serialized.FindProperty("_rest");
            SerializedProperty movements = serialized.FindProperty("_motion");
            kinds.arraySize = layerKinds.Count;
            poses.arraySize = rest.Count;
            movements.arraySize = motion.Count;
            for (int i = 0; i < layerKinds.Count; ++i)
            {
                kinds.GetArrayElementAtIndex(i).intValue = layerKinds[i];
            }
            for (int i = 0; i < rest.Count; ++i)
            {
                poses.GetArrayElementAtIndex(i).vector3Value = rest[i];
                movements.GetArrayElementAtIndex(i).vector4Value = motion[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(sign), out bool saved);
            UnityEngine.Object.DestroyImmediate(root);
            if (!saved)
            {
                throw new InvalidOperationException("부적 프리팹 저장 실패: " + G_Names[sign]);
            }
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static bool IsRadiusRole(string role)
        {
            return (role == "Wisps") || (role == "GroundPetals") || (role == "FootCore") || (role == "Mist");
        }

        private static bool IsWispRole(string role)
        {
            return (role == "Wisps") || (role == "AirWisps") || (role == "SuccessWisps");
        }

        private static int LayerKind(string role)
        {
            switch (role)
            {
                case "Wisps": return 0;
                case "Petals": return 1;
                case "Mist": return 2;
                case "GroundPetals": return 3;
                case "Paper": return 4;
                case "PaperRunes": return 5;
                case "Motes": return 6;
                case "FootCore": return 7;
                case "AirWisps": return 8;
                case "SuccessWisps": return 9;
                default: throw new InvalidDataException("표현 레이어 종류 없음: " + role);
            }
        }

        private static int MotionKind(string role)
        {
            switch (role)
            {
                case "Wisps":
                case "AirWisps": return 0;
                case "Petals": return 1;
                case "Motes": return 2;
                case "Mist": return 3;
                case "Paper":
                case "PaperRunes": return 4;
                case "GroundPetals": return 5;
                case "FootCore": return 6;
                case "SuccessWisps": return 7;
                default: throw new InvalidDataException("뼈 움직임 종류 없음: " + role);
            }
        }

        private static SkinnedMeshRenderer Skin(GameObject layer, Mesh mesh, Part part,
            float[] paperCenters, List<Transform> bones, List<Vector3> rest, List<Vector4> motion)
        {
            bool paper = (part.Role == "Paper") || (part.Role == "PaperRunes");
            int stations = IsWispRole(part.Role) ? FLOW_STATIONS : 1;
            int pieces = 0;
            foreach (int piece in part.PieceIds)
            {
                pieces = Math.Max(pieces, piece + 1);
            }
            int total = pieces * stations;
            Vector3[] centers = new Vector3[total];
            float[] sums = new float[total];
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = new BoneWeight[vertices.Length];
            for (int i = 0; i < vertices.Length; ++i)
            {
                float along = stations == 1 ? 0f : part.Uv[i * UV_CHANNELS + 1] * (stations - 1);
                int first = Mathf.Clamp(Mathf.FloorToInt(along), 0, stations - 1);
                int second = Mathf.Min(stations - 1, first + 1);
                float mix = along - first;
                first += part.PieceIds[i] * stations;
                second += part.PieceIds[i] * stations;
                centers[first] += vertices[i] * (1f - mix);
                centers[second] += vertices[i] * mix;
                sums[first] += 1f - mix;
                sums[second] += mix;
                weights[i] = new BoneWeight
                {
                    boneIndex0 = first,
                    boneIndex1 = second,
                    weight0 = 1f - mix,
                    weight1 = mix
                };
            }

            Transform[] local = new Transform[total];
            Matrix4x4[] bind = new Matrix4x4[total];
            int kind = MotionKind(part.Role);
            for (int i = 0; i < total; ++i)
            {
                if (paper)
                {
                    // 종이와 룬은 독립 메시이므로 같은 피벗과 위상을 주어 회전 중에도 표면을 맞춘다
                    int center = i * POSITION_CHANNELS;
                    centers[i] = new Vector3(paperCenters[center], paperCenters[center + 1], paperCenters[center + 2]);
                }
                else
                {
                    centers[i] /= Mathf.Max(MIN_SUM, sums[i]);
                }
                local[i] = Child(layer.transform, "Flow_" + i);
                local[i].localPosition = centers[i];
                bind[i] = local[i].worldToLocalMatrix * layer.transform.localToWorldMatrix;
                bones.Add(local[i]);
                rest.Add(centers[i]);
                int piece = i / stations;
                float amplitude = part.Role == "SuccessWisps" ? (i % stations) / (float)(stations - 1)
                    : part.Role == "GroundPetals" ? GROUND_AMPLITUDE : FLOW_AMPLITUDE;
                float phase = piece * GOLDEN_PHASE + (paper ? 0f : i % stations * FLOW_STATION_PHASE);
                float spin = piece % UV_CHANNELS == 0 ? 1f : -1f;
                motion.Add(new Vector4(kind, amplitude, phase, spin));
            }
            mesh.boneWeights = weights;
            mesh.bindposes = bind;
            EditorUtility.SetDirty(mesh);
            SkinnedMeshRenderer renderer = layer.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = local;
            renderer.rootBone = layer.transform;
            renderer.quality = SkinQuality.Bone2;
            Bounds bounds = mesh.bounds;
            bounds.Expand(part.Role == "SuccessWisps" ? _successBoundsPadding : _boundsPadding);
            renderer.localBounds = bounds;
            return renderer;
        }

        private static Renderer MeshRendererAsset(Transform parent, string name, Mesh mesh, Material material, bool enabled)
        {
            GameObject child = Child(parent, name).gameObject;
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigureRenderer(renderer, enabled);
            return renderer;
        }

        private static void ConfigureRenderer(Renderer renderer, bool enabled)
        {
            renderer.enabled = enabled;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Mesh RingAsset(string name, float innerRadius, float outerRadius, bool progress)
        {
            string path = ROOT + "/Models/" + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = mesh == null;
            if (fresh)
            {
                mesh = new Mesh();
            }
            mesh.Clear();
            mesh.name = name;
            int count = (RING_SEGMENTS + 1) * UV_CHANNELS;
            Vector3[] vertices = new Vector3[count];
            Vector3[] normals = new Vector3[count];
            Vector2[] uv = new Vector2[count];
            Color[] colors = new Color[count];
            int[] triangles = new int[RING_SEGMENTS * PAPER_COUNT];
            for (int i = 0; i <= RING_SEGMENTS; ++i)
            {
                float u = i / (float)RING_SEGMENTS;
                float angle = u * TAU;
                for (int side = 0; side < UV_CHANNELS; ++side)
                {
                    int index = i * UV_CHANNELS + side;
                    float radius = side == 0 ? innerRadius : outerRadius;
                    vertices[index] = new Vector3(Mathf.Sin(angle) * radius, RING_HEIGHT, Mathf.Cos(angle) * radius);
                    normals[index] = Vector3.up;
                    uv[index] = progress ? new Vector2(u, side) : new Vector2(HALF, HALF);
                    colors[index] = Color.white;
                }
                if (i < RING_SEGMENTS)
                {
                    int vertex = i * UV_CHANNELS;
                    int triangle = i * PAPER_COUNT;
                    triangles[triangle] = vertex;
                    triangles[triangle + 1] = vertex + 1;
                    triangles[triangle + 2] = vertex + UV_CHANNELS;
                    triangles[triangle + 3] = vertex + 1;
                    triangles[triangle + 4] = vertex + UV_CHANNELS + 1;
                    triangles[triangle + 5] = vertex + UV_CHANNELS;
                }
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            StoreAsset(mesh, path, fresh);
            return mesh;
        }

        private static Renderer[] BuildPedestal(Transform parent, Material material)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(PEDESTAL_MODEL);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent.gameObject.scene);
            instance.name = "Decorative_StonePedestal";
            instance.transform.SetParent(parent, false);
            Renderer[][] levels = new Renderer[POSITION_CHANNELS][];
            List<Renderer> all = new List<Renderer>();
            for (int i = 0; i < levels.Length; ++i)
            {
                List<Renderer> renderers = new List<Renderer>();
                foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.name.EndsWith("_LOD" + i, StringComparison.Ordinal))
                    {
                        renderer.sharedMaterial = material;
                        renderer.shadowCastingMode = ShadowCastingMode.On;
                        renderer.receiveShadows = true;
                        renderer.enabled = false;
                        renderers.Add(renderer);
                        all.Add(renderer);
                    }
                }
                if (renderers.Count == 0)
                {
                    throw new InvalidDataException("받침 FBX LOD가 없습니다: " + i);
                }
                levels[i] = renderers.ToArray();
            }

            if (!instance.TryGetComponent(out LODGroup group))
            {
                group = instance.AddComponent<LODGroup>();
            }
            group.SetLODs(new[] { new LOD(LOD_NEAR, levels[0]), new LOD(LOD_MIDDLE, levels[1]), new LOD(LOD_FAR, levels[2]) });
            group.RecalculateBounds();
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            return all.ToArray();
        }

        private static void SetReferences<T>(SerializedProperty property, IList<T> references) where T : UnityEngine.Object
        {
            property.arraySize = references.Count;
            for (int i = 0; i < references.Count; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = references[i];
            }
        }

        private static VolumeProfile PreviewProfile()
        {
            string path = ROOT + "/VP_TalismanPreview.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile != null)
            {
                return profile;
            }

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(BLOOM_INTENSITY);
            bloom.threshold.Override(BLOOM_THRESHOLD);
            bloom.scatter.Override(BLOOM_SCATTER);
            AssetDatabase.AddObjectToAsset(bloom, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void BuildScene()
        {
            GuardEditor();
            Scene original = SceneManager.GetActiveScene();
            bool singleTarget = (SceneManager.sceneCount == 1) && (original.path == SCENE);
            bool targetWasOpen = false;
            bool targetWasActive = false;
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene existing = SceneManager.GetSceneAt(i);
                if (existing.path == SCENE)
                {
                    targetWasOpen = true;
                    targetWasActive = existing == original;
                    if ((!singleTarget) && (!EditorSceneManager.CloseScene(existing, true)))
                    {
                        throw new InvalidOperationException("기존 부적 검수 씬을 닫지 못했다");
                    }
                    break;
                }
            }
            // 마지막 열린 개발 씬은 Single로 재생성하고 다른 씬이 있으면 Additive로 보존한다
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                singleTarget ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            bool saved = false;
            try
            {
                BuildStage(scene);
                Light light = new GameObject("Talisman_QuarterView_Key").AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = _previewKeyColor;
                light.intensity = PREVIEW_KEY_INTENSITY;
                light.transform.rotation = Quaternion.Euler(PREVIEW_KEY_PITCH, PREVIEW_KEY_YAW, 0f);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = _previewSkyColor;
                RenderSettings.ambientEquatorColor = _previewEquatorColor;
                RenderSettings.ambientGroundColor = _previewGroundColor;
                Camera camera = new GameObject("Talisman_GameplayCamera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = _previewCameraPosition;
                camera.transform.LookAt(Vector3.zero);
                camera.fieldOfView = PREVIEW_CAMERA_FOV;
                camera.nearClipPlane = PREVIEW_CAMERA_NEAR;
                camera.farClipPlane = PREVIEW_CAMERA_FAR;
                camera.allowHDR = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = _previewBackgroundColor;
                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.requiresDepthTexture = true;
                cameraData.renderPostProcessing = true;
                Volume volume = new GameObject("Talisman_Restrained_Bloom").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = PreviewProfile();

                WolbaekTalismanPlayer[] players = new WolbaekTalismanPlayer[SIGN_COUNT];
                Vector3[] positions =
                {
                    Vector3.forward * STATION_DISTANCE, Vector3.left * STATION_DISTANCE,
                    Vector3.right * STATION_DISTANCE, Vector3.back * STATION_DISTANCE
                };
                for (int i = 0; i < SIGN_COUNT; ++i)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i));
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    instance.name = "Talisman_" + G_Names[i];
                    instance.transform.position = positions[i];
                    players[i] = instance.GetComponent<WolbaekTalismanPlayer>();
                }

                WolbaekTalismanPreview preview = new GameObject("Development_SharedProgress_Cues").AddComponent<WolbaekTalismanPreview>();
                SerializedObject serialized = new SerializedObject(preview);
                SetReferences(serialized.FindProperty("_players"), players);
                serialized.FindProperty("_camera").objectReferenceValue = camera;
                serialized.FindProperty("_radius").floatValue = PREVIEW_RADIUS;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                saved = EditorSceneManager.SaveScene(scene, SCENE);
                if (!saved)
                {
                    throw new InvalidOperationException("부적 검수 씬 저장 실패");
                }
            }
            finally
            {
                if (singleTarget)
                {
                    if ((!saved) && (File.Exists(SCENE)))
                    {
                        EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);
                    }
                }
                else
                {
                    EditorSceneManager.CloseScene(scene, true);
                    if ((targetWasOpen) && (File.Exists(SCENE)))
                    {
                        // 열린 검수 씬은 재빌드된 파일로 다시 읽되 통합 씬의 활성 상태를 바꾸지 않는다
                        Scene reloaded = EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Additive);
                        if (targetWasActive)
                        {
                            SceneManager.SetActiveScene(reloaded);
                        }
                        else
                        {
                            RestoreActiveScene(original);
                        }
                    }
                    else
                    {
                        RestoreActiveScene(original);
                    }
                }
            }
            AssetDatabase.SaveAssets();
        }

        private static void BuildStage(Scene scene)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(STONE_FLOOR);
            Transform stage = new GameObject("Project_StoneCourtyard_Stage").transform;
            for (int z = -FLOOR_HALF_COUNT; z <= FLOOR_HALF_COUNT; ++z)
            {
                for (int x = -FLOOR_HALF_COUNT; x <= FLOOR_HALF_COUNT; ++x)
                {
                    GameObject tile = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                    tile.transform.SetParent(stage, false);
                    tile.transform.localPosition = new Vector3(x * FLOOR_SPACING, GROUND_OFFSET, z * FLOOR_SPACING);
                    foreach (Renderer renderer in tile.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.sharedMaterial = StageMaterial(renderer.sharedMaterial);
                    }
                }
            }

            GameObject centre = AssetDatabase.LoadAssetAtPath<GameObject>(STONE_CENTRE);
            if (centre != null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(centre, scene);
                instance.name = "Project_Wolbaek_Centre_Reference";
                instance.transform.SetParent(stage, false);
                instance.transform.localPosition = Vector3.up * CENTRE_OFFSET;
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = StageMaterial(renderer.sharedMaterial);
                }
            }
        }

        private static Material StageMaterial(Material source)
        {
            if (source == null)
            {
                throw new InvalidDataException("검수 바닥 재질이 비어 있다");
            }
            string path = ROOT + "/Materials/M_TalismanPreview_" + source.name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = material == null;
            if (fresh)
            {
                material = new Material(source);
            }
            material.CopyPropertiesFromMaterial(source);
            material.name = "M_TalismanPreview_" + source.name;
            material.SetColor("_BaseColor", source.GetColor("_BaseColor") * _previewFloorTint);
            StoreAsset(material, path, fresh);
            return material;
        }

        private static void RestoreActiveScene(Scene original)
        {
            if ((original.IsValid()) && (original.isLoaded))
            {
                SceneManager.SetActiveScene(original);
            }
        }
    }
}
