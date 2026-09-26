using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// Power-up kombinasyonunun sonucu
public class ComboResult
{
    public List<Crystal> Crystals = new List<Crystal>();
    public bool IsColorBomb;          // elektrik efekti oynatılacak mı
    public Vector3 SourcePosition;    // ışınların çıkacağı nokta

    public Crystal MergeFrom;           // ışınların çıktığı multicolor taş

    // --- Multicolor + başka bir buff ---
    // Rastgele bir rengin tamamı o buff'a dönüşüp ateşleniyor. Patlama listesi
    // hemen dolmuyor: önce dönüşüm animasyonu oynuyor, alan ondan SONRA
    // hesaplanıyor - yoksa patlama animasyondan önce çakardı.
    public bool IsConversion;
    public CrystalType ConvertType;     // hangi renk dönüşecek (rastgele)
    public PowerType ConvertPower;    // hangi buff'a dönüşecek
}

// Bir eşleşme grubu - kaç kristal, hangi yönde
public class MatchGroup
{
    public List<Crystal> Crystals = new List<Crystal>();
    public bool IsHorizontal;

    public int Count => Crystals.Count;
}

public class Board : MonoBehaviour
{
    [Header("Prefablar")]
    [SerializeField] GameObject backgroundPrefab;

    [Tooltip("Engel görseli. Üstünde Obstacle scripti + SpriteRenderer olmalı, " +
             "COLLIDER OLMAMALI. Boşsa engeller çalışır ama görünmez.")]
    [SerializeField] GameObject obstaclePrefab;

    [Header("Grid Görünümü")]
    [SerializeField] float spacing = 1f;

    [Header("Buff Patlama Dalgası")]
    [Tooltip("4-way gibi buff'lar patlarken merkezden en uzak kristale kadar " +
             "geçen süre. 0 = dalga yok, hepsi aynı anda patlar.")]
    [SerializeField] float waveDuration = 0.3f;

    // Tempo değerleri Royal Match / Candy Crush hissine göre (2026-09-25).
    // Eski değerler çok hızlıydı: zincir derinleşince animasyonlar 2 katına
    // kadar hızlanıyor, düşme turları üst üste biniyordu. Karşılaştırmak için
    // Board bileşeninin sağ tık menüsünde iki hazır ayar var (ApplySmoothTempo /
    // ApplyOldTempo). Sahnedeki değerler kodun varsayılanını EZER - var olan
    // sahnede değiştirmek için o menüyü kullan.

    [Header("Genel Tempo")]
    [Tooltip("Tüm animasyon sürelerinin ortak çarpanı. Oyunun hızını tek yerden " +
             "ayarlamak için: 1 = normal, büyük değer daha sakin, küçük değer daha hızlı.")]
    [Range(0.5f, 2f)]
    [SerializeField] float tempoScale = 1f;

    [Header("Animasyon Süreleri")]
    [SerializeField] float swapDuration = 0.2f;
    [SerializeField] float fallDuration = 0.28f;
    [SerializeField] float destroyDuration = 0.2f;

    [Tooltip("Düşme süresi mesafeyle ne kadar uzasın: kare başına oran.\n" +
             "0.08 = her kare %8 daha uzun. Yerçekimi hissi: uzaktan düşen " +
             "belirgin şekilde daha uzun yolda. Düşük değer her düşüşü aynı " +
             "sürede bitirir, tahta 'ışınlanıyor' gibi görünür.")]
    [Range(0f, 0.2f)]
    [SerializeField] float fallDistanceFactor = 0.08f;

    [Tooltip("Yeni doğan kristaller arasındaki şelale gecikmesi")]
    [SerializeField] float fillStagger = 0.04f;

    [Tooltip("Tahta oturma turlarının üst üste binme oranı.\n" +
             "1 = bir tur bitmeden sıradaki başlamaz.\n" +
             "0.6 = tur %60'ına gelince sıradakine geçilir.\n\n" +
             "Kristallerin animasyon SÜRELERİ değişmez - sadece turlar erken " +
             "başlar. Düşük değer akışı hızlandırır ama hareket birbirine karışır.")]
    [Range(0.3f, 1f)]
    [SerializeField] float settleOverlap = 0.8f;

    // Sürelerin tamamı tempoScale üzerinden geçer
    float SwapTime => swapDuration * tempoScale;
    float FallTime => fallDuration * tempoScale;
    float DestroyTime => destroyDuration * tempoScale;
    float WaveTime => waveDuration * tempoScale;

    [Header("Cascade Tempo")]
    [Tooltip("Zincir derinleştikçe animasyonlar hızlansın - heyecan artar")]
    [SerializeField] bool useCascadeTempo = true;
    [Tooltip("Her cascade seviyesinde ne kadar hızlansın (0.05 = %5)")]
    [Range(0f, 0.3f)]
    [SerializeField] float cascadeSpeedStep = 0.05f;
    [Tooltip("En fazla bu kadar hızlansın. 0.8 = en hızlı hâli normalin 1.25 katı. " +
             "0.5 eski değerdi: uzun zincirde her şey 2 kat hızlı oynuyordu.")]
    [Range(0.3f, 1f)]
    [SerializeField] float minSpeedMultiplier = 0.8f;

    [Tooltip("Zincirin bir adımı oturduktan sonra, sıradaki patlamadan önceki " +
             "kısa bekleme. Göz yeni tahtayı görsün, sonra patlasın.")]
    [Range(0f, 0.2f)]
    [SerializeField] float cascadePause = 0.07f;

    [Header("Anticipation")]
    [Tooltip("Kristaller patlamadan önce minik titreme yapsın. " +
             "Sadece ilk patlamanın küçük gruplarında uygulanır.")]
    [SerializeField] bool useAnticipation = true;
    [Tooltip("Bu sayıdan fazla kristal patlıyorsa titreme atlanır")]
    [SerializeField] int anticipationMaxCrystals = 6;

    [Header("Elektrik Efekti")]
    [Tooltip("Işınlar arası bekleme - küçük değer hepsi birden çıkar")]
    [SerializeField] float lightningInterval = 0.025f;
    [Tooltip("Işınlar çıktıktan sonra patlamadan önceki bekleme")]
    [SerializeField] float lightningHoldTime = 0.3f;

    [Header("Multicolor + Buff Dönüşümü")]
    [Tooltip("Kristaller buff'a dönüştükten sonra ateşlenmeden önceki bekleme. " +
             "Doğuş animasyonu 0.4sn sürüyor - altına inersen yarıda kesilir.")]
    [SerializeField] float conversionHoldTime = 0.45f;

    [Header("Tahta Yenileme (hamle kalmadığında)")]
    [Tooltip("Uyarı çıktıktan sonra silme dalgasının başlamasına kadar bekleme")]
    [SerializeField] float noMovesHoldTime = 0.8f;

    [Tooltip("Çapraz dalganın bir köşegenden diğerine geçme süresi. " +
             "Sol üstten sağ alta doğru ilerler.")]
    [SerializeField] float refreshWaveStep = 0.045f;

    [Tooltip("Tek bir kristalin yok olma süresi")]
    [SerializeField] float refreshVanishDuration = 0.22f;

    [Header("İpucu")]
    [Tooltip("Oyuncu bu kadar saniye hamle yapmazsa yapılabilecek bir hamle " +
             "yanıp sönerek gösterilir. 0 = ipucu kapalı.")]
    [SerializeField] float hintDelay = 5f;

    [Tooltip("İpucu kristallerinin zıplama yüksekliği (kare boyu 1 birim)")]
    [SerializeField] float hintHopHeight = 0.18f;

    [Tooltip("Bir zıplama + yanıp sönme döngüsünün süresi")]
    [SerializeField] float hintCycleDuration = 0.9f;

    [Header("Engeller")]
    [Tooltip("Engel görseli kristalin ÖNÜNDE dursun diye kamera yönünde kaydırma. " +
             "Sıralama Order in Layer ile de garantiye alınmalı.")]
    [SerializeField] float obstacleZOffset = -0.1f;

    int width = 8;
    int height = 8;

    // Kristal çeşidi sayısı levelden gelir. Her rastgele kristal için tekrar
    // okumak yerine dolum başına bir kez tazeleniyor.
    int variety = 4;

    // Yukarıdan düşen yeni kristallerin ne kadarı tamamen rastgele (LevelData.refillLuck).
    // Level boyunca değişmiyor, Start'ta bir kez okunuyor.
    float refillLuck = 1f;

    Crystal[,] crystals;

    // Kristal gridinin üstündeki ikinci katman: hangi karede hangi engel var.
    ObstacleGrid obstacles;

    bool isProcessing = false;

    // Power-up'ın nerede doğacağını belirleyen takas konumları.
    // movedTo = oyuncunun parmağıyla sürüklediği kristalin takastan sonra durduğu kare.
    // movedFrom = yer değiştiren diğer kristalin geldiği kare (yedek).
    int movedToX = -1, movedToY = -1;
    int movedFromX = -1, movedFromY = -1;

    // Tahta yenilenirken kristali olan kareler - boşluklar yerinde kalmalı
    readonly List<Vector2Int> refreshCells = new List<Vector2Int>();

    // GetNonMatchingType'ın aday listesi - her çağrıda yeni liste ayırmayalım
    readonly List<CrystalType> safeTypes = new List<CrystalType>();

    // --- İpucu ---
    float idleTimer;
    bool hintShowing;
    readonly List<Crystal> hintCrystals = new List<Crystal>();

    // Cascade sürerken gelen swipe yutulmasın - tek slotluk bekleyen hamle
    bool hasPendingSwap;
    int pendingX, pendingY;
    Vector2Int pendingDir;

    // --- Yeniden kullanılan koleksiyonlar ---
    // Cascade her adımında yeni liste/küme allocate etmek GC duraklamalarının
    // kod tarafındaki ana kaynağıydı. Bunlar Clear() ile tekrar kullanılıyor.
    readonly List<MatchGroup> groupBuffer = new List<MatchGroup>();
    readonly Stack<MatchGroup> groupPool = new Stack<MatchGroup>();
    readonly HashSet<Crystal> claimedH = new HashSet<Crystal>();
    readonly HashSet<Crystal> claimedV = new HashSet<Crystal>();
    readonly HashSet<Crystal> destroySet = new HashSet<Crystal>();
    readonly List<Crystal> destroyList = new List<Crystal>();
    // Bu cascade adımında doğacak power-up'lar: hangi hücrede, hangi güç,
    // hangi buff doğacak.
    readonly List<(int x, int y, PowerType power)> powersToSpawn =
        new List<(int, int, PowerType)>();

    // L / T kesişimleri - bu karelerde bomba doğuyor
    readonly List<Vector2Int> bombCells = new List<Vector2Int>();

    // Kesişim aramasının tamponları: kristal -> ait olduğu yatay / dikey dizi
    readonly Dictionary<Crystal, MatchGroup> horizontalOwner = new Dictionary<Crystal, MatchGroup>();
    readonly Dictionary<Crystal, MatchGroup> verticalOwner = new Dictionary<Crystal, MatchGroup>();

    readonly Queue<Crystal> expandQueue = new Queue<Crystal>();
    readonly HashSet<Crystal> expandInitial = new HashSet<Crystal>();
    readonly HashSet<Crystal> firedPowers = new HashSet<Crystal>();
    readonly List<Crystal> affectedBuffer = new List<Crystal>();

