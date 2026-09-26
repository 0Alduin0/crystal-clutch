using System;
using System.Globalization;
using UnityEngine;

// PlayerPrefs üzerinden basit kayıt sistemi.
// Oyunun tek modu hikaye ve o da TEK YÖNLÜ: oyuncu Level 1'den başlar, geçtikçe
// ilerler, geçilen levele dönüş yoktur. O yüzden tutulacak tek şey oyuncunun
// nerede kaldığı - "hangi levellar açıldı" listesine gerek yok.
//
// NOT: Ayrı bir "save dosyası" yok, olması da gerekmiyor. PlayerPrefs platformun
// kendi kalıcı deposunu kullanır:
//   Windows/Editor -> HKCU\Software\Unity\UnityEditor\<Company>\<Product> (registry)
//   Android        -> /data/data/<paket>/shared_prefs/<paket>.v2.playerprefs.xml
//   iOS            -> Library/Preferences/<bundleId>.plist
// Yani sahneye koyulacak bir obje ya da elle oluşturulacak bir dosya yok;
// static class olduğu için her yerden SaveSystem.X diye çağrılır.
public static class SaveSystem
{
    const string CURRENT_LEVEL_KEY = "CurrentLevel";
    const string GOLD_KEY = "Gold";
    // Anahtar BILEREK "StarFruit" kaldi: ejder yumurtasi eski yildiz meyvesinin
    // yerini aldi, anahtari degistirseydik oyuncularin bakiyesi sifirlanirdi.
    const string DRAGON_EGG_KEY = "StarFruit";
    const string LIVES_KEY = "Lives";
    const string LIFE_TIMER_KEY = "LifeRefillAt";
    const string POWERUP_KEY = "PowerUp";
    const string PLAYER_NAME_KEY = "PlayerName";
    const string FIRST_PLAYED_KEY = "FirstPlayedAt";
    const string DRAGON_EGGS_EARNED_KEY = "DragonEggsEarned";
    const string LEADERBOARD_SEED_KEY = "LeaderboardSeed";
    const string DAILY_ITEM_KEY = "DailyGiftItem";
    const string DAILY_AMOUNT_KEY = "DailyGiftAmount";
    const string DAILY_CLAIMED_AT_KEY = "DailyGiftClaimedAt";
    const string DRAGON_FED_PREFIX = "DragonFed_";
    const string DRAGON_NAME_PREFIX = "DragonName_";

    // İlk açılışta ve kayıt sıfırlanınca oyuncunun başlayacağı can sayısı
    public const int StartingLives = 5;

    // Zamanla dolmanın üst sınırı. Satın alınan ya da hediye gelen canlar bu
    // sınırı AŞABİLİR - aşınca sayaç durur, fazlalık harcanınca kaldığı yerden
    // devam eder. Böylece "10 can hediye" vermek sayacı bozmuyor.
    public const int MaxLives = 5;

    // Bir canın kendiliğinden dolması kaç dakika sürsün
    public const int LifeRegenMinutes = 30;

    static TimeSpan RegenInterval => TimeSpan.FromMinutes(LifeRegenMinutes);

    // Kaç çeşit power-up var. Market de oyun içi bar da bu sayıya bakıyor.
    public const int PowerUpTypes = 4;

    // Power-up paketleri. Markette her power-up SADECE bunlarla satılıyor.
    // Butonlar her satırda bu sırayla diziliyor.
    static readonly int[] PowerUpPacks = { 1, 3, 5 };

    public static int PowerUpPackCount => PowerUpPacks.Length;

    public static int GetPackSize(int packIndex)
    {
        if (packIndex < 0 || packIndex >= PowerUpPacks.Length) return 0;

        return PowerUpPacks[packIndex];
    }

    // Günlük sürpriz kutusu alındıktan ne kadar sonra yeniden açılsın
    public static readonly TimeSpan DailyGiftCooldown = TimeSpan.FromHours(24);

    // Günlük sürprizde kaç kutu var, bir kutudan kaç tane çıkabilir (iki uç dahil)
    public const int DailyGiftSlots = 3;
    public const int DailyGiftMinAmount = 1;
    public const int DailyGiftMaxAmount = 5;

