using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Build almadan önce: Match3 -> Build'e Hazırla
//
// Editördeki bütün kayıtları siler, oyun ilk kez açılıyormuş gibi sıfırdan
// başlar. Test menülerinin (+100 yumurta, ejderha testi...) bıraktığı her şey
// gider. Sonra build'in ilk sahnesini açar: Play'e basınca da oradan başlasın.
//
// Kayıtların hepsi PlayerPrefs'te (SaveSystem) - ayrı dosya yok. Liderlik
// tablosunun anonim oturumu da PlayerPrefs'te; o da siliniyor, bir sonraki
// açılışta yeni oyuncu olarak giriş yapılıyor.
//
// Telefondaki kayıt telefonda duruyor. "Bilgisayar + telefon" seçilirse build'e
// bir damga gömülüyor (SaveResetStamp.cs): telefon o build'i ilk açtığında
// kayıtları bir kez siliyor. Mağaza build'i için "Sadece bilgisayar" - damga
// boşalıyor, telefondaki kayda dokunulmuyor.
public static class BuildPrep
{
    const string Title = "Build'e Hazırla";
    const string StampPath = "Assets/Scripts/SaveResetStamp.cs";

    [MenuItem("Match3/Build'e Hazırla", priority = 0)]
    static void Prepare()
    {
        // Oyun açıkken silinen kayıtları, kapanırken sahnedeki objeler geri yazabilir
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(Title, "Önce Play modundan çık.", "Tamam");
            return;
        }

        // 0 = Bilgisayar + telefon, 1 = Vazgeç, 2 = Sadece bilgisayar
        int choice = EditorUtility.DisplayDialogComplex(Title,
            "TÜM kayıtlar silinecek: level, altın, can, ejder yumurtası, power-up'lar, " +
            "günlük hediyeler, ejderha ve adı, oyuncu adı, liderlik oturumu.\n\n" +
            "BİLGİSAYAR + TELEFON: Bundan sonra alacağın build, telefonda ilk açıldığında " +
            "oradaki kaydı da siler. Test build'i için bunu seç.\n\n" +
            "SADECE BİLGİSAYAR: Telefondaki kayda dokunulmaz.\n\n" +
            "UYARI: Oyun mağazadayken güncelleme build'inde 'Bilgisayar + telefon' SEÇME - " +
            "güncellemeyi alan herkesin ilerlemesi silinir.",
            "Bilgisayar + telefon", "Vazgeç", "Sadece bilgisayar");

        if (choice == 1) return;

        bool resetPhone = choice == 0;

        SaveSystem.ClearAll();
        Debug.Log("[Build] Bütün kayıtlar silindi - oyun sıfırdan başlayacak.");

        // Yeni damga = telefon bu build'i "yeni" görüp bir kez silsin.
        // Boş damga = özellik kapalı.
        WriteResetStamp(resetPhone ? Guid.NewGuid().ToString("N") : string.Empty);

        ApplyPerformanceSettings();
        OpenFirstScene();

        EditorUtility.DisplayDialog(Title,
            resetPhone
                ? "Kayıtlar silindi. Şimdi alacağın build telefonda ilk açıldığında oradaki " +
                  "kaydı da silecek - eski kurulumun üstüne yükleyebilirsin.\n\nPerformans ayarları da uygulandı."
                : "Bu bilgisayardaki kayıtlar silindi. Telefondaki kayda dokunulmayacak.\n\n" +
                  "Performans ayarları da uygulandı.",
            "Tamam");
    }

    static void WriteResetStamp(string stamp)
    {
        string code =
            "// OTOMATİK YAZILIYOR - elle değiştirme. Match3 -> Build'e Hazırla yazıyor.\n" +
            "//\n" +
            "// Boş değilse bu build, telefonda ilk açılışta bütün kayıtları BİR KEZ siler\n" +
            "// (SaveSystem.ApplyBuildReset) - test build'i telefonda sıfırdan başlasın diye.\n" +
            "//\n" +
            "// MAĞAZAYA ÇIKACAK BUILD'DE BOŞ OLMALI: yoksa güncellemeyi alan herkesin\n" +
            "// ilerlemesi silinir. Build'e Hazırla -> \"Sadece bilgisayar\" boşaltır; dolu\n" +
            "// damgayla build alırken de ayrıca sorulur (BuildPrep).\n" +
            "public static class SaveResetStamp\n" +
            "{\n" +
            "    public const string Value = \"" + stamp + "\";\n" +
            "}\n";

        File.WriteAllText(StampPath, code, new System.Text.UTF8Encoding(false));

        // Script değişti: Unity yeniden derleyecek, build yeni damgayla çıkacak
        AssetDatabase.ImportAsset(StampPath);
    }

    // Android build'inin akıcı çalışması için gereken oyuncu ayarları. Çoğu
    // zaten doğruydu; burada her build öncesi garanti altına alınıyor ki biri
    // Player Settings'te farkında olmadan değiştirirse build yavaşlamasın.
    // Çalışma anındaki ayarlar (60 FPS, fizik) AppPerformance'ta.
    [MenuItem("Match3/Performans Ayarlarını Uygula", priority = 1)]
    static void ApplyPerformanceSettings()
    {
        var android = NamedBuildTarget.Android;

        // IL2CPP, "hızlı çalışsın" diye derlensin. "Küçük build" modu
        // (OptimizeSize) daha yavaş kod üretiyor - projede o seçiliydi.
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetIl2CppCodeGeneration(android, Il2CppCodeGeneration.OptimizeSpeed);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // Kare zamanlaması (Swappy): 60 FPS'te bile kareler eşit aralıkla
        // gösterilmezse akış takılıyor gibi görünür
        PlayerSettings.Android.optimizedFramePacing = true;

        // Çöp toplama parça parça: tek seferde uzun duraklama (kasma) olmasın
        PlayerSettings.gcIncremental = true;

        // Oyun verisinin sıkıştırması (LZ4HC önerilir) koddan ayarlanamıyor -
        // Unity'de o API internal. Build Profiles -> Android -> Compression Method.

        AssetDatabase.SaveAssets();
        Debug.Log("[Build] Performans ayarları uygulandı: IL2CPP (hızlı), ARM64, frame pacing, artımlı GC.");
    }

    // Build'in ilk sahnesi (Build Settings'te en üstteki açık sahne)
    static void OpenFirstScene()
    {
        EditorBuildSettingsScene first = Array.Find(EditorBuildSettings.scenes, s => s.enabled);

        if (first == null)
        {
            Debug.LogWarning("[Build] Build Settings'te açık sahne yok.");
            return;
        }

        if (EditorSceneManager.GetActiveScene().path == first.path) return;

        // Kaydedilmemiş değişiklik varsa sor; "Vazgeç" derse sahneye dokunma
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(first.path);
    }
}

// Dolu damgayla build alınırken sor: test için mi, mağaza için mi?
// Mağazaya giden güncelleme bu damgayı taşırsa herkesin kaydı silinir.
public class BuildResetStampCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (string.IsNullOrEmpty(SaveResetStamp.Value) || Application.isBatchMode) return;

        bool go = EditorUtility.DisplayDialog("Build'e Hazırla",
            "Bu build telefonda ilk açıldığında KAYITLARI SİLECEK (test damgası var).\n\n" +
            "Test için alıyorsan devam et. Mağazaya yükleyeceksen İPTAL et, " +
            "Match3 -> Build'e Hazırla -> 'Sadece bilgisayar' seç ve öyle build al.",
            "Devam (test build'i)", "İptal");

        if (!go) throw new BuildFailedException("Build iptal edildi: kayıt silme damgası taşıyordu.");
    }
}