    // Tek patlamada aynı engele iki kez vurulmasın: y * width + x anahtarı.
    // (Üç kristalin ortak komşusu olan bir taş, aksi hâlde tek hamlede üç can yerdi.)
    readonly HashSet<int> damagedObstacles = new HashSet<int>();

    // Bu patlamada güçlerin (4-way, bomba, kombolar) ALANINA giren kareler,
    // y * width + x. Güç kristal topluyor ama katı ve blokta kristal yok: alan
    // üstlerinden geçse de kristal tabanlı yol onlara dokunmuyordu. Patlama
    // işlenirken bu karelerdeki engeller doğrudan vurulur (DamageObstaclesFrom).
    //
    // Her patlamanın başında temizlenir (TryPowerCombo, ProcessCascade adımı,
    // alt bar power-up'ı), DamageObstaclesFrom tüketince de boşalır.
    readonly HashSet<int> blastArea = new HashSet<int>();

    // Alt bardaki power-up'ların hedefleri. Cascade tamponlarından AYRI:
    // power-up kendi patlamasını kurarken cascade da aynı kareyi işliyor olabilir.
    readonly HashSet<Crystal> powerUpSet = new HashSet<Crystal>();
    readonly List<Crystal> powerUpList = new List<Crystal>();
    readonly List<Vector2Int> powerUpCells = new List<Vector2Int>();

    void Start()
    {
        // Grid ayarları moddan bağımsız olarak GameManager'dan gelir
        if (GameManager.Instance != null)
        {
            width = GameManager.Instance.BoardWidth;
            height = GameManager.Instance.BoardHeight;
        }

        crystals = new Crystal[width, height];

        // Engeller kristallerden ÖNCE kurulmalı: katı karelere kristal doğmuyor.
        BuildObstacles();

        RefreshVariety();

        if (GameManager.Instance != null)
            refillLuck = GameManager.Instance.RefillLuck;

        CreateBoard();

        // Engel hedefi olan levellarda göstergenin doğru sayıyla açılması için:
        // GameManager tahta kurulmadan kaç engel olduğunu bilemez.
        if (GameManager.Instance != null)
            GameManager.Instance.RegisterBoard(this);
    }

    // ==================== TEMPO ÖNAYARLARI ====================
    // Sahnede kayıtlı değerler kodun varsayılanını ezdiği için tempo
    // değişikliği var olan sahneye kendiliğinden gelmiyor. Board bileşeninin
    // sağ tık menüsünden (ya da sağ üstteki ⋮) tek tıkla uygulanır; Ctrl+Z geri alır.

    [ContextMenu("Tempo: Yumuşak (Royal Match / Candy Crush)")]
    void ApplySmoothTempo()
    {
        ApplyTempo(swap: 0.2f, fall: 0.28f, fallFactor: 0.08f, destroy: 0.2f, stagger: 0.04f,
                   overlap: 0.8f, step: 0.05f, minSpeed: 0.8f, pause: 0.07f, wave: 0.3f);
    }

    // Karşılaştırma için - 2026-09-25 öncesi değerler
    [ContextMenu("Tempo: Eski (hızlı)")]
    void ApplyOldTempo()
    {
        ApplyTempo(swap: 0.15f, fall: 0.24f, fallFactor: 0.05f, destroy: 0.13f, stagger: 0.03f,
                   overlap: 0.6f, step: 0.15f, minSpeed: 0.5f, pause: 0f, wave: 0.25f);
    }

    void ApplyTempo(float swap, float fall, float fallFactor, float destroy, float stagger,
                    float overlap, float step, float minSpeed, float pause, float wave)
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Tempo önayarı");
#endif
        tempoScale = 1f;
        swapDuration = swap;
        fallDuration = fall;
        fallDistanceFactor = fallFactor;
        destroyDuration = destroy;
        fillStagger = stagger;
        settleOverlap = overlap;
        useCascadeTempo = true;
        cascadeSpeedStep = step;
        minSpeedMultiplier = minSpeed;
        cascadePause = pause;
        waveDuration = wave;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // ==================== GRID KURULUMU ====================

    void BuildObstacles()
    {
        obstacles = new ObstacleGrid();
        obstacles.Init(width, height, obstaclePrefab, transform, GridToWorld, obstacleZOffset);

        LevelData level = GameManager.Instance != null ? GameManager.Instance.CurrentLevel : null;

        obstacles.ApplyLevelLayout(level);
        WarnIfNoObstacles(level);
    }

    // Levelin tek hedefi engelleri kırmak. Engelsiz bir level kazanılamaz -
    // oyuncu hamlesi bitene kadar oynar ve kaybeder. Sessiz kalmayalım.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void WarnIfNoObstacles(LevelData level)
    {
        if (level == null) return;
        if (obstacles.Total > 0) return;

        Debug.LogWarning($"[Board] '{level.name}' levelinde hiç engel yok. " +
                         $"Levelin tek hedefi engelleri kırmak, yani bu level " +
                         $"kazanılamaz. Level asset'inin Inspector'ından engel " +
                         $"tahtasını doldur.", this);
    }

