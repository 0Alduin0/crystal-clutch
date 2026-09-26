using UnityEngine;
using UnityEngine.UI;

// Alt bardaki power-up'ları tahtaya bağlayan katman. Bu bileşen eklenene kadar
// PowerUpBarUI.Used olayını kimse dinlemiyordu: markette power-up satılıyor,
// altın gidiyor, butona basılıyor ve tahtada hiçbir şey olmuyordu.
//
// AKIŞ
//   1. Oyuncu bir power-up'a basar            -> PowerUpBarUI.Used
//   2. Hedefsiz etkiyse (karıştır, hamle ekle) anında çalışır
//   3. Hedefliyse tahta SEÇİM MODU'na girer, oyuncu bir kareye dokunur
//   4. Etki uygulanır ve ANCAK O ZAMAN stoktan düşer
//
// Stoğun uygulama anında düşmesi bilinçli: seçim modunda vazgeçen oyuncu
// power-up'ını kaybetmiyor. Aynı butona ikinci kez basmak seçimi iptal eder.
//
// Hangi slotun hangi etkiyi taşıdığı burada, Inspector'da duruyor. Board bu
// eşleştirmeyi bilmiyor - "şu etkiyi şu karede uygula" emrini alıyor sadece.
public class PowerUpController : MonoBehaviour
{
    // InputHandler dokunuşu buraya yönlendirmek için buna bakıyor.
    public static PowerUpController Instance;

    [System.Serializable]
    public class Definition
    {
        [Tooltip("Alt bardaki HANGİ buton bu etkiyi kullansın - sürükleyip bırak.\n\n" +
                 "Butonun BARDAKİ yeri stok indeksini de belirliyor (marketin " +
                 "kaçıncı satırından alındığını), yani bu listenin sırası " +
                 "önemli değil - istediğin sırada dizebilirsin.")]
        public Button button;

        [Tooltip("Sadece Inspector'da ayırt etmek için - oyunda kullanılmıyor")]
        public string label;

        [Header("Yönlendirme Penceresi")]
        [Tooltip("Kurulunca tahtanın üstünde çıkan ikon")]
        public Sprite icon;

        [Tooltip("Power-up'ın adı: 'Ejder Pençesi'")]
        public string title;

        [Tooltip("Ne işe yaradığı VE nasıl kullanıldığı. Hedefli olanlarda " +
                 "'...bir kare seç', hedefsizlerde '...KULLAN'a bas' gibi.")]
        [TextArea(2, 4)]
        public string description;

        [Tooltip("Bu slot tahtada ne yapsın")]
        public PowerUpEffect effect = PowerUpEffect.BlokKirma;

        [Tooltip("Etkinin şiddeti.\n" +
                 "Hamle Ekle: eklenecek hamle sayısı\n" +
                 "Diğerlerinde kullanılmıyor.")]
        public int amount = 1;

        [Tooltip("Kullanmak bir hamleye de mal olsun mu. Kapalı olması standart: " +
                 "oyuncu parayla aldığı şeyi bir de hamleyle ödemesin.")]
        public bool costsMove;
    }

    [Tooltip("Hangi butonun ne yaptığı. Sıra serbest - eşleştirmeyi her " +
             "elemanın kendi 'Button' alanı yapıyor.")]
    [SerializeField] Definition[] definitions = new Definition[SaveSystem.PowerUpTypes];

    [Tooltip("Sahnedeki tahta. Boş bırakılırsa power-up'lar hiç çalışmaz.")]
    [SerializeField] Board board;

    [Header("Seçim Modu")]
    [Tooltip("Kurulunca tahtanın üstünde çıkan yönlendirme penceresi. " +
             "Hedefsiz power-up'ların KULLAN butonu da onun üzerinde.")]
    [SerializeField] PowerUpHintUI hint;

    // Kurulu slot. -1 = seçim modu kapalı.
    int armedIndex = -1;

    // Tahta dışına dokunularak iptal edilen slot ve iptalin olduğu kare.
    // Bar butonunun kendisi de "tahta dışı" olduğu için, aynı butona basmak
    // önce iptali sonra yeniden kurulumu tetikliyordu ve power-up hiç
    // kapanmıyordu. Aynı karede gelen kurulum isteğini bu ikili yutuyor.
    int cancelledIndex = -1;
    int cancelledFrame = -1;

    // Bardaki slot indeksi -> definitions dizisindeki sıra. -1 = o slota
    // hiçbir etki atanmamış. Start'ta bir kez kuruluyor; her basışta
    // butonları taramanın anlamı yok.
    int[] slotToDef;