    // Oyuncunun şu an oynadığı level indeksi. 0 = Level 1.
    //
    // Level sayısına EŞİT olması "hepsini bitirdi" demek. Bilerek clamp
    // etmiyoruz: sete yeni level eklendiğinde sayaç zaten geçerli hale gelir ve
    // oyuncu kaldığı yerden devam eder. Clamp etseydik son leveli sonsuza kadar
    // tekrar oynardı.
    public static int CurrentLevel
    {
        get => PlayerPrefs.GetInt(CURRENT_LEVEL_KEY, 0);
        set
        {
            PlayerPrefs.SetInt(CURRENT_LEVEL_KEY, value);
            PlayerPrefs.Save();
        }
    }

    // ==================== PARA BİRİMLERİ ====================
    // Setter'lar private: dışarıdan Gold = 5 diye yazılamaz, sadece Add/Spend
    // üzerinden geçilir. Böylece bakiye kontrolü atlanamıyor.

    // Altın: level kazanınca kalan hamle başına kazanılıyor, kaybedince
    // devam etmek için harcanacak.
    public static int Gold
    {
        get => PlayerPrefs.GetInt(GOLD_KEY, 0);
        private set
        {
            PlayerPrefs.SetInt(GOLD_KEY, Mathf.Max(0, value));
            PlayerPrefs.Save();
        }
    }

    // Yıldız kristali: her level bir tane
    public static int DragonEgg
    {
        get => PlayerPrefs.GetInt(DRAGON_EGG_KEY, 0);
        private set
        {
            PlayerPrefs.SetInt(DRAGON_EGG_KEY, Mathf.Max(0, value));
            PlayerPrefs.Save();
        }
    }

    // Bugüne kadar KAZANILAN toplam yumurta - profil bunu gösteriyor. Bakiye
    // (DragonEgg) harcandıkça düşüyor, bu hiç düşmüyor.
    //
    // Bu sayaç gelmeden önce kazanılanları bilmiyoruz. Eski oyuncuda eldeki
    // bakiyeden başlıyor: en azından o kadarını kazandığı kesin.
    public static int DragonEggsEarned
    {
        get => PlayerPrefs.GetInt(DRAGON_EGGS_EARNED_KEY, DragonEgg);
        private set
        {
            PlayerPrefs.SetInt(DRAGON_EGGS_EARNED_KEY, Mathf.Max(0, value));
            PlayerPrefs.Save();
        }
    }

    // ==================== CAN ====================
    // Can level kaybedince azalır, zamanla kendiliğinden dolar. Doldurma işi
    // arka planda çalışan bir sayaçla DEĞİL, tek bir zaman damgasıyla yapılıyor:
    // "sıradaki can şu anda dolacak" bilgisini saklıyoruz ve okurken aradan kaç
    // tam aralık geçtiğine bakıyoruz. Oyun kapalıyken de dolmasının tek yolu bu -
    // Update'te sayaç işletseydik uygulama kapalıyken zaman durmuş olurdu.
    //
    // Cihazın saati ileri alınırsa canlar bedava dolar. Sunucu saati olmadan
    // bunun temiz çözümü yok (günlük sürprizdeki durumun aynısı). Saati GERİ
    // almaya karşı ise koruma var: sayaç bir tam aralıktan uzağa düşemiyor.

    // Okurken zamanla dolan canlar da hesaba katılır.
    public static int Lives
    {
        get
        {
            ApplyRegen();
            return ReadLives();
        }
    }

    public static bool IsLivesFull => Lives >= MaxLives;

