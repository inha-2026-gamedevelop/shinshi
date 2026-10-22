// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Editor
{
    /// <summary> 궁수 기본 공격 화살을 뾰족한 바늘로 바꾸고, 콤보 3타 강공격의 총구, 화살, 적중 효과 프리팹을 만든다. 기존 릴리즈 발광 재질에 입자를 많이 얹은 금빛 효과다 </summary>
    public static class ArcherHeavyVfxBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH     = "Shinshi/VFX/Build Archer Heavy Shot";
        private const string NEEDLE_FOLDER = "Assets/02.Prefab/VFX/Archer/Meshes";
        private const string NEEDLE_MESH   = "Assets/02.Prefab/VFX/Archer/Meshes/SM_ArcherNeedle.asset";
        private const string FOLDER        = "Assets/02.Prefab/VFX/Archer/";
        private const string PLAYER_PREFAB = "Assets/02.Prefab/Network/NetPlayer/PF_NetPlayer.prefab";
        private const string GLOW_MATERIAL = "Assets/02.Prefab/VFX/Archer/Materials/M_ReleaseGlow.mat";
        private const string BASE_ARROW    = "Assets/02.Prefab/VFX/Archer/PF_ArcherVfx_Arrow.prefab";
        private static readonly string[] G_ArrowPaths =
        {
            "Assets/02.Prefab/VFX/Archer/PF_ArcherVfx_Arrow.prefab", "Assets/02.Prefab/VFX/Archer/PF_ArcherVfx_IceArrow.prefab",
            "Assets/02.Prefab/VFX/Archer/PF_ArcherVfx_WindArrow.prefab", "Assets/02.Prefab/VFX/Archer/PF_ArcherVfx_FireArrow.prefab"
        };
        private const string BASE_IMPACT   = "Assets/02.Prefab/VFX/Archer/PF_ArcherVfx_Impact.prefab";
        private const string MUZZLE_PATH   = "Assets/02.Prefab/VFX/Archer/PF_ArcherVfx_HeavyMuzzle.prefab";
        private const string ARROW_PATH    = "Assets/02.Prefab/VFX/Archer/PF_ArcherVfx_HeavyArrow.prefab";
        private const string HIT_PATH      = "Assets/02.Prefab/VFX/Archer/PF_ArcherVfx_HeavyHit.prefab";

        // 바늘: 앞 끝이 뾰족하고 머리 쪽이 가장 굵으며 뒤로 가늘게 사라진다. 단면은 가로와 세로 두 장의 마름모다
        private const float  NEEDLE_TIP_Z     = 0.9f;
        private const float  NEEDLE_HEAD_Z    = 0.55f;
        private const float  NEEDLE_TAIL_Z    = -0.6f;
        private const float  NEEDLE_HALF      = 0.03f;
        private const float  UV_CENTER        = 0.5f;
        private const string ARROW_BODY_NAME  = "Effect";
        private const string TRAIL_NAME       = "Wake";
        private const float  BODY_HEIGHT      = 1f; // m, 화살 몸통이 지면 위로 떠 있는 높이. 꼬리 입자도 이 높이에서 나와야 화살을 따라간다
        private const float  ARROW_BODY_SCALE = 1.6f;
        private const float  FADE_IN_KEY      = 0.1f;
        private const float QUARTER_TURN = 90f;

        private static readonly Color _needleColor = new Color(0.82f, 1f, 0.93f, 1f);
        private static readonly Color _whiteGold = new Color(1f, 0.96f, 0.78f, 1f);
        private static readonly Color _gold       = new Color(1f, 0.78f, 0.25f, 1f);
        private static readonly Color _orange     = new Color(1f, 0.46f, 0.1f, 1f);

        /// <summary> 한 입자 계통의 값, 만든 뒤 모듈에 옮긴다 </summary>
        private struct Layer
        {
            public string                      Name;
            public ParticleSystemRenderMode    Render;
            public bool                        World;
            public float                       Duration;
            public bool                        Loop;
            public Vector2                     Life;
            public Vector2                     Speed;
            public Vector2                     Size;
            public float                       Gravity;
            public float                       Rate;
            public int                         BurstCount;
            public ParticleSystemShapeType     Shape;
            public float                       ShapeRadius;
            public float                       ShapeAngle;
            public Quaternion                  ShapeRotation;
            public Color                       Start;
            public Color                       End;
            public float                       SizeFrom;
            public float                       SizeTo;
            public float                       StretchLength;
            public float                       StretchSpeed;
            public float                       RiseSpeed;
        }

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void BuildAll()
        {
            Material glow = AssetDatabase.LoadAssetAtPath<Material>(GLOW_MATERIAL);
            if (glow == null)
            {
                Debug.LogError("[ArcherHeavyVfx] 발광 재질을 찾지 못했다: " + GLOW_MATERIAL);
                return;
            }

            AttachTrailsToBody();
            BuildNeedle();
            BuildMuzzle(glow);
            BuildArrow(glow);
            BuildHit(glow);
            WirePlayer();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ArcherHeavyVfx] 강공격 효과 프리팹 3개를 만들었다");
        }

        private static void WirePlayer()
        {
            GameObject player = PrefabUtility.LoadPrefabContents(PLAYER_PREFAB);
            try
            {
                ArcherVfxView view = player.GetComponent<ArcherVfxView>();
                var serialized = new SerializedObject(view);
                serialized.FindProperty("_heavyArrowPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ARROW_PATH);
                serialized.FindProperty("_heavyMuzzlePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(MUZZLE_PATH);
                serialized.FindProperty("_heavyHitPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(HIT_PATH);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, PLAYER_PREFAB);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }
        }

        // 화살 꼬리 입자는 프리팹 뿌리(지면)에 있어 땅바닥을 따라 흘렀다. 몸통과 같은 높이로 올려 화살에 붙인다
        private static void AttachTrailsToBody()
        {
            for (int i = 0; i < G_ArrowPaths.Length; ++i)
            {
                GameObject prefab = PrefabUtility.LoadPrefabContents(G_ArrowPaths[i]);
                try
                {
                    Transform trail = prefab.transform.Find(TRAIL_NAME);
                    if (trail != null)
                    {
                        Vector3 position = trail.localPosition;
                        position.y = BODY_HEIGHT;
                        trail.localPosition = position;
                        PrefabUtility.SaveAsPrefabAsset(prefab, G_ArrowPaths[i]);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefab);
                }
            }
        }

        // 기본 화살 프리팹의 몸통 모양을 바늘 메시로 바꾼다
        private static void BuildNeedle()
        {
            if (!AssetDatabase.IsValidFolder(NEEDLE_FOLDER))
            {
                AssetDatabase.CreateFolder("Assets/02.Prefab/VFX/Archer", "Meshes");
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(NEEDLE_MESH);
            bool isNew = mesh == null;
            if (isNew)
            {
                mesh = new Mesh { name = "SM_ArcherNeedle" };
            }

            mesh.Clear();
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, NEEDLE_TIP_Z), new Vector3(0f, 0f, NEEDLE_TAIL_Z),
                new Vector3(-NEEDLE_HALF, 0f, NEEDLE_HEAD_Z), new Vector3(NEEDLE_HALF, 0f, NEEDLE_HEAD_Z),
                new Vector3(0f, -NEEDLE_HALF, NEEDLE_HEAD_Z), new Vector3(0f, NEEDLE_HALF, NEEDLE_HEAD_Z)
            };
            mesh.triangles = new[] { 0, 2, 3, 1, 3, 2, 0, 4, 5, 1, 5, 4 };

            var uv = new Vector2[mesh.vertexCount];
            for (int i = 0; i < uv.Length; ++i)
            {
                uv[i] = new Vector2(UV_CENTER, UV_CENTER);
            }

            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (isNew)
            {
                AssetDatabase.CreateAsset(mesh, NEEDLE_MESH);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            GameObject prefab = PrefabUtility.LoadPrefabContents(BASE_ARROW);
            try
            {
                Transform body = prefab.transform.Find(ARROW_BODY_NAME);
                if (body != null)
                {
                    body.GetComponent<ParticleSystemRenderer>().mesh = mesh;
                    ParticleSystem.MainModule main = body.GetComponent<ParticleSystem>().main;
                    main.startColor = _needleColor;
                }

                PrefabUtility.SaveAsPrefabAsset(prefab, BASE_ARROW);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        // 총구: 번쩍임, 앞으로 뻗는 선, 불꽃 다발, 퍼지는 빛 원반, 떠오르는 불티
        private static void BuildMuzzle(Material glow)
        {
            var root = new GameObject("PF_ArcherVfx_HeavyMuzzle");
            AddLayer(root.transform, glow, new Layer
            {
                Name = "Flash", Render = ParticleSystemRenderMode.Billboard, Duration = 0.5f, Life = new Vector2(0.14f, 0.14f),
                Size = new Vector2(3.2f, 3.2f), BurstCount = 1, Start = _whiteGold, End = _gold, SizeFrom = 0.4f, SizeTo = 1f
            });
            AddLayer(root.transform, glow, new Layer
            {
                Name = "Streak", Render = ParticleSystemRenderMode.Stretch, Duration = 0.5f, Life = new Vector2(0.08f, 0.14f),
                Speed = new Vector2(30f, 45f), Size = new Vector2(0.1f, 0.16f), BurstCount = 8, Shape = ParticleSystemShapeType.Cone,
                ShapeRadius = 0.03f, ShapeAngle = 4f, Start = _whiteGold, End = _gold, SizeFrom = 1f, SizeTo = 0.3f, StretchLength = 7f
            });
            AddLayer(root.transform, glow, new Layer
            {
                Name = "Sparks", Render = ParticleSystemRenderMode.Stretch, Duration = 0.5f, Life = new Vector2(0.22f, 0.42f),
                Speed = new Vector2(9f, 18f), Size = new Vector2(0.07f, 0.14f), BurstCount = 48, Shape = ParticleSystemShapeType.Cone,
                ShapeRadius = 0.04f, ShapeAngle = 24f, Start = _gold, End = _orange, SizeFrom = 1f, SizeTo = 0.2f, StretchLength = 2.2f,
                StretchSpeed = 0.12f
            });
            AddLayer(root.transform, glow, new Layer
            {
                Name = "Disc", Render = ParticleSystemRenderMode.HorizontalBillboard, Duration = 0.5f, Life = new Vector2(0.32f, 0.32f),
                Size = new Vector2(1.2f, 1.2f), BurstCount = 1, Start = new Color(_gold.r, _gold.g, _gold.b, 0.4f), End = _orange, SizeFrom = 0.3f, SizeTo = 4f
            });
            AddLayer(root.transform, glow, new Layer
            {
                Name = "Embers", Render = ParticleSystemRenderMode.Billboard, Duration = 0.5f, Life = new Vector2(0.5f, 1f),
                Speed = new Vector2(0.5f, 2.2f), Size = new Vector2(0.05f, 0.1f), BurstCount = 28, Shape = ParticleSystemShapeType.Sphere,
                ShapeRadius = 0.5f, Start = _gold, End = _orange, SizeFrom = 1f, SizeTo = 0.2f, RiseSpeed = 0.8f
            });

            Save(root, MUZZLE_PATH);
        }

        // 화살: 기본 화살 모양 위에 머리 빛, 불꽃 흐름, 빛 꼬리, 불티를 얹는다. 흐름은 월드 공간이라 지나간 자리에 남는다
        private static void BuildArrow(Material glow)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BASE_ARROW));
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "PF_ArcherVfx_HeavyArrow";

            // 기본 화살 몸통은 금빛으로 바꾸고 크게 키운다
            Transform body = root.transform.Find(ARROW_BODY_NAME);
            if (body != null)
            {
                ParticleSystem.MainModule bodyMain = body.GetComponent<ParticleSystem>().main;
                bodyMain.startColor = _whiteGold;
                bodyMain.startSizeMultiplier *= ARROW_BODY_SCALE;
            }

            AddLayer(root.transform, glow, new Layer
            {
                Name = "HeadGlow", Render = ParticleSystemRenderMode.Billboard, Duration = 1f, Loop = true, Life = new Vector2(0.12f, 0.12f),
                Size = new Vector2(0.55f, 0.8f), Rate = 90f, Start = _whiteGold, End = _gold, SizeFrom = 1f, SizeTo = 0.3f
            });
            AddLayer(root.transform, glow, new Layer
            {
                Name = "SparkStream", Render = ParticleSystemRenderMode.Stretch, World = true, Duration = 1f, Loop = true,
                Life = new Vector2(0.25f, 0.45f), Speed = new Vector2(0.5f, 2.2f), Size = new Vector2(0.05f, 0.1f), Rate = 140f,
                Shape = ParticleSystemShapeType.Sphere, ShapeRadius = 0.05f, Start = _gold, End = _orange, SizeFrom = 1f, SizeTo = 0.1f,
                StretchLength = 1.5f, StretchSpeed = 0.1f
            });
            AddLayer(root.transform, glow, new Layer
            {
                Name = "GlowTail", Render = ParticleSystemRenderMode.Billboard, World = true, Duration = 1f, Loop = true,
                Life = new Vector2(0.35f, 0.35f), Size = new Vector2(0.5f, 0.5f), Rate = 60f,
                Start = new Color(_gold.r, _gold.g, _gold.b, 0.35f), End = _orange, SizeFrom = 1f, SizeTo = 0.3f
            });
            AddLayer(root.transform, glow, new Layer
            {
                Name = "EmberDrift", Render = ParticleSystemRenderMode.Billboard, World = true, Duration = 1f, Loop = true,
                Life = new Vector2(0.6f, 0.9f), Speed = new Vector2(0.1f, 0.5f), Size = new Vector2(0.04f, 0.08f), Rate = 25f,
                Shape = ParticleSystemShapeType.Sphere, ShapeRadius = 0.12f, Start = _gold, End = _orange, SizeFrom = 1f, SizeTo = 0.2f,
                RiseSpeed = 1f
            });

            // 화살 몸통과 같은 높이로 올려 입자가 화살에서 나오게 한다
            for (int i = 0; i < root.transform.childCount; ++i)
            {
                Transform child = root.transform.GetChild(i);
                Vector3   position = child.localPosition;
                position.y = BODY_HEIGHT;
                child.localPosition = position;
            }

            Save(root, ARROW_PATH);
        }

        // 적중: 큰 번쩍임, 퍼지는 빛 원반, 사방 불꽃, 빛 구름, 솟는 불티
        private static void BuildHit(Material glow)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BASE_IMPACT));
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "PF_ArcherVfx_HeavyHit";

            AddLayer(root.transform, glow, new Layer
            {
                Name = "Flash", Render = ParticleSystemRenderMode.Billboard, Duration = 1f, Life = new Vector2(0.16f, 0.16f),
                Size = new Vector2(4f, 4f), BurstCount = 1, Start = _whiteGold, End = _gold, SizeFrom = 0.4f, SizeTo = 1f
            });
            AddLayer(root.transform, glow, new Layer
            {
                Name = "Shockwave", Render = ParticleSystemRenderMode.HorizontalBillboard, Duration = 1f, Life = new Vector2(0.4f, 0.4f),
                Size = new Vector2(7f, 7f), BurstCount = 1, Start = new Color(_gold.r, _gold.g, _gold.b, 0.5f), End = _orange, SizeFrom = 0.15f, SizeTo = 1f
            });
            AddLayer(root.transform, glow, new Layer
            {
                Name = "Sparks", Render = ParticleSystemRenderMode.Stretch, Duration = 1f, Life = new Vector2(0.25f, 0.55f),
                Speed = new Vector2(6f, 15f), Size = new Vector2(0.07f, 0.15f), BurstCount = 80, Shape = ParticleSystemShapeType.Sphere,
                ShapeRadius = 0.1f, Gravity = 0.6f, Start = _whiteGold, End = _orange, SizeFrom = 1f, SizeTo = 0.15f, StretchLength = 2.5f,
                StretchSpeed = 0.1f
            });
            AddLayer(root.transform, glow, new Layer
            {
                Name = "GlowCloud", Render = ParticleSystemRenderMode.Billboard, Duration = 1f, Life = new Vector2(0.5f, 0.8f),
                Speed = new Vector2(0.5f, 2f), Size = new Vector2(1.2f, 2f), BurstCount = 14, Shape = ParticleSystemShapeType.Sphere,
                ShapeRadius = 0.4f, Start = new Color(_gold.r, _gold.g, _gold.b, 0.18f), End = _orange, SizeFrom = 0.6f, SizeTo = 1.2f
            });
            AddLayer(root.transform, glow, new Layer
            {
                Name = "EmberRise", Render = ParticleSystemRenderMode.Billboard, Duration = 1f, Life = new Vector2(0.7f, 1.2f),
                Speed = new Vector2(3f, 8f), Size = new Vector2(0.05f, 0.1f), BurstCount = 36, Shape = ParticleSystemShapeType.Cone,
                ShapeRadius = 0.2f, ShapeAngle = 40f, ShapeRotation = Quaternion.Euler(-QUARTER_TURN, 0f, 0f), Gravity = 0.5f,
                Start = _gold, End = _orange, SizeFrom = 1f, SizeTo = 0.2f
            });

            Save(root, HIT_PATH);
        }

        private static void AddLayer(Transform parent, Material glow, Layer layer)
        {
            var go = new GameObject(layer.Name);
            go.transform.SetParent(parent, false);

            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.duration        = layer.Duration;
            main.loop            = layer.Loop;
            main.playOnAwake     = true;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(layer.Life.x, layer.Life.y);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(layer.Speed.x, layer.Speed.y);
            main.startSize       = new ParticleSystem.MinMaxCurve(layer.Size.x, layer.Size.y);
            main.startColor      = Color.white; // 색은 수명에 따른 색이 정한다
            main.gravityModifier = layer.Gravity;
            main.simulationSpace = layer.World ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode     = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles    = 400;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled      = true;
            emission.rateOverTime = layer.Rate;
            if (layer.BurstCount > 0)
            {
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)layer.BurstCount) });
            }

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = layer.Shape != 0 || layer.ShapeRadius > 0f || layer.ShapeAngle > 0f;
            if (shape.enabled)
            {
                shape.shapeType = (layer.Shape == 0) ? ParticleSystemShapeType.Sphere : layer.Shape;
                shape.radius    = layer.ShapeRadius;
                shape.angle     = layer.ShapeAngle;
                shape.rotation  = IsUnset(layer.ShapeRotation) ? Vector3.zero : layer.ShapeRotation.eulerAngles;
            }
            else
            {
                shape.enabled = false;
            }

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(layer.Start, 0f), new GradientColorKey(layer.End, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(layer.Start.a, FADE_IN_KEY), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, layer.SizeFrom <= 0f ? 1f : layer.SizeFrom, 1f, layer.SizeTo <= 0f ? 1f : layer.SizeTo));

            if (layer.RiseSpeed > 0f)
            {
                ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space   = ParticleSystemSimulationSpace.World;
                velocity.y       = layer.RiseSpeed;
                velocity.x       = 0f;
                velocity.z       = 0f;
            }

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial      = glow;
            particleRenderer.renderMode          = layer.Render;
            particleRenderer.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
            particleRenderer.receiveShadows      = false;
            if (layer.Render == ParticleSystemRenderMode.Stretch)
            {
                particleRenderer.lengthScale   = layer.StretchLength > 0f ? layer.StretchLength : 2f;
                particleRenderer.velocityScale = layer.StretchSpeed;
            }
        }

        // 값을 따로 채우지 않은 쿼터니언은 모두 0이다
        private static bool IsUnset(Quaternion rotation)
        {
            return (rotation.x == 0f) && (rotation.y == 0f) && (rotation.z == 0f) && (rotation.w == 0f);
        }

        private static void Save(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }
    }
}