    public bool IsArmed => armedIndex >= 0;

    // InputHandler buna bakıyor: kurulu power-up tahtada kare mi bekliyor,
    // yoksa KULLAN butonunu mu? İkincisinde dokunuşu hiç işlemiyor, iptali
    // de kullanımı da hint penceresinin kendi butonları hallediyor.
    public bool IsAwaitingBoardTap
    {
        get
        {
            Definition def = GetDefinition(armedIndex);

            return def != null && def.effect.NeedsTarget();
        }
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;

        // Bar bizden önce yok olmuş olabilir - null kontrolü şart
        if (PowerUpBarUI.Instance != null) PowerUpBarUI.Instance.Used -= OnPowerUpPressed;

        if (hint != null)
        {
            hint.Used -= OnUsePressed;
            hint.Cancelled -= CancelSelection;
        }
    }

    void Start()
    {
        if (board == null)
            Debug.LogError("PowerUpController'ın 'Board' alanı boş - alt bardaki " +
                           "power-up'lar hiçbir şey yapmaz. Sahnedeki Board'u sürükle.", this);

        if (PowerUpBarUI.Instance != null)
            PowerUpBarUI.Instance.Used += OnPowerUpPressed;
        else
            Debug.LogError("Sahnede PowerUpBarUI yok - power-up butonları " +
                           "kontrolcüye ulaşamıyor.", this);

        if (hint != null)
        {
            hint.Used += OnUsePressed;
            hint.Cancelled += CancelSelection;
        }
        else
        {
            Debug.LogWarning("[PowerUpController] 'Hint' alanı boş - power-up " +
                             "kurulunca yönlendirme çıkmaz ve hedefsiz olanlar " +
                             "hiç kullanılamaz (KULLAN butonu orada).", this);
        }

        ResolveSlots();

        Disarm();
    }

    // ==================== BUTON -> ETKİ EŞLEŞTİRMESİ ====================