    // Sıradaki cana kalan süre. Can doluysa TimeSpan.Zero - arayüz sayacı
    // buna bakıp gizleniyor.
    public static TimeSpan TimeToNextLife
    {
        get
        {
            ApplyRegen();

            if (ReadLives() >= MaxLives) return TimeSpan.Zero;

            DateTime next = NextLifeAt;

            // Sayaç henüz kurulmamışsa (ApplyRegen bu kareyi kuruyor) tam
            // aralık göster - "00:00" yazıp takılı kalmasın.
            if (next == DateTime.MinValue) return RegenInterval;

            TimeSpan left = next - DateTime.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    // Canın TAMAMEN dolmasına kalan süre - "canın bitti" ekranı bunu yazıyor
    public static TimeSpan TimeToFullLives
    {
        get
        {
            TimeSpan next = TimeToNextLife;   // ApplyRegen'i o çalıştırıyor

            int missing = MaxLives - ReadLives();
            if (missing <= 0) return TimeSpan.Zero;

            return next + TimeSpan.FromTicks(RegenInterval.Ticks * (missing - 1));
        }
    }

    public static void AddLives(int amount)
    {
        if (amount <= 0) return;

        int lives = Lives;   // önce zamanla dolanları işle
        WriteLives(lives + amount);

        // Tepeye ulaştıysa sayaç dursun
        if (lives + amount >= MaxLives) NextLifeAt = DateTime.MinValue;
    }

    // Canı tepeye çek. Altınla doldurma ve reklam ödülü buradan geçiyor -
    // bekleyen süre de sıfırlanıyor, oyuncu parasını verip yine beklemesin.
    public static void RefillLives()
    {
        if (ReadLives() < MaxLives) WriteLives(MaxLives);

        NextLifeAt = DateTime.MinValue;
    }

    // Canı yoksa false döner - çağıran taraf oyuncuyu levele sokmadan
    // "canın bitti" ekranını gösterebilsin.
    public static bool SpendLife(int amount = 1)
    {
        if (amount <= 0) return true;

        int lives = Lives;
        if (lives < amount) return false;

        // Doluyken harcandı: geri sayım tam bu anda başlıyor. Zaten
        // çalışıyorsa dokunmuyoruz, yoksa her kayıp sayacı sıfırlardı.
        bool wasFull = lives >= MaxLives;

        WriteLives(lives - amount);

        if (wasFull) NextLifeAt = DateTime.UtcNow + RegenInterval;

        return true;
    }

    // --- can: iç kısım ---

    static int ReadLives() => PlayerPrefs.GetInt(LIVES_KEY, StartingLives);

    static void WriteLives(int value)
    {
        PlayerPrefs.SetInt(LIVES_KEY, Mathf.Max(0, value));
        PlayerPrefs.Save();
    }

    // Sıradaki canın dolacağı an (UTC). Sayaç çalışmıyorsa DateTime.MinValue.
    static DateTime NextLifeAt
    {
        get => ReadUtc(LIFE_TIMER_KEY);
        set => WriteUtc(LIFE_TIMER_KEY, value);
    }

    // Aradan geçen süre kadar can ekler. Can okuyan her yol buradan geçiyor,
    // yani "canım ne zaman dolacak" diye ayrıca bir yerden tetiklemek gerekmiyor.
    static void ApplyRegen()
    {
        int lives = ReadLives();

        // Dolu (ya da hediyeyle üstünde): sayaç çalışmaz
        if (lives >= MaxLives)
        {
            if (NextLifeAt != DateTime.MinValue) NextLifeAt = DateTime.MinValue;
            return;
        }

        DateTime now = DateTime.UtcNow;
        DateTime next = NextLifeAt;

        // Sayaç hiç kurulmamış: eski kayıttan gelen oyuncu ya da canı elle
        // düşürülmüş bir kayıt. Şimdi başlat.
        if (next == DateTime.MinValue)
        {
            NextLifeAt = now + RegenInterval;
            return;
        }

        // Cihaz saati GERİ alınmış - sayaç bir tam aralıktan uzağa düşemez.
        // Yoksa saati bir yıl geri alan oyuncunun canı hiç dolmazdı.
        if (next - now > RegenInterval)
        {
            NextLifeAt = now + RegenInterval;
            return;
        }

        if (now < next) return;

        // İlk can 'next' anında doldu, sonrakiler aralık aralık
        long passed = 1 + (now - next).Ticks / RegenInterval.Ticks;

        int granted = (int)Math.Min(passed, MaxLives - lives);

        WriteLives(lives + granted);

        if (lives + granted >= MaxLives)
        {
            NextLifeAt = DateTime.MinValue;
            return;
        }

        // Kalan dakikalar korunuyor: sayacı 'şimdi'den başlatmak oyuncudan
        // birikmiş süreyi çalardı.
        NextLifeAt = next + TimeSpan.FromTicks(RegenInterval.Ticks * passed);
    }

    public static void AddGold(int amount)
    {
        if (amount <= 0) return;

        Gold += amount;
    }

    public static void AddDragonEgg(int amount)
    {
        if (amount <= 0) return;

        // Sıra önemli: toplam sayacın ilk değeri bakiyeden okunuyor. Bakiyeyi
        // önce artırsaydık bu miktar eski oyuncuda iki kez sayılırdı.
        DragonEggsEarned += amount;
        DragonEgg += amount;
    }

    // Yetmiyorsa HİÇBİR ŞEY harcamaz ve false döner. Çağıran taraf buna bakıp
    // "yetersiz bakiye" ekranını gösterir - yarım harcama diye bir şey olmasın.
    public static bool SpendGold(int amount)
    {
        if (amount <= 0) return true;
        if (Gold < amount) return false;

        Gold -= amount;
        return true;
    }

    public static bool SpendDragonEgg(int amount)
    {
        if (amount <= 0) return true;
        if (DragonEgg < amount) return false;

        DragonEgg -= amount;
        return true;
    }

    // ==================== EJDERHA ====================
    // Her ejderhanın tek bir sayısı var: bugüne kadar kaç yumurta yedirildi.
    // Aşama bu sayıdan hesaplanıyor (DragonDefinition.StageAt) - maliyetler
    // sonradan değişse de kayıt tutarsız kalmıyor.

    public static int GetDragonFed(string dragonId)
    {
        return PlayerPrefs.GetInt(DRAGON_FED_PREFIX + dragonId, 0);
    }

    // Yumurtayı bakiyeden düşer ve ejderhaya yazar. Bakiye yetmezse HİÇBİR
    // ŞEY yapmaz ve false döner - yarım besleme olmasın.
    public static bool FeedDragon(string dragonId, int eggs)
    {
        if (eggs <= 0 || string.IsNullOrEmpty(dragonId)) return false;
        if (!SpendDragonEgg(eggs)) return false;

        PlayerPrefs.SetInt(DRAGON_FED_PREFIX + dragonId, GetDragonFed(dragonId) + eggs);
        PlayerPrefs.Save();

        return true;
    }

    // Test için: ejderhayı yumurta hâline döndür, ismini de sil - isim
    // penceresi yeniden denenebilsin (yenen yumurtalar geri gelmez)
    public static void ResetDragon(string dragonId)
    {
        PlayerPrefs.DeleteKey(DRAGON_FED_PREFIX + dragonId);
        PlayerPrefs.DeleteKey(DRAGON_NAME_PREFIX + dragonId);
        PlayerPrefs.Save();
    }

    // Ejderha adının üst sınırı: Nest'teki başlığa sığan uzunluk
    public const int MaxDragonNameLength = 16;

    // Oyuncunun ejderhasına verdiği isim. Yumurtadan çıkınca bir kez soruluyor,
    // kaydedildikten sonra DEĞİŞTİRİLEMİYOR. Boş = henüz isim verilmedi.
    public static string GetDragonName(string dragonId)
    {
        return PlayerPrefs.GetString(DRAGON_NAME_PREFIX + dragonId, string.Empty);
    }

    public static bool HasDragonName(string dragonId) => !string.IsNullOrEmpty(GetDragonName(dragonId));

    // Sadece ilk seferde yazar. İsim zaten varsa ya da temizlenince boş
    // kalıyorsa hiçbir şey yapmaz ve false döner - değiştirilemezlik burada,
    // arayüz ne yaparsa yapsın ikinci isim yazılamıyor.
    public static bool SetDragonName(string dragonId, string name)
    {
        if (string.IsNullOrEmpty(dragonId) || HasDragonName(dragonId)) return false;

        name = CleanDragonName(name);
        if (name.Length == 0) return false;

        PlayerPrefs.SetString(DRAGON_NAME_PREFIX + dragonId, name);
        PlayerPrefs.Save();
        return true;
    }

    // Oyuncu ne yazdıysa o: harfler, büyük/küçük harf, Türkçe karakterler
    // olduğu gibi kalıyor. Sadece baştaki/sondaki boşluk atılıyor, aradaki
    // boşluklar teke iniyor, görünmeyen kontrol karakterleri siliniyor.
    public static string CleanDragonName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new System.Text.StringBuilder(raw.Length);
        bool pendingSpace = false;

        foreach (char c in raw)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (char.IsControl(c)) continue;

            if (pendingSpace) sb.Append(' ');
            pendingSpace = false;

            sb.Append(c);
        }

        string name = sb.ToString();
        return name.Length > MaxDragonNameLength ? name.Substring(0, MaxDragonNameLength).TrimEnd() : name;
    }

