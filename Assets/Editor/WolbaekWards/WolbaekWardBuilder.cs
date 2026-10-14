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
    /// <summary> 검증된 Blender 결계 메시를 독립 프리팹과 개발 검수 씬으로 만든다 </summary>
    public static class WolbaekWardBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ROOT     = "Assets/01.Scripts/05.Art/VFX/Wolbaek/Wards";
        public const string PREFABS  = "Assets/02.Prefab/VFX/Wolbaek/Wards";
        public const string SCENE    = "Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_Wards.unity";
        public const string EVIDENCE = ".claude/docs/2026-10-14/wolbaek-wards-blender";

        private const string GEOMETRY_FILE   = "wards_geometry_v02.json";
        private const string SOURCE_TEXTURE_FOLDER = "source-textures-v02";
        private const string PARTICLE_SHADER = "Shinshi/VFX/Particle";
        private const string LIT_SHADER      = "Universal Render Pipeline/Lit";
        private const string STONE_FLOOR     = "Assets/02.Prefab/Environment/StoneCourtyard/PF_StoneCourtyardTile.prefab";
        private const string MOTE_TEXTURE    = "Assets/01.Scripts/05.Art/VFX/Wolbaek/Warp/Materials/T_Warp_MoteGlow.png";
        private const string PROFILE_PATH    = ROOT + "/VP_WardPreview.asset";
        private const int SIGN_COUNT         = 2;
        private const int PLAYER_COUNT       = 4;
        private const int POSITION_CHANNELS  = 3;
        private const int UV_CHANNELS        = 2;
        private const int COLOR_CHANNELS     = 4;
        private const int FLOW_STATIONS      = 9;
        private const int GLYPH_STATIONS     = 4;
        private const int SPARK_PIECES        = 12;
        private const int MAX_PIECES         = 4096;
        private const int FLOOR_HALF_COUNT   = 5;
        private const int PNG_HEADER_BYTES   = 24;
        private const int CHECK_TEXTURE_SIZE = 2;
        private const int MAX_TEXTURE_SIZE   = 4096;
        private const int MAX_ASSET_NAME     = 128;
        private const int WARD_SHELL_LAYER   = 0;
        private const int WARD_RIBBON_LAYER  = 1;
        private const int WARD_PETAL_LAYER   = 2;
        private const int WARD_MOTE_LAYER    = 3;
        private const int LINK_RIBBON_LAYER  = 0;
        private const int LINK_MOTE_LAYER    = 1;
        private const int RIBBON_MOTION      = 0;
        private const int PETAL_MOTION       = 1;
        private const int WARD_MOTE_MOTION   = 2;
        private const int LINK_MOTE_MOTION   = 1;
        private const int LINK_GLYPH_MOTION  = 2;
        private const float MIN_SUM          = 0.0001f;
        private const float DATA_BOUND       = 1000f;
        private const float GEOMETRY_EPSILON = 0.002f;
        private const float GOLDEN_PHASE     = 2.399963f;
        private const float STATION_PHASE    = 0.23f;
        private const float SOFT_FADE        = 0.001f;
        private const float PREVIEW_RADIUS   = 3f;
        private const float WARD_SEPARATION  = 8f;
        private const float FLOOR_SPACING    = 6f;
        private const float FLOOR_HEIGHT     = -0.025f;
        private const float CHARACTER_HEIGHT = 0.9f;
        private const float CHARACTER_WIDTH  = 0.55f;
        private const float CHARACTER_GAP    = 0.9f;
        private const float BLOOM_INTENSITY  = 0.7f;
        private const float BLOOM_THRESHOLD  = 1.1f;
        private const float BLOOM_SCATTER    = 0.65f;
        private const float LIGHT_INTENSITY  = 0.9f;
        private const float CAMERA_FOV       = 42f;
        private const float CAMERA_NEAR      = 0.1f;
        private const float CAMERA_FAR       = 100f;
        private const float CHARACTER_TINT   = 0.15f;
        private const float CHARACTER_SMOOTH = 0.35f;
        private const float DECORATIVE_CULL  = 0.025f;
        private const float WARD_MOTE_AMPLITUDE = 0.45f;

        private static readonly string[] G_Names =
        {
            "Red", "Blue"
        };
        private static readonly string[] G_TextureNames =
        {
            "T_Ward_Veil.png", "T_Ward_Flow.png"
        };
        private static readonly string[] G_WardGlyphRoles =
        {
            "WardGlyphDiamond", "WardGlyphMoon"
        };
        private static readonly string[] G_MarkGlyphRoles =
        {
            "NextGlyphDiamond", "NextGlyphMoon"
        };
        private static readonly string[] G_LinkGlyphRoles =
        {
            "LinkGlyphDiamond", "LinkGlyphMoon"
        };
        private static readonly string[] G_RequiredRoles =
        {
            "WardShell", "WardRibbons", "Boundary", "NextBoundary", "NextGlyphDiamond", "NextGlyphMoon",
            "WardGlyphDiamond", "WardGlyphMoon", "Petals", "LinkRibbons", "LinkGlyphDiamond", "LinkGlyphMoon",
            "EndpointRings", "LinkSparks", "WardMotes"
        };
        private static readonly Color[] G_Colors =
        {
            new Color(1.5f, 0.35f, 0.3f, 1f), new Color(0.35f, 0.72f, 1.5f, 1f)
        };
        private static readonly Vector3 _wardBoundsPadding = new Vector3(1f, 1f, 1f);
        private static readonly Vector3 _linkBoundsPadding = new Vector3(1f, 1f, 1f);
        private static readonly Vector3 _cameraPosition    = new Vector3(0f, 16f, -19f);
        private static readonly Vector3 _lightRotation     = new Vector3(48f, -32f, 0f);
        private static readonly byte[] G_PngSignature =
        {
            137, 80, 78, 71, 13, 10, 26, 10
        };
        private static readonly Color _previewLightColor          = new Color(0.86f, 0.92f, 1f);
        private static readonly Color _previewAmbientSkyColor     = new Color(0.22f, 0.27f, 0.36f);
        private static readonly Color _previewAmbientEquatorColor = new Color(0.13f, 0.17f, 0.23f);
        private static readonly Color _previewAmbientGroundColor  = new Color(0.08f, 0.1f, 0.14f);
        private static readonly Color _previewBackgroundColor     = new Color(0.008f, 0.014f, 0.024f);
        private static readonly Color _previewStageTint           = new Color(0.28f, 0.3f, 0.34f, 1f);
        private static readonly Color _previewPlayerColor         = new Color(0.45f, 0.48f, 0.52f);

        [Serializable]
        private sealed class Export
        {
            [SerializeField] private Part[] _meshes = null;
            public Part[] Meshes => _meshes;
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

        /// <summary> 입력 전체를 먼저 검사하고 기존 에셋의 GUID와 SO 조정값을 보존한다 </summary>
        [MenuItem("Shinshi/VFX/Wolbaek/Build Wards and Links")]
        public static void Build()
        {
            GuardEditor();
            Export export = ReadValidatedExport();
            ValidateDependencies();
            ValidateAssetTargets(export);
            EnsureFolders();
            CopyAndImportTextures();
            Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            Dictionary<string, Part> parts = new Dictionary<string, Part>(StringComparer.Ordinal);
            foreach (Part part in export.Meshes)
            {
                meshes.Add(part.Role, MeshAsset(part));
                parts.Add(part.Role, part);
            }
            Dictionary<string, Material> materials = CreateMaterials();
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                meshes.Add(G_LinkGlyphRoles[i] + "Instances", GlyphInstances(meshes[G_LinkGlyphRoles[i]], i));
            }

            Scene original = SceneManager.GetActiveScene();
            Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            try
            {
                for (int i = 0; i < SIGN_COUNT; ++i)
                {
                    BuildWardPrefab(i, parts, meshes, materials);
                    BuildLinkPrefab(i, parts, meshes, materials);
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

        /// <summary> 0은 빨강 마름모, 1은 파랑 초승달 결계 프리팹이다 </summary>
        public static string WardPrefabPath(int sign)
        {
            ValidateSign(sign);
            return PREFABS + "/PF_WolbaekWard_" + G_Names[sign] + ".prefab";
        }

        /// <summary> 결계와 같은 색 순서의 연결선 프리팹 경로를 반환한다 </summary>
        public static string LinkPrefabPath(int sign)
        {
            ValidateSign(sign);
            return PREFABS + "/PF_WolbaekLink_" + G_Names[sign] + ".prefab";
        }

        private static void ValidateSign(int sign)
        {
            if ((sign < 0) || (sign >= SIGN_COUNT))
            {
                throw new ArgumentOutOfRangeException(nameof(sign));
            }
        }

        private static void GuardEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("PlayMode 중에는 결계 에셋을 생성하지 않습니다.");
            }
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if ((scene.path == SCENE) && (scene.isDirty))
                {
                    throw new InvalidOperationException("결계 검수 씬의 미저장 변경을 먼저 보존하세요.");
                }
            }
        }

        private static Export ReadValidatedExport()
        {
            string path = Path.Combine(EVIDENCE, GEOMETRY_FILE);
            RequireFile(path);
            Export export = JsonUtility.FromJson<Export>(File.ReadAllText(path));
            if ((export == null) || (export.Meshes == null) || (export.Meshes.Length != G_RequiredRoles.Length))
            {
                throw new InvalidDataException("결계 export의 " + G_RequiredRoles.Length + "개 필수 메시가 필요합니다.");
            }
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> roles = new HashSet<string>(StringComparer.Ordinal);
            foreach (Part part in export.Meshes)
            {
                ValidatePart(part);
                if ((!names.Add(part.Name)) || (!roles.Add(part.Role)))
                {
                    throw new InvalidDataException("중복 메시 이름 또는 역할: " + part.Name);
                }
            }
            foreach (string role in G_RequiredRoles)
            {
                if (!roles.Contains(role))
                {
                    throw new InvalidDataException("필수 결계 레이어 없음: " + role);
                }
            }
            return export;
        }

        private static void ValidatePart(Part part)
        {
            if ((part == null) || (string.IsNullOrWhiteSpace(part.Name)) || (string.IsNullOrEmpty(part.Role))
                || (!part.Name.StartsWith("SM_", StringComparison.Ordinal)) || (part.Name.Length > MAX_ASSET_NAME)
                || (part.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                || (part.Name.Contains("/")) || (part.Name.Contains("\\")) || (part.Name.EndsWith(".", StringComparison.Ordinal))
                || (part.Name.EndsWith(" ", StringComparison.Ordinal)) || (Array.IndexOf(G_RequiredRoles, part.Role) < 0))
            {
                throw new InvalidDataException("메시 이름 또는 역할이 잘못되었습니다.");
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
            HashSet<int> pieces = new HashSet<int>();
            int maxPiece = -1;
            for (int i = 0; i < count; ++i)
            {
                int piece = part.PieceIds[i];
                if ((piece < 0) || (piece >= MAX_PIECES))
                {
                    throw new InvalidDataException("조각 ID 범위 오류: " + part.Name);
                }
                pieces.Add(piece);
                maxPiece = Math.Max(maxPiece, piece);
                if (IsRibbon(part.Role))
                {
                    float along = part.Uv[i * UV_CHANNELS + 1];
                    if ((along < 0f) || (along > 1f))
                    {
                        throw new InvalidDataException("리본 길이 UV는 0부터 1 사이여야 합니다: " + part.Name);
                    }
                }
                int vertex = i * POSITION_CHANNELS;
                Vector3 position = new Vector3(part.Vertices[vertex], part.Vertices[vertex + 1], part.Vertices[vertex + 2]);
                if ((part.Role == "WardShell") || (part.Role == "WardRibbons") || (part.Role == "Boundary"))
                {
                    float radial = new Vector2(position.x, position.z).magnitude;
                    if ((radial > 1f + GEOMETRY_EPSILON) || (position.y < -GEOMETRY_EPSILON)
                        || (position.y > 1f + GEOMETRY_EPSILON))
                    {
                        throw new InvalidDataException("결계의 정규화 반경 또는 높이 범위 오류: " + part.Name);
                    }
                }
                if (((part.Role == "LinkRibbons") || (part.Role == "LinkSparks"))
                    && ((position.z < -GEOMETRY_EPSILON) || (position.z > 1f + GEOMETRY_EPSILON)))
                {
                    throw new InvalidDataException("연결선의 정규화 길이 범위 오류: " + part.Name);
                }
            }
            if (pieces.Count != maxPiece + 1)
            {
                throw new InvalidDataException("메시 조각 ID에 빈 구간이 있습니다: " + part.Name);
            }
            int expected = ExpectedPieces(part.Role);
            if ((expected > 0) && (pieces.Count != expected))
            {
                throw new InvalidDataException("메시 조각 수가 다릅니다: " + part.Name);
            }
            foreach (int triangle in part.Triangles)
            {
                if ((triangle < 0) || (triangle >= count))
                {
                    throw new InvalidDataException("삼각형 정점 참조 범위 오류: " + part.Name);
                }
            }
        }

        private static int ExpectedPieces(string role)
        {
            switch (role)
            {
                case "WardRibbons":
                case "Petals":
                case "WardMotes": return 0;
                case "LinkRibbons": return POSITION_CHANNELS;
                case "WardGlyphDiamond":
                case "WardGlyphMoon": return GLYPH_STATIONS;
                case "LinkSparks": return SPARK_PIECES;
                default: return 1;
            }
        }

        private static void ValidateFinite(float[] values, string channel)
        {
            foreach (float value in values)
            {
                if ((float.IsNaN(value)) || (float.IsInfinity(value)) || (Mathf.Abs(value) > DATA_BOUND))
                {
                    throw new InvalidDataException("유한한 메시 값이 필요합니다: " + channel);
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
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(MOTE_TEXTURE) == null)
            {
                throw new FileNotFoundException("기존 Warp 미립자 텍스처가 필요합니다.", MOTE_TEXTURE);
            }
            foreach (string name in G_TextureNames)
            {
                string path = EVIDENCE + "/" + SOURCE_TEXTURE_FOLDER + "/" + name;
                RequireFile(path);
                ValidateTexture(path);
            }
        }

        private static void ValidateTexture(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < PNG_HEADER_BYTES)
            {
                throw new InvalidDataException("원본 알파 마스크의 PNG 헤더가 잘못되었습니다: " + path);
            }
            for (int i = 0; i < G_PngSignature.Length; ++i)
            {
                if (bytes[i] != G_PngSignature[i])
                {
                    throw new InvalidDataException("원본 알파 마스크의 PNG 형식이 다릅니다: " + path);
                }
            }
            Texture2D check = new Texture2D(CHECK_TEXTURE_SIZE, CHECK_TEXTURE_SIZE, TextureFormat.RGBA32, false, true);
            try
            {
                if ((!ImageConversion.LoadImage(check, bytes, false)) || (check.width > MAX_TEXTURE_SIZE)
                    || (check.height > MAX_TEXTURE_SIZE))
                {
                    throw new InvalidDataException("원본 알파 마스크를 읽지 못했습니다: " + path);
                }
                Color32[] pixels = check.GetPixels32();
                for (int y = 0; y < check.height; ++y)
                {
                    for (int x = 0; x < check.width; ++x)
                    {
                        if (((x == 0) || (y == 0) || (x == check.width - 1) || (y == check.height - 1))
                            && (pixels[y * check.width + x].a != 0))
                        {
                            throw new InvalidDataException("원본 알파 마스크 외곽은 완전히 투명해야 합니다: " + path);
                        }
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(check);
            }
        }

        private static void ValidateAssetTargets(Export export)
        {
            foreach (Part part in export.Meshes)
            {
                ValidateTarget<Mesh>(ROOT + "/Models/" + part.Name + ".asset");
            }
            foreach (string role in G_RequiredRoles)
            {
                ValidateTarget<Material>(ROOT + "/Materials/M_Ward_" + role + ".mat");
            }
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                ValidateTarget<Mesh>(GlyphInstancesPath(i));
                ValidateTarget<WolbaekWardSettingsSO>(ROOT + "/SO_WolbaekWard_" + G_Names[i] + ".asset");
                ValidateTarget<WolbaekLinkSettingsSO>(ROOT + "/SO_WolbaekLink_" + G_Names[i] + ".asset");
                ValidateTarget<GameObject>(WardPrefabPath(i));
                ValidateTarget<GameObject>(LinkPrefabPath(i));
                ValidateTarget<Material>(ROOT + "/Materials/M_WardPreview_Player_" + G_Names[i] + ".mat");
            }
            ValidateTarget<VolumeProfile>(PROFILE_PATH);
            foreach (string name in G_TextureNames)
            {
                ValidateTarget<Texture2D>(ROOT + "/Textures/" + name);
            }
            ValidateTarget<SceneAsset>(SCENE);
            GameObject floor = AssetDatabase.LoadAssetAtPath<GameObject>(STONE_FLOOR);
            foreach (Renderer renderer in floor.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        throw new InvalidDataException("검수 바닥 재질 참조가 비어 있습니다.");
                    }
                    ValidateTarget<Material>(StageMaterialPath(material));
                }
            }
        }

        private static void ValidateTarget<T>(string path) where T : UnityEngine.Object
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullPath = Path.GetFullPath(Path.Combine(project, path));
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("에셋 저장 경로는 현재 프로젝트 Assets 아래여야 합니다: " + path);
            }
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (((existing != null) && (!(existing is T))) || ((existing == null) && (File.Exists(fullPath))))
            {
                throw new InvalidDataException("기존 파일 형식이 저장 대상과 다릅니다: " + path);
            }
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("결계 제작 의존 파일이 필요합니다.", path);
            }
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

        private static void CopyAndImportTextures()
        {
            foreach (string name in G_TextureNames)
            {
                string path = ROOT + "/Textures/" + name;
                File.Copy(EVIDENCE + "/" + SOURCE_TEXTURE_FOLDER + "/" + name, path, true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    throw new InvalidOperationException("알파 마스크 임포터가 없습니다: " + path);
                }
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.wrapModeV = name == G_TextureNames[1] ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, Material> CreateMaterials()
        {
            Texture2D veil = AssetDatabase.LoadAssetAtPath<Texture2D>(ROOT + "/Textures/" + G_TextureNames[0]);
            Texture2D flow = AssetDatabase.LoadAssetAtPath<Texture2D>(ROOT + "/Textures/" + G_TextureNames[1]);
            Texture2D mote = AssetDatabase.LoadAssetAtPath<Texture2D>(MOTE_TEXTURE);
            Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (string role in G_RequiredRoles)
            {
                Texture2D texture = role == "WardShell" ? veil : IsRibbon(role) ? flow
                    : ((role == "LinkSparks") || (role == "WardMotes")) ? mote : Texture2D.whiteTexture;
                string name = "M_Ward_" + role;
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
                material.SetFloat("_DstBlend", (float)(role == "WardShell" ? BlendMode.OneMinusSrcAlpha : BlendMode.One));
                material.SetTextureScale("_BaseMap", Vector2.one);
                material.SetTextureOffset("_BaseMap", Vector2.zero);
                material.renderQueue = (int)RenderQueue.Transparent;
                StoreAsset(material, path, fresh);
                materials.Add(role, material);
            }
            return materials;
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

        private static string GlyphInstancesPath(int sign)
        {
            return ROOT + "/Models/SK_Ward_LinkGlyph_" + G_Names[sign] + "_Instances.asset";
        }

        private static Mesh GlyphInstances(Mesh source, int sign)
        {
            // 일반 결합 대신 원본 채널을 복제해 네 지점 각각의 단일 뼈 가중치를 고정한다
            string path = GlyphInstancesPath(sign);
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = mesh == null;
            if (fresh)
            {
                mesh = new Mesh();
            }
            mesh.Clear();
            mesh.name = "SK_Ward_LinkGlyph_" + G_Names[sign] + "_Instances";
            mesh.indexFormat = IndexFormat.UInt32;
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector2[] sourceUv = source.uv;
            Color[] sourceColors = source.colors;
            int[] sourceTriangles = source.triangles;
            Vector3[] vertices = new Vector3[sourceVertices.Length * GLYPH_STATIONS];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            Color[] colors = new Color[vertices.Length];
            BoneWeight[] weights = new BoneWeight[vertices.Length];
            int[] triangles = new int[sourceTriangles.Length * GLYPH_STATIONS];
            for (int station = 0; station < GLYPH_STATIONS; ++station)
            {
                float along = (station + 1f) / (GLYPH_STATIONS + 1f);
                int offset = station * sourceVertices.Length;
                for (int i = 0; i < sourceVertices.Length; ++i)
                {
                    int index = offset + i;
                    vertices[index] = sourceVertices[i] + Vector3.forward * along;
                    normals[index] = sourceNormals[i];
                    uv[index] = sourceUv[i];
                    colors[index] = sourceColors[i];
                    weights[index] = new BoneWeight
                    {
                        boneIndex0 = station,
                        weight0 = 1f
                    };
                }
                for (int i = 0; i < sourceTriangles.Length; ++i)
                {
                    triangles[station * sourceTriangles.Length + i] = offset + sourceTriangles[i];
                }
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.boneWeights = weights;
            mesh.RecalculateBounds();
            StoreAsset(mesh, path, fresh);
            return mesh;
        }

        private static WolbaekWardSettingsSO WardSettings(int sign)
        {
            string path = ROOT + "/SO_WolbaekWard_" + G_Names[sign] + ".asset";
            WolbaekWardSettingsSO settings = AssetDatabase.LoadAssetAtPath<WolbaekWardSettingsSO>(path);
            if (settings != null)
            {
                return settings;
            }
            settings = ScriptableObject.CreateInstance<WolbaekWardSettingsSO>();
            ConfigureDefaults(settings, sign);
            AssetDatabase.CreateAsset(settings, path);
            return settings;
        }

        private static WolbaekLinkSettingsSO LinkSettings(int sign)
        {
            string path = ROOT + "/SO_WolbaekLink_" + G_Names[sign] + ".asset";
            WolbaekLinkSettingsSO settings = AssetDatabase.LoadAssetAtPath<WolbaekLinkSettingsSO>(path);
            if (settings != null)
            {
                return settings;
            }
            settings = ScriptableObject.CreateInstance<WolbaekLinkSettingsSO>();
            ConfigureDefaults(settings, sign);
            AssetDatabase.CreateAsset(settings, path);
            return settings;
        }

        private static void ConfigureDefaults(ScriptableObject settings, int sign)
        {
            SerializedObject serialized = new SerializedObject(settings);
            serialized.FindProperty("_defaultSign").enumValueIndex = sign;
            serialized.FindProperty("_defaultColor").colorValue = G_Colors[sign];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildWardPrefab(int sign, Dictionary<string, Part> parts,
            Dictionary<string, Mesh> meshes, Dictionary<string, Material> materials)
        {
            GameObject root = new GameObject("PF_WolbaekWard_" + G_Names[sign]);
            root.SetActive(false);
            try
            {
                WolbaekWardPlayer player = root.AddComponent<WolbaekWardPlayer>();
                Transform shapeRoot = Child(root.transform, "Normalized_Dome");
                Transform radiusRoot = Child(root.transform, "Authoritative_Radius");
                List<Renderer> layers = new List<Renderer>();
                List<int> kinds = new List<int>();
                List<Transform> bones = new List<Transform>();
                List<Vector3> rest = new List<Vector3>();
                List<Vector4> motion = new List<Vector4>();
                Renderer shell = StaticRenderer(shapeRoot, parts["WardShell"].Name,
                    meshes["WardShell"], materials["WardShell"]);
                layers.Add(shell);
                kinds.Add(WARD_SHELL_LAYER);
                AddSkin("WardRibbons", shapeRoot, WARD_RIBBON_LAYER, RIBBON_MOTION, parts, meshes, materials, layers, kinds, bones, rest, motion);
                AddSkin("Petals", shapeRoot, WARD_PETAL_LAYER, PETAL_MOTION, parts, meshes, materials, layers, kinds, bones, rest, motion);
                AddSkin("WardMotes", shapeRoot, WARD_MOTE_LAYER, WARD_MOTE_MOTION, parts, meshes, materials, layers, kinds, bones, rest, motion);
                Renderer[] glyphs = new Renderer[SIGN_COUNT];
                Renderer[] marks = new Renderer[SIGN_COUNT];
                for (int i = 0; i < SIGN_COUNT; ++i)
                {
                    string glyph = G_WardGlyphRoles[i];
                    string mark = G_MarkGlyphRoles[i];
                    glyphs[i] = StaticRenderer(shapeRoot, parts[glyph].Name, meshes[glyph], materials[glyph]);
                    marks[i] = StaticRenderer(radiusRoot, "Confirmed_Current_" + parts[mark].Name, meshes[mark], materials[mark]);
                }
                Renderer boundary = StaticRenderer(radiusRoot, "Exact_Current_Boundary", meshes["Boundary"], materials["Boundary"]);
                CreateDecorativeLOD(shapeRoot, layers);
                SerializedObject serialized = new SerializedObject(player);
                serialized.FindProperty("_settings").objectReferenceValue = WardSettings(sign);
                serialized.FindProperty("_shapeRoot").objectReferenceValue = shapeRoot;
                serialized.FindProperty("_radiusRoot").objectReferenceValue = radiusRoot;
                serialized.FindProperty("_boundary").objectReferenceValue = boundary;
                SetReferences(serialized.FindProperty("_glyphs"), glyphs);
                SetReferences(serialized.FindProperty("_markGlyphs"), marks);
                SetRig(serialized, layers, kinds, bones, rest, motion);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                SavePrefab(root, WardPrefabPath(sign));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildLinkPrefab(int sign, Dictionary<string, Part> parts,
            Dictionary<string, Mesh> meshes, Dictionary<string, Material> materials)
        {
            GameObject root = new GameObject("PF_WolbaekLink_" + G_Names[sign]);
            root.SetActive(false);
            try
            {
                WolbaekLinkPlayer player = root.AddComponent<WolbaekLinkPlayer>();
                Transform lengthRoot = Child(root.transform, "Normalized_Link_Length");
                Transform glyphRoot = Child(root.transform, "Unstretched_Link_Glyphs");
                List<Renderer> layers = new List<Renderer>();
                List<int> kinds = new List<int>();
                List<Transform> bones = new List<Transform>();
                List<Vector3> rest = new List<Vector3>();
                List<Vector4> motion = new List<Vector4>();
                AddSkin("LinkRibbons", lengthRoot, LINK_RIBBON_LAYER, RIBBON_MOTION, parts, meshes, materials, layers, kinds, bones, rest, motion);
                AddSkin("LinkSparks", glyphRoot, LINK_MOTE_LAYER, LINK_MOTE_MOTION, parts, meshes, materials, layers, kinds, bones, rest, motion);
                Transform[] glyphBones = new Transform[GLYPH_STATIONS];
                for (int i = 0; i < GLYPH_STATIONS; ++i)
                {
                    float along = (i + 1f) / (GLYPH_STATIONS + 1f);
                    glyphBones[i] = Child(glyphRoot, "Glyph_Station_" + i);
                    glyphBones[i].localPosition = Vector3.forward * along;
                    bones.Add(glyphBones[i]);
                    rest.Add(glyphBones[i].localPosition);
                    motion.Add(new Vector4(LINK_GLYPH_MOTION, 1f, i * GOLDEN_PHASE, 1f));
                }
                Renderer[] glyphs = new Renderer[SIGN_COUNT];
                for (int i = 0; i < SIGN_COUNT; ++i)
                {
                    string role = G_LinkGlyphRoles[i];
                    GameObject layer = Child(glyphRoot, "Repeated_" + role).gameObject;
                    Mesh mesh = meshes[role + "Instances"];
                    Matrix4x4[] bind = new Matrix4x4[GLYPH_STATIONS];
                    for (int j = 0; j < GLYPH_STATIONS; ++j)
                    {
                        bind[j] = glyphBones[j].worldToLocalMatrix * layer.transform.localToWorldMatrix;
                    }
                    mesh.bindposes = bind;
                    EditorUtility.SetDirty(mesh);
                    SkinnedMeshRenderer renderer = layer.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh;
                    renderer.bones = glyphBones;
                    renderer.rootBone = glyphRoot;
                    renderer.quality = SkinQuality.Bone1;
                    Bounds bounds = mesh.bounds;
                    bounds.Expand(_linkBoundsPadding);
                    renderer.localBounds = bounds;
                    renderer.sharedMaterial = materials[role];
                    ConfigureRenderer(renderer);
                    glyphs[i] = renderer;
                }
                Transform endpointA = Child(root.transform, "Feet_Endpoint_A");
                Transform endpointB = Child(root.transform, "Feet_Endpoint_B");
                Renderer[] endpoints =
                {
                    StaticRenderer(endpointA, "Endpoint_Ring_A", meshes["EndpointRings"], materials["EndpointRings"]),
                    StaticRenderer(endpointB, "Endpoint_Ring_B", meshes["EndpointRings"], materials["EndpointRings"])
                };
                CreateDecorativeLOD(lengthRoot, new[]
                {
                    layers[LINK_RIBBON_LAYER]
                });
                SerializedObject serialized = new SerializedObject(player);
                serialized.FindProperty("_settings").objectReferenceValue = LinkSettings(sign);
                serialized.FindProperty("_lengthRoot").objectReferenceValue = lengthRoot;
                serialized.FindProperty("_glyphRoot").objectReferenceValue = glyphRoot;
                serialized.FindProperty("_endpointA").objectReferenceValue = endpointA;
                serialized.FindProperty("_endpointB").objectReferenceValue = endpointB;
                SetReferences(serialized.FindProperty("_glyphs"), glyphs);
                SetReferences(serialized.FindProperty("_endpointLayers"), endpoints);
                SetRig(serialized, layers, kinds, bones, rest, motion);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                SavePrefab(root, LinkPrefabPath(sign));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void SavePrefab(GameObject root, string path)
        {
            // 저장 직전 렌더러를 다시 숨겨 Awake/OnDisable 정리가 흰 초기 재질을 노출하지 않게 한다
            root.SetActive(true);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved)
            {
                throw new InvalidOperationException("결계 프리팹 저장 실패: " + path);
            }
        }

        private static bool IsRibbon(string role)
        {
            return (role == "WardRibbons") || (role == "LinkRibbons");
        }

        private static int PieceCount(Part part)
        {
            int count = 0;
            foreach (int piece in part.PieceIds)
            {
                count = Math.Max(count, piece + 1);
            }
            return count;
        }

        private static void AddSkin(string role, Transform parent, int layerKind, int motionKind,
            Dictionary<string, Part> parts, Dictionary<string, Mesh> meshes, Dictionary<string, Material> materials,
            List<Renderer> layers, List<int> kinds, List<Transform> bones, List<Vector3> rest, List<Vector4> motion)
        {
            GameObject layer = Child(parent, parts[role].Name).gameObject;
            SkinnedMeshRenderer renderer = Skin(layer, meshes[role], parts[role], motionKind, bones, rest, motion);
            renderer.sharedMaterial = materials[role];
            ConfigureRenderer(renderer);
            layers.Add(renderer);
            kinds.Add(layerKind);
        }

        private static SkinnedMeshRenderer Skin(GameObject layer, Mesh mesh, Part part, int kind,
            List<Transform> bones, List<Vector3> rest, List<Vector4> motion)
        {
            int stations = IsRibbon(part.Role) ? FLOW_STATIONS : 1;
            int count = PieceCount(part) * stations;
            Vector3[] centers = new Vector3[count];
            float[] sums = new float[count];
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
                    boneIndex0 = first, boneIndex1 = second, weight0 = 1f - mix, weight1 = mix
                };
            }
            Transform[] local = new Transform[count];
            Matrix4x4[] bind = new Matrix4x4[count];
            for (int i = 0; i < count; ++i)
            {
                centers[i] /= Mathf.Max(MIN_SUM, sums[i]);
                local[i] = Child(layer.transform, "Flow_" + i);
                local[i].localPosition = centers[i];
                bind[i] = local[i].worldToLocalMatrix * layer.transform.localToWorldMatrix;
                bones.Add(local[i]);
                rest.Add(centers[i]);
                int piece = i / stations;
                int station = i % stations;
                // 양끝은 고정점이다. Sin(pi)의 음수 오차가 유효 리그의 진폭 검사를 깨뜨리지 않게 한다
                float amplitude = stations == 1 ? (part.Role == "WardMotes" ? WARD_MOTE_AMPLITUDE : 1f)
                    : ((station == 0) || (station == stations - 1))
                    ? 0f : Mathf.Max(0f, Mathf.Sin(station / (float)(stations - 1) * Mathf.PI));
                motion.Add(new Vector4(kind, amplitude, piece * GOLDEN_PHASE + i % stations * STATION_PHASE,
                    piece % UV_CHANNELS == 0 ? 1f : -1f));
            }
            mesh.boneWeights = weights;
            mesh.bindposes = bind;
            EditorUtility.SetDirty(mesh);
            SkinnedMeshRenderer renderer = layer.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = local;
            renderer.rootBone = layer.transform;
            renderer.quality = stations > 1 ? SkinQuality.Bone2 : SkinQuality.Bone1;
            Bounds bounds = mesh.bounds;
            bounds.Expand(part.Role.StartsWith("Link", StringComparison.Ordinal) ? _linkBoundsPadding : _wardBoundsPadding);
            renderer.localBounds = bounds;
            return renderer;
        }

        private static Renderer StaticRenderer(Transform parent, string name, Mesh mesh, Material material)
        {
            GameObject layer = Child(parent, name).gameObject;
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigureRenderer(renderer);
            return renderer;
        }

        private static void ConfigureRenderer(Renderer renderer)
        {
            renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static void CreateDecorativeLOD(Transform root, IList<Renderer> renderers)
        {
            if ((root == null) || (renderers == null) || (renderers.Count == 0))
            {
                throw new InvalidOperationException("장식 LOD는 같은 리그 아래의 렌더러가 필요합니다.");
            }
            Renderer[] owned = new Renderer[renderers.Count];
            for (int i = 0; i < renderers.Count; ++i)
            {
                if ((renderers[i] == null) || (!renderers[i].transform.IsChildOf(root)))
                {
                    throw new InvalidOperationException("장식 LOD 렌더러는 소유 루트 아래에 있어야 합니다.");
                }
                owned[i] = renderers[i];
            }
            // 경계와 문양은 목록에서 제외해 화면 크기와 관계없이 의미 표식을 남긴다
            LODGroup group = root.gameObject.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(DECORATIVE_CULL, owned)
            });
            group.RecalculateBounds();
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static void SetReferences<T>(SerializedProperty property, IList<T> references) where T : UnityEngine.Object
        {
            property.arraySize = references.Count;
            for (int i = 0; i < references.Count; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = references[i];
            }
        }

        private static void SetRig(SerializedObject serialized, List<Renderer> layers, List<int> kinds,
            List<Transform> bones, List<Vector3> rest, List<Vector4> motion)
        {
            SetReferences(serialized.FindProperty("_layers"), layers);
            SetReferences(serialized.FindProperty("_bones"), bones);
            SerializedProperty layerKinds = serialized.FindProperty("_layerKinds");
            SerializedProperty poses = serialized.FindProperty("_rest");
            SerializedProperty movements = serialized.FindProperty("_motion");
            layerKinds.arraySize = kinds.Count;
            poses.arraySize = rest.Count;
            movements.arraySize = motion.Count;
            for (int i = 0; i < kinds.Count; ++i)
            {
                layerKinds.GetArrayElementAtIndex(i).intValue = kinds[i];
            }
            for (int i = 0; i < rest.Count; ++i)
            {
                poses.GetArrayElementAtIndex(i).vector3Value = rest[i];
                movements.GetArrayElementAtIndex(i).vector4Value = motion[i];
            }
        }

        private static void StoreAsset(UnityEngine.Object asset, string path, bool fresh)
        {
            if (fresh)
            {
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }
        }

        private static VolumeProfile PreviewProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PROFILE_PATH);
            if (profile != null)
            {
                return profile;
            }
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PROFILE_PATH);
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
                        throw new InvalidOperationException("기존 결계 검수 씬을 닫지 못했습니다.");
                    }
                    break;
                }
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                singleTarget ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            bool saved = false;
            try
            {
                BuildStage(scene);
                Light light = new GameObject("Ward_Preview_KeyLight").AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = _previewLightColor;
                light.intensity = LIGHT_INTENSITY;
                light.transform.rotation = Quaternion.Euler(_lightRotation);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = _previewAmbientSkyColor;
                RenderSettings.ambientEquatorColor = _previewAmbientEquatorColor;
                RenderSettings.ambientGroundColor = _previewAmbientGroundColor;
                Camera camera = new GameObject("Ward_GameplayCamera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = _cameraPosition;
                camera.transform.LookAt(Vector3.zero);
                camera.fieldOfView = CAMERA_FOV;
                camera.nearClipPlane = CAMERA_NEAR;
                camera.farClipPlane = CAMERA_FAR;
                camera.allowHDR = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = _previewBackgroundColor;
                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.requiresDepthTexture = true;
                cameraData.renderPostProcessing = true;
                Volume volume = new GameObject("Ward_Restrained_Bloom").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = PreviewProfile();
                WolbaekWardPlayer[] wards = new WolbaekWardPlayer[SIGN_COUNT];
                WolbaekLinkPlayer[] links = new WolbaekLinkPlayer[SIGN_COUNT];
                Transform[] players = new Transform[PLAYER_COUNT];
                for (int i = 0; i < SIGN_COUNT; ++i)
                {
                    Vector3 center = Vector3.right * (i == 0 ? -WARD_SEPARATION / SIGN_COUNT : WARD_SEPARATION / SIGN_COUNT);
                    GameObject ward = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WardPrefabPath(i)), scene);
                    ward.name = "Ward_" + G_Names[i];
                    ward.transform.position = center;
                    wards[i] = ward.GetComponent<WolbaekWardPlayer>();
                    GameObject link = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(LinkPrefabPath(i)), scene);
                    link.name = "Same_Color_Link_" + G_Names[i];
                    links[i] = link.GetComponent<WolbaekLinkPlayer>();
                    for (int member = 0; member < SIGN_COUNT; ++member)
                    {
                        Transform anchor = new GameObject("Reference_Player_" + G_Names[i] + "_" + member).transform;
                        anchor.position = center + Vector3.forward * (member == 0 ? -CHARACTER_GAP : CHARACTER_GAP);
                        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        body.name = "Reference_Mannequin";
                        body.transform.SetParent(anchor, false);
                        body.transform.localPosition = Vector3.up * CHARACTER_HEIGHT;
                        body.transform.localScale = new Vector3(CHARACTER_WIDTH, CHARACTER_HEIGHT, CHARACTER_WIDTH);
                        UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
                        body.GetComponent<Renderer>().sharedMaterial = CharacterMaterial(i);
                        players[i * SIGN_COUNT + member] = anchor;
                    }
                }
                WolbaekWardPreview preview = new GameObject("Development_External_Ward_Cues").AddComponent<WolbaekWardPreview>();
                SerializedObject serialized = new SerializedObject(preview);
                SetReferences(serialized.FindProperty("_wards"), wards);
                SetReferences(serialized.FindProperty("_links"), links);
                SetReferences(serialized.FindProperty("_players"), players);
                serialized.FindProperty("_camera").objectReferenceValue = camera;
                serialized.FindProperty("_radius").floatValue = PREVIEW_RADIUS;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                saved = EditorSceneManager.SaveScene(scene, SCENE);
                if (!saved)
                {
                    throw new InvalidOperationException("결계 검수 씬 저장 실패");
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
                    tile.transform.localPosition = new Vector3(x * FLOOR_SPACING, FLOOR_HEIGHT, z * FLOOR_SPACING);
                    foreach (Renderer renderer in tile.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] originals = renderer.sharedMaterials;
                        Material[] owned = new Material[originals.Length];
                        for (int i = 0; i < originals.Length; ++i)
                        {
                            owned[i] = StageMaterial(originals[i]);
                        }
                        renderer.sharedMaterials = owned;
                    }
                    foreach (Collider collider in tile.GetComponentsInChildren<Collider>(true))
                    {
                        UnityEngine.Object.DestroyImmediate(collider);
                    }
                }
            }
        }

        private static string StageMaterialPath(Material source)
        {
            return ROOT + "/Materials/M_WardPreview_" + source.name + ".mat";
        }

        private static Material StageMaterial(Material source)
        {
            string path = StageMaterialPath(source);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = material == null;
            if (fresh)
            {
                material = new Material(source);
            }
            material.CopyPropertiesFromMaterial(source);
            material.name = "M_WardPreview_" + source.name;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", source.GetColor("_BaseColor") * _previewStageTint);
            }
            StoreAsset(material, path, fresh);
            return material;
        }

        private static Material CharacterMaterial(int sign)
        {
            string path = ROOT + "/Materials/M_WardPreview_Player_" + G_Names[sign] + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = material == null;
            if (fresh)
            {
                material = new Material(Shader.Find(LIT_SHADER));
            }
            material.name = "M_WardPreview_Player_" + G_Names[sign];
            material.SetColor("_BaseColor", Color.Lerp(_previewPlayerColor, G_Colors[sign], CHARACTER_TINT));
            material.SetFloat("_Smoothness", CHARACTER_SMOOTH);
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