    void CreateBoard()
    {
        refreshCells.Clear();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector3 pos = GridToWorld(x, y);

                if (backgroundPrefab != null)
                    Instantiate(backgroundPrefab, pos + Vector3.forward, Quaternion.identity, transform);

                // Katı engelde kristal olmaz - kare tamamen kapalı
                if (IsBlocked(x, y)) continue;

                refreshCells.Add(new Vector2Int(x, y));
            }
        }

        BuildValidLayout();
    }

    // ==================== ENGEL SORGULARI ====================

    // Katı engel ya da blok: kristal yok, kare oynanamaz, üstünden kristal düşemez.
    // Şeffaf engel buraya GİRMEZ - o kare her açıdan normal oynanır.
    bool IsBlocked(int x, int y)
    {
        return obstacles != null && obstacles.IsBlocking(x, y);
    }

    // Tahtada kalan engel sayısı - levelin hedefi bunu sıfıra indirmek
    public int RemainingObstacles => obstacles != null ? obstacles.Remaining : 0;

    // Level başında tahtada kaç engel vardı
    public int TotalObstacles => obstacles != null ? obstacles.Total : 0;

    // Üst bardaki tür bazlı göstergeler için. "Kalan" değerler bir kare
    // tamamen temizlendiğinde düşer, yani hiçbir zaman artmazlar.
    public int TotalTransparentObstacles => obstacles != null ? obstacles.TotalTransparent : 0;
    public int TotalSolidObstacles => obstacles != null ? obstacles.TotalSolid : 0;
    public int RemainingTransparentObstacles => obstacles != null ? obstacles.RemainingTransparent : 0;
    public int RemainingSolidObstacles => obstacles != null ? obstacles.RemainingSolid : 0;
    public int TotalBlockObstacles => obstacles != null ? obstacles.TotalBlock : 0;
    public int RemainingBlockObstacles => obstacles != null ? obstacles.RemainingBlock : 0;

    // Hedef göstergesi blok ikonunu buna göre seçiyor
    public BlockTheme BlockTheme => obstacles != null ? obstacles.BlockTheme : BlockTheme.Lav;

    void RefreshVariety()
    {
        int v = GameManager.Instance != null ? GameManager.Instance.CrystalVariety : 4;

        if (CrystalPool.Instance != null)
            v = Mathf.Min(v, CrystalPool.Instance.PrefabCount);

        variety = Mathf.Max(3, v);
    }

    CrystalType RandomCrystalType()
    {
        return (CrystalType)Random.Range(0, variety);
    }

    // (x,y)'ye konulduğunda hazır eşleşme yapmayacak bir çeşit seç.
    // DÖRT yöne birden bakıyor: tahta kurulurken sağ ve üst komşular henüz boş
    // ama alttan gelen yeni sıra için üsttekiler dolu - tek bir kural yetsin.
    CrystalType GetNonMatchingType(int x, int y)
    {
        safeTypes.Clear();

        for (int i = 0; i < variety; i++)
        {
            CrystalType candidate = (CrystalType)i;

            if (SameRun(x, y, -1, 0, candidate) + SameRun(x, y, 1, 0, candidate) >= 2) continue;
            if (SameRun(x, y, 0, -1, candidate) + SameRun(x, y, 0, 1, candidate) >= 2) continue;

            safeTypes.Add(candidate);
        }

        if (safeTypes.Count == 0) return RandomCrystalType();
        return safeTypes[Random.Range(0, safeTypes.Count)];
    }

    // (x,y)'den (dx,dy) yönünde kaç tane aynı çeşit var - en fazla 2 sayıyoruz,
    // üçüncüsü zaten eşleşme demek.
    int SameRun(int x, int y, int dx, int dy, CrystalType type)
    {
        int count = 0;

        for (int i = 1; i <= 2; i++)
        {
            if (GetMatchTypeAt(x + dx * i, y + dy * i) != type) break;
            count++;
        }

        return count;
    }

    // Eşleşme için tip döndürür.
    // ÖNEMLİ: Renk bombası hiçbir şeyle eşleşmez, null döner.
    CrystalType? GetMatchTypeAt(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return null;

        Crystal f = crystals[x, y];
        if (f == null) return null;

        // Renk bombası eşleşmeye girmez - sadece takasla kullanılır
        if (f.Power == PowerType.ColorBomb) return null;

        return f.Type;
    }

    Crystal SpawnCrystal(int x, int y, CrystalType type, Vector3 spawnPos)
    {
        Crystal f = CrystalPool.Instance.Get(type, spawnPos, transform);

        f.X = x;
        f.Y = y;
        f.Type = type;

        crystals[x, y] = f;
        return f;
    }

    // Cascade derinleştikçe animasyonlar hızlansın
    float SpeedMultiplier(int cascadeLevel)
    {
        if (!useCascadeTempo) return 1f;

        float mult = 1f - (cascadeLevel - 1) * cascadeSpeedStep;
        return Mathf.Max(minSpeedMultiplier, mult);
    }

    // Tahtanın ÜST kenarının dünya Y'si: en üst satırın merkezi + yarım hücre.
    // Yukarıdan dökülen kristaller bu çizgiyi geçene kadar gizleniyor.
    float BoardTopY => transform.TransformPoint(GridToWorld(0, height - 1)).y
                       + spacing * 0.5f * transform.lossyScale.y;

    Vector3 GridToWorld(int x, int y)
    {
        Vector3 offset = new Vector3((width - 1) / 2f * spacing, (height - 1) / 2f * spacing, 0);
        return new Vector3(x * spacing, y * spacing, 0) - offset;
    }

    // ==================== İPUCU ====================

    // Oyuncu bir süre kıpırdamayınca yapılabilecek bir hamleyi gösteriyoruz.
    // Sayaç yalnızca tahta boştayken işliyor: cascade sürerken ya da oyun
    // bitmişken oyuncunun beklemesi normal.
    void Update()
    {
        // Kapalıysa ekranda kalmış bir ipucu varsa da toplansın
        if (hintDelay <= 0f) { ClearHint(); return; }

        if (!CanShowHint())
        {
            ClearHint();
            idleTimer = 0f;
            return;
        }

        // İpucu ekranda: oyuncu hamle yapana kadar orada kalsın
        if (hintShowing) return;

        idleTimer += Time.deltaTime;

        if (idleTimer >= hintDelay) ShowHint();
    }

    bool CanShowHint()
    {
        if (crystals == null || isProcessing) return false;
        if (GameManager.Instance != null && GameManager.Instance.InputLocked) return false;

        return true;
    }

    // Oyuncu ekrana dokundu ya da hamle yaptı: ipucu kalksın, sayaç sıfırlansın.
    // InputHandler basma anında da çağırıyor - parmağın altında kristal
    // zıplarken hamle yapmak rahatsız edici.
    public void NotifyPlayerActivity()
    {
        ClearHint();
        idleTimer = 0f;
    }

    void ShowHint()
    {
        ClearHint();

        if (!FindHintMove(hintCrystals))
        {
            // Hamle yoksa gösterecek bir şey de yok. Sayacı sıfırlıyoruz ki
            // her karede tüm tahtayı yeniden taramayalım.
            idleTimer = 0f;
            return;
        }

        foreach (Crystal f in hintCrystals)
            if (f != null) f.PlayHint(hintHopHeight, hintCycleDuration);

        hintShowing = true;
    }

    void ClearHint()
    {
        hintShowing = false;

        if (hintCrystals.Count == 0) return;

        foreach (Crystal f in hintCrystals)
            if (f != null) f.StopHint();

        hintCrystals.Clear();
    }

    // Yapılabilecek bir hamle bul ve ilgili kristalleri output'a yaz.
    // Tarama rastgele bir kareden başlıyor: hep (0,0)'dan başlasaydı aynı
    // ipucu tekrar tekrar gösterilirdi.
    bool FindHintMove(List<Crystal> output)
    {
        output.Clear();

        int total = width * height;
        int start = Random.Range(0, total);

        for (int i = 0; i < total; i++)
        {
            int index = (start + i) % total;
            int x = index % width;
            int y = index / width;

            if (TryHintSwap(x, y, x + 1, y, output)) return true;
            if (TryHintSwap(x, y, x, y + 1, output)) return true;
        }

        return false;
    }

    bool TryHintSwap(int x1, int y1, int x2, int y2, List<Crystal> output)
    {
        output.Clear();

        if (x2 >= width || y2 >= height) return false;
        if (IsBlocked(x1, y1) || IsBlocked(x2, y2)) return false;

        Crystal a = crystals[x1, y1];
        Crystal b = crystals[x2, y2];

        if (a == null || b == null) return false;

        // Buff'lardan biri varsa hamle geçerli: üçü de neyle takas edilirse
        // edilsin ateşleniyor, eşleşme aranmıyor. Oyuncuya önce bunu
        // gösterelim - en değerli hamle.
        if (a.Power != PowerType.None || b.Power != PowerType.None)
        {
            output.Add(a);
            output.Add(b);
            return true;
        }

        // Sanal takas - gridde deneyip hemen geri alıyoruz
        crystals[x1, y1] = b;
        crystals[x2, y2] = a;

        CollectMatchAt(x1, y1, output);
        CollectMatchAt(x2, y2, output);

        crystals[x1, y1] = a;
        crystals[x2, y2] = b;

        return output.Count > 0;
    }

    // (x,y)'deki kristalin parçası olduğu 3+ diziyi output'a ekler
    void CollectMatchAt(int x, int y, List<Crystal> output)
    {
        CrystalType? type = GetMatchTypeAt(x, y);
        if (type == null) return;

        int left = x, right = x;
        while (left - 1 >= 0 && GetMatchTypeAt(left - 1, y) == type) left--;
        while (right + 1 < width && GetMatchTypeAt(right + 1, y) == type) right++;

        if (right - left + 1 >= 3)
            for (int i = left; i <= right; i++) AddUnique(output, crystals[i, y]);

        int down = y, up = y;
        while (down - 1 >= 0 && GetMatchTypeAt(x, down - 1) == type) down--;
        while (up + 1 < height && GetMatchTypeAt(x, up + 1) == type) up++;

        if (up - down + 1 >= 3)
            for (int i = down; i <= up; i++) AddUnique(output, crystals[x, i]);
    }

    static void AddUnique(List<Crystal> list, Crystal f)
    {
        if (f == null || list.Contains(f)) return;
        list.Add(f);
    }

    // ==================== HAMLE ====================

    public void TrySwap(int x, int y, Vector2Int direction)
    {
        NotifyPlayerActivity();

        if (GameManager.Instance != null && GameManager.Instance.InputLocked) return;

        int targetX = x + direction.x;
        int targetY = y + direction.y;

        if (targetX < 0 || targetX >= width || targetY < 0 || targetY >= height) return;

        // Katı engelli kare oynanmaz - ne kaynak ne hedef olabilir
        if (IsBlocked(x, y) || IsBlocked(targetX, targetY)) return;

        // Cascade sürerken hamleyi yutma, sakla. Üzerine yazıyoruz:
        // oyuncunun en son yaptığı swipe geçerli olsun.
        if (isProcessing)
        {
            hasPendingSwap = true;
            pendingX = x;
            pendingY = y;
            pendingDir = direction;
            return;
        }

        if (crystals[x, y] == null || crystals[targetX, targetY] == null) return;

        StartCoroutine(SwapRoutine(x, y, targetX, targetY));
    }

    IEnumerator SwapRoutine(int x, int y, int targetX, int targetY)
    {
        isProcessing = true;

        yield return StartCoroutine(SwapSequence(x, y, targetX, targetY));

        isProcessing = false;

        // Beklemede hamle varsa hemen işle - oyuncu boşluk hissetmesin
        if (hasPendingSwap)
        {
            hasPendingSwap = false;
            TrySwap(pendingX, pendingY, pendingDir);
        }
    }

    IEnumerator SwapSequence(int x, int y, int targetX, int targetY)
    {
        SwapCrystals(x, y, targetX, targetY);
        yield return new WaitForSeconds(SwapTime);

        Crystal atOrigin = crystals[x, y];
        Crystal atTarget = crystals[targetX, targetY];

        // --- 1. Özel power-up kombinasyonu var mı? ---
        ComboResult combo = TryPowerCombo(atOrigin, atTarget);

        if (combo != null)
        {
            // Kombo iki kristali de tüketiyor; sonraki ProcessCascade artık
            // oyuncunun yerleştirmesiyle değil, dökülen kristallerle ilgili.
            ClearSwapOrigin();

            if (GameManager.Instance != null) GameManager.Instance.UseMove();

            // Multicolor + buff: ışınlar çıkar, o rengin tamamı buff'a
            // dönüşür, patlama listesi dolar
            if (combo.IsConversion)
                yield return StartCoroutine(PlayPowerConversion(combo));

            // Renk bombasıysa önce elektrik ışınları gitsin
            if (combo.IsColorBomb)
                yield return StartCoroutine(PlayLightningStrike(combo.SourcePosition, combo.Crystals));

            yield return StartCoroutine(DestroyAndRefill(combo.Crystals, 1, null,
                                                          combo.SourcePosition));
            yield return StartCoroutine(ProcessCascade());
            yield return StartCoroutine(EndOfMoveChecks());

            yield break;
        }

        // --- 2. Normal eşleşme var mı? ---
        // Grup listesi kurmaya gerek yok, sadece "eşleşme var mı" bilgisi yeterli.
        bool matchAtOrigin = HasMatchAtCell(x, y);
        bool matchAtTarget = HasMatchAtCell(targetX, targetY);

        if (matchAtOrigin || matchAtTarget)
        {
            // Oyuncunun sürüklediği kristal takastan sonra HEDEF karede duruyor;
            // yerinden oynayan diğer kristal ise başlangıç karesine geçiyor.
            // Power-up öncelikle hedefte doğsun - "getirdiğim yerde oluştu" hissi.
            // Eskiden ikisi de eşleşince başlangıç karesi seçiliyordu, yani
            // oyuncunun dokunmadığı kristalin yeri; power-up alakasız bir hücrede
            // beliriyordu (ya da hiç bulunamayıp grubun ortasına düşüyordu).
            movedToX = targetX;
            movedToY = targetY;
            movedFromX = x;
            movedFromY = y;

            if (GameManager.Instance != null) GameManager.Instance.UseMove();

            yield return StartCoroutine(ProcessCascade());
            yield return StartCoroutine(EndOfMoveChecks());
        }
        else
        {
            // Geçersiz hamle - geri al
            SwapCrystals(x, y, targetX, targetY);
            yield return new WaitForSeconds(SwapTime);
        }
    }

    // Hamle bittikten sonra: hedef kontrolü, deadlock, oyun sonu
    IEnumerator EndOfMoveChecks()
    {
        if (GameManager.Instance == null) yield break;
        if (GameManager.Instance.IsGameOver) yield break;

        // --- Hedefler tamamlandıysa kazandın ---
        // Kalan hamlelere hiçbir şey olmuyor; tahtanın durulmasını bekleyip
        // kazanma ekranını açıyoruz.
        if (GameManager.Instance.AreGoalsComplete())
        {
            yield return new WaitForSeconds(0.4f);

            GameManager.Instance.FinishWin();
            yield break;
        }

        // --- Hamle kalmadıysa tahtayı baştan kur ---
        if (!HasValidMove())
        {
            yield return new WaitForSeconds(0.3f);
            yield return StartCoroutine(RefreshBoardRoutine());
        }

        // --- Hamle bittiyse kaybettin ---
        if (GameManager.Instance.MovesLeft <= 0)
            GameManager.Instance.FinishLose();
    }

    // İki kristali hem grid'de hem ekranda yer değiştir. Sadece veri + animasyon;
    // eşleşme kontrolü çağıranın işi.
    void SwapCrystals(int x1, int y1, int x2, int y2)
    {
        Crystal a = crystals[x1, y1];
        Crystal b = crystals[x2, y2];

        if (a == null || b == null) return;

        crystals[x1, y1] = b;
        crystals[x2, y2] = a;

        a.X = x2;
        a.Y = y2;
        b.X = x1;
        b.Y = y1;

        a.AnimateSwap(GridToWorld(x2, y2), SwapTime);
        b.AnimateSwap(GridToWorld(x1, y1), SwapTime);
    }

    // ==================== POWER-UP KOMBOLARI ====================

    // İki power-up takas edilince ne olacağını çözer. Kombo yoksa null döner
    // ve normal eşleşme akışı devam eder.
    ComboResult TryPowerCombo(Crystal a, Crystal b)
    {
        bool aBomb = a.Power == PowerType.ColorBomb;
        bool bBomb = b.Power == PowerType.ColorBomb;

        // Multicolor dışındaki buff'lar: 4-way ve bomba
        bool aBuff = a.Power != PowerType.None && !aBomb;
        bool bBuff = b.Power != PowerType.None && !bBomb;

        // Yeni patlama - önceki hamlenin alanı buraya taşınmasın
        blastArea.Clear();

        // --- İki renk bombası: tahtadaki HER ŞEY gider ---
        if (aBomb && bBomb)
        {
            ComboResult all = new ComboResult
            {
                IsColorBomb = true,
                SourcePosition = a.transform.position
            };

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    AddBlastArea(x, y);
                    if (crystals[x, y] != null) all.Crystals.Add(crystals[x, y]);
                }

            if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();

            return all;
        }

        // --- Multicolor + başka bir buff ---
        // RASTGELE bir rengin tamamı o buff'a dönüşür ve hemen ateşlenir.
        // Rengi takas edilen kristalden değil rastgele seçiyoruz: oyuncu hangi
        // renge denk geleceğini bilemesin, kombo sürpriz kalsın.
        if ((aBomb && bBuff) || (bBomb && aBuff))
        {
            Crystal bomb = aBomb ? a : b;
            Crystal buff = aBomb ? b : a;

            return new ComboResult
            {
                IsConversion = true,
                ConvertType = RandomCrystalType(),
                ConvertPower = buff.Power,
                MergeFrom = bomb,
                SourcePosition = bomb.transform.position
            };
        }

        // --- Renk bombası + normal kristal: o renkteki her şey patlar ---
        if (aBomb || bBomb)
        {
            Crystal bomb = aBomb ? a : b;
            Crystal target = aBomb ? b : a;

            bomb.BombTargetType = target.Type;

            HashSet<Crystal> hit = new HashSet<Crystal> { bomb, target };

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Crystal f = crystals[x, y];

                    if (f != null && f.Type == target.Type) hit.Add(f);
                }
            }

            // Patlayanların arasında kalan power-up'lar da zincirleme tetiklensin
            ExpandWithPowers(hit);

            if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();

            return new ComboResult
            {
                Crystals = new List<Crystal>(hit),
                IsColorBomb = true,
                SourcePosition = bomb.transform.position
            };
        }

        // --- Bomba + 4-way: TÜM tahta ---
        // İki farklı buff'ın en güçlü birleşimi. Renk seçimi yok, ayrım yok,
        // her şey gider.
        if ((a.Power == PowerType.Bomb && b.Power == PowerType.Cross) ||
            (a.Power == PowerType.Cross && b.Power == PowerType.Bomb))
        {
            HashSet<Crystal> all = new HashSet<Crystal>();

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    AddBlastArea(x, y);
                    if (crystals[x, y] != null) all.Add(crystals[x, y]);
                }

            if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();

            return new ComboResult
            {
                Crystals = new List<Crystal>(all),
                SourcePosition = b.transform.position
            };
        }

        // --- Bomba + bomba: 5x5 ---
        // Tek bombanın 3x3'ü yerine yarıçap 2. Merkez oyuncunun sürüklediği
        // kare, yani takastan sonra hedefte duran taş.
        if (a.Power == PowerType.Bomb && b.Power == PowerType.Bomb)
        {
            HashSet<Crystal> hit = new HashSet<Crystal>();

            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    int nx = b.X + dx;
                    int ny = b.Y + dy;

                    if (!InBounds(nx, ny)) continue;

                    AddBlastArea(nx, ny);
                    if (crystals[nx, ny] != null) hit.Add(crystals[nx, ny]);
                }

            // Alandaki başka buff'lar da zincirleme tetiklensin
            ExpandWithPowers(hit);

            if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();

            return new ComboResult
            {
                Crystals = new List<Crystal>(hit),
                SourcePosition = b.transform.position
            };
        }

        // --- 4-way + 4-way: 3 satır + 3 sütun ---
        // Tek 4-way bir satır bir sütun süpürüyor; ikisi birleşince merkezin
        // iki yanındaki komşu sıralar da gidiyor.
        if (a.Power == PowerType.Cross && b.Power == PowerType.Cross)
        {
            HashSet<Crystal> hit = new HashSet<Crystal>();

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    bool inRows = Mathf.Abs(y - b.Y) <= 1;
                    bool inCols = Mathf.Abs(x - b.X) <= 1;

                    if (!inRows && !inCols) continue;

                    AddBlastArea(x, y);

                    Crystal f = crystals[x, y];
                    if (f != null) hit.Add(f);
                }

            ExpandWithPowers(hit);

            if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();

            return new ComboResult
            {
                Crystals = new List<Crystal>(hit),
                SourcePosition = b.transform.position
            };
        }

        // --- 4-way / bomba, neyle takas edilirse edilsin patlar ---
        // Eşleşme aramıyoruz: oyuncu buff'ı istediği kareye sürükleyip orada
        // ateşleyebilsin. Konumlar takastan SONRAKİ hâl, yani buff oyuncunun
        // getirdiği karede patlıyor.
        //
        // İki buff yan yana geldiyse ikisi de tohum oluyor; özel bir birleşim
        // yok, ExpandWithPowers zincirlemeyi zaten yapıyor.
        if (aBuff || bBuff)
        {
            HashSet<Crystal> hit = new HashSet<Crystal>();

            if (aBuff) hit.Add(a);
            if (bBuff) hit.Add(b);

            ExpandWithPowers(hit);

            if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();

            return new ComboResult
            {
                Crystals = new List<Crystal>(hit),
                SourcePosition = (aBuff ? a : b).transform.position
            };
        }

        return null;
    }

    // Renk bombasının ışınları: kaynaktan her hedefe bir şimşek çakıyor.
    // İki hedefte bir bekliyoruz - her hedefte beklemek sekansı sürünmeye
    // çeviriyor, hiç beklememek de hepsini tek karede çakıp efekti öldürüyor.
    IEnumerator PlayLightningStrike(Vector3 source, List<Crystal> targets)
    {
        if (LightningManager.Instance == null) yield break;

        int fired = 0;

        foreach (Crystal target in targets)
        {
            if (target == null) continue;

            Vector3 position = target.transform.position;

            // Kaynağın kendisine şimşek çakma
            if ((position - source).sqrMagnitude < 0.0001f) continue;

            LightningManager.Instance.Fire(source, position);
            target.PlayShake(0.25f, 0.08f);

            fired++;

            if (fired % 2 == 0) yield return new WaitForSeconds(lightningInterval);
        }

        if (JuiceManager.Instance != null) JuiceManager.Instance.PunchZoom(1.4f);
        if (HapticManager.Instance != null) HapticManager.Instance.Play(HapticStrength.Heavy);

        yield return new WaitForSeconds(lightningHoldTime);
    }

    // ==================== EŞLEŞME BULMA ====================

    MatchGroup RentGroup(bool horizontal)
    {
        MatchGroup g = groupPool.Count > 0 ? groupPool.Pop() : new MatchGroup();

        g.Crystals.Clear();
        g.IsHorizontal = horizontal;

        return g;
    }

    void ReleaseGroups(List<MatchGroup> groups)
    {
        foreach (MatchGroup g in groups) groupPool.Push(g);
        groups.Clear();
    }

    // Tahtadaki tüm eşleşme gruplarını 'output' içine yazar.
    // Kullanan taraf işi bitince ReleaseGroups ile grupları havuza geri vermeli.
    void FindAllGroups(List<MatchGroup> output)
    {
        ReleaseGroups(output);

        claimedH.Clear();
        claimedV.Clear();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                CrystalType? type = GetMatchTypeAt(x, y);
                if (type == null) continue;

                TryAddGroup(output, x, y, type.Value, true);
                TryAddGroup(output, x, y, type.Value, false);
            }
        }
    }

    // (x,y)'den uzanan yatay/dikey diziyi çıkar. 3+ ise ve aynı yönde daha önce
    // sahiplenilmemişse output'a ekle, değilse grubu havuza geri ver.
    void TryAddGroup(List<MatchGroup> output, int x, int y, CrystalType type, bool horizontal)
    {
        MatchGroup g = RentGroup(horizontal);
        g.Crystals.Add(crystals[x, y]);

        if (horizontal)
        {
            for (int i = x + 1; i < width && GetMatchTypeAt(i, y) == type; i++) g.Crystals.Add(crystals[i, y]);
            for (int i = x - 1; i >= 0 && GetMatchTypeAt(i, y) == type; i--) g.Crystals.Add(crystals[i, y]);
        }
        else
        {
            for (int i = y + 1; i < height && GetMatchTypeAt(x, i) == type; i++) g.Crystals.Add(crystals[x, i]);
            for (int i = y - 1; i >= 0 && GetMatchTypeAt(x, i) == type; i--) g.Crystals.Add(crystals[x, i]);
        }

        HashSet<Crystal> claimed = horizontal ? claimedH : claimedV;

        if (g.Count < 3 || IsAnyClaimed(g, claimed))
        {
            groupPool.Push(g);
            return;
        }

        foreach (Crystal f in g.Crystals) claimed.Add(f);

        output.Add(g);
    }

    bool IsAnyClaimed(MatchGroup g, HashSet<Crystal> claimed)
    {
        foreach (Crystal f in g.Crystals)
            if (claimed.Contains(f)) return true;

        return false;
    }

    // ==================== MULTICOLOR + BUFF ====================

    // Multicolor başka bir buff'la takas edilince RASTGELE bir rengin tamamı
    // o buff'a dönüşür, sonra hepsi birden ateşlenir.
    IEnumerator PlayPowerConversion(ComboResult combo)
    {
        Crystal superBuff = combo.MergeFrom;

        // Dönüşecek kristalleri topla.
        // Zaten multicolor olanlara dokunmuyoruz: onları daha zayıf bir buff'a
        // çevirmek oyuncunun elindeki gücü düşürürdü. Patlamaya denk gelirlerse
        // ExpandWithPowers onları zaten zincirleme tetikliyor.
        List<Crystal> targets = new List<Crystal>();

        for (int i = 0; i < width; i++)
            for (int j = 0; j < height; j++)
            {
                Crystal f = crystals[i, j];

                if (f == null || f == superBuff) continue;
                if (f.Type != combo.ConvertType) continue;
                if (f.Power == PowerType.ColorBomb) continue;

                targets.Add(f);
            }

        // Süper buff bu komboda tükeniyor. Gridden şimdi düşüyor ki ne dönüşüm
        // taramasına ne de patlama listesine karışsın - patlama listesine girseydi
        // havuza iki kez dönerdi.
        if (superBuff != null &&
            superBuff.X >= 0 && superBuff.X < width &&
            superBuff.Y >= 0 && superBuff.Y < height &&
            crystals[superBuff.X, superBuff.Y] == superBuff)
            crystals[superBuff.X, superBuff.Y] = null;

        // --- 1. Elektrik ışınları hedeflere gitsin ---
        yield return StartCoroutine(PlayLightningStrike(combo.SourcePosition, targets));

        // --- 2. Süper buff patlayıp havuza dönsün ---
        if (superBuff != null)
        {
            Crystal captured = superBuff;
            superBuff.PlayDestroy(DestroyTime, false, () => CrystalPool.Instance.Return(captured));
        }

        // --- 3. Hepsi seçilen buff'a dönüşsün ---
        for (int i = 0; i < targets.Count; i++)
        {
            Crystal t = targets[i];
            if (t == null) continue;

            t.SetPower(combo.ConvertPower);
            t.PlaySpawnAnimation();
        }

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayPowerUp();

        if (HapticManager.Instance != null)
            HapticManager.Instance.Play(HapticStrength.Medium);

        yield return new WaitForSeconds(conversionHoldTime);

        // --- 4. Hepsini ateşle ---
        HashSet<Crystal> blast = new HashSet<Crystal>(targets);
        ExpandWithPowers(blast);

        combo.Crystals.Clear();
        combo.Crystals.AddRange(blast);
    }

    // ==================== POWER-UP ETKİSİ ====================

    void GetPowerAffected(Crystal power, List<Crystal> output)
    {
        switch (power.Power)
        {
            case PowerType.Cross:
            {
                // 4-way: bulunduğu karenin HEM satırı HEM sütunu.
                // Görsel iz yok - buff efektleri sıfırdan kurulacak.
                for (int i = 0; i < width; i++)
                {
                    AddBlastArea(i, power.Y);
                    if (crystals[i, power.Y] != null) output.Add(crystals[i, power.Y]);
                }

                for (int j = 0; j < height; j++)
                {
                    AddBlastArea(power.X, j);
                    if (crystals[power.X, j] != null) output.Add(crystals[power.X, j]);
                }

                if (AudioManager.Instance != null)
                    AudioManager.Instance.PlayPowerUp();

                break;
            }

            case PowerType.Bomb:
            {
                // 3x3: kendisi ve sekiz komşusu
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = power.X + dx;
                        int ny = power.Y + dy;

                        if (!InBounds(nx, ny)) continue;

                        AddBlastArea(nx, ny);
                        if (crystals[nx, ny] != null) output.Add(crystals[nx, ny]);
                    }

                if (AudioManager.Instance != null)
                    AudioManager.Instance.PlayPowerUp();

                break;
            }

            // Multicolor'ın ALANI yok - bir rengi topluyor. Engele yalnızca
            // yanında patlayan kristal üstünden işliyor.
            case PowerType.ColorBomb:
            {
                // Zincirleme tetiklendiyse hedef tip belirlenmemiş olabilir
                CrystalType target = power.BombTargetType;

                for (int i = 0; i < width; i++)
                    for (int j = 0; j < height; j++)
                    {
                        Crystal f = crystals[i, j];
                        if (f == null || f == power) continue;

                        if (f.Type == target) output.Add(f);
                    }

                // Tek görsel elektrik ışını (PlayLightningStrike) - burada efekt yok
                break;
            }
        }
    }

    // Patlayacak kümeyi power-up zincirleriyle genişletir (kümeyi yerinde büyütür)
    void ExpandWithPowers(HashSet<Crystal> all)
    {
        expandQueue.Clear();
        expandInitial.Clear();
        firedPowers.Clear();

        foreach (Crystal f in all)
        {
            expandInitial.Add(f);
            expandQueue.Enqueue(f);
        }

        while (expandQueue.Count > 0)
        {
            Crystal f = expandQueue.Dequeue();

            if (f == null || f.Power == PowerType.None) continue;
            if (!firedPowers.Add(f)) continue;   // aynı power iki kez patlamasın

            // Zincirleme tetiklenen renk bombası rastgele bir çeşit seçsin
            if (f.Power == PowerType.ColorBomb && !expandInitial.Contains(f))
                f.BombTargetType = RandomCrystalType();

            affectedBuffer.Clear();
            GetPowerAffected(f, affectedBuffer);

            foreach (Crystal affected in affectedBuffer)
                if (all.Add(affected)) expandQueue.Enqueue(affected);
        }
    }

    // DÜZ dizi uzunluğuna göre hangi buff doğacak.
    //
    //   3'lü -> hiçbir şey, sadece patlar
    //   4'lü -> 4-way   (satır + sütun)
    //   5'li -> multicolor
    //
    // L ve T buraya girmiyor: onlar tek dizi değil, kesişim.
    // FindBombIntersections hallediyor.
    PowerType GetPowerForGroup(MatchGroup group)
    {
        if (group.Count >= 5) return PowerType.ColorBomb;
        if (group.Count == 4) return PowerType.Cross;
        return PowerType.None;
    }

    // L / T / + şekillerini bulur: aynı kristal hem bir yatay hem bir dikey
    // diziye aitse orası kesişimdir. FindAllGroups zaten yatay ve dikey
    // taramaları ayrı tuttuğu (claimedH / claimedV) için bir kristal ikisine
    // birden girebiliyor - şekli buradan yakalıyoruz.
    //
    // 5'li düz dizinin kestiği kesişimler atlanıyor: orada multicolor doğacak,
    // üstüne bir de bomba vermek tek hamleden iki buff çıkarırdı.
    void FindBombIntersections(List<MatchGroup> groups, List<Vector2Int> output)
    {
        output.Clear();

        horizontalOwner.Clear();
        verticalOwner.Clear();

        foreach (MatchGroup g in groups)
        {
            var owner = g.IsHorizontal ? horizontalOwner : verticalOwner;

            foreach (Crystal f in g.Crystals) owner[f] = g;
        }

        foreach (var pair in horizontalOwner)
        {
            if (!verticalOwner.TryGetValue(pair.Key, out MatchGroup vertical)) continue;

            if (pair.Value.Count >= 5 || vertical.Count >= 5) continue;

            output.Add(new Vector2Int(pair.Key.X, pair.Key.Y));
        }
    }

    // Grup, bulunan kesişim hücrelerinden birini içeriyor mu
    bool GroupHitsBombCell(MatchGroup group)
    {
        if (bombCells.Count == 0) return false;

        foreach (Crystal f in group.Crystals)
            foreach (Vector2Int cell in bombCells)
                if (f.X == cell.x && f.Y == cell.y) return true;

        return false;
    }

    // ==================== ALT BARDAKİ POWER-UP'LAR ====================
    // Marketten alınan power-up'ların tahtaya değdiği TEK yer. Hangi slotun
    // hangi etkiyi taşıdığını Board bilmiyor - PowerUpController söylüyor.
    // Board'ın işi sadece "şu etkiyi şu karede uygula".

    // Tahta bir hamleyi işliyor mu. Cascade ortasında power-up kullanılamaz:
    // patlamakta olan bir kristali hedef almak grid'i tutarsız bırakır.
    public bool IsBusy => isProcessing;

    public bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

    // GridToWorld'ün tersi. Tahta sahnede kaydırılmış ya da ölçeklenmiş
    // olabilir, o yüzden dünya -> yerel dönüşümü transform üzerinden yapıyoruz.
    public bool WorldToGrid(Vector3 world, out int x, out int y)
    {
        x = y = -1;

        if (spacing <= 0f) return false;

        Vector3 local = transform.InverseTransformPoint(world);

        float ox = (width - 1) / 2f * spacing;
        float oy = (height - 1) / 2f * spacing;

        x = Mathf.RoundToInt((local.x + ox) / spacing);
        y = Mathf.RoundToInt((local.y + oy) / spacing);

        return InBounds(x, y);
    }

    // Power-up'ı uygula. Tahta meşgulse ya da oyun kilitliyse FALSE döner -
    // çağıran taraf stoktan düşmeden önce buna bakmalı, yoksa hiçbir şey
    // olmadan power-up yanardı.
    public bool ApplyPowerUp(PowerUpEffect effect, int x, int y, int amount)
    {
        if (isProcessing) return false;
        if (GameManager.Instance != null && GameManager.Instance.InputLocked) return false;

        if (effect.NeedsTarget() && !InBounds(x, y)) return false;

        StartCoroutine(PowerUpRoutine(effect, x, y, amount));
        return true;
    }

    IEnumerator PowerUpRoutine(PowerUpEffect effect, int x, int y, int amount)
    {
        isProcessing = true;

        NotifyPlayerActivity();

        // Power-up bir takas değil: doğacak buff'ların "oyuncunun getirdiği
        // kare" tercihi burada anlamsız, grubun ortası kullanılsın.
        ClearSwapOrigin();

        yield return StartCoroutine(RunPowerUpEffect(effect, x, y, amount));

        yield return StartCoroutine(ProcessCascade());
        yield return StartCoroutine(EndOfMoveChecks());

        isProcessing = false;

        // Power-up sürerken yapılan swipe yutulmasın - takasla aynı kural
        if (hasPendingSwap)
        {
            hasPendingSwap = false;
            TrySwap(pendingX, pendingY, pendingDir);
        }
    }

    IEnumerator RunPowerUpEffect(PowerUpEffect effect, int x, int y, int amount)
    {
        switch (effect)
        {
            case PowerUpEffect.Karistir:
                // Banner'sız: "HAMLE KALMADI!" yazısı burada yalan olur -
                // tahtada hamle var, oyuncu kendi isteğiyle karıştırıyor.
                yield return StartCoroutine(RefreshBoardRoutine(false));
                yield break;

            case PowerUpEffect.HamleEkle:
                if (GameManager.Instance != null)
                    GameManager.Instance.AddMoves(Mathf.Max(1, amount));

                if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();
                if (HapticManager.Instance != null) HapticManager.Instance.Play(HapticStrength.Medium);

                yield break;

            case PowerUpEffect.MultiColorOlustur:
            {
                // Bu etki hiçbir şey patlatmıyor: seçilen kristali multicolor
                // buff'ına çeviriyor. Oyuncu onu sonra istediği kristalle
                // takas edip kendi ateşliyor.
                Crystal target = InBounds(x, y) ? crystals[x, y] : null;

                // Katı engelli kareye basıldı - çevrilecek kristal yok.
                // Vuruşu engele yazıyoruz ki power-up sessizce yanmasın.
                if (target == null)
                {
                    powerUpCells.Clear();
                    powerUpCells.Add(new Vector2Int(x, y));

                    DamageObstaclesFrom(null, null, powerUpCells);

                    if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();

                    yield return StartCoroutine(SettleBoard(1f));
                    yield break;
                }

                target.SetPower(PowerType.ColorBomb);
                target.PlaySpawnAnimation();

                if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();
                if (HapticManager.Instance != null) HapticManager.Instance.Play(HapticStrength.Medium);

                yield break;
            }
        }

        powerUpSet.Clear();
        CollectPowerUpTargets(effect, x, y, amount, powerUpSet);

        // Hedef karede kristal yok - katı engelli bir kareye vuruldu. Patlayacak
        // bir şey yok ama engel vuruşu YENMELİ, yoksa çekiç taşa işlemiyor gibi
        // görünürdü.
        if (powerUpSet.Count == 0)
        {
            powerUpCells.Clear();
            powerUpCells.Add(new Vector2Int(x, y));

            DamageObstaclesFrom(null, null, powerUpCells);

            if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();

            // Kırılan katı engel şeffafa döndü, o kare artık kristal bekliyor
            yield return StartCoroutine(SettleBoard(1f));
            yield break;
        }

        // Alandaki mevcut buff'lar da zincirleme patlasın -
        // eşleşmeyle aynı kural, oyuncunun biriktirdiği güçler boşa gitmesin.
        blastArea.Clear();
        ExpandWithPowers(powerUpSet);

        powerUpList.Clear();
        powerUpList.AddRange(powerUpSet);

        if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUp();
        if (HapticManager.Instance != null) HapticManager.Instance.Play(HapticStrength.Heavy);

        yield return StartCoroutine(DestroyAndRefill(powerUpList, 1));
    }

    // Etkinin hangi kristalleri kapsadığı. Sadece TOPLAR - patlatma, engel
    // hasarı ve zincirleme tetikleme çağıranın işi.
    void CollectPowerUpTargets(PowerUpEffect effect, int x, int y, int amount,
                               HashSet<Crystal> output)
    {
        switch (effect)
        {
            case PowerUpEffect.BlokKirma:
                AddCrystalAt(x, y, output);
                break;
        }
    }

    void AddCrystalAt(int x, int y, HashSet<Crystal> output)
    {
        if (!InBounds(x, y)) return;

        Crystal f = crystals[x, y];
        if (f != null) output.Add(f);
    }

    // ==================== PATLAMA / DÜŞME ====================

    IEnumerator ProcessCascade()
    {
        int cascadeLevel = 0;

        while (true)
        {
            FindAllGroups(groupBuffer);
            if (groupBuffer.Count == 0) break;

            cascadeLevel++;

            // Zincirin sıradaki adımı: tahta yeni oturdu, göz görsün sonra
            // patlasın. İlk adımda yok - oyuncu kendi hamlesini zaten izledi.
            // Beklerken tahta durgun, bulunan gruplar geçerliliğini koruyor.
            if (cascadeLevel > 1 && cascadePause > 0f)
                yield return new WaitForSeconds(cascadePause * tempoScale);

            powersToSpawn.Clear();
            destroySet.Clear();
            blastArea.Clear();

            // L ve T şekilleri tek grup değil: kesişen bir yatay + bir dikey
            // dizi. Grupları tek tek işlemeden önce kesişimleri buluyoruz ki
            // o hücrelerde 4-way yerine bomba doğsun.
            FindBombIntersections(groupBuffer, bombCells);

            // Kesişim başına TEK bomba. Grupların içinde doğursaydık L'yi
            // oluşturan yatay ve dikey dizi ikisi birden bomba üretirdi.
            foreach (Vector2Int cell in bombCells)
                powersToSpawn.Add((cell.x, cell.y, PowerType.Bomb));

            foreach (MatchGroup group in groupBuffer)
            {
                PowerType power = GetPowerForGroup(group);

                // Bu grup bir kesişimin parçasıysa bombasını yukarıda zaten
                // doğurduk; ayrıca 4-way de vermesin.
                if (GroupHitsBombCell(group)) power = PowerType.None;

                if (power != PowerType.None)
                {
                    // Power-up nerede doğsun?
                    // 1) Oyuncunun sürüklediği kristalin karesi (sadece kendi hamlesinde)
                    // 2) Takasta yer değiştiren diğer kristalin karesi
                    // 3) Cascade'lerde parmak yok - grubun geometrik ortası
                    Crystal spawnAt = null;

                    if (cascadeLevel == 1)
                    {
                        spawnAt = FindInGroup(group, movedToX, movedToY);

                        if (spawnAt == null)
                            spawnAt = FindInGroup(group, movedFromX, movedFromY);
                    }

                    if (spawnAt == null)
                        spawnAt = GroupCenterCrystal(group);

                    powersToSpawn.Add((spawnAt.X, spawnAt.Y, power));
                }

                foreach (Crystal fr in group.Crystals) destroySet.Add(fr);
            }

            ReleaseGroups(groupBuffer);

            // Power-up'ları tetikle (zincirleme dahil)
            ExpandWithPowers(destroySet);

            // Power-up doğacak hücreleri patlatma listesinden çıkar
            foreach (var p in powersToSpawn)
            {
                Crystal atCell = crystals[p.x, p.y];
                if (atCell != null) destroySet.Remove(atCell);
            }

            destroyList.Clear();
            destroyList.AddRange(destroySet);

            yield return StartCoroutine(DestroyAndRefill(destroyList, cascadeLevel, powersToSpawn));
        }

        ClearSwapOrigin();
    }

    void ClearSwapOrigin()
    {
        movedToX = movedToY = -1;
        movedFromX = movedFromY = -1;
    }

    // Gruptaki belirli hücrenin kristali (yoksa null)
    static Crystal FindInGroup(MatchGroup group, int x, int y)
    {
        if (x < 0 || y < 0) return null;

        foreach (Crystal f in group.Crystals)
            if (f != null && f.X == x && f.Y == y) return f;

        return null;
    }

    // Grubun geometrik ortasındaki kristal.
    // Crystals listesi tarama sırasına göre doluyor (önce sağa, sonra sola), yani
    // Crystals[Count/2] ortadaki hücre DEĞİL - rastgele bir hücre. Gerçek ortayı
    // koordinattan hesaplıyoruz.
    static Crystal GroupCenterCrystal(MatchGroup group)
    {
        float sum = 0f;

        foreach (Crystal f in group.Crystals)
            sum += group.IsHorizontal ? f.X : f.Y;

        float center = sum / group.Count;

        Crystal best = group.Crystals[0];
        float bestDist = float.MaxValue;

        foreach (Crystal f in group.Crystals)
        {
            float d = Mathf.Abs((group.IsHorizontal ? f.X : f.Y) - center);

            if (d < bestDist)
            {
                bestDist = d;
                best = f;
            }
        }

        return best;
    }

    // waveOrigin verilirse kristaller hep birlikte değil, o noktadan DIŞARI
    // doğru dalga hâlinde patlar: en yakındaki önce, en uzaktaki en son.
    IEnumerator DestroyAndRefill(List<Crystal> toDestroy, int cascadeLevel,
                                  List<(int x, int y, PowerType power)> powers = null,
                                  Vector3? waveOrigin = null)
    {
        DestroyCrystals(toDestroy, cascadeLevel, waveOrigin);

        // Engeller patlamayla aynı anda kırılsın. Yerçekiminden ÖNCE olması şart:
        // kırılan katı engel şeffafa dönüyor ve o kare artık kristal bekliyor.
        DamageObstaclesFrom(toDestroy, powers);

        // Power-up'ları yerleştir
        if (powers != null)
        {
            foreach (var p in powers)
            {
                Crystal f = crystals[p.x, p.y];
                if (f == null) continue;

                f.SetPower(p.power);
                f.PlaySpawnAnimation();

                // Power-up doğuşu belirgin hissedilsin
                if (HapticManager.Instance != null)
                    HapticManager.Instance.Play(HapticStrength.Medium);

                if (AudioManager.Instance != null)
                    AudioManager.Instance.PlayPowerUp();
            }
        }

        float speed = SpeedMultiplier(cascadeLevel);

        // Patlama animasyonu bitsin. Kristaller ancak bittiğinde havuza dönüyor,
        // dolum onlara ihtiyaç duyabileceği için küçük bir pay bırakıyoruz.
        // Dalga varsa en uzaktaki kristal o kadar geç başlıyor - onu da ekle,
        // yoksa dolum daha patlamayanların üstüne biner.
        float waveTail = waveOrigin.HasValue ? WaveTime * speed : 0f;

        yield return new WaitForSeconds(DestroyTime * speed * 1.15f + waveTail);

        RefreshVariety();

        // Düşen ve yeni doğan kristallerin EN UZUN animasyon süresini öğren.
        // Sabit bir çarpanla beklemek yanlış: yeni kristaller tahtanın üstünde,
        // ekran dışında doğuyor ve şelale gecikmesiyle iniyor. Kısa beklersek
        // bir sonraki cascade onları daha yere inmeden patlatır - hücreler o an
        // boş görünür, kristal de ekran dışında yok olur.
        yield return StartCoroutine(SettleBoard(speed));
    }

    // ==================== TAHTAYI OTURTMA ====================

    // Düş - doldur - çapraz kay, hiçbir şey kıpırdamayana kadar.
    //
    // Tek geçiş yetmiyor: katı engelin ALTINDAKİ boşluğa kristal dikey olarak
    // giremiyor, oraya yandaki sütundan ÇAPRAZ geliyor. Çapraz gelen kristal de
    // yukarıda yeni bir boşluk bırakıyor, o boşluk bir sonraki turda dikey
    // doluyor. Akış bu yüzden döngü.
    //
    // SIRA ÖNEMLİ: çapraz adım yerçekiminin hemen ARDINDAN geliyor.
    // Yerçekimi bittiğinde her sütun parçası aşağıdan yukarı SIKIŞMIŞ durumda,
    // yani bir kristalin altı ya doluysa ya da engelliyse. Çapraz kaynağın aradığı
    // koşul tam olarak bu - dolayısıyla düz düşecek bir kristali yandan çalmak
    // imkânsız. İkisini ayrı turlara bölmek gereksiz bir tur (ve bir bekleme)
    // daha ekliyordu; birleştirince oturma süresi yarıya indi.
    //
    // Bazı kareler kalıcı olarak BOŞ kalabilir; bu bilinçli. Geniş bir bloğun
    // tam altındaki orta karelerin üstü de iki çaprazı da katıdır - oraya
    // hiçbir akış ulaşamaz. Zorla kristal doğurmuyoruz, boş duruyorlar.
    IEnumerator SettleBoard(float speed)
    {
        int guard = 0;

        // Son turun animasyonundan geriye kalan süre
        float pending = 0f;

        while (guard++ < SettleStepLimit)
        {
            float fall = ApplyGravity(speed);
            float slide = ApplyDiagonalSlide(speed);
            float fill = FillEmptySpaces(speed);

            float wait = Mathf.Max(fall, Mathf.Max(slide, fill));
            if (wait <= 0f) break;

            // Sıradaki tur, bu turun animasyonu bitmeden başlıyor. Kristal
            // süreleri aynı kalıyor; sadece turlar üst üste biniyor, akış
            // duraklamadan devam ediyor. Tekrar hareket eden bir kristalin
            // tween'i bulunduğu YERDEN yeni hedefe döner - zıplama olmaz.
            float step = wait * settleOverlap;
            pending = wait - step;

            yield return new WaitForSeconds(step);
        }

        // Son turun kalanını tam bekle. Eşleşme taraması tahta gerçekten
        // durduktan sonra yapılmalı: hâlâ havada olan bir kristal eşleşmeye
        // girerse ekrana hiç inmeden yok olur, kare boş görünür.
        if (pending > 0f)
            yield return new WaitForSeconds(pending);
    }

    // Oturma döngüsünün üst sınırı. Normal tahtada 2 tur sürüyor; sınır sadece
    // beklenmedik bir durumda oyunun kilitlenmemesi için.
    const int SettleStepLimit = 60;

    void DestroyCrystals(List<Crystal> toDestroy, int cascadeLevel, Vector3? waveOrigin = null)
    {
        if (toDestroy.Count == 0) return;

        float speed = SpeedMultiplier(cascadeLevel);
        // DestroyTime: tempoScale'i de içeriyor. Eskiden ham destroyDuration
        // kullanılıyordu; bekleme tempoScale'e uyarken animasyon uymuyordu.
        float dur = DestroyTime * speed;

        // Dalganın en uzak kristale kadar süresi sabit tutuluyor: patlama
        // alanı büyüdükçe dalga hızlanıyor. Mesafeyle orantılı sabit bir
        // gecikme verseydik tahtayı baştan başa süpüren bir 4-way sürünürdü.
        float waveSpan = 0f;

        if (waveOrigin.HasValue)
            foreach (Crystal f in toDestroy)
                if (f != null)
                    waveSpan = Mathf.Max(waveSpan,
                        Vector3.Distance(f.transform.position, waveOrigin.Value));

        // Titreme sadece ilk patlamanın küçük gruplarında. Büyük patlamalarda
        // kristal başına shake tween'i açmak boşuna CPU ve GC yükü.
        bool anticipation = useAnticipation
                            && cascadeLevel == 1
                            && toDestroy.Count <= anticipationMaxCrystals;

        int destroyed = 0;

        foreach (Crystal f in toDestroy)
        {
            if (f == null) continue;

            if (f.X >= 0 && f.X < width && f.Y >= 0 && f.Y < height && crystals[f.X, f.Y] == f)
                crystals[f.X, f.Y] = null;

            destroyed++;

            // Dalga: merkeze olan mesafe 0..1'e normalize edilip toplam
            // dalga süresiyle çarpılıyor.
            float delay = 0f;

            if (waveOrigin.HasValue && waveSpan > 0.001f)
            {
                float d = Vector3.Distance(f.transform.position, waveOrigin.Value);
                delay = (d / waveSpan) * WaveTime * speed;
            }

            // Parçacıklar da dalgayla gelsin. Hepsini burada anında çaksaydık
            // kristaller sırayla patlarken toz bulutu tek seferde çıkar ve
            // dalga hissi kaybolurdu.
            if (JuiceManager.Instance != null)
            {
                Vector3 popAt = f.transform.position;
                Color popColor = f.PopColor;

                if (delay > 0f)
                    DOVirtual.DelayedCall(delay,
                        () => JuiceManager.Instance?.PlayPopEffect(popAt, popColor));
                else
                    JuiceManager.Instance.PlayPopEffect(popAt, popColor);
            }

            // Şişme + dönerek yok olma, sonra havuza dön
            Crystal captured = f;
            f.PlayDestroy(dur, anticipation, () => CrystalPool.Instance.Return(captured), delay);
        }

        if (destroyed == 0) return;

        // Hedef göstergesi burada tazelenmiyor: levelin hedefi engelleri kırmak
        // ve o sayaç ancak DamageObstaclesFrom'da değişiyor. Kristal patlaması
        // başına Canvas'ı yeniden kurmanın anlamı kalmadı.

        // Patlama büyüklüğüne göre kamera sarsıntısı, zoom, flaş, hit-stop
        if (JuiceManager.Instance != null)
            JuiceManager.Instance.BigBlastFeedback(destroyed, cascadeLevel);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayPop(cascadeLevel);
    }

    // ==================== ENGEL KIRMA ====================

    // Patlayan kristallerin çevresindeki engellere vuruş dağıt.
    //
    // Kural ikiye ayrılıyor, çünkü iki engelin doğası farklı:
    //   ŞEFFAF - üstünde kristal var. SADECE o kristal patlayınca kırılır.
    //            Yanındaki patlama onu ilgilendirmez.
    //   KATI   - içinde kristal yok, kendi karesinde hiç patlama olmaz.
    //            Sadece KOMŞU karedeki patlama kırar.
    //   BLOK   - katıyla aynı kural; farkı canının çok olması ve kırılınca
    //            şeffafa dönmeden doğrudan boşalması (ObstacleGrid.Damage).
    //
    // Ayrıca GÜÇ ALANI: 4-way'in satırı/sütunu, bombanın 3x3'ü ve kombo alanları
    // üstünden geçtikleri her engeli doğrudan vurur (blastArea). Yoksa 4-way bir
    // blok kümesinin içinden hasarsız geçerdi. Multicolor'ın alanı yok.
    //
    // KÖŞELER SAYILMAZ: yalnızca 4 dik komşu (üst/alt/sağ/sol). Çaprazı da
    // eklemek cazip görünüyor ama tahtanın yarısını tek hamlede söker.
    //
    // extraCells: patlayan kristale bağlı OLMAYAN doğrudan vuruşlar. Alt bardaki
    // çekiç gibi bir power-up katı engelli bir kareye vurduğunda orada kristal
    // yok, yani kristal tabanlı yol o engele hiç dokunmuyordu.
    void DamageObstaclesFrom(List<Crystal> destroyed,
                             List<(int x, int y, PowerType power)> powers = null,
                             List<Vector2Int> extraCells = null)
    {
        if (obstacles == null || !obstacles.HasAny)
        {
            blastArea.Clear();
            return;
        }

        bool hasCrystals = destroyed != null && destroyed.Count > 0;
        bool hasPowers = powers != null && powers.Count > 0;
        bool hasExtra = extraCells != null && extraCells.Count > 0;
        bool hasArea = blastArea.Count > 0;

        if (!hasCrystals && !hasPowers && !hasExtra && !hasArea) return;

        damagedObstacles.Clear();
        obstacleBreaksThisBlast = 0;

        if (hasCrystals)
            foreach (Crystal f in destroyed)
                if (f != null) DamageAroundCell(f.X, f.Y);

        // Power-up'ın doğduğu kare de eşleşmenin parçasıydı - sadece oradaki
        // kristal patlamak yerine güce dönüşüyor. Eşleşme orada da olduğu için
        // altındaki engel kırılmalı; yoksa 4'lü zincirde 4 buzun 3'ü kırılır,
        // güç doğan karedeki buz sağlam kalırdı.
        if (hasPowers)
            foreach (var p in powers)
                DamageAroundCell(p.x, p.y);

        if (hasExtra)
            foreach (Vector2Int c in extraCells)
                DamageAroundCell(c.x, c.y);

        // Güç alanının üstünden geçtiği engeller: kendi karesinden vurulur.
        // Komşuya yayılmıyor - alanın komşularına zaten patlayan kristaller vuruyor.
        // Alanda kristali patlayan şeffaflar yukarıda vuruldu, burada tekrar
        // vurulmuyor (damagedObstacles).
        if (hasArea)
        {
            foreach (int key in blastArea)
                TryDamageObstacle(key % width, key / width, true);

            blastArea.Clear();
        }

        if (obstacleBreaksThisBlast == 0) return;

        // Hedef sayacı. Patlama başına tek güncelleme: DestroyCrystals'un
        // içindeki eski RefreshGoalUI engeller kırılmadan ÖNCE çalışıyordu,
        // orada kalsaydı sayaç bir patlama geriden gelirdi.
        if (GameManager.Instance != null)
            GameManager.Instance.RefreshGoalUI();

        // Ses ve titreşim de patlama başına TEK. Engel başına çalmak sesi
        // bozuyordu: PlayOneShot kopyaları üst üste bindiriyor, tek hamlede
        // 5 engel kırılınca aynı ses 5 kat yüksek duyuluyordu.
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayObstacleBreak();

        if (HapticManager.Instance != null)
            HapticManager.Instance.Play(HapticStrength.Medium);
    }

    // Bu patlamada kademe düşüren engel sayısı (katı -> şeffaf ve şeffaf -> yok)
    int obstacleBreaksThisBlast;

    // Güç etkisinin kapladığı kare - kristali olsun olmasın
    void AddBlastArea(int x, int y)
    {
        if (InBounds(x, y)) blastArea.Add(y * width + x);
    }

    // Bir karede olan patlamanın engel etkisi: kendi karesi (şeffafı kırar)
    // ve dört dik komşusu (katıyı kırar).
    void DamageAroundCell(int x, int y)
    {
        TryDamageObstacle(x, y, true);

        // Dik komşular: katı engeli kırar. Çapraz YOK - köşeden vurulmaz.
        TryDamageObstacle(x + 1, y, false);
        TryDamageObstacle(x - 1, y, false);
        TryDamageObstacle(x, y + 1, false);
        TryDamageObstacle(x, y - 1, false);
    }

    void TryDamageObstacle(int x, int y, bool ownCell)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return;

        ObstacleType type = obstacles.TypeAt(x, y);
        if (type == ObstacleType.None) return;

        // Şeffaf engel SADECE kendi karesindeki kristal patlayınca kırılır.
        // Komşudan gelen darbe onu ilgilendirmez.
        if (!ownCell && type == ObstacleType.Transparent) return;

        // Aynı patlamada aynı engele bir kez vurulur
        if (!damagedObstacles.Add(y * width + x)) return;

        ApplyObstacleDamage(x, y);
    }

    void ApplyObstacleDamage(int x, int y)
    {
        // Renk hasardan ÖNCE okunmalı: kırılan engelin görseli yok ediliyor
        Color popColor = obstacles.PopColorAt(x, y);

        ObstacleHit hit = obstacles.Damage(x, y);
        if (hit == ObstacleHit.None) return;

        if (JuiceManager.Instance != null)
            JuiceManager.Instance.PlayPopEffect(GridToWorld(x, y), popColor);

        // Sadece çatladı - henüz kademe düşmedi, ödül yok
        if (hit == ObstacleHit.Damaged) return;

        // Ses ve titreşim engel başına değil patlama başına -
        // DamageObstaclesFrom sonunda.
        obstacleBreaksThisBlast++;
    }

    // Boşlukların üstündeki kristalleri aşağı kaydırır.
    // En uzun düşüş süresini döndürür - çağıran taraf o kadar beklemeli.
    float ApplyGravity(float speed)
    {
        float longest = 0f;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (IsBlocked(x, y)) continue;      // katı engel - burası hiç dolmaz
                if (crystals[x, y] != null) continue;

                for (int above = y + 1; above < height; above++)
                {
                    // Katı engel yolu kesiyor: üstündeki kristaller bu boşluğa inemez
                    if (IsBlocked(x, above)) break;

                    Crystal f = crystals[x, above];
                    if (f == null) continue;

                    crystals[x, y] = f;
                    crystals[x, above] = null;

                    f.X = x;
                    f.Y = y;

                    // Uzaktan düşen biraz daha uzun sürsün (hızlanma hissi)
                    int fallDistance = above - y;
                    float dur = FallTime * speed * (1f + fallDistance * fallDistanceFactor);

                    // Squash dahil düşüş animasyonu
                    f.AnimateFall(GridToWorld(x, y), dur);

                    if (dur > longest) longest = dur;

                    break;
                }
            }
        }

        return longest;
    }

    // Boş hücreleri tahtanın üstünden düşen yeni kristallerle doldurur.
    // En uzun (gecikme + düşüş) süresini döndürür.
    //
    // SADECE sütunun tahtaya açık kısmı doldurulur. Katı engelin altındaki
    // boşluğa yukarıdan kristal giremez - orası ApplyDiagonalSlide'ın işi.
    float FillEmptySpaces(float speed)
    {
        float longest = 0f;

        for (int x = 0; x < width; x++)
        {
            int floor = TopOpenFloor(x);
            int spawnOffset = 1;

            for (int y = floor; y < height; y++)
            {
                if (crystals[x, y] != null) continue;

                CrystalType type = RefillType(x, y);

                Vector3 spawnPos = GridToWorld(x, height + spawnOffset);
                Crystal f = SpawnCrystal(x, y, type, spawnPos);

                // Üsttekiler biraz gecikmeli düşsün - şelale hissi
                float delay = (spawnOffset - 1) * fillStagger * tempoScale * speed;
                float dur = FallTime * speed * (1f + spawnOffset * fallDistanceFactor);

                // Tahtanın üst kenarını geçene kadar görünmesin
                f.AnimateFall(GridToWorld(x, y), dur, delay, BoardTopY);

                if (delay + dur > longest) longest = delay + dur;

                spawnOffset++;
            }
        }

        return longest;
    }

    // Yukarıdan düşen yeni kristalin rengi.
    //
    // Eskiden tamamen rastgeleydi: 4 renkte bile yeni gelenler kendi aralarında
    // sürekli eşleşip güç doğuruyor, güçler birbirini tetikliyordu. Tek hamle
    // tahtayı defalarca patlatıp leveli bitiriyordu.
    //
    // Şimdi levelin şans payı kadarı rastgele, kalanı düştüğü yerde hazır
    // eşleşme kurmayan bir renk. Zincir yine oluyor ama taşların kaymasından
    // doğuyor - oyuncunun hamlesinin sonucu, gökten yağan şans değil.
    //
    // Sütunlar soldan sağa, her sütun aşağıdan yukarı doluyor ve SpawnCrystal
    // grid'e hemen yazıyor. Yani GetNonMatchingType o an dolu olan komşulara
    // bakıyor, sonra gelenler de bu kristale bakacak.
    //
    // LevelLab/Core/LabBoard.FillEmptySpaces bunun kopyası - birlikte değiştir.
    CrystalType RefillType(int x, int y)
    {
        if (Random.value < refillLuck) return RandomCrystalType();

        return GetNonMatchingType(x, y);
    }

    // Sütunda tahtanın üstünden beslenebilen EN ALT satır: en yüksek katı
    // engelin bir üstü. Sütunda engel yoksa 0, tepesi engelliyse height.
    int TopOpenFloor(int x)
    {
        for (int y = height - 1; y >= 0; y--)
            if (IsBlocked(x, y)) return y + 1;

        return 0;
    }

    // ==================== ÇAPRAZ AKIŞ ====================

    // Katı engelin altında kalan, yukarıdan beslenemeyen boşlukları çapraz
    // komşudan doldurur. Bir kristal turda EN FAZLA BİR kare kayar: sol-üstten
    // ya da sağ-üstten. İki çapraz ötedeki bir kareye gidemez.
    //
    // Örnek - alttaki 4x4 boşluk (s13 sol alt, s14 onun sağı):
    //   ... s9  s10 s11 s12
    //   ... s13 s14 s15 s16
    // s13 dolduktan sonra s9 doluyor; s14 boş kalınca s9'daki kristal oraya
    // ÇAPRAZ kayıyor. Aynı kristal s15'e gidemez - iki sütun ötede.
    float ApplyDiagonalSlide(float speed)
    {
        // Engel yoksa hiçbir kare yukarıdan yalıtılmış olamaz
        if (obstacles == null || !obstacles.HasAny) return 0f;

        float longest = 0f;

        // Alttan üste tarıyoruz: bu turda çapraz gelen bir kristal, bir üst
        // satırdaki boşluk tarafından tekrar çalınmasın.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (IsBlocked(x, y) || crystals[x, y] != null) continue;

                // Yukarıdan dolabilecek bir boşluksa karışma - dikey akış
                // halleder. Yoksa kristaller sebepsiz yere yana savrulur.
                if (!IsSealedFromAbove(x, y)) continue;

                Crystal source = TakeDiagonalSource(x, y);
                if (source == null) continue;

                crystals[source.X, source.Y] = null;
                crystals[x, y] = source;

                source.X = x;
                source.Y = y;

                float dur = FallTime * speed;
                source.AnimateFall(GridToWorld(x, y), dur);

                if (dur > longest) longest = dur;
            }
        }

        return longest;
    }

    // Bu karenin üstünde, aynı sütunda katı engel var mı? Varsa kareye
    // yukarıdan kristal inemez.
    bool IsSealedFromAbove(int x, int y)
    {
        for (int above = y + 1; above < height; above++)
            if (IsBlocked(x, above)) return true;

        return false;
    }

    // (x,y) boşluğuna kayabilecek çapraz kristal. Sol-üst ve sağ-üst adayları
    // dönüşümlü deniyoruz: hep soldan alsaydık boşluklar hep sağa kayardı.
    Crystal TakeDiagonalSource(int x, int y)
    {
        bool leftFirst = ((x + y) & 1) == 0;

        Crystal first = DiagonalCandidate(leftFirst ? x - 1 : x + 1, y);
        if (first != null) return first;

        return DiagonalCandidate(leftFirst ? x + 1 : x - 1, y);
    }

    // (sx, y+1)'deki kristal (x,y) boşluğuna kayabilir mi?
    Crystal DiagonalCandidate(int sx, int y)
    {
        int sy = y + 1;

        if (sx < 0 || sx >= width || sy >= height) return null;
        if (IsBlocked(sx, sy)) return null;

        Crystal f = crystals[sx, sy];
        if (f == null) return null;

        // Kendi sütununda düz aşağı inebiliyorsa çapraza sapmasın: dikey
        // akıştaki kristali yana çalmış oluruz.
        if (!IsBlocked(sx, y) && crystals[sx, y] == null) return null;

        return f;
    }

    // ==================== DEADLOCK / SHUFFLE ====================

    bool HasValidMove()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Crystal here = crystals[x, y];

                // Buff'ın HANGİSİ olursa olsun tek başına yeterli: üçü de
                // neyle takas edilirse edilsin ateşleniyor, eşleşme aranmıyor.
                // (Eskiden roket yalnızsa hamle sayılmıyordu çünkü eşleşme
                //  olmadan takas geri alınıyordu - artık öyle değil.)
                if (here != null && here.Power != PowerType.None) return true;

                if (x + 1 < width && WouldCreateMatch(x, y, x + 1, y)) return true;
                if (y + 1 < height && WouldCreateMatch(x, y, x, y + 1)) return true;
            }
        }
        return false;
    }

    bool WouldCreateMatch(int x1, int y1, int x2, int y2)
    {
        Crystal a = crystals[x1, y1];
        Crystal b = crystals[x2, y2];

        if (a == null || b == null) return false;

        crystals[x1, y1] = b;
        crystals[x2, y2] = a;

        bool result = HasMatchAtCell(x1, y1) || HasMatchAtCell(x2, y2);

        crystals[x1, y1] = a;
        crystals[x2, y2] = b;

        return result;
    }

    bool HasMatchAtCell(int x, int y)
    {
        CrystalType? type = GetMatchTypeAt(x, y);
        if (type == null) return false;

        int count = 1;
        for (int i = x + 1; i < width && GetMatchTypeAt(i, y) == type; i++) count++;
        for (int i = x - 1; i >= 0 && GetMatchTypeAt(i, y) == type; i--) count++;
        if (count >= 3) return true;

        count = 1;
        for (int i = y + 1; i < height && GetMatchTypeAt(x, i) == type; i++) count++;
        for (int i = y - 1; i >= 0 && GetMatchTypeAt(x, i) == type; i--) count++;
        return count >= 3;
    }

    // ==================== TAHTA YENİLEME ====================
    // Hamle kalmadığında tahtayı karıştırmak yerine baştan kuruyoruz:
    // kristaller sol üstten sağ alta çapraz bir dalga hâlinde yok oluyor, sonra
    // aynı sırayla yeni kristaller beliriyor.
    //
    // Boşluklar korunuyor: yenileme öncesi kristali OLAN kareler doldurulur.
    // Katı engelin altında kalıcı boş kalmış bir cep varsa öyle kalır.

    // showBanner: "HAMLE KALMADI!" yazısı çıksın mı. Deadlock'ta çıkıyor,
    // oyuncunun kendi bastığı "karıştır" power-up'ında çıkmıyor - orada
    // tahtada hamle VAR, yazı yalan olurdu.
    IEnumerator RefreshBoardRoutine(bool showBanner = true)
    {
        ClearHint();

        if (showBanner && UIManager.Instance != null)
            UIManager.Instance.ShowNoMovesBanner();

        yield return new WaitForSeconds(showBanner ? noMovesHoldTime : 0f);

        // --- 1. Çapraz dalga: hepsi yok olsun ---
        float vanish = VanishDiagonalWave();

        yield return StartCoroutine(PlayWaveTicks(vanish, RefreshSoundTicks));

        // Bir kare pay: yok olan kristallerin OnComplete'i havuza dönüşü bu
        // karede yapıyor. Beklemezsek havuz boşken yeni kristal isteriz ve
        // gereksiz yere Instantiate'e düşeriz.
        yield return null;

        // --- 2. Geçerli hamlesi olan yeni bir dizilim kur ---
        // Kristaller son konumlarında doğuyor ama boyutları hemen sıfırlanıyor,
        // bu yüzden deneme turları ekrana hiç yansımıyor.
        if (!BuildValidLayout())
            WarnLayoutFailed();

        // --- 3. Aynı dalga sırasıyla belirsinler ---
        float appear = RevealDiagonalWave();
        yield return new WaitForSeconds(appear);

        if (showBanner && UIManager.Instance != null)
            UIManager.Instance.HideNoMovesBanner();
    }

    // Bir karenin dalgadaki sırası: sol üst = 0, sağ alt = en son.
    int WaveIndex(int x, int y) => x + (height - 1 - y);

    // Tahtadaki her kristali çapraz gecikmeyle yok eder, en uzun süreyi döndürür.
    // Hangi karelerin dolu olduğunu da refreshCells'e yazar - yeniden dolum
    // tam olarak o kareleri kullanacak.
    float VanishDiagonalWave()
    {
        refreshCells.Clear();

        float longest = 0f;

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                Crystal f = crystals[x, y];
                if (f == null) continue;

                refreshCells.Add(new Vector2Int(x, y));
                crystals[x, y] = null;

                float delay = WaveIndex(x, y) * refreshWaveStep;

                Crystal captured = f;
                f.PlayDestroy(refreshVanishDuration, false,
                              () => CrystalPool.Instance.Return(captured), delay);

                float total = delay + refreshVanishDuration;
                if (total > longest) longest = total;
            }

        return longest;
    }

    // Yeni kristalleri aynı çapraz sırayla belirt
    float RevealDiagonalWave()
    {
        float longest = 0f;

        foreach (Vector2Int c in refreshCells)
        {
            Crystal f = crystals[c.x, c.y];
            if (f == null) continue;

            float delay = WaveIndex(c.x, c.y) * refreshWaveStep;
            f.PlaySpawnAnimation(delay);

            float total = delay + Crystal.SpawnAnimTime;
            if (total > longest) longest = total;
        }

        return longest;
    }

    // Dalga boyunca birkaç ses - sessiz bir süpürme ölü hissettiriyor.
    // Toplam süreyi de bekler, çağıran ayrıca beklemesin.
    IEnumerator PlayWaveTicks(float total, int ticks)
    {
        float step = total / Mathf.Max(1, ticks);

        for (int i = 0; i < ticks; i++)
        {
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayPop();

            yield return new WaitForSeconds(step);
        }
    }

    // refreshCells'teki karelere, hazır eşleşme içermeyen ve EN AZ BİR hamlesi
    // olan bir dizilim kurar. Kristaller son konumlarında doğar.
    bool BuildValidLayout()
    {
        for (int attempt = 0; attempt < LayoutRetryLimit; attempt++)
        {
            ReturnAllCrystals();

            // Sıra önemli: GetNonMatchingType komşulara bakıyor,
            // refreshCells x-y artan sırada dolduğu için onlar hazır oluyor.
            foreach (Vector2Int c in refreshCells)
                SpawnCrystal(c.x, c.y, GetNonMatchingType(c.x, c.y), GridToWorld(c.x, c.y));

            if (HasValidMove()) return true;
        }

        return false;
    }

    void ReturnAllCrystals()
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (crystals[x, y] == null) continue;

                CrystalPool.Instance.Return(crystals[x, y]);
                crystals[x, y] = null;
            }
    }

    // Geçerli dizilim kaç kez denensin. Engellerle iyice daralmış bir tahtada
    // hiçbir dizilim hamle üretmeyebilir - sonsuz denemeye girmeyelim.
    const int LayoutRetryLimit = 30;

    // Dalga boyunca kaç ses çalınsın
    const int RefreshSoundTicks = 5;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void WarnLayoutFailed()
    {
        Debug.LogWarning($"[Board] {LayoutRetryLimit} denemede hamlesi olan bir dizilim " +
                         $"kurulamadı. Tahta engellerle fazla daralmış olabilir - " +
                         $"level tasarımını gözden geçir.", this);
    }
}