    // ==================== POWER-UP ====================

    // Dört power-up'ın stoğu. Market'ten altınla alınıyor, oyun içinde
    // harcanıyor. Her biri kendi anahtarında duruyor (PowerUp0..PowerUp3):
    // tek bir string'e paketleseydik yeni power-up eklerken eski kayıtları
    // ayrıştırmak zorunda kalırdık.
    public static int GetPowerUp(int index)
    {
        if (index < 0 || index >= PowerUpTypes) return 0;

        return PlayerPrefs.GetInt(POWERUP_KEY + index, 0);
    }

    public static void AddPowerUp(int index, int amount)
    {
        if (index < 0 || index >= PowerUpTypes || amount <= 0) return;

        WritePowerUp(index, GetPowerUp(index) + amount);
    }

    // Yetmiyorsa hiçbir şey harcamaz ve false döner - altınla aynı kural.
    public static bool SpendPowerUp(int index, int amount = 1)
    {
        if (amount <= 0) return true;
        if (GetPowerUp(index) < amount) return false;

        WritePowerUp(index, GetPowerUp(index) - amount);
        return true;
    }

    static void WritePowerUp(int index, int value)
    {
        PlayerPrefs.SetInt(POWERUP_KEY + index, Mathf.Max(0, value));
        PlayerPrefs.Save();
    }

