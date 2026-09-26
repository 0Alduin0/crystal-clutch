using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Yuva'yı (ejderha sahnesi) tek tıkla kurar:  Match3 -> Yuva -> Kur
//
// Yaptıkları:
//   1. URP'ye bir 3D renderer ekler. Proje 2D renderer'la kurulu; 2D renderer
//      3D modeli ışıksız, düz renk çizer. Nest sahnesinin kamerası bu renderer'ı
//      kullanıyor, diğer sahneler etkilenmiyor.
//   2. Geçici ejderha modellerini ilkel şekillerden kurar ve 7 aşamalı ejderha
//      asset'ini oluşturur. Tripo modelleri gelince sadece
//      Assets/Dragons/Dragon_Kizil.asset'teki model alanları değişecek.
//   3. Assets/Scenes/Nest.unity sahnesini kurar: kamera, ışıklar, bloom, kaide,
//      ejderha, efektler, arka plan, arayüz. Ses ve titreşim yöneticilerini
//      Game sahnesinden kopyalar. Build Settings'e ekler.
//   4. MainPages'te YUVA butonunu Nest sahnesine bağlar; ilk sürümün panel +
//      RenderTexture kurulumu varsa temizler.
//
// Sahneleri kendisi kaydediyor. Başlamadan önce kaydedilmemiş değişiklik varsa sorar.
public static class NestSetup
{
    const string DragonFolder = "Assets/Dragons";
    const string PlaceholderFolder = "Assets/Dragons/GeciciModel";
    const string RendererPath = "Assets/Settings/Renderer3D_Yuva.asset";
    const string VolumePath = "Assets/Dragons/YuvaIsik.asset";

    const string MainPagesPath = "Assets/Scenes/MainPages.unity";
    const string GamePath = "Assets/Scenes/Game.unity";
    const string NestPath = "Assets/Scenes/Nest.unity";

    const string MenuButtonName = "NestButton";

    [MenuItem("Match3/Yuva/Kur")]
    static void Setup()
    {
        if (File.Exists(NestPath) &&
            !EditorUtility.DisplayDialog("Yuva",
                "Nest sahnesi zaten var. Baştan kurulursa sahnede elle yaptığın değişiklikler gider " +
                "(ejderha asset'i ve modeller korunur).", "Baştan Kur", "Vazgeç"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        int rendererIndex = EnsureRenderer3D();
        if (rendererIndex < 0) return;

        Directory.CreateDirectory(PlaceholderFolder);

        Materials mats = CreateMaterials();
        Material particleMat = CreateParticleMaterial();
        GameObject embers = CreateEmbersPrefab(particleMat);
        DragonDefinition dragon = EnsureDragon(mats, embers);
        VolumeProfile glow = EnsureGlowProfile();

        // --- 1. Ana menü ---
        Scene main = EditorSceneManager.OpenScene(MainPagesPath, OpenSceneMode.Single);
        bool buttonOk = SetupMenuButton(dragon);
        RemoveOldOverlay();
        EditorSceneManager.SaveScene(main);

        // --- 2. Nest sahnesi ---
        // Ses/titreşim yöneticileri sadece Game sahnesinde var; oradan kopyalanıyor
        Scene game = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Additive);
        Scene nest = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(nest);

        CopyFromScene(game, nest, "AudioManager");
        CopyFromScene(game, nest, "HapticManager");
        EditorSceneManager.CloseScene(game, true);

        SetupLighting();

        DragonView view = BuildStage(mats, particleMat, rendererIndex, glow, out Camera camera, out Transform eggTarget);
        BuildUI(dragon, view, camera, eggTarget);

        EditorSceneManager.SaveScene(nest, NestPath);
        EditorSceneManager.CloseScene(main, true);

        AddToBuildSettings(NestPath);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Yuva",
            "Nest sahnesi kuruldu ve açık.\n\n" +
            (buttonOk ? "" : $"UYARI: MainPages'te '{MenuButtonName}' bulunamadı - butona NestButton bileşenini elle ekle.\n\n") +
            "Denemek için: MainPages'i açıp oyna ve YUVA'ya bas. Ya da bu sahnede doğrudan Play'e bas.",
            "Tamam");
    }

    // ==================== TEST ====================
    // Büyümeyi görmek için onlarca level oynamaya gerek kalmasın

    [MenuItem("Match3/Yuva/Test: +100 yumurta")]
    static void AddTestEggs()
    {
        SaveSystem.AddDragonEgg(100);
        Debug.Log($"[Yuva] +100 yumurta. Bakiye: {SaveSystem.DragonEgg}");
    }

    [MenuItem("Match3/Yuva/Test: ejderhayı sıfırla")]
    static void ResetTestDragon()
    {
        var dragon = AssetDatabase.LoadAssetAtPath<DragonDefinition>($"{DragonFolder}/Dragon_Kizil.asset");
        SaveSystem.ResetDragon(dragon != null ? dragon.id : "kizil");
        Debug.Log("[Yuva] Ejderha yumurta hâline döndü, ismi silindi.");
    }

    // ==================== ANA MENÜ ====================

