using System;
using System.Collections.Generic;
using UnityEngine;

// Liderlik tablosu: ulaşılan bölüme göre sıralı, en fazla 100 kişilik liste.
//
// Veri LeaderboardService'ten (UGS) geliyor. Oyuncu ilk 100'de değilse listenin
// sonunda kendi gerçek sırasıyla görünüyor.
//
// Bağlantı yoksa ya da servis hata verdiyse eski SAHTE listeye düşüyoruz:
// rakipler SaveSystem'deki tohumdan türetiliyor, yani aynı cihazda her
// açılışta aynı 99 kişi aynı bölümlerde duruyor. Sekme boş kalmasın diye.
//
// Kart tarafı verinin nereden geldiğini hiç bilmiyor.
public class LeaderboardUI : MonoBehaviour
{
    struct Entry
    {
        public int Rank;
        public string Name;
        public int Level;
        public Sprite Avatar;
        public bool IsPlayer;

        // Profil paneli için. Bilinmiyorsa MinValue / -1.
        public DateTime Since;
        public int DragonEggs;
    }

    [Header("Kart")]
    [Tooltip("Listedeki satırın kalıbı. Content'in içinde dursun, çalışma anında " +
             "kapatılıp kopyalanıyor.")]
    [SerializeField] LeaderboardCard cardTemplate;

    [Header("Liste")]
    [Tooltip("Tabloda en fazla kaç kişi görünsün")]
    [SerializeField] int maxEntries = 100;

    [Tooltip("Levellerin tamamı - sahte rakiplerin bölümü bunun sınırları içinde kalıyor")]
    [SerializeField] LevelSet levelSet;

    [Tooltip("levelSet bağlanmazsa kullanılacak üst sınır")]
    [SerializeField] int fallbackMaxLevel = 20;

    [Header("Sahte Rakipler")]
    [SerializeField] string[] names =
    {
        "Ahmet", "Zeynep", "Mert", "Elif", "Burak", "Ayşe", "Can", "Deniz",
        "Ece", "Emre", "Selin", "Kaan", "Merve", "Onur", "Buse", "Serkan",
        "Gizem", "Tolga", "Nazlı", "Efe", "Ceren", "Barış", "Pınar", "Umut",
        "Melis", "Kerem", "Sude", "Arda", "İrem", "Yusuf", "Damla", "Berk",
        "Esra", "Cem", "Aslı", "Kuzey", "Nehir", "Sarp", "Duru", "Ali"
    };

    [Tooltip("Profil fotoğrafı - listedeki herkes bununla görünüyor")]
    [SerializeField] Sprite playerAvatar;

    [Header("Renkler")]
    [SerializeField] Color normalColor = Color.white;

    [Tooltip("Oyuncunun kendi satırının zemini")]
    [SerializeField] Color playerColor = new Color(1f, 0.9f, 0.55f, 1f);

    [Header("Profil")]
    [Tooltip("Karttaki fotoğrafa basınca açılan panel. Kendi satırında kendi " +
             "profilin (isim değiştirilebilir), diğerlerinde o oyuncunun bilgileri.")]
    [SerializeField] ProfilePanel profilePanel;

    readonly List<LeaderboardCard> cards = new List<LeaderboardCard>();

    // Cevap gelene kadar sekme kapanıp açılırsa eski cevap yenisini ezmesin
    int requestId;

    void OnEnable()
    {
        // Sahne her açıldığında tazele: oyuncu bölüm geçip dönmüş olabilir,
        // sırası değişsin. Sayfalar hep aktif durduğu için sekme değişimi
        // OnEnable'ı tetiklemiyor - menü içindeki değişiklikleri (isim) servis
        // Changed ile haber veriyor.
        LeaderboardService.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        LeaderboardService.Changed -= Refresh;
    }

    async void Refresh()
    {
        int request = ++requestId;

        // Önbellekteyse senkron dönüyor, liste aynı karede doluyor. Değilse
        // cevap gelene kadar ekrandaki eski liste kalıyor.
        List<LeaderboardService.Row> rows = await LeaderboardService.FetchAsync(maxEntries);

        // Beklerken obje yok edilmiş, kapanmış ya da yeni istek atılmış olabilir
        if (this == null || !isActiveAndEnabled || request != requestId) return;

        Show(rows != null ? FromServer(rows) : BuildFakeEntries());
    }