    // ==================== OYUNCU ====================

    public const int MinPlayerNameLength = 3;

    // Liderlik kartına sığan uzunluk
    public const int MaxPlayerNameLength = 16;

    // Profilde ve liderlik tablosunda görünen isim. Oyuncu profilden
    // değiştirebiliyor.
    //
    // İlk okunduğunda "Oyuncu4821" gibi bir isim üretilip saklanıyor. Sabit bir
    // varsayılan ("SEN" gibi) olsaydı gerçek tabloda herkes aynı isimle
    // görünürdü.
    public static string PlayerName
    {
        get
        {
            if (!PlayerPrefs.HasKey(PLAYER_NAME_KEY))
            {
                PlayerPrefs.SetString(PLAYER_NAME_KEY, "Oyuncu" + UnityEngine.Random.Range(1000, 10000));
                PlayerPrefs.Save();
            }

            return PlayerPrefs.GetString(PLAYER_NAME_KEY);
        }
        set
        {
            PlayerPrefs.SetString(PLAYER_NAME_KEY, value);
            PlayerPrefs.Save();
        }
    }

    // Oyuncunun yazdığı ismi kaydedilebilir hale getirir: boşluklar atılıyor,
    // fazlası kırpılıyor. Boşluğu UGS kabul etmiyor. Sessizce silseydik oyuncu
    // kendini "Ali Veli", başkaları "AliVeli" görürdü; kayıttan önce temizleyip
    // oyuncuya da temiz halini gösteriyoruz.
    public static string CleanPlayerName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        string name = new string(Array.FindAll(raw.ToCharArray(), c => !char.IsWhiteSpace(c)));