    // Sadece butonu bağlar - Nest sahnesine dokunmaz. MainPages açıkken.
    [MenuItem("Match3/Yuva/Ana menü butonunu bağla (MainPages açıkken)")]
    static void BindMenuButtonOnly()
    {
        Scene scene = SceneManager.GetActiveScene();

        if (scene.name != "MainPages")
        {
            EditorUtility.DisplayDialog("Yuva", "Önce MainPages sahnesini aç.", "Tamam");
            return;
        }

        var dragon = AssetDatabase.LoadAssetAtPath<DragonDefinition>($"{DragonFolder}/Dragon_Kizil.asset");

        if (dragon == null)
        {
            EditorUtility.DisplayDialog("Yuva", "Ejderha asset'i yok - önce Match3 -> Yuva -> Kur.", "Tamam");
            return;
        }

        if (!SetupMenuButton(dragon))
        {
            EditorUtility.DisplayDialog("Yuva", $"'{MenuButtonName}' bulunamadı.", "Tamam");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorUtility.DisplayDialog("Yuva", "YUVA butonu bağlandı. Sahneyi kaydet (Ctrl+S).", "Tamam");
    }

    // YUVA butonu: yazısı, rozeti, NestButton bileşeni. Undo'ya kayıtlı.
    static bool SetupMenuButton(DragonDefinition dragon)
    {
        Button button = FindByName<Button>(MenuButtonName);
        if (button == null) return false;

        TextMeshProUGUI label = null;
        foreach (TextMeshProUGUI t in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (t.transform.parent == button.transform && t.name != "Badge") { label = t; break; }

        if (label != null)
        {
            Undo.RecordObject(label, "YUVA butonu");
            label.text = "YUVA";
        }

        // Rozet: önceden kaldıysa onu kullan
        Transform badge = button.transform.Find("Badge");

        if (badge == null)
        {
            RectTransform badgeRt = Rect("Badge", button.transform);
            Anchor(badgeRt, new Vector2(1f, 1f), new Vector2(-20f, -10f), new Vector2(120f, 72f));

            Image badgeImg = badgeRt.gameObject.AddComponent<Image>();
            badgeImg.sprite = LoadSprite("Assets/Sprites/CurrencyBackground.png");
            badgeImg.type = Image.Type.Sliced;
            badgeImg.color = new Color(1f, 0.45f, 0.2f);
            badgeImg.raycastTarget = false;

            TextMeshProUGUI count = Text("Count", badgeRt, "0", 38, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(count.rectTransform);

            Undo.RegisterCreatedObjectUndo(badgeRt.gameObject, "YUVA butonu");
            badge = badgeRt;
        }

        NestButton nb = button.GetComponent<NestButton>();
        if (nb == null) nb = Undo.AddComponent<NestButton>(button.gameObject);

        var so = new SerializedObject(nb);
        so.FindProperty("dragon").objectReferenceValue = dragon;
        so.FindProperty("badge").objectReferenceValue = badge.gameObject;
        so.FindProperty("badgeText").objectReferenceValue = badge.GetComponentInChildren<TextMeshProUGUI>(true);
        so.ApplyModifiedProperties();

        return true;
    }

    // İlk sürüm menüye bir panel ve y = -500'de bir 3D sahne kurmuştu
    static void RemoveOldOverlay()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == "NestStage") Object.DestroyImmediate(root);
        }

        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.name != "Nest") continue;
            if (t.GetComponent<NestUI>() == null) continue;

