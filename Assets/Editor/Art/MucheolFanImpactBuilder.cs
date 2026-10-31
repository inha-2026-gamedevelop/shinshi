// System
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;

// 무철 부채꼴 착탄 VFX 프리팹을 만든다. 기준 반경 8m, 부채꼴 90도로 만들고 FanImpactView가 값을 바꾼다
public static class MucheolFanImpactBuilder
{
    /****************************************
    *                Fields
    ****************************************/

    private const string ROOT        = "Assets/02.Prefab/VFX/Mucheol/FanImpact";
    private const string TEXTURE_ROOT = ROOT + "/Textures";
    private const string MODEL_PATH   = ROOT + "/Models/MucheolFanImpact_Rocks.fbx";

    /****************************************
    *                Methods
    ****************************************/

    // 처음 한 번만 필요한 원본. 개인 작업 폴더(99.ThirdParty, git 제외)에 있는 것을 커밋되는 폴더로 복사한다
    private static readonly string[][] G_SourceCopies =
    {
        new[] { "Assets/99.ThirdParty/VFX/giwoongVFX/GroundRush/Textures/SoftDot.png",      TEXTURE_ROOT + "/SoftDot.png" },
        new[] { "Assets/99.ThirdParty/VFX/giwoongVFX/GroundRush/Textures/DustPuff_4x4.png", TEXTURE_ROOT + "/DustPuff_4x4.png" },
        new[] { "Assets/99.ThirdParty/VFX/giwoongVFX/GroundRush/Models/GroundRush_Rocks.fbx", MODEL_PATH },
        new[] { "Assets/99.ThirdParty/VFX/giwoongVFX/FanImpact/Textures/LavaRock.png",         TEXTURE_ROOT + "/LavaRock.png" },
    };

    [MenuItem("Giwoong/VFX/Build Fan Impact")]
    public static void Build()
    {
        PrepareFolders();
        Texture2D soft = AssetDatabase.LoadAssetAtPath<Texture2D>(TEXTURE_ROOT + "/SoftDot.png");
        Texture2D puff = AssetDatabase.LoadAssetAtPath<Texture2D>(TEXTURE_ROOT + "/DustPuff_4x4.png");

        Material add   = MakeMaterial("MAT_FI_Fire",  soft, new Color(3.2f, 0.32f, 0.02f, 1f),  true);
        Material ember = MakeMaterial("MAT_FI_Ember", soft, new Color(4f, 0.4f, 0.02f, 1f),  true);
        Material smoke = MakeMaterial("MAT_FI_Smoke", puff, new Color(2.1f, 1.5f, 1.2f, 1f), false);
        Material rock  = MakeRockMaterial();
        Texture2D whiteTex = MakeWhiteTexture();
        Texture2D spikeTex = MakeSpikeTexture();
        Material glowMat = MakeMaterial("MAT_FI_Glow", soft, new Color(3.6f, 1.5f, 0.85f, 1f), true);
        glowMat.SetFloat("_ZTest", 8f);
        glowMat.SetFloat("_SoftFade", 0.0001f);
        Material spike = MakeMaterial("MAT_FI_Spike", spikeTex, new Color(5.5f, 1.5f, 0.3f, 1f), true);
        Material shard = MakeMaterial("MAT_FI_Shard", whiteTex, new Color(3.4f, 1.6f, 1.1f, 1f), true);

        Mesh rockMesh = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(MODEL_PATH))
        {
            rockMesh = asset as Mesh;
            if (rockMesh != null)
            {
                break;
            }
        }

        GameObject root = new GameObject("PF_VFX_MucheolFanImpact");
        List<ParticleSystem> all = new List<ParticleSystem>();
        List<ParticleSystem> radial = new List<ParticleSystem>();   // 시작점에서 호를 따라 퍼지는 것
        List<ParticleSystem> centered = new List<ParticleSystem>(); // 부채꼴 한가운데에서 터지는 것
        List<float> centerRadius = new List<float>();
        List<float> centerHeight = new List<float>();