        return name.Length > MaxPlayerNameLength ? name.Substring(0, MaxPlayerNameLength) : name;
    }

    // Oyuncunun oyunu ilk açtığı an (UTC). Ana menü her açılışta
    // MarkFirstPlay çağırıyor, ama sadece ilk seferde yazılıyor.
    //
    // Bu özellik gelmeden önce kurmuş oyuncuda güncellemeden sonraki ilk
    // açılış - elimizde daha eskisi yok.
    public static DateTime FirstPlayedAt
    {
        get
        {
            MarkFirstPlay();
            return ReadUtc(FIRST_PLAYED_KEY);
        }
    }

    public static void MarkFirstPlay()
    {
        if (!PlayerPrefs.HasKey(FIRST_PLAYED_KEY)) WriteUtc(FIRST_PLAYED_KEY, DateTime.UtcNow);
    }

    // Liderlik tablosundaki sahte rakipleri üretmek için kullanılan tohum.
    // İlk açılışta bir kez üretilip saklanıyor: aynı cihazda liste her
    // açılışta AYNI kişilerden oluşsun. Rastgele üretseydik oyuncu her
    // sekmeye girdiğinde bambaşka isimler görür, tablo bozuk sanırdı.
    public static int LeaderboardSeed
    {
        get
        {
            if (!PlayerPrefs.HasKey(LEADERBOARD_SEED_KEY))
            {
                PlayerPrefs.SetInt(LEADERBOARD_SEED_KEY, UnityEngine.Random.Range(1, int.MaxValue));
                PlayerPrefs.Save();
            }

            return PlayerPrefs.GetInt(LEADERBOARD_SEED_KEY);
        }
    }

    // ==================== GÜNLÜK SÜRPRİZ ====================
    // Üç kutu, ücretsiz. Her kutuda rastgele bir power-up, rastgele adette
    // (1-5); alınınca o kutu 24 saat kapanıyor, süre dolunca içine yeni
    // rastgele power-up ve adet geliyor. Kutuların sayaçları birbirinden
    // bağımsız ve gece yarısına değil alındığı ana bağlı: 15:00'te alınan
    // kutu ertesi gün 15:00'te açılıyor.
    //
    // Kutu başına üç şey saklanıyor: içindeki power-up, adedi ve alındığı an.
    // Power-up ve adet alınınca silinmiyor - geri sayım boyunca kutuda ne
    // alındığı görünsün. Yeni hediye çekme işi canlardaki ApplyRegen gibi OKURKEN
    // yapılıyor, "süre doldu" diye ayrıca bir yerden tetiklemek gerekmiyor.
    //
    // Saatin ileri alınmasına karşı canlardaki gibi sunucusuz temiz çözüm yok.
    // Geri almaya karşı koruma var: sayaç 24 saatten uzağa düşemiyor.

    public static bool IsDailyGiftClaimed(int slot)
    {
        if (!IsDailySlot(slot)) return false;

        ApplyDailyGiftTimer(slot);
        return ReadUtc(DAILY_CLAIMED_AT_KEY + slot) != DateTime.MinValue;
    }

    // Kutudaki power-up'ın indeksi
    public static int GetDailyGiftItem(int slot)
    {
        if (!IsDailySlot(slot)) return 0;

        ApplyDailyGiftTimer(slot);
        return PlayerPrefs.GetInt(DAILY_ITEM_KEY + slot, 0);
    }

    // Kutudaki power-up adedi
    public static int GetDailyGiftAmount(int slot)
    {
        if (!IsDailySlot(slot)) return 0;

        ApplyDailyGiftTimer(slot);

        // Adet eskiden kutunun sırasıydı (1x, 3x, 5x). O zamandan kalan, alınmış
        // ve sayacı işleyen kutuda anahtar yok: gerçekten alınan adet görünsün.
        return PlayerPrefs.GetInt(DAILY_AMOUNT_KEY + slot, GetPackSize(slot));
    }

    // Kutunun yeniden açılmasına kalan süre. Alınmamışsa TimeSpan.Zero.
    public static TimeSpan GetTimeToDailyGift(int slot)
    {
        if (!IsDailySlot(slot)) return TimeSpan.Zero;

        ApplyDailyGiftTimer(slot);

        DateTime claimedAt = ReadUtc(DAILY_CLAIMED_AT_KEY + slot);
        if (claimedAt == DateTime.MinValue) return TimeSpan.Zero;

        TimeSpan left = DailyGiftCooldown - (DateTime.UtcNow - claimedAt);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    // Kutudaki power-up'ı kutunun adedi kadar stoğa ekler, 24 saatlik sayacı
    // başlatır. Kutu zaten alınmışsa hiçbir şey yapmaz ve false döner.
    public static bool ClaimDailyGift(int slot)
    {
        if (!IsDailySlot(slot) || IsDailyGiftClaimed(slot)) return false;

        AddPowerUp(GetDailyGiftItem(slot), GetDailyGiftAmount(slot));
        WriteUtc(DAILY_CLAIMED_AT_KEY + slot, DateTime.UtcNow);
        return true;
    }

    static bool IsDailySlot(int slot) => slot >= 0 && slot < DailyGiftSlots;

    static int RollDailyGiftAmount() => UnityEngine.Random.Range(DailyGiftMinAmount, DailyGiftMaxAmount + 1);

    // İlk açılışta ya da sayaç dolunca kutuya yeni power-up koyar
    static void ApplyDailyGiftTimer(int slot)
    {
        string claimedKey = DAILY_CLAIMED_AT_KEY + slot;
        DateTime claimedAt = ReadUtc(claimedKey);

        if (claimedAt == DateTime.MinValue)
        {
            // Alınmayı bekleyen hediye zaten var
            if (PlayerPrefs.HasKey(DAILY_ITEM_KEY + slot))
            {
                // Adet rastgele olmadan önce çekilmiş, henüz alınmamış hediye:
                // kimse bir şey almadı, adedi şimdi çek
                if (!PlayerPrefs.HasKey(DAILY_AMOUNT_KEY + slot))
                {
                    PlayerPrefs.SetInt(DAILY_AMOUNT_KEY + slot, RollDailyGiftAmount());
                    PlayerPrefs.Save();
                }

                return;
            }
        }
        else
        {
            DateTime now = DateTime.UtcNow;

            // Cihaz saati geri alınmış: sayacı şimdiden başlat. Yoksa saati bir
            // yıl geri alan oyuncu bir yıl bu kutuyu açamazdı.
            if (claimedAt > now)
            {
                WriteUtc(claimedKey, now);
                return;
            }

            // Sayaç hâlâ işliyor
            if (now - claimedAt < DailyGiftCooldown) return;
        }

        PlayerPrefs.SetInt(DAILY_ITEM_KEY + slot, UnityEngine.Random.Range(0, PowerUpTypes));
        PlayerPrefs.SetInt(DAILY_AMOUNT_KEY + slot, RollDailyGiftAmount());
        WriteUtc(claimedKey, DateTime.MinValue);   // Save'i o yapıyor
    }

    // ==================== ZAMAN DAMGASI ====================
    // PlayerPrefs long tutamadığı için tick'ler metin olarak yazılıyor.
    // DateTime.MinValue = "yok", anahtar siliniyor.

    static DateTime ReadUtc(string key)
    {
        string raw = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrEmpty(raw)) return DateTime.MinValue;

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
            return DateTime.MinValue;

        // Bozuk/elle kurcalanmış değer DateTime kurucusunu patlatmasın
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            return DateTime.MinValue;

        return new DateTime(ticks, DateTimeKind.Utc);
    }

    static void WriteUtc(string key, DateTime value)
    {
        if (value == DateTime.MinValue) PlayerPrefs.DeleteKey(key);
        else PlayerPrefs.SetString(key, value.Ticks.ToString(CultureInfo.InvariantCulture));

        PlayerPrefs.Save();
    }

    // ==================== İLERLEME ====================

    // Level bitince çağrılır: sayaç bir ileri gider. Sayacı asla geri
    // çekmiyoruz - ileride bir yerden eski bir level oynatılsa bile oyuncunun
    // ilerlemesi silinmesin.
    public static void CompleteLevel(int levelIndex)
    {
        if (levelIndex >= CurrentLevel)
            CurrentLevel = levelIndex + 1;   // setter kendi Save'ini yapıyor
    }

    // Test için - tüm ilerlemeyi sil
    public static void ClearAll()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
    }

    // ==================== BUILD SIFIRLAMA ====================
    // Telefondaki kayıt telefonda duruyor: build eski kurulumun üstüne
    // yüklenince kalıyor, Android'in otomatik yedeği yüzünden silip yeniden
    // kurunca bile geri gelebiliyor. Test build'i sıfırdan başlasın diye
    // Build'e Hazırla -> "Bilgisayar + telefon" build'e bir damga gömüyor.
    //
    // Damga boşsa hiçbir şey yapmıyor - mağaza build'i böyle olmalı. Doluysa
    // ve bu cihaz o damgayı henüz görmediyse bütün kayıtları BİR KEZ siliyor;
    // aynı build'in sonraki açılışlarında dokunmuyor.
    //
    // En erken aşamada çalışıyor: MusicPlayer (BeforeSceneLoad) müzik ayarını
    // kayıttan okuyor, silinmemiş eski ayarı görmesin.

    const string RESET_STAMP_KEY = "SaveResetStamp";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void ApplyBuildReset()
    {
        string stamp = SaveResetStamp.Value;
        if (string.IsNullOrEmpty(stamp)) return;

        if (PlayerPrefs.GetString(RESET_STAMP_KEY, string.Empty) == stamp) return;

        PlayerPrefs.DeleteAll();
        PlayerPrefs.SetString(RESET_STAMP_KEY, stamp);
        PlayerPrefs.Save();

        Debug.Log("[SaveSystem] Build'e Hazırla damgası: kayıtlar bir kez sıfırlandı.");
    }
}
