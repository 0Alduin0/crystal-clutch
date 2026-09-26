using UnityEngine;

// Development build'de sol üstte FPS ve son bir saniyenin EN UZUN karesi.
// "60'ın altına düşüyor mu, nerede takılıyor" telefonda görülsün diye:
// ortalama 60 görünüp tek bir 80 ms'lik kare (kasma) gözden kaçmasın.
// 60 FPS = kare başına ~16.7 ms; en uzun kare 25 ms'yi geçerse kırmızı.
//
// Normal (release) build'de ve editörde hiç oluşmuyor - sıfır maliyet.
// Görmek için: Build Profiles / Build Settings -> Development Build işaretli build al.
public class FpsOverlay : MonoBehaviour
{
    const float Window = 1f;
    const float SlowFrame = 0.025f;

    int frames;
    float elapsed;
    float worst;
    float shownWorst;
    string text = string.Empty;
    GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (!Debug.isDebugBuild || Application.isEditor) return;

        var go = new GameObject("FpsOverlay");
        DontDestroyOnLoad(go);
        go.AddComponent<FpsOverlay>();
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        frames++;
        elapsed += dt;
        if (dt > worst) worst = dt;

        if (elapsed < Window) return;

        // Yazı saniyede bir üretiliyor: ölçüm aracı kendisi çöp üretip kasmasın
        text = $"{frames / elapsed:0} FPS   en uzun kare {worst * 1000f:0} ms";
        shownWorst = worst;

        frames = 0;
        elapsed = 0f;
        worst = 0f;
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(18, Mathf.RoundToInt(Screen.height * 0.016f)),
                fontStyle = FontStyle.Bold
            };
        }

        style.normal.textColor = shownWorst > SlowFrame ? Color.red : Color.yellow;

        // Çentik / durum çubuğunun altına
        float top = Screen.height - Screen.safeArea.yMax + 10f;
        GUI.Label(new Rect(20f, top, Screen.width - 40f, style.fontSize * 2f), text, style);
    }
}
