using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Levellar")]
    [Tooltip("Bütün levellerin sırasını tutan asset - Levels/LevelSet.asset")]
    [SerializeField] LevelSet levelSet;

    [Tooltip("Test için: -1 ise oyuncunun kaldığı level yüklenir. 0 ve üstü " +
             "verirsen o level açılır ve kazanmak ilerlemeyi KAYDETMEZ.")]
    [SerializeField] int forceLevelIndex = -1;

    [Header("Ödüller")]
    [Tooltip("Level kazanınca KALAN HAMLE başına kaç altın verilsin")]
    [SerializeField] int goldPerMoveLeft = 10;

    [Tooltip("Level kazanınca kaç yıldız kristali verilsin")]
    [SerializeField] int dragonEggPerWin = 1;

    [Header("Altınla Devam")]
    [Tooltip("Devam etmenin maliyeti BÖLÜM NUMARASI ile çarpılıyor: " +
             "1. bölümde bu değer kadar, 10. bölümde 10 katı. Kazanç da " +
             "bölüm ilerledikçe arttığı için maliyet onunla birlikte ölçekleniyor.")]
    [SerializeField] int continueGoldPerLevel = 10;

    [Tooltip("Devam edince kaç hamle eklensin")]
    [SerializeField] int continueMovesGranted = 5;

    // --- Durum ---
    // Oyunun tek modu var: hikaye. Hangi levelin oynanacağını SaveSystem
    // söylüyor, oyuncu seçmiyor.
    public LevelData CurrentLevel { get; private set; }
    public int CurrentLevelIndex { get; private set; }
    public int MovesLeft { get; private set; }
    public bool IsGameOver { get; private set; }

    // Panel açıkken (çıkış onayı gibi) tahta dursun. IsGameOver'dan ayrı
    // tutuluyor: bu geçici bir duraklama, level bitmiş değil.
    public bool IsPaused { get; private set; }

    // Tahtanın girdiye kapalı olduğu her durum. Board bunu sorguluyor.
    public bool InputLocked => IsGameOver || IsPaused;

    // Bu levelde kazanılan ödüller - kazanma ekranı yazmak isterse hazır
    public int LastGoldEarned { get; private set; }
    public int LastDragonEggEarned { get; private set; }

    // Tahtadaki engel sayacına ulaşmak için. Board Start'ında kendini tanıtıyor -
    // GameManager'ın sahneyi FindObjectOfType ile taramasına gerek kalmıyor.
    Board board;

    // Inspector'dan elle level seçilerek mi açıldı. Öyleyse kazanmak
    // ilerlemeyi kaydetmiyor - test uğruna oyuncunun kaydı ileri sarmasın.
    bool isForcedLevel;

    void Awake()
    {
        Instance = this;

        SetupPerformance();
        LoadGame();
    }

    // Tween kapasitesi - sahne başına bir kez.
    // Kare hızı burada DEĞİL: uygulama geneli bir ayar olduğu için AppPerformance
    // hallediyor, yoksa menü sahnesi ayarsız kalıyordu.
    void SetupPerformance()
    {
        // Cascade sırasında aynı anda yüzlerce tween çalışıyor. Kapasiteyi baştan
        // ayırmazsak DOTween oyun ortasında diziyi büyütür ve o kare takılır.
        DOTween.SetTweensCapacity(500, 200);
    }

    void Start()
    {
        UpdateAllUI();
    }

    // ==================== YÜKLEME ====================

    void LoadGame()
    {
        IsGameOver = false;
        LoadLevel();
    }

    void LoadLevel()
    {
        isForcedLevel = forceLevelIndex >= 0;

        // Hikaye modu tek yönlü: oynanacak level, oyuncunun kayıttaki sayacı.
        // Level seçme ekranı yok, buraya dışarıdan indeks gelmiyor.
        CurrentLevelIndex = isForcedLevel ? forceLevelIndex : SaveSystem.CurrentLevel;

        if (levelSet != null && levelSet.Count > 0)
        {
            // Sayaç set'in dışına taşmışsa (hepsi bitmiş) son levele oturt -
            // menü zaten bu durumda oyuna sokmuyor, bu sadece emniyet kemeri.
            CurrentLevelIndex = Mathf.Clamp(CurrentLevelIndex, 0, levelSet.Count - 1);
            CurrentLevel = levelSet.Get(CurrentLevelIndex);
        }

        if (CurrentLevel != null)
        {
            MovesLeft = CurrentLevel.maxMoves;
        }
        else
        {
            Debug.LogError("GameManager'ın 'Level Set' alanı boş - " +
                           "Levels/LevelSet.asset'i Inspector'da bu alana sürükle.", this);
            MovesLeft = 20;
        }
    }

    // ==================== TAHTA AYARLARI ====================
    // Board bu değerleri okur - hepsinin tek kaynağı levelin kendisi

    public int BoardWidth => CurrentLevel != null ? CurrentLevel.width : 8;

    public int BoardHeight => CurrentLevel != null ? CurrentLevel.height : 8;

    public int CrystalVariety => CurrentLevel != null ? CurrentLevel.crystalVariety : 4;

    // Level yoksa eski davranış: yeni kristallerin hepsi rastgele
    public float RefillLuck => CurrentLevel != null ? CurrentLevel.refillLuck : 1f;

    // ==================== HAMLE ====================

    public void UseMove()
    {
        MovesLeft--;

        if (UIManager.Instance != null)
            UIManager.Instance.UpdateMoves(MovesLeft);
    }

    // Hamle ekleyen bir power-up kullanıldı. Altınla devamdan farklı: burada
    // oyun bitmemiş, sadece nefes alınıyor.
    public void AddMoves(int amount)
    {
        if (amount <= 0) return;

        MovesLeft += amount;

        if (UIManager.Instance != null)
            UIManager.Instance.UpdateMoves(MovesLeft);
    }

    // ==================== HEDEF: ENGELLER ====================
    // Levelin tek hedefi tahtadaki bütün engelleri kırmak. Kristal toplama
    // mekaniği kaldırıldı; kristaller artık sadece engellere ulaşmanın aracı.

    // Board sahnede kurulunca kendini tanıtır. Aynı zamanda ilk göstergeyi
    // kurar: Board.Start ile GameManager.Start'ın sırası garanti değil, kaç
    // engel olduğunu ancak tahta kurulduktan sonra bilebiliyoruz.
    public void RegisterBoard(Board b)
    {
        board = b;
        RefreshGoalUI();
    }

    // Tahtada kırılmayı bekleyen engel sayısı
    public int ObstaclesLeft => board != null ? board.RemainingObstacles : 0;

    // Level başında tahtada kaç engel vardı
    public int ObstaclesTotal => board != null ? board.TotalObstacles : 0;

    // Bu levelden sonra bir level daha var mı. Kazanma ekranı buna göre
    // "SONRAKİ" ya da "hepsini bitirdin" gösteriyor.
    public bool HasNextLevel => levelSet != null && CurrentLevelIndex + 1 < levelSet.Count;

    // Bir patlamanın tamamı işlendikten sonra hedef göstergesini tazele
    public void RefreshGoalUI()
    {
        if (CurrentLevel == null) return;
        if (UIManager.Instance == null || board == null) return;

        UIManager.Instance.UpdateObstacleGoal(
            board.RemainingTransparentObstacles, board.RemainingSolidObstacles, board.RemainingBlockObstacles,
            board.TotalTransparentObstacles, board.TotalSolidObstacles, board.TotalBlockObstacles,
            board.BlockTheme);
    }

    // ==================== OYUN SONU ====================

    public bool AreGoalsComplete()
    {
        if (CurrentLevel == null) return false;

        // Engelsiz bir level tasarım hatası. Sessizce kazandırmak, oyuncunun
        // hiçbir şey yapmadan "tebrikler" görmesi demek olurdu - Board sahne
        // açılırken konsola uyarı basıyor.
        if (ObstaclesTotal <= 0) return false;

        return ObstaclesLeft <= 0;
    }

    public void FinishWin()
    {
        IsGameOver = true;

        // Test için elle level seçildiyse ne ilerleme ne ödül yazılır -
        // editörde level denemek oyuncunun kasasını şişirmesin.
        if (!isForcedLevel)
        {
            SaveSystem.CompleteLevel(CurrentLevelIndex);
            LeaderboardService.Push();
            GrantRewards();
        }

        if (UIManager.Instance != null) UIManager.Instance.ShowWin(HasNextLevel);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayWin();
    }

    // Ödül kalan hamleye göre. Kalan hamleler artık hiçbir yerde harcanmıyor,
    // o yüzden MovesLeft doğrudan doğru değeri veriyor.
    void GrantRewards()
    {
        LastGoldEarned = MovesLeft * goldPerMoveLeft;
        LastDragonEggEarned = dragonEggPerWin;

        SaveSystem.AddGold(LastGoldEarned);
        SaveSystem.AddDragonEgg(LastDragonEggEarned);

        // Menü açılınca üst bar bu kadar sayarak artsın. Araya sahne değişimi
        // girdiği için normal alanla taşınamıyor.
        TopBarUI.PendingGold += LastGoldEarned;
        TopBarUI.PendingDragonEgg += LastDragonEggEarned;
    }

    // Bu levelde şu an devam etmenin maliyeti. Her devam edişte artıyor.
    // Bölüm numarası x sabit. CurrentLevelIndex 0 tabanlı, oyuncunun gördüğü
    // numara +1 - maliyet de onunla aynı olsun ki "3. bölüm, 30 altın" tutsun.
    public int ContinueCost => (CurrentLevelIndex + 1) * continueGoldPerLevel;

    // Devam edince eklenecek hamle - teklif ekranı bunu yazıyor
    public int ContinueMoves => continueMovesGranted;

    // Hamle bitti. HENÜZ kaybetmiş sayılmıyor: önce altınla devam teklifi
    // gidiyor. Can ancak oyuncu teklifi reddedince yanıyor.
    public void FinishLose()
    {
        if (IsGameOver) return;

        IsGameOver = true;   // teklif ekrandayken tahta kilitli kalsın

        bool offered = UIManager.Instance != null &&
                       UIManager.Instance.ShowContinueOffer(ContinueCost, ContinueMoves, SaveSystem.Gold);

        // Teklif paneli sahnede kurulmamışsa akış tıkanmasın, doğrudan kaybet
        if (!offered) GiveUp();
    }

    // "Devam et" - altını harca, hamle ekle, aynı levele geri dön.
    // Can YANMIYOR: oyuncu levelden çıkmış olmuyor.
    public void ContinueWithGold()
    {
        if (!IsGameOver) return;

        // Yetmiyorsa SpendGold hiçbir şey harcamıyor ve false dönüyor
        if (!SaveSystem.SpendGold(ContinueCost)) return;

        MovesLeft += continueMovesGranted;
        IsGameOver = false;   // tahta yeniden oynanabilir

        if (UIManager.Instance != null)
        {
            UIManager.Instance.HideContinueOffer();
            UIManager.Instance.UpdateMoves(MovesLeft);
        }
    }

    // "Hayır" - level gerçekten kaybedildi: bir can gider, ödül yoktur,
    // ilerleme kaydedilmez. Kaybetme ekranı açılır.
    public void GiveUp()
    {
        IsGameOver = true;

        // Elle level seçilerek test ediliyorsa can yakmıyoruz - ödül ve
        // ilerlemeyle aynı kural.
        if (!isForcedLevel) SaveSystem.SpendLife();

        if (UIManager.Instance != null)
        {
            UIManager.Instance.HideContinueOffer();
            UIManager.Instance.ShowLose();
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlayLose();
    }

    // ==================== UI ====================

    void UpdateAllUI()
    {
        if (UIManager.Instance == null) return;

        UIManager.Instance.UpdateMoves(MovesLeft);

        // Engel göstergesi burada kurulmuyor: tahta henüz kurulmamış olabilir.
        // Board.Start içindeki RegisterBoard tazeliyor.
    }

    // ==================== BUTONLAR ====================

    // Canı olmayan levele giremez - menüdeki "DEVAM ET" ile aynı kural.
    // Kaybetme ekranındaki "TEKRAR DENE" bu kontrol olmadan canı atlayan bir
    // arka kapıydı: oyuncu 0 canla sonsuza kadar aynı leveli deneyebiliyordu.
    //
    // true dönerse çağıran taraf sahneyi YÜKLEMEMELİ.
    bool BlockedByNoLives()
    {
        // Elle level seçilerek test ediliyorsa can kuralı işlemiyor -
        // ödül ve ilerlemeyle aynı istisna.
        if (isForcedLevel) return false;

        if (SaveSystem.Lives > 0) return false;

        // Panel sahnede varsa geri sayımı gösterir; yoksa oyuncuyu ekranda
        // kilitli bırakmayalım, menüye dönsün.
        if (LivesUI.Instance != null) LivesUI.Instance.ShowNoLives();
        else GoToLevelSelect();

        return true;
    }

    public void RestartLevel()
    {
        if (BlockedByNoLives()) return;

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void NextLevel()
    {
        // Son level bitti: gidilecek bir sonraki yok, menüye dön.
        if (!HasNextLevel)
        {
            GoToLevelSelect();
            return;
        }

        if (BlockedByNoLives()) return;

        // Sıradaki indeksi taşımaya gerek yok - FinishWin sayacı ilerletti,
        // sahne yeniden kurulunca LoadLevel onu okuyacak.
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void SetPaused(bool paused)
    {
        IsPaused = paused;
    }

    // Oyuncu leveli yarıda bırakıyor: bu kaybetmek sayılıyor, bir can gider.
    // Elle level seçilerek test ediliyorsa can yakmıyoruz - GiveUp ile aynı kural.
    public void QuitToMenu()
    {
        if (!isForcedLevel) SaveSystem.SpendLife();

        GoToLevelSelect();
    }

    // Ana menüye dön. Sahne adı hâlâ LevelSelect - 3 butona UnityEvent olarak
    // bağlı olduğu için metot adı da olduğu gibi bırakıldı.
    public void GoToLevelSelect()
    {
        SceneManager.LoadScene("MainPages");
    }
}
