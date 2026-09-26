using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Exceptions;
using Unity.Services.Leaderboards.Models;
using UnityEngine;

// Gerçek liderlik tablosu: Unity Gaming Services (Authentication + Leaderboards).
//
// UGS'yi bilen TEK yer burası. UI sadece Row listesi alıyor; UGS'nin fiyatı
// değişir de başka bir servise geçersek değişecek dosya bu.
//
// Hiçbir çağrı exception fırlatmıyor: internet yoksa, proje Unity Cloud'a
// bağlanmamışsa ya da servis çöktüyse null / false dönüyor ve oyun yoluna
// devam ediyor. Liderlik tablosu yüzünden oyunun kırılması kabul edilemez.
//
// Oyuncu anonim giriş yapıyor - hesap açtırmıyoruz. Oturum UGS tarafından
// PlayerPrefs'te tutuluyor, yani oyun silinmedikçe oyuncu hep aynı kişi.
// SaveSystem.ClearAll() onu da siler: test sonrası tabloda yeni bir oyuncu
// belirmesi normal.
public static class LeaderboardService
{
    // Unity Cloud panelinde açılan leaderboard'un ID'si - panelde BİREBİR aynı
    // yazılmalı. Sıralama "Descending", güncelleme "Keep best" olmalı.
    public const string LeaderboardId = "levels";

    // Sunucuda ismi boş gelen oyuncu olursa
    const string UnknownName = "Oyuncu";

    // Metadata'daki tarih biçimi - ülkeden bağımsız, panelde de okunaklı
    const string DateFormat = "yyyy-MM-dd";

    // Liste bu kadar saniye taze sayılıyor. Sekmeler arası gidip gelmek her
    // seferinde sunucuya gitmesin.
    const float CacheSeconds = 60f;

    public struct Row
    {
        public string Name;
        public int Level;
        public int Rank;        // 1 tabanlı, ekranda yazan sıra
        public bool IsPlayer;

        // Profil panelinde gösterilenler. Metadata'sı olmayan kayıtta
        // bilinmiyor: Since = DateTime.MinValue, DragonEggs = -1.
        public DateTime Since;
        public int DragonEggs;
    }

    // Skorla birlikte giden profil bilgisi; başka oyuncular profil panelinde
    // bunu görüyor.
    //
    // "Keep best" yüzünden sadece skor ARTTIĞINDA güncelleniyor. Bizde yumurta
    // sadece bölüm geçilince kazanılıyor ve her geçiş skoru artırıyor - yani
    // bu yetiyor. Yumurta başka yoldan da kazanılır hale gelirse bir sonraki
    // bölüm geçişine kadar eski sayı görünür.
    //
    // Alan adları JSON anahtarı: değiştirirsen sunucudaki eski kayıtlar okunmaz.
    [Serializable]
    class ScoreMetadata
    {
        public string since;
        public int eggs;
    }

    // Liste değişti, ekrandaki eskidi - LeaderboardUI dinliyor. Sayfalar
    // hep aktif olduğu için OnEnable'a güvenemiyoruz.
    public static event Action Changed;

    static Task<bool> signIn;
    static Task<List<Row>> fetching;

    static List<Row> cached;
    static float cachedAt;

    // Bu oturumda sunucuya yazılan son bölüm. Aynı bölümü tekrar göndermiyoruz.
    static int submittedLevel;

    // Tabloda görünen bölüm: oyuncunun şu an oynadığı bölüm (0. indeks = 1. bölüm)
    static int ReachedLevel => SaveSystem.CurrentLevel + 1;

    static bool Offline => Application.internetReachability == NetworkReachability.NotReachable;

    // AuthenticationService.Instance / LeaderboardsService.Instance KULLANMA.
    // Paketler editörde o statik alanları sahne yüklendikten SONRA sıfırlıyor
    // ([RuntimeInitializeOnLoadMethod]). Biz servisleri sahne açılırken
    // (LeaderboardUI.OnEnable) başlattığımız için alan, dolduğu anda null'a
    // çekiliyordu: "Leaderboards service has not been initialized". Core'un
    // kayıt defteri bu sıfırlamadan etkilenmiyor.
    static IAuthenticationService Auth => UnityServices.Instance.GetAuthenticationService();
    static ILeaderboardsService Leaderboards => UnityServices.Instance.GetLeaderboardsService();