    void Show(List<Entry> entries)
    {
        if (cardTemplate == null) return;

        cardTemplate.gameObject.SetActive(false);

        // Kartlar bir kez yaratılıyor, sonraki tazelemelerde yeniden kullanılıyor.
        // 100 kartı her sekme değişiminde yok edip yaratmak sarsıntı yaratır.
        EnsureCards(entries.Count);

        for (int i = 0; i < cards.Count; i++)
        {
            bool used = i < entries.Count;

            cards[i].gameObject.SetActive(used);
            if (!used) continue;

            Entry entry = entries[i];

            cards[i].Bind(entry.Rank, entry.Avatar, entry.Name, entry.Level,
                          entry.IsPlayer, normalColor, playerColor, ClickFor(entry));
        }
    }

    Action ClickFor(Entry entry)
    {
        if (profilePanel == null) return null;

        if (entry.IsPlayer) return profilePanel.ShowSelf;

        return () => profilePanel.ShowPlayer(entry.Avatar, entry.Name, entry.Level,
                                             entry.Since, entry.DragonEggs);
    }

    void EnsureCards(int count)
    {
        while (cards.Count < count)
        {
            LeaderboardCard card = Instantiate(cardTemplate, cardTemplate.transform.parent);
            card.name = "Card_" + cards.Count;

            cards.Add(card);
        }
    }

    // ==================== GERÇEK VERİ ====================

    List<Entry> FromServer(List<LeaderboardService.Row> rows)
    {
        List<Entry> entries = new List<Entry>(rows.Count);

        foreach (LeaderboardService.Row row in rows)
        {
            entries.Add(new Entry
            {
                Rank = row.Rank,
                Name = row.Name,
                Level = row.Level,
                Avatar = playerAvatar,
                IsPlayer = row.IsPlayer,
                Since = row.Since,
                DragonEggs = row.DragonEggs
            });
        }

        return entries;
    }

    // ==================== SAHTE VERİ ====================

    List<Entry> BuildFakeEntries()
    {
        int playerLevel = SaveSystem.CurrentLevel + 1;   // 0. indeks = 1. bölüm
        int topLevel = levelSet != null && levelSet.Count > 0 ? levelSet.Count : fallbackMaxLevel;

        List<Entry> entries = new List<Entry>(maxEntries);

        entries.Add(new Entry
        {
            Name = SaveSystem.PlayerName,
            Level = playerLevel,
            Avatar = playerAvatar,
            IsPlayer = true,
            Since = DateTime.MinValue,
            DragonEggs = -1
        });

        // Tohumlu üretici: aynı cihazda hep aynı rakipler çıksın
        System.Random rng = new System.Random(SaveSystem.LeaderboardSeed);

        int opponents = Mathf.Max(0, maxEntries - 1);

        for (int i = 0; i < opponents; i++)
        {
            // Merdiven: baştakiler son bölümlerde, sondakiler ilk bölümlerde.
            // Araya gürültü katıyoruz ki düz bir cetvel gibi durmasın.
            float t = opponents > 1 ? i / (float)(opponents - 1) : 0f;
            int level = Mathf.RoundToInt(Mathf.Lerp(topLevel, 1f, t)) + rng.Next(-1, 2);

            entries.Add(new Entry
            {
                Name = PickName(rng, i),
                Level = Mathf.Clamp(level, 1, topLevel),
                Avatar = playerAvatar,
                IsPlayer = false,
                // Sahte rakibin geçmişi yok - panelde "bilinmiyor" görünsün
                Since = DateTime.MinValue,
                DragonEggs = -1
            });
        }

        // Bölüme göre büyükten küçüğe. Eşitlikte oyuncu öne geçsin - kendi
        // satırını aramak zorunda kalmasın.
        entries.Sort((a, b) =>
        {
            int compare = b.Level.CompareTo(a.Level);
            if (compare != 0) return compare;

            if (a.IsPlayer != b.IsPlayer) return a.IsPlayer ? -1 : 1;

            return 0;
        });

        if (entries.Count > maxEntries) entries.RemoveRange(maxEntries, entries.Count - maxEntries);

        // Sahte listede sıra, listedeki yer
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            entry.Rank = i + 1;
            entries[i] = entry;
        }

        return entries;
    }

    string PickName(System.Random rng, int index)
    {
        if (names == null || names.Length == 0) return "Oyuncu " + (index + 1);

        string name = names[rng.Next(names.Length)];

        // Aynı isim birden fazla çıkabilir; listede iki "Mert" görmek doğal
        // durmuyor, sonuna kısa bir sayı ekliyoruz.
        return name + rng.Next(10, 100);
    }
}