        // 세로 불기둥: 가늘고 긴 줄무늬가 위로 길게 솟는다
        ParticleSystem pillar = Make(root.transform, "Pillar", add, all, 0.35f, true);
        Burst(pillar, 0.0f, 80);
        Main(pillar, 0.5f, 0.7f, 1.8f, 0f);
        { ParticleSystem.MainModule m = pillar.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f); }
        Centered(pillar, 0.9f, 0.2f, centered, centerRadius, centerHeight);
        WorldVelocity(pillar, 0f, 0f, 16f, 30f, 0f, 0f);
        Stretch(pillar, 0.04f, 7f);
        FadeHot(pillar, 0.06f);
        SizeCurve(pillar, 1f, 1f, 0.15f);

        // 큰 글로우: 착탄 순간 중심이 하얗게 번진다. 바닥에 잘리지 않게 깊이 검사를 끈다
        ParticleSystem glowBig = Make(root.transform, "Glow", glowMat, all, 0.35f, true);
        Burst(glowBig, 0f, 1);
        Main(glowBig, 0.6f, 20f, 24f, 0f);
        { ParticleSystem.ShapeModule sh = glowBig.shape; sh.enabled = false; }
        glowBig.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        SizeCurve(glowBig, 0.25f, 1f, 1.15f);
        FadeHot(glowBig, 0.04f);

        // 톱니형 흰 핵: 부채꼴 안쪽 몇 곳에서 순간적으로 터진다
        ParticleSystem core = Make(root.transform, "SpikeCore", spike, all, 0.35f, true);
        Burst(core, 0f, 8);
        Main(core, 0.42f, 5f, 8.5f, 0f);
        { ParticleSystem.MainModule m = core.main; m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        Centered(core, 1f, 1.3f, centered, centerRadius, centerHeight);
        SizeCurve(core, 0.35f, 1f, 1.25f);
        FadeHot(core, 0.03f);

        // 흰 판 파편: 불빛을 받아 하얗게 빛나는 다각형 조각이 튄다
        ParticleSystem shards = Make(root.transform, "Shards", shard, all, 0.35f, true);
        Burst(shards, 0.0f, 64);
        Main(shards, 0.7f, 0.3f, 0.85f, 0f);
        { ParticleSystem.MainModule m = shards.main; m.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f); }
        { ParticleSystem.MainModule m = shards.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f); }
        { ParticleSystem.MainModule m = shards.main; m.gravityModifier = 1.8f; }
        { ParticleSystem.MainModule m = shards.main; m.startRotation3D = true; }
        { ParticleSystem.MainModule m = shards.main; m.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        { ParticleSystem.MainModule m = shards.main; m.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        { ParticleSystem.MainModule m = shards.main; m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        Centered(shards, 0.8f, 0.5f, centered, centerRadius, centerHeight);
        WorldVelocity(shards, 0f, 0f, 4f, 10f, 0f, 0f);
        ParticleSystemRenderer shardRenderer = shards.GetComponent<ParticleSystemRenderer>();
        shardRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        shardRenderer.mesh       = rockMesh;
        ParticleSystem.RotationOverLifetimeModule shardSpin = shards.rotationOverLifetime;
        shardSpin.enabled = true;
        shardSpin.separateAxes = true;
        shardSpin.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
        shardSpin.y = new ParticleSystem.MinMaxCurve(-9f, 9f);
        shardSpin.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
        FadeHot(shards, 0.04f);
        SizeCurve(shards, 1f, 1f, 0.3f);

        // 검은 파편: 큼직한 바위 조각이 위로 튄다
        ParticleSystem debris = Make(root.transform, "Debris", rock, all, 0.35f, true);
        Burst(debris, 0.0f, 22);
        Main(debris, 0.9f, 0.7f, 1.7f, 0f);
        { ParticleSystem.MainModule m = debris.main; m.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f); }
        { ParticleSystem.MainModule m = debris.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.85f, 1.0f); }
        { ParticleSystem.MainModule m = debris.main; m.gravityModifier = 2.4f; }
        { ParticleSystem.MainModule m = debris.main; m.startRotation3D = true; }
        { ParticleSystem.MainModule m = debris.main; m.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        { ParticleSystem.MainModule m = debris.main; m.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        { ParticleSystem.MainModule m = debris.main; m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        Centered(debris, 0.75f, 0.15f, centered, centerRadius, centerHeight);
        WorldVelocity(debris, 0f, 0f, 8f, 12f, 0f, 0f);
        ParticleSystemRenderer debrisRenderer = debris.GetComponent<ParticleSystemRenderer>();
        debrisRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        debrisRenderer.mesh       = rockMesh;
        ParticleSystem.RotationOverLifetimeModule spin = debris.rotationOverLifetime;
        spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
        spin.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
        spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        SizeCurve(debris, 1f, 1f, 0.1f);

        // 주황 연기 고리: 불빛을 받은 몽글한 연기가 부채꼴 바깥으로 크게 퍼진다
        ParticleSystem dust = Make(root.transform, "Smoke", smoke, all, 0.35f, true);
        Burst(dust, 0.12f, 80);
        Main(dust, 2f, 4f, 7f, 0f);
        { ParticleSystem.MainModule m = dust.main; m.startSpeed = new ParticleSystem.MinMaxCurve(6f, 13f); }
        { ParticleSystem.MainModule m = dust.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 2.1f); }
        { ParticleSystem.MainModule m = dust.main; m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        Centered(dust, 0.85f, 0.5f, centered, centerRadius, centerHeight);
        Damp(dust, 2.2f, 0.05f);
        WorldVelocity(dust, 0f, 0f, 0.5f, 1.8f, 0f, 0f);
        ParticleSystem.TextureSheetAnimationModule sheet = dust.textureSheetAnimation;
        sheet.enabled   = true;
        sheet.numTilesX = 4;
        sheet.numTilesY = 4;
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
        ParticleSystem.RotationOverLifetimeModule smokeSpin = dust.rotationOverLifetime;
        smokeSpin.enabled = true;
        smokeSpin.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        SizeCurve(dust, 0.45f, 1f, 1.5f);
        {
            // 처음엔 불빛에 달아오른 주황, 식으면서 어두운 적갈색, 마지막에 투명
            ParticleSystem.ColorOverLifetimeModule c = dust.colorOverLifetime;
            c.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.62f, 0.3f), 0f), new GradientColorKey(new Color(0.8f, 0.3f, 0.16f), 0.45f), new GradientColorKey(new Color(0.28f, 0.12f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.15f), new GradientAlphaKey(0.55f, 0.6f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
        }

        // 먼지 점: 아주 작고 흐린 점이 크레이터 위를 천천히 떠다닌다
        ParticleSystem specks = Make(root.transform, "Specks", ember, all, 0.35f, true);
        Burst(specks, 0.1f, 90);
        Main(specks, 4f, 0.025f, 0.055f, 0f);
        { ParticleSystem.MainModule m = specks.main; m.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f); }
        { ParticleSystem.MainModule m = specks.main; m.startColor = new Color(1f, 0.8f, 0.7f, 0.5f); }
        Centered(specks, 1.6f, 0.8f, centered, centerRadius, centerHeight);
        WorldVelocity(specks, 0f, 0f, 0.1f, 0.5f, 0f, 0f);
        {
            ParticleSystem.NoiseModule n = specks.noise;
            n.enabled     = true;
            n.strength    = 0.35f;
            n.frequency   = 0.6f;
            n.scrollSpeed = 0.4f;
        }
        FadeColor(specks, Color.white, 0.3f);

        // 용암 바닥과 판 조각 재질. 판 메시는 FanImpactView가 재생 때마다 만든다
        Texture2D plateVein = MakeVeinTexture("PlateVein", 0f);
        Texture2D crackRock = LoadCrackRock();
        Texture2D emberGlow = MakeEmberGlowTexture();
        Material lavaFloor = MakeCrackMaterial("MAT_FI_LavaFloor", emberGlow, new Color(2.6f, 1.3f, 1.0f, 1f), true);
        lavaFloor.SetFloat("_UseVertexColor", 1f);
        lavaFloor.SetFloat("_RevealUV1", 1f);
        Material plateMat = MakePlateMaterial(plateVein);
        Material sideMat = MakeSideMaterial();
        Material charMat = MakeMaterial("MAT_FI_Char", crackRock, new Color(0.62f, 0.6f, 0.6f, 0.98f), false);
        charMat.SetFloat("_SoftFade", 0.0001f);
        charMat.SetFloat("_RevealUV1", 1f);
        charMat.SetFloat("_UseVertexColor", 1f);
        charMat.renderQueue = 3000;
        lavaFloor.renderQueue = 3001;
        GameObject charGo = MakeCrackObject(root.transform, "CharFloor", charMat, out MeshFilter charFilter, out MeshRenderer charRenderer);
        GameObject lavaGo = MakeCrackObject(root.transform, "LavaFloor", lavaFloor, out MeshFilter lavaFilter, out MeshRenderer lavaRenderer);

        GameObject lightGo = new GameObject("FlashLight");
        lightGo.transform.SetParent(root.transform, false);
        Light flashLight = lightGo.AddComponent<Light>();
        flashLight.type      = LightType.Point;
        flashLight.color     = new Color(1f, 0.55f, 0.22f);
        flashLight.intensity = 0f;
        flashLight.range     = 15f;
        flashLight.shadows   = LightShadows.None;

        MucheolFanImpactView view = root.AddComponent<MucheolFanImpactView>();
        SerializedObject so = new SerializedObject(view);
        SerializedProperty list = so.FindProperty("_systems");
        list.arraySize = all.Count;
        for (int i = 0; i < all.Count; ++i)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
        }
        so.FindProperty("_radialSystems").arraySize = radial.Count;
        for (int i = 0; i < radial.Count; ++i)
        {
            so.FindProperty("_radialSystems").GetArrayElementAtIndex(i).objectReferenceValue = radial[i];
        }

        SerializedProperty centerList = so.FindProperty("_centerSystems");
        SerializedProperty radiusList = so.FindProperty("_centerRadius");
        SerializedProperty heightList = so.FindProperty("_centerHeight");
        centerList.arraySize = centered.Count;
        radiusList.arraySize = centered.Count;
        heightList.arraySize = centered.Count;
        for (int i = 0; i < centered.Count; ++i)
        {
            centerList.GetArrayElementAtIndex(i).objectReferenceValue = centered[i];
            radiusList.GetArrayElementAtIndex(i).floatValue = centerRadius[i];
            heightList.GetArrayElementAtIndex(i).floatValue = centerHeight[i];
        }

        so.FindProperty("_flashLight").objectReferenceValue = flashLight;
        so.FindProperty("_sideMaterial").objectReferenceValue = sideMat;
        so.FindProperty("_charFilter").objectReferenceValue = charFilter;
        so.FindProperty("_char").objectReferenceValue = charRenderer;
        so.FindProperty("_plateMaterial").objectReferenceValue = plateMat;
        so.FindProperty("_lavaFilter").objectReferenceValue    = lavaFilter;
        so.FindProperty("_lava").objectReferenceValue          = lavaRenderer;
        so.ApplyModifiedPropertiesWithoutUndo();

        string path = ROOT + "/Prefabs/PF_VFX_MucheolFanImpact.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        BuildWave(rockMesh, glowMat, smoke, rock, shard, whiteTex);
        Debug.Log("FanImpact 프리팹 생성: " + path);
    }

    // 원형 충격파(작은/큰) 프리팹. 불꽃 고리 둘, 전기 고리, 연기 고리, 튀는 바위와 파편, 섬광
    private static void BuildWave(Mesh rockMesh, Material glowMat, Material smoke, Material rock, Material shard, Texture2D whiteTex)
    {
        Texture2D ringTex = MakeRingTexture();
        Material ringMat  = MakeCrackMaterial("MAT_FI_WaveRing", ringTex, new Color(15f, 4.4f, 0.8f, 1f), true);
        ringMat.SetFloat("_UseVertexColor", 1f);
        ringMat.SetFloat("_Reveal", 1f);   // 바닥 재질 기본값(0)이면 아예 안 드러난다
        EditorUtility.SetDirty(ringMat);
        Material coreMat  = MakeCrackMaterial("MAT_FI_WaveRingCore", ringTex, new Color(12f, 9f, 5.5f, 1f), true);
        coreMat.SetFloat("_UseVertexColor", 1f);
        coreMat.SetFloat("_Reveal", 1f);
        EditorUtility.SetDirty(coreMat);
        Material arcMat   = MakeMaterial("MAT_FI_Arc", whiteTex, new Color(5f, 3.6f, 0.7f, 1f), true);

        GameObject root = new GameObject("PF_VFX_MucheolWaveImpact");
        List<ParticleSystem> all    = new List<ParticleSystem>();
        List<ParticleSystem> strong = new List<ParticleSystem>();

        GameObject ringGo = MakeCrackObject(root.transform, "Ring", ringMat, out MeshFilter ringFilter, out MeshRenderer ringRenderer);
        ringGo.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        GameObject coreGo = MakeCrackObject(root.transform, "RingCore", coreMat, out MeshFilter coreFilter, out MeshRenderer coreRenderer);
        coreGo.transform.localPosition = new Vector3(0f, 0.07f, 0f);

        // 중심의 번쩍임
        ParticleSystem flash = Make(root.transform, "Glow", glowMat, all, 0.35f, true);
        Burst(flash, 0f, 1);
        Main(flash, 0.4f, 11f, 13f, 0f);
        { ParticleSystem.ShapeModule sh = flash.shape; sh.enabled = false; }
        flash.transform.localPosition = new Vector3(0f, 1f, 0f);
        SizeCurve(flash, 0.3f, 1f, 1.1f);
        FadeHot(flash, 0.04f);

        // 전기 고리: 중심에서 사방으로 번개선이 가장자리까지 달린다
        ParticleSystem arc = Make(root.transform, "ElectricArc", arcMat, all, 0.35f, true);
        Burst(arc, 0.03f, 80);
        Main(arc, 0.5f, 0.1f, 0.2f, 0f);
        { ParticleSystem.MainModule m = arc.main; m.startSpeed = new ParticleSystem.MinMaxCurve(15f, 21f); }
        { ParticleSystem.MainModule m = arc.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.38f, 0.5f); }
        Ring360(arc, 0.6f, 0.3f, 1f);
        {
            ParticleSystem.NoiseModule n = arc.noise;
            n.enabled     = true;
            n.strength    = 2.4f;
            n.frequency   = 2.2f;
            n.scrollSpeed = 4f;
            n.damping     = false;
        }
        {
            ParticleSystem.TrailModule tr = arc.trails;
            tr.enabled           = true;
            tr.mode              = ParticleSystemTrailMode.PerParticle;
            tr.ratio             = 1f;
            tr.lifetime          = 0.55f;
            tr.minVertexDistance = 0.12f;
            tr.dieWithParticles  = true;
            tr.worldSpace        = true;
            tr.inheritParticleColor = false;
            tr.widthOverTrail    = new ParticleSystem.MinMaxCurve(0.34f, new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f)));
            Gradient tg = new Gradient();
            tg.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.15f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorOverLifetime = tg;
            ParticleSystemRenderer ar = arc.GetComponent<ParticleSystemRenderer>();
            ar.renderMode    = ParticleSystemRenderMode.None;
            ar.trailMaterial = arcMat;
        }

        // 연기 고리: 고리를 따라 주황 연기가 바깥으로 번진다
        ParticleSystem dust = Make(root.transform, "Dust", smoke, all, 0.35f, true);
        Burst(dust, 0.06f, 90);
        Main(dust, 2f, 3.2f, 5.8f, 0f);
        { ParticleSystem.MainModule m = dust.main; m.startSpeed = new ParticleSystem.MinMaxCurve(8f, 14f); }
        { ParticleSystem.MainModule m = dust.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 2.1f); }
        { ParticleSystem.MainModule m = dust.main; m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        Ring360(dust, 1.0f, 0.5f, 1f);
        Damp(dust, 2.2f, 0.05f);
        WorldVelocity(dust, 0f, 0f, 0.5f, 1.8f, 0f, 0f);
        ParticleSystem.TextureSheetAnimationModule sheet = dust.textureSheetAnimation;
        sheet.enabled    = true;
        sheet.numTilesX  = 4;
        sheet.numTilesY  = 4;
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
        ParticleSystem.RotationOverLifetimeModule smokeSpin = dust.rotationOverLifetime;
        smokeSpin.enabled = true;
        smokeSpin.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        SizeCurve(dust, 0.45f, 1f, 1.5f);
        {
            ParticleSystem.ColorOverLifetimeModule c = dust.colorOverLifetime;
            c.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.62f, 0.3f), 0f), new GradientColorKey(new Color(0.8f, 0.3f, 0.16f), 0.45f), new GradientColorKey(new Color(0.28f, 0.12f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.15f), new GradientAlphaKey(0.55f, 0.6f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
        }

        // 튀는 검은 바위, 강한 충격파만
        ParticleSystem rocks = Make(root.transform, "Rocks", rock, all, 0.35f, true);
        Burst(rocks, 0.02f, 18);
        Main(rocks, 0.9f, 0.6f, 1.5f, 0f);
        { ParticleSystem.MainModule m = rocks.main; m.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f); }
        { ParticleSystem.MainModule m = rocks.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.85f, 1.0f); }
        { ParticleSystem.MainModule m = rocks.main; m.gravityModifier = 2.4f; }
        { ParticleSystem.MainModule m = rocks.main; m.startRotation3D = true; }
        { ParticleSystem.MainModule m = rocks.main; m.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        { ParticleSystem.MainModule m = rocks.main; m.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        { ParticleSystem.MainModule m = rocks.main; m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        Ring360(rocks, 2.2f, 0.2f, 1f);
        WorldVelocity(rocks, 0f, 0f, 7f, 11f, 0f, 0f);
        ParticleSystemRenderer rockRenderer = rocks.GetComponent<ParticleSystemRenderer>();
        rockRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        rockRenderer.mesh       = rockMesh;
        ParticleSystem.RotationOverLifetimeModule rockSpin = rocks.rotationOverLifetime;
        rockSpin.enabled = true;
        rockSpin.separateAxes = true;
        rockSpin.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
        rockSpin.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
        rockSpin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        SizeCurve(rocks, 1f, 1f, 0.1f);
        strong.Add(rocks);

        // 흰 파편, 강한 충격파만
        ParticleSystem shards = Make(root.transform, "Shards", shard, all, 0.35f, true);
        Burst(shards, 0.0f, 44);
        Main(shards, 0.7f, 0.3f, 0.8f, 0f);
        { ParticleSystem.MainModule m = shards.main; m.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f); }
        { ParticleSystem.MainModule m = shards.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f); }
        { ParticleSystem.MainModule m = shards.main; m.gravityModifier = 1.8f; }
        { ParticleSystem.MainModule m = shards.main; m.startRotation3D = true; }
        { ParticleSystem.MainModule m = shards.main; m.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        { ParticleSystem.MainModule m = shards.main; m.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        { ParticleSystem.MainModule m = shards.main; m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
        Ring360(shards, 1.8f, 0.4f, 1f);
        WorldVelocity(shards, 0f, 0f, 4f, 10f, 0f, 0f);
        ParticleSystemRenderer shardRenderer = shards.GetComponent<ParticleSystemRenderer>();
        shardRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        shardRenderer.mesh       = rockMesh;
        ParticleSystem.RotationOverLifetimeModule shardSpin = shards.rotationOverLifetime;
        shardSpin.enabled = true;
        shardSpin.separateAxes = true;
        shardSpin.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
        shardSpin.y = new ParticleSystem.MinMaxCurve(-9f, 9f);
        shardSpin.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
        FadeHot(shards, 0.04f);
        SizeCurve(shards, 1f, 1f, 0.3f);
        strong.Add(shards);

        GameObject lightGo = new GameObject("FlashLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        Light flashLight = lightGo.AddComponent<Light>();
        flashLight.type      = LightType.Point;
        flashLight.color     = new Color(1f, 0.55f, 0.22f);
        flashLight.intensity = 0f;
        flashLight.range     = 14f;
        flashLight.shadows   = LightShadows.None;

        MucheolWaveImpactView view = root.AddComponent<MucheolWaveImpactView>();
        SerializedObject so = new SerializedObject(view);
        SerializedProperty list = so.FindProperty("_systems");
        list.arraySize = all.Count;
        for (int i = 0; i < all.Count; ++i)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
        }

        SerializedProperty strongList = so.FindProperty("_strongOnly");
        strongList.arraySize = strong.Count;
        for (int i = 0; i < strong.Count; ++i)
        {
            strongList.GetArrayElementAtIndex(i).objectReferenceValue = strong[i];
        }

        SerializedProperty rings = so.FindProperty("_rings");
        rings.arraySize = 2;
        rings.GetArrayElementAtIndex(0).objectReferenceValue = ringRenderer;
        rings.GetArrayElementAtIndex(1).objectReferenceValue = coreRenderer;
        so.FindProperty("_flashLight").objectReferenceValue = flashLight;
        so.ApplyModifiedPropertiesWithoutUndo();

        string path = ROOT + "/Prefabs/PF_VFX_MucheolWaveImpact.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("WaveImpact 프리팹 생성: " + path);
    }

    // 원판 360도 방출. 가장자리 가까이에서 바깥으로 나간다
    private static void Ring360(ParticleSystem ps, float radius, float height, float thickness)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled         = true;
        shape.shapeType       = ParticleSystemShapeType.Circle;
        shape.radius          = radius;
        shape.radiusThickness = thickness;
        shape.arc             = 360f;
        shape.arcMode         = ParticleSystemShapeMultiModeValue.Random;
        shape.rotation        = Vector3.zero;
        shape.position        = new Vector3(0f, 0f, -height);
    }

    // 고리 폭 방향 밝기: 안쪽은 어둡고 바깥 가장자리로 갈수록 밝아졌다가 끝에서 사라진다
    private static Texture2D MakeRingTexture()
    {
        string path = ROOT + "/Textures/WaveRing.png";
        Texture2D tex = new Texture2D(256, 4, TextureFormat.RGBA32, false);
        for (int x = 0; x < 256; ++x)
        {
            float u = (x + 0.5f) / 256f;
            float a = Mathf.Pow(u, 2.2f) * (1f - Mathf.SmoothStep(0.92f, 1f, u));
            for (int y = 0; y < 4; ++y)
            {
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a * 1.3f)));
            }
        }

        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode            = TextureWrapMode.Clamp;
        importer.mipmapEnabled       = false;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void PrepareFolders()
    {
        foreach (string sub in new[] { "Prefabs", "Materials", "Textures", "Models", "Shaders" })
        {
            Directory.CreateDirectory(Path.Combine(ROOT, sub));
        }

        AssetDatabase.Refresh();
        foreach (string[] pair in G_SourceCopies)
        {
            if ((!File.Exists(pair[1])) && (File.Exists(pair[0])))
            {
                File.Copy(pair[0], pair[1]);
                AssetDatabase.ImportAsset(pair[1]);
            }
        }
    }

    private static GameObject MakeCrackObject(Transform parent, string name, Material material, out MeshFilter filter, out MeshRenderer renderer)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        filter   = go.AddComponent<MeshFilter>();
        renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial    = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows    = false;
        return go;
    }

    // 이어 붙일 수 있는 잔 균열(Voronoi 경계) 무늬. 밝은 선이 용암 줄기이고 base는 선이 아닌 곳의 밝기
    private static Texture2D MakeVeinTexture(string name, float baseLevel)
    {
        string path = ROOT + "/Textures/" + name + ".png";
        const int SIZE = 256;
        const int CELLS = 11;
        Random.State saved = Random.state;
        Random.InitState(1234);

        Vector2[] points = new Vector2[CELLS * CELLS];
        for (int y = 0; y < CELLS; ++y)
        {
            for (int x = 0; x < CELLS; ++x)
            {
                points[(y * CELLS) + x] = new Vector2(x + Random.value, y + Random.value);
            }
        }

        Texture2D tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        for (int py = 0; py < SIZE; ++py)
        {
            for (int px = 0; px < SIZE; ++px)
            {
                Vector2 p = new Vector2((px + 0.5f) / SIZE * CELLS, (py + 0.5f) / SIZE * CELLS);
                float f1 = 99f;
                float f2 = 99f;
                for (int oy = -1; oy <= 1; ++oy)
                {
                    for (int ox = -1; ox <= 1; ++ox)
                    {
                        int cx = Mathf.FloorToInt(p.x) + ox;
                        int cy = Mathf.FloorToInt(p.y) + oy;
                        int wx = ((cx % CELLS) + CELLS) % CELLS;
                        int wy = ((cy % CELLS) + CELLS) % CELLS;
                        Vector2 q = points[(wy * CELLS) + wx] + new Vector2(cx - wx, cy - wy);
                        float d = (q - p).magnitude;
                        if (d < f1)
                        {
                            f2 = f1;
                            f1 = d;
                        }
                        else if (d < f2)
                        {
                            f2 = d;
                        }
                    }
                }

                float edge = Mathf.Clamp01(1f - ((f2 - f1) / 0.085f));
                float noise = Mathf.PerlinNoise(px * 0.09f, py * 0.09f);
                float patch = Mathf.SmoothStep(0.42f, 0.7f, Mathf.PerlinNoise((px * 0.028f) + 7f, (py * 0.028f) + 3f));   // 붉은 빛이 군데군데 몰리게 한다
                float v = Mathf.Pow(edge, 1.6f) * Mathf.Lerp(0.45f, 1.3f, noise) * Mathf.Lerp(0.12f, 1.5f, patch);
                v = Mathf.Clamp01(baseLevel + ((1f - baseLevel) * v));
                tex.SetPixel(px, py, new Color(v, v, v, 1f));
            }
        }

        Random.state = saved;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.sRGBTexture = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // 사용자가 준 균열 바위 텍스처. 어두운 돌 위에 불규칙한 붉은 균열이 있다
    private static Texture2D LoadCrackRock()
    {
        string path = ROOT + "/Textures/LavaRock.png";
        AssetDatabase.ImportAsset(path);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if ((importer != null) && ((importer.wrapMode != TextureWrapMode.Repeat) || (importer.maxTextureSize < 1024)))
        {
            importer.wrapMode       = TextureWrapMode.Repeat;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // 불씨 바위 사진에서 붉게 달아오른 부분만 뽑은 발광 마스크. 회색 돌 부분은 검게 둔다
    private static Texture2D MakeEmberGlowTexture()
    {
        string src = ROOT + "/Textures/LavaRock.png";
        string dst = ROOT + "/Textures/EmberGlow.png";
        Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        source.LoadImage(System.IO.File.ReadAllBytes(src));
        Color[] pixels = source.GetPixels();
        for (int i = 0; i < pixels.Length; ++i)
        {
            Color c = pixels[i];
            // 어두운 돌(세 채널이 비슷하고 낮음)은 0, 주황~노랑으로 달아오른 곳(R이 B보다 훨씬 큼)만 남긴다
            float heat = Mathf.Clamp01((c.r - c.b - 0.18f) * 2.2f);
            pixels[i] = new Color(c.r * heat * 1.15f, c.g * heat * 1.15f, c.b * heat, 1f);
        }

        Texture2D glow = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        glow.SetPixels(pixels);
        System.IO.File.WriteAllBytes(dst, glow.EncodeToPNG());
        Object.DestroyImmediate(source);
        Object.DestroyImmediate(glow);
        AssetDatabase.ImportAsset(dst);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(dst);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
    }

    private static Material MakeSideMaterial()
    {
        string path = ROOT + "/Materials/MAT_FI_PlateSide.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", new Color(0.2f, 0.1f, 0.07f, 1f));
        material.SetFloat("_Smoothness", 0.15f);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", new Color(1.5f, 0.42f, 0.1f, 1f));
        EditorUtility.SetDirty(material);
        return material;
    }

    // 이어 붙일 수 있는 거친 돌 바닥 무늬(여러 겹의 Perlin 잡음)
    private static Texture2D MakeNoiseTexture(string name)
    {
        string path = ROOT + "/Textures/" + name + ".png";
        const int SIZE = 256;
        Texture2D tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        for (int y = 0; y < SIZE; ++y)
        {
            for (int x = 0; x < SIZE; ++x)
            {
                float v = 0f;
                float amp = 0.5f;
                float freq = 3f;
                for (int o = 0; o < 5; ++o)
                {
                    // 가장자리가 이어지게 네 방향 값을 섞는다
                    float fx = (float)x / SIZE;
                    float fy = (float)y / SIZE;
                    float a = Mathf.PerlinNoise(fx * freq, fy * freq);
                    float b2 = Mathf.PerlinNoise((fx - 1f) * freq, fy * freq);
                    float c = Mathf.PerlinNoise(fx * freq, (fy - 1f) * freq);
                    float d = Mathf.PerlinNoise((fx - 1f) * freq, (fy - 1f) * freq);
                    float n = Mathf.Lerp(Mathf.Lerp(a, b2, fx), Mathf.Lerp(c, d, fx), fy);
                    v += n * amp;
                    amp *= 0.5f;
                    freq *= 2f;
                }

                v = Mathf.Clamp01(0.2f + (v * 0.95f));
                tex.SetPixel(x, y, new Color(v, v, v, 1f));
            }
        }

        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Texture2D MakeWhiteTexture()
    {
        string path = ROOT + "/Textures/White.png";
        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        for (int y = 0; y < 4; ++y)
        {
            for (int x = 0; x < 4; ++x)
            {
                tex.SetPixel(x, y, Color.white);
            }
        }

        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material MakePlateMaterial(Texture2D vein)
    {
        string path = ROOT + "/Materials/MAT_FI_Plate.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", new Color(0.15f, 0.125f, 0.175f, 1f));
        material.SetFloat("_Smoothness", 0.3f);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", new Color(0.05f, 0.012f, 0.008f, 1f));
        material.SetTexture("_EmissionMap", vein);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material MakeCrackMaterial(string name, Texture2D texture, Color color, bool additive)
    {
        Material material = MakeMaterial(name, texture, color, additive);
        material.SetFloat("_SoftFade", 0.0001f);   // 바닥과 거의 붙어 있어서 가장자리 흐림을 끈다
        material.SetFloat("_UseVertexColor", 0f);
        material.SetFloat("_Reveal", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static ParticleSystem Make(Transform parent, string name, Material material, List<ParticleSystem> all, float unused, bool flat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        // 원판 모양 방출이 바닥(XZ) 위에 눕도록 90도 세운다
        go.transform.localRotation = flat ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop             = false;
        main.playOnAwake      = false;
        main.duration         = 0.2f;
        main.simulationSpace  = ParticleSystemSimulationSpace.World;
        main.scalingMode      = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles     = 400;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        all.Add(ps);
        return ps;
    }

    // 원판 방출 위치를 위로 띄운다. 계통은 90도 눕혀져 있어 로컬 -Z가 월드 위쪽이다
    private static void Centered(ParticleSystem ps, float radiusFactor, float height, List<ParticleSystem> list, List<float> radii, List<float> heights)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled         = true;
        shape.shapeType       = ParticleSystemShapeType.Circle;
        shape.radiusThickness = 1f;
        shape.arcMode         = ParticleSystemShapeMultiModeValue.Random;
        list.Add(ps);
        radii.Add(radiusFactor);
        heights.Add(height);
    }

    private static void Offset(ParticleSystem ps, float height)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.position = new Vector3(0f, 0f, -height);
    }

    // 날카로운 톱니 모양 가장자리를 가진 중심이 밝은 별 텍스처
    private static Texture2D MakeSpikeTexture()
    {
        string path = ROOT + "/Textures/Spike.png";
        const int SIZE = 128;
        Texture2D tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        for (int y = 0; y < SIZE; ++y)
        {
            for (int x = 0; x < SIZE; ++x)
            {
                float u = ((x + 0.5f) / SIZE * 2f) - 1f;
                float v = ((y + 0.5f) / SIZE * 2f) - 1f;
                float r = Mathf.Sqrt((u * u) + (v * v));
                float a = Mathf.Atan2(v, u);
                float spike = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a * 7f)), 5f);
                float jag   = 0.5f + (0.5f * Mathf.Sin((a * 17f) + 1.3f));
                float edge  = 0.26f + (0.74f * spike * (0.55f + (0.45f * jag)));
                float body  = Mathf.Clamp01(1f - (r / edge));
                float glow  = Mathf.Exp(-r * r * 9f);
                float alpha = Mathf.Clamp01((Mathf.Pow(body, 0.6f) * 1.1f) + glow);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode      = TextureWrapMode.Clamp;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void Burst(ParticleSystem ps, float time, short count)
    {
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(time, count) });
    }

    private static void Main(ParticleSystem ps, float lifetime, float sizeMin, float sizeMax, float speed)
    {
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = lifetime;
        main.startSize     = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startSpeed    = speed;
        main.startColor    = Color.white;
    }

    private static void Fan(ParticleSystem ps, float radius)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled         = true;
        shape.shapeType       = ParticleSystemShapeType.Circle;
        shape.radius          = radius;
        shape.radiusThickness = 1f;
        shape.arc             = 90f;
        shape.arcMode         = ParticleSystemShapeMultiModeValue.Random;
        shape.rotation        = new Vector3(0f, 0f, 45f);
    }

    private static void WorldVelocity(ParticleSystem ps, float x, float z, float yMin, float yMax, float unusedA, float unusedB)
    {
        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space   = ParticleSystemSimulationSpace.World;
        velocity.x       = new ParticleSystem.MinMaxCurve(0f, 0f);   // 세 축의 모드를 같게 맞춰야 적용된다
        velocity.z       = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y       = new ParticleSystem.MinMaxCurve(yMin, yMax);
    }

    private static void Damp(ParticleSystem ps, float limit, float dampen)
    {
        ParticleSystem.LimitVelocityOverLifetimeModule limiter = ps.limitVelocityOverLifetime;
        limiter.enabled = true;
        limiter.limit   = limit;
        limiter.dampen  = dampen;
    }

    private static void Stretch(ParticleSystem ps, float velocityScale, float lengthScale)
    {
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode    = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = velocityScale;
        renderer.lengthScale   = lengthScale;
    }

    private static void SizeCurve(ParticleSystem ps, float a, float b, float c)
    {
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, a), new Keyframe(0.35f, b), new Keyframe(1f, c)));
    }

    private static void FadeColor(ParticleSystem ps, Color tint, float fadeIn)
    {
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    // 하얗게 터졌다가 주황, 붉은색으로 식으며 사라지는 색
    private static void FadeHot(ParticleSystem ps, float fadeIn)
    {
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.82f, 0.62f), 0f), new GradientColorKey(new Color(1f, 0.42f, 0.12f), 0.4f), new GradientColorKey(new Color(0.55f, 0.1f, 0.03f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    private static void Trail(ParticleSystem ps, Material material)
    {
        ParticleSystem.TrailModule trails = ps.trails;
        trails.enabled             = true;
        trails.mode                = ParticleSystemTrailMode.PerParticle;
        trails.ratio               = 1f;
        trails.lifetime            = 8f;
        trails.minVertexDistance   = 0.25f;
        trails.dieWithParticles    = false;
        trails.worldSpace          = true;
        trails.inheritParticleColor = false;
        trails.widthOverTrail      = new ParticleSystem.MinMaxCurve(0.32f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(1f, 1f)));
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.85f, 0.55f), 0f), new GradientColorKey(new Color(1f, 0.32f, 0.1f), 0.2f), new GradientColorKey(new Color(0.45f, 0.05f, 0.02f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        trails.colorOverLifetime = fade;

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode    = ParticleSystemRenderMode.None;
        renderer.trailMaterial = material;
    }

    private static Material MakeMaterial(string name, Texture2D texture, Color color, bool additive)
    {
        string path = ROOT + "/Materials/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("GiwoongVFX/FanImpactParticle"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = Shader.Find("GiwoongVFX/FanImpactParticle");
        material.SetFloat("_ZTest", 4f);
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_Color", color);
        material.SetFloat("_SrcBlend", 5f);
        material.SetFloat("_DstBlend", additive ? 1f : 10f);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_UseVertexColor", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material MakeRockMaterial()
    {
        string path = ROOT + "/Materials/MAT_FI_Rock.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", new Color(0.07f, 0.06f, 0.06f, 1f));
        material.SetFloat("_Smoothness", 0.2f);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", new Color(0.7f, 0.14f, 0.02f, 1f));
        EditorUtility.SetDirty(material);
        return material;
    }
}