    // ==================== DIŞARIYA AÇIK ====================

    // Bölüm geçilince çağrılır. Beklemeye gerek yok, arka planda gönderiyor.
    public static void Push()
    {
        _ = PushAsync();
    }

    // Profilde isim değişince çağrılır. Başka oyuncular yeni ismi kendi
    // listeleri tazelenince görür.
    public static void PlayerNameChanged()
    {
        // Kendi satırımızdaki isim önbellekteki listede duruyor
        cached = null;

        _ = SyncNameThenNotifyAsync();
    }

    // İlk N kişi, oyuncu bu N'in dışındaysa listenin sonunda kendi sırasıyla.
    // Bağlantı yoksa ya da servis hata verdiyse null.
    //
    // Dönen liste paylaşılıyor (önbellek) - değiştirme, kopyala.
    public static Task<List<Row>> FetchAsync(int limit)
    {
        if (cached != null && Time.realtimeSinceStartup - cachedAt < CacheSeconds)
            return Task.FromResult(cached);

        // Süren bir istek varsa ona bağlan. IsCompleted kontrolü şart: istek
        // senkron biterse (internet yok) task atanmadan önce tamamlanmış olur,
        // "bitince null'a çek" yaklaşımı onu sonsuza kadar takılı bırakırdı.
        if (fetching == null || fetching.IsCompleted)
            fetching = FetchFreshAsync(limit);

        return fetching;
    }

    // ==================== GİRİŞ ====================

    static Task<bool> EnsureSignedInAsync()
    {
        // Başarısız giriş kalıcı değil: bir sonraki çağrıda yeniden denenir
        if (signIn == null || (signIn.IsCompleted && !signIn.Result))
            signIn = SignInAsync();

        return signIn;
    }

    static async Task<bool> SignInAsync()
    {
        if (Offline) return false;

        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!Auth.IsSignedIn)
                await Auth.SignInAnonymouslyAsync();