            Object.DestroyImmediate(t.gameObject);
        }
    }

    static void CopyFromScene(Scene from, Scene to, string rootName)
    {
        foreach (GameObject root in from.GetRootGameObjects())
        {
            if (root.name != rootName) continue;

            GameObject copy = Object.Instantiate(root);
            copy.name = rootName;
            SceneManager.MoveGameObjectToScene(copy, to);
            return;
        }

        Debug.LogWarning($"[Yuva] Game sahnesinde '{rootName}' bulunamadı - Nest sahnesinde olmayacak.");
    }

    static void AddToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        foreach (EditorBuildSettingsScene s in scenes)
            if (s.path == path) return;

        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ==================== 3D RENDERER ====================

    // Yoksa oluşturur, pipeline'ın renderer listesine ekler, indeksini döner
    static int EnsureRenderer3D()
    {
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        if (pipeline == null)
        {
            EditorUtility.DisplayDialog("Yuva", "Aktif URP asset'i bulunamadı.", "Tamam");
            return -1;
        }

        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);

        if (data == null)
        {
            // URP'nin kendi "Create > Rendering > URP Universal Renderer" menüsünün yaptığı
            data = ScriptableObject.CreateInstance<UniversalRendererData>();
            data.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");

            AssetDatabase.CreateAsset(data, RendererPath);
            ResourceReloader.ReloadAllNullIn(data, UniversalRenderPipelineAsset.packagePath);
        }

        var so = new SerializedObject(pipeline);
        SerializedProperty list = so.FindProperty("m_RendererDataList");

        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) return i;

        list.arraySize++;
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(pipeline);
        return list.arraySize - 1;
    }

    // Bloom: lav çatlakları ve parlayan hatlar gerçekten ışık saçsın.
    // Sadece Nest sahnesindeki Volume'da - oyunun geri kalanı etkilenmiyor.
    static VolumeProfile EnsureGlowProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
        if (profile != null) return profile;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, VolumePath);

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(0.9f);
        bloom.scatter.Override(0.65f);
        bloom.tint.Override(new Color(1f, 0.85f, 0.75f));

        // Çeyrek çözünürlük: yumuşak parıltıda farkı görünmüyor, mobilde yarı maliyet
        bloom.downscale.Override(BloomDownscaleMode.Quarter);
        AssetDatabase.AddObjectToAsset(bloom, profile);

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.3f);
        vignette.smoothness.Override(0.5f);
        AssetDatabase.AddObjectToAsset(vignette, profile);

        EditorUtility.SetDirty(profile);
        return profile;
    }

    // ==================== MALZEMELER ====================

    sealed class Materials
    {
        public Material Scale, Belly, Wing, Eye, Horn, Egg, EggGlow, Stone, Lava;
    }

    static Materials CreateMaterials()
    {
        return new Materials
        {
            Scale = Lit("Pul", new Color(0.55f, 0.1f, 0.12f), 0.45f),
            Belly = Lit("Karin", new Color(0.95f, 0.55f, 0.25f), 0.3f),
            Wing = Lit("Kanat", new Color(0.3f, 0.08f, 0.28f), 0.2f),
            Eye = Lit("Goz", new Color(1f, 0.85f, 0.2f), 0.9f, new Color(1f, 0.7f, 0.1f) * 2.5f),
            Horn = Lit("Boynuz", new Color(0.85f, 0.8f, 0.7f), 0.5f),
            Egg = Lit("Yumurta", new Color(0.3f, 0.06f, 0.08f), 0.7f),
            EggGlow = Lit("YumurtaLav", new Color(1f, 0.45f, 0.1f), 0.5f, new Color(1f, 0.35f, 0.05f) * 3f),
            Stone = Lit("Kaide", new Color(0.22f, 0.2f, 0.27f), 0.15f),
            Lava = Lit("Lav", new Color(1f, 0.4f, 0.1f), 0.5f, new Color(1f, 0.3f, 0.05f) * 2.5f)
        };
    }

    static Material Lit(string name, Color color, float smoothness, Color? emission = null)
    {
        string path = $"{PlaceholderFolder}/{name}.mat";

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }

        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);

        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Parçacıklar: yumuşak nokta dokusu + Sprites/Default (URP'de ışıksız çalışıyor,
    // ek ayar istemiyor)
    static Material CreateParticleMaterial()
    {
        string texPath = $"{PlaceholderFolder}/YumusakNokta.png";

        if (!File.Exists(texPath))
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f - 0.5f, size / 2f - 0.5f)) / (size / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }

            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(texPath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        string matPath = $"{PlaceholderFolder}/Kivilcim.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        if (mat == null)
        {
            mat = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(mat, matPath);
        }

        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        EditorUtility.SetDirty(mat);

        return mat;
    }

    // ==================== EFEKTLER ====================

    static ParticleSystem MakeParticles(string name, Transform parent, Material mat, bool loop,
                                        int burst, float rate, float speedMin, float speedMax,
                                        float life, Color a, Color b, float sizeMin, float sizeMax,
                                        float gravity, float radius)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = loop;
        main.playOnAwake = loop;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 300;

        var emission = ps.emission;
        emission.rateOverTime = rate;
        if (burst > 0) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;

        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });

        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(fade);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;

        return ps;
    }

    // Son aşamaların sürekli efekti: ejderhanın etrafında yükselen közler
    static GameObject CreateEmbersPrefab(Material mat)
    {
        string path = $"{PlaceholderFolder}/Kozler.prefab";

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        var temp = new GameObject("Kozler");
        ParticleSystem ps = MakeParticles("Kozler", temp.transform, mat, loop: true, burst: 0, rate: 14f,
                                          speedMin: 0.2f, speedMax: 0.6f, life: 2.5f,
                                          a: new Color(1f, 0.55f, 0.15f), b: new Color(1f, 0.25f, 0.6f),
                                          sizeMin: 0.05f, sizeMax: 0.12f, gravity: -0.08f, radius: 1.1f);
        ps.transform.localPosition = new Vector3(0f, 0.8f, 0f);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
        Object.DestroyImmediate(temp);

        return prefab;
    }

    // ==================== EJDERHA ====================
    // İlkel şekillerden kaba bir ejderha. Amaç sadece döngüyü (besle, büyüt,
    // kutla) şimdiden denemek; Tripo modelleri gelince yerini alacak.
    // Kamera -Z tarafından bakıyor: ejderhanın yüzü -Z'ye dönük. Tripo
    // prefab'ları (TripoModels/EjderPrefabs) da bu yöne çevrilmiş durumda.

    // 7 aşama: yumurta, yavru, çocuk, ergen, genç, yetişkin, yaşlı. Geçici
    // modeller üç tane (yumurta, yavru, yetişkin); aşamalar boyutla ayrışıyor.
    // Asset zaten 7 aşamalıysa DOKUNULMUYOR - içine konan Tripo modelleri korunsun.
    static DragonDefinition EnsureDragon(Materials m, GameObject embers)
    {
        GameObject egg = SavePrefab("Yumurta", BuildEgg(m));
        GameObject baby = SavePrefab("Yavru", BuildDragon(m, baby: true));
        GameObject adult = SavePrefab("Yetiskin", BuildDragon(m, baby: false));

        string path = $"{DragonFolder}/Dragon_Kizil.asset";

        var dragon = AssetDatabase.LoadAssetAtPath<DragonDefinition>(path);
        if (dragon == null)
        {
            dragon = ScriptableObject.CreateInstance<DragonDefinition>();
            AssetDatabase.CreateAsset(dragon, path);
        }
        else if (dragon.StageCount == 7)
        {
            return dragon;
        }

        dragon.id = "kizil";

        // Maliyet 1-10-14-18-22-30 = 95 yumurta, level başına 1 yumurta.
        // Yumurtadan çıkmak 1 yumurta: ilk beslemede çıksın, oyuncu hemen
        // isim versin (bağ erken kurulsun). Sonra aralar uzuyor.
        dragon.stages = new[]
        {
            new DragonStage { name = "Yumurta", eggsToNext = 1, model = egg, scaleStart = 0.9f, scaleEnd = 1.1f },
            new DragonStage { name = "Yavru", eggsToNext = 10, model = baby, scaleStart = 0.65f, scaleEnd = 0.8f },
            new DragonStage { name = "Çocuk", eggsToNext = 14, model = baby, scaleStart = 0.9f, scaleEnd = 1.1f },
            new DragonStage { name = "Ergen", eggsToNext = 18, model = adult, scaleStart = 0.5f, scaleEnd = 0.6f },
            new DragonStage { name = "Genç", eggsToNext = 22, model = adult, scaleStart = 0.62f, scaleEnd = 0.75f },
            new DragonStage { name = "Yetişkin", eggsToNext = 30, model = adult, scaleStart = 0.8f, scaleEnd = 0.95f, ambientEffect = embers },
            new DragonStage { name = "Yaşlı", eggsToNext = 1, model = adult, scaleStart = 1.1f, scaleEnd = 1.1f, ambientEffect = embers }
        };

        EditorUtility.SetDirty(dragon);
        return dragon;
    }

    static GameObject SavePrefab(string name, GameObject temp)
    {
        string path = $"{PlaceholderFolder}/{name}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
        Object.DestroyImmediate(temp);
        return prefab;
    }

    static GameObject BuildEgg(Materials m)
    {
        var root = new GameObject("Yumurta");

        Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 0.58f, 0f), new Vector3(0.9f, 1.15f, 0.9f), Vector3.zero, m.Egg);

        // Kabuktaki lav benekleri
        Vector3[] spots =
        {
            new Vector3(0.28f, 0.75f, -0.3f), new Vector3(-0.3f, 0.5f, -0.28f),
            new Vector3(0.05f, 0.95f, -0.38f), new Vector3(-0.1f, 0.3f, -0.38f),
            new Vector3(0.35f, 0.35f, -0.15f)
        };

        foreach (Vector3 p in spots)
            Part(PrimitiveType.Sphere, root.transform, p, Vector3.one * 0.14f, Vector3.zero, m.EggGlow);

        return root;
    }

    static GameObject BuildDragon(Materials m, bool baby)
    {
        var root = new GameObject(baby ? "Yavru" : "Yetiskin");
        Transform t = root.transform;

        if (baby)
        {
            // Yavru: iri kafa, kısa gövde, küçük kanatlar
            Part(PrimitiveType.Sphere, t, new Vector3(0f, 0.45f, 0.05f), new Vector3(0.85f, 0.7f, 1f), Vector3.zero, m.Scale);
            Part(PrimitiveType.Sphere, t, new Vector3(0f, 0.4f, -0.25f), new Vector3(0.6f, 0.5f, 0.5f), Vector3.zero, m.Belly);
            Part(PrimitiveType.Sphere, t, new Vector3(0f, 0.98f, -0.4f), Vector3.one * 0.72f, Vector3.zero, m.Scale);
            Part(PrimitiveType.Sphere, t, new Vector3(0f, 0.88f, -0.75f), new Vector3(0.38f, 0.28f, 0.35f), Vector3.zero, m.Scale);
            Part(PrimitiveType.Sphere, t, new Vector3(0.17f, 1.05f, -0.7f), Vector3.one * 0.17f, Vector3.zero, m.Eye);
            Part(PrimitiveType.Sphere, t, new Vector3(-0.17f, 1.05f, -0.7f), Vector3.one * 0.17f, Vector3.zero, m.Eye);
            Part(PrimitiveType.Capsule, t, new Vector3(0.18f, 1.35f, -0.3f), new Vector3(0.08f, 0.14f, 0.08f), new Vector3(-30f, 0f, -15f), m.Horn);
            Part(PrimitiveType.Capsule, t, new Vector3(-0.18f, 1.35f, -0.3f), new Vector3(0.08f, 0.14f, 0.08f), new Vector3(-30f, 0f, 15f), m.Horn);
            Part(PrimitiveType.Cube, t, new Vector3(0.45f, 0.75f, 0.15f), new Vector3(0.04f, 0.35f, 0.45f), new Vector3(0f, 0f, -35f), m.Wing);
            Part(PrimitiveType.Cube, t, new Vector3(-0.45f, 0.75f, 0.15f), new Vector3(0.04f, 0.35f, 0.45f), new Vector3(0f, 0f, 35f), m.Wing);
            Part(PrimitiveType.Capsule, t, new Vector3(0f, 0.25f, 0.55f), new Vector3(0.18f, 0.35f, 0.18f), new Vector3(70f, 0f, 0f), m.Scale);
            Legs(t, m, 0.28f, 0.22f, 0.3f, 0.12f);
        }
        else
        {
            // Yetişkin: uzun boyun, geniş kanatlar, sırtında lav dikenleri
            Part(PrimitiveType.Capsule, t, new Vector3(0f, 0.95f, 0.15f), new Vector3(0.85f, 0.75f, 0.85f), new Vector3(90f, 0f, 0f), m.Scale);
            Part(PrimitiveType.Sphere, t, new Vector3(0f, 0.85f, -0.3f), new Vector3(0.7f, 0.65f, 0.6f), Vector3.zero, m.Belly);
            Part(PrimitiveType.Capsule, t, new Vector3(0f, 1.55f, -0.55f), new Vector3(0.32f, 0.5f, 0.32f), new Vector3(-35f, 0f, 0f), m.Scale);
            Part(PrimitiveType.Sphere, t, new Vector3(0f, 2.05f, -0.85f), new Vector3(0.5f, 0.45f, 0.65f), Vector3.zero, m.Scale);
            Part(PrimitiveType.Sphere, t, new Vector3(0f, 1.95f, -1.2f), new Vector3(0.3f, 0.22f, 0.35f), Vector3.zero, m.Scale);
            Part(PrimitiveType.Sphere, t, new Vector3(0.15f, 2.12f, -1.05f), Vector3.one * 0.1f, Vector3.zero, m.Eye);
            Part(PrimitiveType.Sphere, t, new Vector3(-0.15f, 2.12f, -1.05f), Vector3.one * 0.1f, Vector3.zero, m.Eye);
            Part(PrimitiveType.Capsule, t, new Vector3(0.16f, 2.35f, -0.65f), new Vector3(0.08f, 0.25f, 0.08f), new Vector3(-50f, 0f, -12f), m.Horn);
            Part(PrimitiveType.Capsule, t, new Vector3(-0.16f, 2.35f, -0.65f), new Vector3(0.08f, 0.25f, 0.08f), new Vector3(-50f, 0f, 12f), m.Horn);

            // Kanatlar: kemik + zar
            Part(PrimitiveType.Cube, t, new Vector3(0.95f, 1.75f, 0.25f), new Vector3(0.04f, 1.1f, 1.3f), new Vector3(0f, 15f, -50f), m.Wing);
            Part(PrimitiveType.Cube, t, new Vector3(-0.95f, 1.75f, 0.25f), new Vector3(0.04f, 1.1f, 1.3f), new Vector3(0f, -15f, 50f), m.Wing);
            Part(PrimitiveType.Capsule, t, new Vector3(0.9f, 2.05f, -0.2f), new Vector3(0.07f, 0.65f, 0.07f), new Vector3(0f, 0f, -55f), m.Horn);
            Part(PrimitiveType.Capsule, t, new Vector3(-0.9f, 2.05f, -0.2f), new Vector3(0.07f, 0.65f, 0.07f), new Vector3(0f, 0f, 55f), m.Horn);

            // Kuyruk: üç parça, geriye ve aşağı
            Part(PrimitiveType.Capsule, t, new Vector3(0f, 0.75f, 0.95f), new Vector3(0.3f, 0.4f, 0.3f), new Vector3(70f, 0f, 0f), m.Scale);
            Part(PrimitiveType.Capsule, t, new Vector3(0f, 0.45f, 1.5f), new Vector3(0.2f, 0.35f, 0.2f), new Vector3(80f, 0f, 0f), m.Scale);
            Part(PrimitiveType.Cube, t, new Vector3(0f, 0.35f, 1.95f), new Vector3(0.05f, 0.25f, 0.25f), new Vector3(45f, 0f, 0f), m.Lava);

            // Sırt dikenleri
            for (int i = 0; i < 4; i++)
                Part(PrimitiveType.Cube, t, new Vector3(0f, 1.45f - i * 0.08f, -0.2f + i * 0.3f), Vector3.one * 0.14f, new Vector3(45f, 0f, 0f), m.Lava);

            Legs(t, m, 0.4f, 0.38f, 0.55f, 0.18f);
        }

        return root;
    }

    static void Legs(Transform t, Materials m, float x, float front, float back, float thick)
    {
        float h = thick * 1.6f;

        Part(PrimitiveType.Capsule, t, new Vector3(x, h, -front), new Vector3(thick, h, thick), Vector3.zero, m.Scale);
        Part(PrimitiveType.Capsule, t, new Vector3(-x, h, -front), new Vector3(thick, h, thick), Vector3.zero, m.Scale);
        Part(PrimitiveType.Capsule, t, new Vector3(x, h, back), new Vector3(thick, h, thick), Vector3.zero, m.Scale);
        Part(PrimitiveType.Capsule, t, new Vector3(-x, h, back), new Vector3(thick, h, thick), Vector3.zero, m.Scale);
    }

    static GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);

        // Tıklanacak bir şey yok; collider sadece fizik maliyeti
        Object.DestroyImmediate(go.GetComponent<Collider>());

        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.transform.localEulerAngles = euler;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;

        return go;
    }

    // ==================== NEST SAHNESİ ====================

    // Mor gökyüzü, loş ufuk, karanlık zemin - gökyüzü materyali yok
    static void SetupLighting()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.35f, 0.25f, 0.5f);
        RenderSettings.ambientEquatorColor = new Color(0.2f, 0.14f, 0.26f);
        RenderSettings.ambientGroundColor = new Color(0.07f, 0.05f, 0.1f);
        RenderSettings.fog = false;
    }

    static DragonView BuildStage(Materials m, Material particleMat, int rendererIndex, VolumeProfile glow,
                                 out Camera camera, out Transform eggTarget)
    {
        var stage = new GameObject("Stage");

        // Kaide
        Part(PrimitiveType.Cylinder, stage.transform, new Vector3(0f, -0.1f, 0f), new Vector3(2.6f, 0.1f, 2.6f), Vector3.zero, m.Stone);
        Part(PrimitiveType.Cylinder, stage.transform, new Vector3(0f, -0.14f, 0f), new Vector3(2.8f, 0.06f, 2.8f), Vector3.zero, m.Lava);

        // Işıklar: sıcak ana ışık, arkadan mor kontur, önden serin dolgu
        Light(stage.transform, "AnaIsik", LightType.Directional, new Vector3(0f, 3f, -3f), new Vector3(35f, -30f, 0f),
              new Color(1f, 0.88f, 0.75f), 1.2f, 0f);
        Light(stage.transform, "KonturIsik", LightType.Point, new Vector3(0f, 2.6f, 2.4f), Vector3.zero,
              new Color(0.7f, 0.3f, 1f), 6f, 8f);
        Light(stage.transform, "DolguIsik", LightType.Point, new Vector3(-2.5f, 1.4f, -2.2f), Vector3.zero,
              new Color(0.45f, 0.6f, 1f), 2f, 8f);

        // Ejderha: model buraya konuyor. Üç çeyrek açı - dümdüz bakan ejderha cansız durur.
        var viewGo = new GameObject("Dragon");
        viewGo.transform.SetParent(stage.transform, false);

        var modelRoot = new GameObject("ModelRoot").transform;
        modelRoot.SetParent(viewGo.transform, false);
        // Modeller yüzü kameraya (-Z) dönük geliyor; açı prefab'ların kendisinde
        modelRoot.localEulerAngles = Vector3.zero;

        var effectRoot = new GameObject("Efektler").transform;
        effectRoot.SetParent(viewGo.transform, false);

        eggTarget = new GameObject("YumurtaHedefi").transform;
        eggTarget.SetParent(viewGo.transform, false);
        eggTarget.localPosition = new Vector3(0f, 0.9f, 0f);

        ParticleSystem burst = MakeParticles("AsamaPatlamasi", effectRoot, particleMat, loop: false, burst: 90, rate: 0f,
                                             speedMin: 2.5f, speedMax: 6f, life: 1.2f,
                                             a: new Color(1f, 0.8f, 0.3f), b: new Color(0.8f, 0.4f, 1f),
                                             sizeMin: 0.08f, sizeMax: 0.25f, gravity: 0.4f, radius: 0.4f);
        burst.transform.localPosition = new Vector3(0f, 1f, 0f);

        ParticleSystem feed = MakeParticles("BeslemeKivilcimi", effectRoot, particleMat, loop: false, burst: 18, rate: 0f,
                                            speedMin: 1f, speedMax: 2.5f, life: 0.6f,
                                            a: new Color(1f, 0.6f, 0.2f), b: new Color(1f, 0.9f, 0.5f),
                                            sizeMin: 0.05f, sizeMax: 0.14f, gravity: 0.6f, radius: 0.25f);
        feed.transform.localPosition = new Vector3(0f, 0.9f, -0.4f);

        // Kamera: portre ekranda en büyük ejderhanın kanatları sığsın diye geniş
        // açı. Yakın durmuyor - ejderha büyüdükçe kareyi doldursun.
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.position = new Vector3(0f, 1.9f, -6.2f);
        camGo.transform.LookAt(new Vector3(0f, 0.55f, 0f));

        camera = camGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.06f, 0.04f, 0.1f);
        camera.fieldOfView = 55f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 80f;

        camGo.AddComponent<AudioListener>();

        var camData = camera.GetUniversalAdditionalCameraData();
        camData.SetRenderer(rendererIndex);
        camData.renderPostProcessing = true;

        var volumeGo = new GameObject("Isik Efektleri");
        var volume = volumeGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = glow;

        DragonView view = viewGo.AddComponent<DragonView>();

        var so = new SerializedObject(view);
        so.FindProperty("modelRoot").objectReferenceValue = modelRoot;
        so.FindProperty("effectRoot").objectReferenceValue = effectRoot;
        so.FindProperty("stageUpBurst").objectReferenceValue = burst;
        so.FindProperty("feedBurst").objectReferenceValue = feed;
        so.FindProperty("stageCamera").objectReferenceValue = camera;
        so.ApplyModifiedPropertiesWithoutUndo();

        return view;
    }

    static void Light(Transform parent, string name, LightType type, Vector3 pos, Vector3 euler,
                      Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localEulerAngles = euler;

        var light = go.AddComponent<Light>();
        light.type = type;
        light.color = color;
        light.intensity = intensity;
        if (range > 0f) light.range = range;
        light.shadows = LightShadows.None;
    }

    // ==================== ARAYÜZ ====================

    static void BuildUI(DragonDefinition dragon, DragonView view, Camera camera, Transform eggTarget)
    {
        Sprite backdrop = LoadSprite("Assets/Sprites/ComfyUI_00077_.png");
        Sprite card = LoadSprite("Assets/Sprites/PanelBackground.png");
        Sprite pill = LoadSprite("Assets/Sprites/CurrencyBackground.png");
        Sprite buttonSprite = LoadSprite("Assets/Sprites/YazıButonu.png");
        Sprite exitSprite = LoadSprite("Assets/Sprites/exit.png");
        Sprite eggSprite = LoadSprite("Assets/Sprites/ComfyUI_00017_ (1).png");

        // --- Arka plan: kameranın arkasında, 3D'nin gerisinde duran bir tuval.
        // Screen Space - Camera: 3D ejderha önünde, kale görseli arkasında çiziliyor.
        Canvas back = MakeCanvas("Arkaplan", RenderMode.ScreenSpaceCamera, -10);
        back.worldCamera = camera;
        back.planeDistance = 60f;

        RectTransform backImg = Rect("Gorsel", back.transform);
        Stretch(backImg);
        Image bg = backImg.gameObject.AddComponent<Image>();
        bg.sprite = backdrop;
        bg.color = new Color(0.8f, 0.72f, 0.9f);
        bg.raycastTarget = false;

        // --- Arayüz ---
        Canvas ui = MakeCanvas("Canvas", RenderMode.ScreenSpaceOverlay, 0);
        ui.gameObject.AddComponent<GraphicRaycaster>();

        // Parmakla çevirme: en altta, şeffaf, tam ekran. Butonlar ve kart
        // üstünde kaldığı için onların dokunuşu buraya düşmüyor.
        RectTransform spinRt = Rect("DonmeAlani", ui.transform);
        Stretch(spinRt);
        Image spinImg = spinRt.gameObject.AddComponent<Image>();
        spinImg.color = new Color(0f, 0f, 0f, 0f);

        // Aynı alan pinch ve tekerleği de alıyor; kamerayı DragonZoom sürüyor
        DragonZoom zoom = camera.gameObject.AddComponent<DragonZoom>();
        var zoomSo = new SerializedObject(zoom);
        zoomSo.FindProperty("view").objectReferenceValue = view;
        zoomSo.ApplyModifiedPropertiesWithoutUndo();

        DragonSpinArea spin = spinRt.gameObject.AddComponent<DragonSpinArea>();
        var spinSo = new SerializedObject(spin);
        spinSo.FindProperty("view").objectReferenceValue = view;
        spinSo.FindProperty("zoom").objectReferenceValue = zoom;
        spinSo.ApplyModifiedPropertiesWithoutUndo();

        RectTransform safe = Rect("Safe", ui.transform);
        Stretch(safe);
        safe.gameObject.AddComponent<SafeArea>();

        // Üst sol: geri
        Button close = MakeButton("BackButton", safe, exitSprite, null, new Vector2(0f, 1f), new Vector2(100f, -100f), new Vector2(130f, 130f));

        // Üst sağ: yumurta sayacı
        RectTransform counter = Rect("EggCounter", safe);
        Anchor(counter, new Vector2(1f, 1f), new Vector2(-170f, -100f), new Vector2(280f, 100f));
        Image counterBg = counter.gameObject.AddComponent<Image>();
        counterBg.sprite = pill;
        counterBg.type = Image.Type.Sliced;

        RectTransform counterIcon = Rect("Icon", counter);
        Anchor(counterIcon, new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(110f, 110f));
        Image iconImg = counterIcon.gameObject.AddComponent<Image>();
        iconImg.sprite = eggSprite;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;

        TextMeshProUGUI eggCount = Text("Count", counter, "0", 48, FontStyles.Bold, TextAlignmentOptions.Right);
        Anchor(eggCount.rectTransform, new Vector2(1f, 0.5f), new Vector2(-95f, 0f), new Vector2(150f, 90f));

        // Ad ve aşama
        // Oyuncu isim verene kadar "???" - isim yumurtadan çıkınca soruluyor
        TextMeshProUGUI nameText = Text("NameText", safe, "???", 66, FontStyles.Bold, TextAlignmentOptions.Center);
        Anchor(nameText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(900f, 90f));

        TextMeshProUGUI stageText = Text("StageText", safe, "Yumurta", 46, FontStyles.Normal, TextAlignmentOptions.Center);
        stageText.color = new Color(1f, 0.8f, 0.35f);
        Anchor(stageText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -330f), new Vector2(900f, 70f));

        // Aşama kutlaması - ekranın ortasında
        TextMeshProUGUI stageUp = Text("StageUpText", safe, "YAVRU!", 130, FontStyles.Bold, TextAlignmentOptions.Center);
        stageUp.color = new Color(1f, 0.82f, 0.3f);
        Anchor(stageUp.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(1000f, 200f));
        stageUp.gameObject.SetActive(false);

        // Alt kart: ilerleme + butonlar
        RectTransform cardRt = Rect("Card", safe);
        cardRt.anchorMin = new Vector2(0f, 0f);
        cardRt.anchorMax = new Vector2(1f, 0f);
        cardRt.pivot = new Vector2(0.5f, 0f);
        cardRt.sizeDelta = new Vector2(-80f, 470f);
        cardRt.anchoredPosition = new Vector2(0f, 50f);
        Image cardImg = cardRt.gameObject.AddComponent<Image>();
        cardImg.sprite = card;
        cardImg.type = Image.Type.Sliced;

        RectTransform barBack = Rect("ProgressBack", cardRt);
        Anchor(barBack, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(820f, 80f));
        Image barBackImg = barBack.gameObject.AddComponent<Image>();
        barBackImg.sprite = pill;
        barBackImg.type = Image.Type.Sliced;
        barBackImg.color = new Color(0.5f, 0.45f, 0.55f);

        RectTransform barFill = Rect("ProgressFill", barBack);
        Stretch(barFill);
        barFill.sizeDelta = new Vector2(-16f, -16f);
        Image fill = barFill.gameObject.AddComponent<Image>();
        fill.sprite = pill;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillAmount = 0f;
        fill.color = new Color(1f, 0.6f, 0.2f);

        TextMeshProUGUI progress = Text("ProgressText", barBack, "0 / 3", 42, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(progress.rectTransform);

        TextMeshProUGUI hint = Text("HintText", cardRt, "", 34, FontStyles.Normal, TextAlignmentOptions.Center);
        hint.color = new Color(1f, 1f, 1f, 0.8f);
        Anchor(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -195f), new Vector2(900f, 60f));

        Button feed = MakeButton("FeedButton", cardRt, buttonSprite, "BESLE", new Vector2(0.5f, 0f), new Vector2(-210f, 110f), new Vector2(380f, 150f));
        Button feedAll = MakeButton("FeedAllButton", cardRt, buttonSprite, "TÜMÜNÜ VER", new Vector2(0.5f, 0f), new Vector2(210f, 110f), new Vector2(380f, 150f));

        // Uçan yumurtalar arayüzün üstünde
        RectTransform flyLayer = Rect("UcanYumurtalar", ui.transform);
        Stretch(flyLayer);

        // İsim penceresi: arayüzün üstünde, parlama ve perdenin altında
        DragonNamePanel namePanel = BuildNamePanel(ui.transform);

        // Aşama atlarken ekran parlasın
        RectTransform flashRt = Rect("Flash", ui.transform);
        Stretch(flashRt);
        Image flash = flashRt.gameObject.AddComponent<Image>();
        flash.color = new Color(1f, 0.95f, 0.85f, 0f);
        flash.raycastTarget = false;
        flashRt.gameObject.SetActive(false);

        // Sahne geçişi: en üstte siyah perde
        RectTransform fadeRt = Rect("Fade", ui.transform);
        Stretch(fadeRt);
        Image fade = fadeRt.gameObject.AddComponent<Image>();
        fade.color = Color.black;

        // Olay sistemi: proje yeni Input System'i kullanıyor
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        // --- Bağlantılar ---
        NestUI nest = new GameObject("Nest").AddComponent<NestUI>();

        var so = new SerializedObject(nest);
        so.FindProperty("dragon").objectReferenceValue = dragon;
        so.FindProperty("view").objectReferenceValue = view;
        so.FindProperty("eggTarget").objectReferenceValue = eggTarget;
        so.FindProperty("sceneCamera").objectReferenceValue = camera;
        so.FindProperty("backButton").objectReferenceValue = close;
        so.FindProperty("fade").objectReferenceValue = fade;
        so.FindProperty("nameText").objectReferenceValue = nameText;
        so.FindProperty("stageText").objectReferenceValue = stageText;
        so.FindProperty("progressText").objectReferenceValue = progress;
        so.FindProperty("eggCountText").objectReferenceValue = eggCount;
        so.FindProperty("hintText").objectReferenceValue = hint;
        so.FindProperty("progressFill").objectReferenceValue = fill;
        so.FindProperty("feedButton").objectReferenceValue = feed;
        so.FindProperty("feedAllButton").objectReferenceValue = feedAll;
        so.FindProperty("eggSprite").objectReferenceValue = eggSprite;
        so.FindProperty("eggSource").objectReferenceValue = counterIcon;
        so.FindProperty("flyLayer").objectReferenceValue = flyLayer;
        so.FindProperty("stageUpText").objectReferenceValue = stageUp;
        so.FindProperty("flash").objectReferenceValue = flash;
        so.FindProperty("namePanel").objectReferenceValue = namePanel;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ==================== İSİM PENCERESİ ====================

    // Sadece isim penceresini açık Nest sahnesine ekler - sahnenin geri kalanına
    // (elle ayarlanmış kamera, ejderha boyutu...) dokunmuyor. Varsa yenisiyle
    // değiştiriyor. Undo'ya kayıtlı.
    [MenuItem("Match3/Yuva/İsim penceresini kur (Nest açıkken)")]
    static void BuildNamePanelOnly()
    {
        Scene scene = SceneManager.GetActiveScene();

        if (scene.name != "Nest")
        {
            EditorUtility.DisplayDialog("Yuva", "Önce Nest sahnesini aç.", "Tamam");
            return;
        }

        NestUI nest = Object.FindAnyObjectByType<NestUI>(FindObjectsInactive.Include);
        Canvas ui = FindByName<Canvas>("Canvas");

        if (nest == null || ui == null)
        {
            EditorUtility.DisplayDialog("Yuva", "Nest sahnesinde NestUI ya da 'Canvas' yok - önce Match3 -> Yuva -> Kur.", "Tamam");
            return;
        }

        DragonNamePanel old = Object.FindAnyObjectByType<DragonNamePanel>(FindObjectsInactive.Include);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        DragonNamePanel panel = BuildNamePanel(ui.transform);
        Undo.RegisterCreatedObjectUndo(panel.gameObject, "İsim penceresi");

        // Parlamanın ve perdenin altına: ikisi hep en üstte kalmalı
        Transform flash = ui.transform.Find("Flash");
        if (flash != null) panel.transform.SetSiblingIndex(flash.GetSiblingIndex());

        var so = new SerializedObject(nest);
        so.FindProperty("namePanel").objectReferenceValue = panel;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorUtility.DisplayDialog("Yuva", "İsim penceresi kuruldu. Sahneyi kaydet (Ctrl+S).", "Tamam");
    }

    // Tam ekran karartma + ortada kutu: başlık, isim alanı, uyarı, TAMAM.
    // Kapalı kuruluyor, NestUI yumurtadan çıkınca açıyor.
    static DragonNamePanel BuildNamePanel(Transform parent)
    {
        Sprite card = LoadSprite("Assets/Sprites/PanelBackground.png");
        Sprite pill = LoadSprite("Assets/Sprites/CurrencyBackground.png");
        Sprite buttonSprite = LoadSprite("Assets/Sprites/YazıButonu.png");

        // Kök: karartma. Raycast açık - arkadaki butonlara ve döndürmeye dokunuş geçmesin
        RectTransform root = Rect("NamePanel", parent);
        Stretch(root);
        Image dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.65f);

        // Ekranın üst yarısında: telefonda klavye alt yarıyı kaplıyor
        RectTransform box = Rect("Box", root);
        Anchor(box, new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(940f, 700f));
        Image boxImg = box.gameObject.AddComponent<Image>();
        boxImg.sprite = card;
        boxImg.type = Image.Type.Sliced;

        TextMeshProUGUI title = Text("Title", box, "Ejderhan yumurtadan çıktı!", 56, FontStyles.Bold, TextAlignmentOptions.Center);
        title.color = new Color(1f, 0.82f, 0.3f);
        Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(860f, 80f));

        TextMeshProUGUI subtitle = Text("Subtitle", box, "Ona bir isim ver", 44, FontStyles.Normal, TextAlignmentOptions.Center);
        Anchor(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(860f, 60f));

        TMP_InputField input = MakeInputField("NameInput", box, pill, "İsim yaz...");
        Anchor((RectTransform)input.transform, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(780f, 130f));

        TextMeshProUGUI hint = Text("Hint", box, "", 36, FontStyles.Normal, TextAlignmentOptions.Center);
        hint.color = new Color(1f, 0.55f, 0.45f);
        Anchor(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -400f), new Vector2(860f, 50f));
        hint.gameObject.SetActive(false);

        TextMeshProUGUI note = Text("Note", box, "İsim sonradan değiştirilemez", 34, FontStyles.Italic, TextAlignmentOptions.Center);
        note.color = new Color(1f, 1f, 1f, 0.65f);
        Anchor(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 225f), new Vector2(860f, 50f));

        Button ok = MakeButton("ConfirmButton", box, buttonSprite, "TAMAM", new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(380f, 150f));

        DragonNamePanel panel = root.gameObject.AddComponent<DragonNamePanel>();

        var so = new SerializedObject(panel);
        so.FindProperty("box").objectReferenceValue = box;
        so.FindProperty("nameInput").objectReferenceValue = input;
        so.FindProperty("confirmButton").objectReferenceValue = ok;
        so.FindProperty("hintText").objectReferenceValue = hint;
        so.ApplyModifiedPropertiesWithoutUndo();

        root.gameObject.SetActive(false);
        return panel;
    }

    // TMP_InputField'ı parça parça kurar: arka plan, maskeli metin alanı,
    // yer tutucu, metin. TMP'nin menüden eklediğiyle aynı yapı.
    static TMP_InputField MakeInputField(string name, Transform parent, Sprite background, string placeholder)
    {
        RectTransform rt = Rect(name, parent);
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = background;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.22f, 0.18f, 0.3f);

        RectTransform area = Rect("Text Area", rt);
        Stretch(area);
        area.offsetMin = new Vector2(30f, 12f);
        area.offsetMax = new Vector2(-30f, -12f);
        area.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI ph = Text("Placeholder", area, placeholder, 50, FontStyles.Italic, TextAlignmentOptions.Center);
        ph.color = new Color(1f, 1f, 1f, 0.4f);
        Stretch(ph.rectTransform);

        TextMeshProUGUI text = Text("Text", area, "", 54, FontStyles.Bold, TextAlignmentOptions.Center);
        text.richText = false;
        Stretch(text.rectTransform);

        TMP_InputField input = rt.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = bg;
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = ph;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.richText = false;
        input.characterLimit = SaveSystem.MaxDragonNameLength;

        Navigation nav = input.navigation;
        nav.mode = Navigation.Mode.None;
        input.navigation = nav;

        return input;
    }

    // Ana menüyle aynı ölçekleme: 1080x1920, genişliğe göre
    static Canvas MakeCanvas(string name, RenderMode mode, int order)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = mode;
        canvas.sortingOrder = order;

        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0f;

        return canvas;
    }

    // ==================== YARDIMCILAR ====================

    static T FindByName<T>(string name) where T : Component
    {
        foreach (T c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.name == name) return c;

        return null;
    }

    static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static TextMeshProUGUI Text(string name, Transform parent, string text, float size,
                                FontStyles style, TextAlignmentOptions align)
    {
        RectTransform rt = Rect(name, parent);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }

    static Button MakeButton(string name, Transform parent, Sprite sprite, string label,
                             Vector2 anchor, Vector2 pos, Vector2 size)
    {
        RectTransform rt = Rect(name, parent);
        Anchor(rt, anchor, pos, size);

        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;

        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;

        if (!string.IsNullOrEmpty(label))
        {
            TextMeshProUGUI tmp = Text("Text", rt, label, 44, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(tmp.rectTransform);
        }

        return button;
    }
}
