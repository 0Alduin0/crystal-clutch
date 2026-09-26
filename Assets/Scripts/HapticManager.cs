using System.Runtime.InteropServices;
using UnityEngine;

// Titreşim şiddeti
public enum HapticStrength
{
    Light,    // hafif dokunuş - küçük patlamalar
    Medium,   // orta - power-up, büyük patlama
    Heavy     // güçlü - çok büyük patlama, oyun sonu
}

// Platforma göre titreşim yönetir.
// Android: Vibrator API (API 26+ amplitude destekli)
// iOS: UIImpactFeedbackGenerator (native plugin gerekir)
// Editor: sadece log
public class HapticManager : MonoBehaviour
{
    public static HapticManager Instance;

    [Header("Ayarlar")]
    [Tooltip("Titreşim tamamen kapalı olsun mu (kullanıcı ayarından da değişir)")]
    [SerializeField] bool enabledByDefault = true;

    [Tooltip("Arka arkaya titreşim spam'ini önler - bu süre içinde tek titreşim")]
    [SerializeField] float cooldown = 0.06f;

    // Bu altı alan sadece UNITY_ANDROID derlemesinde okunuyor. Editörde ve
    // diğer platformlarda "atanır ama kullanılmaz" (CS0414) uyarısı veriyorlardı -
    // yanlış pozitif. Bastırıyoruz ki gerçek uyarılar görünür kalsın.
#pragma warning disable 0414

    [Header("Android Süreleri (ms)")]
    [Tooltip("Çoğu Android cihazda 15ms altı titreşim hissedilmiyor - kısaltma")]
    [SerializeField] long lightDuration = 20;
    [SerializeField] long mediumDuration = 35;
    [SerializeField] long heavyDuration = 60;

    [Header("Android Şiddet (1-255)")]
    [SerializeField] int lightAmplitude = 60;
    [SerializeField] int mediumAmplitude = 130;
    [SerializeField] int heavyAmplitude = 220;

#pragma warning restore 0414

    const string PREF_KEY = "HapticsEnabled";

    float lastHapticTime = -99f;

    // Kullanıcı ayarı - Settings menüsünden değiştirilebilir.
    // Play() her patlamada çağrılıyor; PlayerPrefs okuması native tarafa gidip
    // string key ile arama yapıyor. Değer bir kez okunup bellekte tutuluyor.
    static int hapticsCache = -1;   // -1 = henüz okunmadı

    public static bool HapticsEnabled
    {
        get
        {
            if (hapticsCache < 0) hapticsCache = PlayerPrefs.GetInt(PREF_KEY, 1);
            return hapticsCache == 1;
        }
        set
        {
            hapticsCache = value ? 1 : 0;
            PlayerPrefs.SetInt(PREF_KEY, hapticsCache);
            PlayerPrefs.Save();
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject vibrator;
    AndroidJavaClass vibrationEffectClass;
    int androidApiLevel;
    bool hasAmplitudeControl;
#endif

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void _PlayImpactHaptic(int style);

    [DllImport("__Internal")]
    private static extern bool _HapticsSupported();
#endif

    void Awake()
    {
        Instance = this;

        // İlk açılışta varsayılanı yaz
        if (!PlayerPrefs.HasKey(PREF_KEY))
            HapticsEnabled = enabledByDefault;

        InitializePlatform();
    }

    void InitializePlatform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            using (AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                androidApiLevel = version.GetStatic<int>("SDK_INT");
            }

            // API 26 (Android 8.0) ve üstü şiddet kontrolü destekliyor
            if (androidApiLevel >= 26)
            {
                vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                hasAmplitudeControl = vibrator != null && vibrator.Call<bool>("hasAmplitudeControl");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Titreşim başlatılamadı: " + e.Message);
        }
#endif
    }

    // ==================== GENEL ARAYÜZ ====================

    public void Play(HapticStrength strength)
    {
        if (!HapticsEnabled) return;

        // Spam koruması - 10 kristal patlayınca 10 titreşim olmasın
        if (Time.unscaledTime - lastHapticTime < cooldown) return;
        lastHapticTime = Time.unscaledTime;

        Trigger(strength);
    }

    // Patlayan kristal sayısına göre otomatik şiddet seç
    public void PlayForBlast(int crystalCount)
    {
        if (crystalCount >= 10) Play(HapticStrength.Heavy);
        else if (crystalCount >= 5) Play(HapticStrength.Medium);
        else Play(HapticStrength.Light);
    }

    void Trigger(HapticStrength strength)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        TriggerAndroid(strength);
#elif UNITY_IOS && !UNITY_EDITOR
        TriggerIOS(strength);
#else
        // Editor'de sadece log - titreşim yok
        // Debug.Log("Haptic: " + strength);
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    void TriggerAndroid(HapticStrength strength)
    {
        if (vibrator == null) return;

        long duration = strength switch
        {
            HapticStrength.Light  => lightDuration,
            HapticStrength.Medium => mediumDuration,
            _                     => heavyDuration
        };

        int amplitude = strength switch
        {
            HapticStrength.Light  => lightAmplitude,
            HapticStrength.Medium => mediumAmplitude,
            _                     => heavyAmplitude
        };

        try
        {
            if (androidApiLevel >= 26 && hasAmplitudeControl && vibrationEffectClass != null)
            {
                // Şiddet kontrollü titreşim - çok daha hassas
                using (AndroidJavaObject effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                    "createOneShot", duration, amplitude))
                {
                    vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                // Eski cihazlar - sadece süre kontrolü
                vibrator.Call("vibrate", duration);
            }
        }
        catch (System.Exception e)
        {
            // Sessizce yutma. İzin eksikse burası SecurityException fırlatıyor ve
            // titreşim hiç çalışmıyor - eskiden bu hiç fark edilmiyordu.
            // Konsolu doldurmamak için sadece ilk hatayı bildiriyoruz.
            if (!vibrateFailed)
            {
                vibrateFailed = true;
                Debug.LogWarning("[HapticManager] Titreşim başarısız: " + e.Message +
                                 "  |  AndroidManifest'te android.permission.VIBRATE var mı?");
            }
        }
    }

    bool vibrateFailed;
#endif

#if UNITY_IOS && !UNITY_EDITOR
    void TriggerIOS(HapticStrength strength)
    {
        try
        {
            // 0 = light, 1 = medium, 2 = heavy
            _PlayImpactHaptic((int)strength);
        }
        catch (System.Exception)
        {
            // Plugin yoksa sessizce geç
        }
    }
#endif
}