            return true;
        }
        catch (Exception e)
        {
            Warn("Giriş yapılamadı (proje Unity Cloud'a bağlı mı?)", e);
            return false;
        }
    }

    // ==================== GÖNDERME ====================

    static async Task PushAsync()
    {
        if (!await EnsureSignedInAsync()) return;

        await SyncNameAsync();

        int level = ReachedLevel;
        if (level <= submittedLevel) return;

        try
        {
            await Leaderboards.AddPlayerScoreAsync(LeaderboardId, level, new AddPlayerScoreOptions
            {
                Metadata = new ScoreMetadata
                {
                    // Oyuncunun kendi takvimindeki gün: gece 01'de başlayan
                    // Türkiye'deki oyuncu UTC'de bir önceki güne düşmesin
                    since = SaveSystem.FirstPlayedAt.ToLocalTime().ToString(DateFormat, CultureInfo.InvariantCulture),
                    eggs = SaveSystem.DragonEggsEarned
                }
            });

            submittedLevel = level;

            // Oyuncunun sırası değişti, eski liste artık yanlış
            cached = null;
        }
        catch (Exception e)
        {
            Warn("Skor gönderilemedi", e);
        }
    }

    static async Task SyncNameThenNotifyAsync()
    {
        if (await EnsureSignedInAsync()) await SyncNameAsync();

        // İsim gittikten SONRA haber veriyoruz. Önce verseydik tablo
        // tazelenirken (PushAsync) aynı ismi paralel olarak ikinci kez
        // gönderirdi. Bağlantı yoksa da haber veriyoruz: yedek listedeki kendi
        // satırımız yeni ismi göstersin.
        Changed?.Invoke();
    }

    // Tabloda görünen isim Authentication'da tutuluyor, leaderboard'da değil.
    // Sadece değiştiyse gönderiyoruz.
    static async Task SyncNameAsync()
    {
        string wanted = SaveSystem.PlayerName;

        if (StripTag(Auth.PlayerName) == wanted) return;

        try
        {
            await Auth.UpdatePlayerNameAsync(wanted);
        }
        catch (Exception e)
        {
            // İsim gitmese de skor gitsin - başkaları o zaman eski ismi görür
            Warn("İsim güncellenemedi", e);
        }
    }

    // ==================== ÇEKME ====================

    static async Task<List<Row>> FetchFreshAsync(int limit)
    {
        if (!await EnsureSignedInAsync()) return null;

        // Önce kendi skorumuz güncel olsun - yoksa oyuncu az önce geçtiği
        // bölümü tabloda göremez
        await PushAsync();

        try
        {
            string me = Auth.PlayerId;

            LeaderboardScoresPage page = await Leaderboards.GetScoresAsync(
                LeaderboardId, new GetScoresOptions { Limit = limit, IncludeMetadata = true });

            List<LeaderboardEntry> results = page.Results ?? new List<LeaderboardEntry>();
            List<Row> rows = new List<Row>(results.Count + 1);
            bool playerListed = false;

            foreach (LeaderboardEntry entry in results)
            {
                bool isPlayer = entry.PlayerId == me;
                playerListed |= isPlayer;

                rows.Add(ToRow(entry, isPlayer));
            }

            // Oyuncu ilk N'de değilse kendi satırını ayrıca çekip sona ekliyoruz -
            // "neredeyim" sorusunun cevabı listede hep olsun
            if (!playerListed)
            {
                try
                {
                    LeaderboardEntry own = await Leaderboards.GetPlayerScoreAsync(
                        LeaderboardId, new GetPlayerScoreOptions { IncludeMetadata = true });

                    rows.Add(ToRow(own, true));
                }
                catch (LeaderboardsException e) when (e.Reason == LeaderboardsExceptionReason.EntryNotFound)
                {
                    // Skor hiç gönderilemediyse kaydı yok, liste onsuz gösterilir
                }
            }

            cached = rows;
            cachedAt = Time.realtimeSinceStartup;

            return rows;
        }
        catch (Exception e)
        {
            Warn("Liste çekilemedi", e);
            return null;
        }
    }

    static Row ToRow(LeaderboardEntry entry, bool isPlayer)
    {
        Row row = new Row
        {
            // Kendi satırımızda cihazdaki isim: profilden az önce değiştirdiysek
            // sunucuya henüz gitmemiş olabilir
            Name = isPlayer ? SaveSystem.PlayerName : DisplayName(entry.PlayerName),
            Level = (int)entry.Score,
            Rank = entry.Rank + 1,
            IsPlayer = isPlayer,
            Since = DateTime.MinValue,
            DragonEggs = -1
        };

        ReadMetadata(entry.Metadata, ref row);

        return row;
    }

    // Bu özellikten önce gönderilmiş skorlarda metadata yok; bozuk JSON da
    // listeyi düşürmesin - ikisinde de bilgiler "bilinmiyor" kalıyor.
    static void ReadMetadata(string json, ref Row row)
    {
        if (string.IsNullOrEmpty(json)) return;

        ScoreMetadata meta;

        try
        {
            meta = JsonUtility.FromJson<ScoreMetadata>(json);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (meta == null) return;

        if (DateTime.TryParseExact(meta.since, DateFormat, CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out DateTime since))
            row.Since = since;

        row.DragonEggs = meta.eggs;
    }

    // ==================== İSİM ====================

    // UGS aynı isimli oyuncuları ayırmak için sona "#1234" ekliyor; ekranda
    // atıyoruz. Kimse elle isim seçmese de isimler zaten "Oyuncu4821" gibi
    // sayılı üretiliyor (SaveSystem.PlayerName).
    static string DisplayName(string raw)
    {
        string name = StripTag(raw);

        return string.IsNullOrEmpty(name) ? UnknownName : name;
    }

    static string StripTag(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        int tag = raw.LastIndexOf('#');
        return tag >= 0 ? raw.Substring(0, tag) : raw;
    }

    static void Warn(string what, Exception e)
    {
        string hint = e is LeaderboardsException le && le.Reason == LeaderboardsExceptionReason.LeaderboardNotFound
            ? " - panelde '" + LeaderboardId + "' ID'li leaderboard yok"
            : string.Empty;

        Debug.LogWarning("[Leaderboard] " + what + hint + ": " + e.Message);
    }
}