    // Her tanımın 'Button' alanına bakıp o butonun bardaki slotunu buluyoruz.
    // Slot indeksi aynı zamanda STOK indeksi: oyuncunun marketin kaçıncı
    // satırından aldığı power-up harcanacak. Yani belirleyici olan butonun
    // BARDAKİ yeri; bu listenin sırası tamamen serbest.
    void ResolveSlots()
    {
        PowerUpBarUI bar = PowerUpBarUI.Instance;
        if (bar == null) return;

        slotToDef = new int[bar.SlotCount];

        for (int i = 0; i < slotToDef.Length; i++) slotToDef[i] = -1;

        if (definitions == null) return;

        for (int d = 0; d < definitions.Length; d++)
        {
            Definition def = definitions[d];
            if (def == null) continue;

            if (def.button == null)
            {
                WarnDefinition(d, "'Button' alanı boş - bu etki hiçbir butona bağlı değil.");
                continue;
            }

            int slot = bar.IndexOf(def.button);

            if (slot < 0)
            {
                WarnDefinition(d, "atanan buton alt barda yok. PowerUpBarUI'ın " +
                                  "'Slots' listesindeki butonlardan birini seç.");
                continue;
            }

            // Aynı butona iki etki atanmış. Sessizce birini yutarsak
            // Inspector'da hatayı bulmak imkânsız olur.
            if (slotToDef[slot] >= 0)
            {
                WarnDefinition(d, $"bu buton zaten {slotToDef[slot]}. tanıma atanmış. " +
                                  $"İkinci atama yok sayıldı.");
                continue;
            }

            slotToDef[slot] = d;
        }

        WarnEmptySlots(bar);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void WarnDefinition(int index, string reason)
    {
        Debug.LogWarning($"[PowerUpController] {index}. tanım: {reason}", this);
    }

    // Bir bar slotuna hiç etki atanmamışsa o butona basmak hiçbir şey yapmaz.
    // Oyuncu stoğundan bir şey kaybetmez ama "bozuk" hisseder - görelim.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void WarnEmptySlots(PowerUpBarUI bar)
    {
        for (int slot = 0; slot < slotToDef.Length; slot++)
        {
            if (slotToDef[slot] >= 0) continue;

            Debug.LogWarning($"[PowerUpController] Alt bardaki {slot}. slota hiçbir " +
                             $"etki atanmadı - o butona basmak hiçbir şey yapmaz.", this);
        }
    }

    void Update()
    {
        // Oyun bittiyse ya da panel açıldıysa seçim modu asılı kalmasın:
        // oyuncu kazanma ekranını kapatınca tahtaya dokunduğunda power-up'ı
        // yanardı.
        if (IsArmed && GameManager.Instance != null && GameManager.Instance.InputLocked)
            Disarm();
    }

    // ==================== BUTON ====================

    void OnPowerUpPressed(int index)
    {
        Definition def = GetDefinition(index);
        if (def == null) return;

        if (GameManager.Instance != null && GameManager.Instance.InputLocked) return;

        // Cascade sürerken power-up yok: patlamakta olan bir kristali hedef
        // almak grid'i tutarsız bırakır.
        if (board == null || board.IsBusy) return;

        // Stok kontrolü BURADA: seçim moduna sokup sonra "stokun yok" demek,
        // oyuncuyu boşuna bir kareye dokundurmak olurdu.
        if (PowerUpBarUI.Instance != null && PowerUpBarUI.Instance.GetCount(index) <= 0) return;

        // Tahta dışına dokunma iptali bu kare zaten bu slotu kapattı. Bar
        // butonu da tahta dışı sayıldığı için buraya düşüyoruz; yeniden
        // kurarsak power-up asla kapanmaz.
        if (index == cancelledIndex && Time.frameCount == cancelledFrame)
        {
            cancelledIndex = -1;
            return;
        }

        // Aynı butona ikinci basış: vazgeç
        if (armedIndex == index)
        {
            Disarm();
            return;
        }

        // Hedefsiz olanlar da artık anında çalışmıyor: hepsi önce kuruluyor,
        // oyuncu ne olacağını okuyor. Hedefsizlerde onay KULLAN butonundan,
        // hedeflilerde tahtadaki kareden geliyor.
        Arm(index);
    }

    // ==================== HEDEF SEÇİMİ ====================

    // InputHandler, seçim modundayken dokunuşu takas yerine buraya yolluyor.
    public void OnBoardTapped(Vector3 worldPos)
    {
        if (!IsArmed || board == null) return;

        // Tahta dışına dokunmak İPTAL eder. Oyuncu başka bir power-up'a
        // basmak istiyorsa o butonun kendi dinleyicisi aynı karede devralıyor
        // - cancelledIndex/cancelledFrame ikilisi o durumu ayırıyor.
        if (!board.WorldToGrid(worldPos, out int x, out int y))
        {
            cancelledIndex = armedIndex;
            cancelledFrame = Time.frameCount;

            Disarm();
            return;
        }

        Activate(armedIndex, x, y);
    }

    // Hedefsiz power-up'ın KULLAN butonu
    void OnUsePressed()
    {
        if (!IsArmed) return;

        Activate(armedIndex, -1, -1);
    }

    // Bir iptal butonuna / karartma alanına bağlanmak için
    public void CancelSelection()
    {
        Disarm();
    }

    // ==================== UYGULAMA ====================

    void Activate(int index, int x, int y)
    {
        Definition def = GetDefinition(index);
        if (def == null || board == null) return;

        // Sıra kritik: önce tahta işi KABUL ETSİN, sonra stok düşsün.
        // Tersi olsaydı meşgul bir tahtada power-up sessizce yanardı.
        if (!board.ApplyPowerUp(def.effect, x, y, def.amount))
        {
            Disarm();
            return;
        }

        if (PowerUpBarUI.Instance != null) PowerUpBarUI.Instance.TryUse(index);

        if (def.costsMove && GameManager.Instance != null) GameManager.Instance.UseMove();

        Disarm();
    }

    // ==================== SEÇİM MODU GÖRÜNÜMÜ ====================

    void Arm(int index)
    {
        armedIndex = index;
        WriteArmedVisuals();
    }

    void Disarm()
    {
        armedIndex = -1;
        WriteArmedVisuals();
    }

    void WriteArmedVisuals()
    {
        if (PowerUpBarUI.Instance != null) PowerUpBarUI.Instance.SetArmed(armedIndex);

        if (hint == null) return;

        Definition def = GetDefinition(armedIndex);

        if (!IsArmed || def == null)
        {
            hint.Hide();
            return;
        }

        hint.Show(def.icon, def.title, def.description, !def.effect.NeedsTarget());
    }

    // slotIndex = bardaki yer = stok indeksi. definitions'taki sıra DEĞİL.
    Definition GetDefinition(int slotIndex)
    {
        if (slotToDef == null || slotIndex < 0 || slotIndex >= slotToDef.Length) return null;

        int d = slotToDef[slotIndex];
        if (d < 0 || definitions == null || d >= definitions.Length) return null;

        return definitions[d];
    }
}
