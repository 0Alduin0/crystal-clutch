using UnityEngine;

// Uygulama geneli performans ayarları. Sahneye konacak bir obje YOK -
// [RuntimeInitializeOnLoadMethod] sayesinde ilk sahne yüklenmeden önce
// kendiliğinden bir kez çalışıyor.
//
// Neden burada: kare hızı sahneye özel bir ayar değil, uygulamanın tamamına ait.
// Eskiden GameManager'ın içindeydi ve GameManager sadece Game sahnesinde
// olduğu için menü platform varsayılanıyla açılıyordu - Android'de bu çoğu
// cihazda 30 FPS demek ve menüdeki kaydırma takılıyor gibi hissettiriyordu.
//
// Build tarafındaki ayarlar (IL2CPP, frame pacing...) editörde:
// Match3 -> Performans Ayarlarını Uygula (Build'e Hazırla da çağırıyor).
public static class AppPerformance
{
    // Android'de bu ayarlanmazsa birçok cihaz 30 FPS'e düşer.
    public const int TargetFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        // vSync açıkken targetFrameRate yok sayılır, o yüzden önce kapatılıyor.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFrameRate;

        // 2D fizik simülasyonu kapalı: oyunda fizik yok, collider'lar sadece
        // "parmağın altında hangi kristal var" sorgusu için (InputHandler).
        // Simülasyon açıkken rigidbody'siz collider'lar "sabit" sayılıyor ve
        // tween'le kayan 64 kristalin her biri saniyede 50 kez fizik dünyasında
        // yeniden yerleştiriliyordu - en yoğun anda (kaskad) boşa iş. Sorgudan
        // önce InputHandler konumları kendisi eşitliyor.
        Physics2D.simulationMode = SimulationMode2D.Script;
    }
}
