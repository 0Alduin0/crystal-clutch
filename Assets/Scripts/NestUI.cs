using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Yuva sahnesi (Nest): ejderhayı gösterir, yumurtayla beslenir. Level geçtikçe
// gelen yumurtaların harcandığı yer.
//
// Ayrı bir sahne: 3D kamera doğrudan ekrana çiziyor (3D renderer), menünün
// 2D sahnesine hiçbir şey karışmıyor. Ana menüdeki YUVA butonu (NestButton)
// bu sahneyi yüklüyor, geri butonu menüye dönüyor.
//
// Kurulumu Match3 -> Yuva -> Kur menüsü yapıyor.
public class NestUI : MonoBehaviour
{
    [Header("Ejderha")]
    [SerializeField] DragonDefinition dragon;
    [SerializeField] DragonView view;

    [Tooltip("Uçan yumurtanın yedek hedefi - normalde ejderhanın görünen ortasına iniyor")]
    [SerializeField] Transform eggTarget;

    [SerializeField] Camera sceneCamera;

    [Header("Dönüş")]
    [SerializeField] string menuScene = "MainPages";
    [SerializeField] Button backButton;

    [Tooltip("Sahne geçişlerinde kararan tam ekran siyah")]
    [SerializeField] Image fade;
    [SerializeField] float fadeTime = 0.3f;

    [Header("Bilgi")]
    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI stageText;
    [SerializeField] TextMeshProUGUI progressText;
    [SerializeField] TextMeshProUGUI eggCountText;
    [SerializeField] TextMeshProUGUI hintText;
    [SerializeField] Image progressFill;

    [Header("Besleme")]
    [SerializeField] Button feedButton;
    [SerializeField] Button feedAllButton;

    [Tooltip("Yumurta sayacından ejderhaya uçan yumurtanın görseli")]
    [SerializeField] Sprite eggSprite;

    [Tooltip("Uçuşun başladığı yer - yumurta sayacı")]
    [SerializeField] RectTransform eggSource;

    [Tooltip("Uçan yumurtaların konduğu katman (arayüzün üstünde)")]
    [SerializeField] RectTransform flyLayer;

    [SerializeField] float eggFlightTime = 0.45f;
    [SerializeField] float eggSize = 110f;

    [Header("Aşama Kutlaması")]
    [SerializeField] TextMeshProUGUI stageUpText;
    [SerializeField] Image flash;

    [Header("İsim")]
    [Tooltip("Yumurtadan çıkınca bir kez açılan isim penceresi")]
    [SerializeField] DragonNamePanel namePanel;

    [Tooltip("İsim verilene kadar başlıkta görünen")]
    [SerializeField] string unnamedText = "???";

    [Header("Yazılar")]
    [SerializeField] string noEggsHint = "Yumurtan yok. Level geçerek kazan!";
    [SerializeField] string grownHint = "Ejderhan tam büyüdü! Yeni ejderhalar yakında.";

    static readonly System.Globalization.CultureInfo Turkish = new System.Globalization.CultureInfo("tr-TR");

    // Kayıttaki yenen yumurta (anında güncel) ve ekranda gösterilen (uçan
    // yumurta ejderhaya varınca yetişiyor). Hızlı art arda basışta bu ikisi
    // ayrışıyor; kutlama gösterilen aşama değiştiği anda oynuyor.
    int fed;
    int shownFed;

    bool celebrating;
    bool leaving;

    void Awake()
    {
        if (backButton != null) backButton.onClick.AddListener(Back);
        if (feedButton != null) feedButton.onClick.AddListener(() => Feed(1));
        if (feedAllButton != null) feedAllButton.onClick.AddListener(FeedAll);

        if (sceneCamera == null) sceneCamera = Camera.main;

        // Başlıkta oyuncunun yazdığı isim var: "<b>" yazan "<b>" görsün
        if (nameText != null) nameText.richText = false;
    }

    void Start()
    {
        // Perde her durumda kalksın: kurulum eksikse ekran simsiyah kalıp
        // sessizce bozuk görünmesin, eksik olan görünsün
        FadeIn();

        if (dragon == null)
        {
            Debug.LogError("[Yuva] NestUI'da ejderha atanmamış - Nest sahnesindeki 'Nest' objesine Dragon_Kizil'i sürükle.", this);
            return;
        }

        fed = shownFed = SaveSystem.GetDragonFed(dragon.id);

        if (view != null) view.Show(dragon, shownFed, false);

        if (stageUpText != null) stageUpText.gameObject.SetActive(false);
        if (flash != null) flash.gameObject.SetActive(false);

        if (namePanel == null)
            Debug.LogWarning("[Yuva] İsim penceresi yok - Nest açıkken Match3 -> Yuva -> İsim penceresini kur.", this);

        RefreshTexts(false);

        // Yumurtadan çıkmış ama isim almadan sahneden çıkılmış: şimdi sor
        AskNameIfNeeded();
    }

    // ==================== SAHNE GEÇİŞİ ====================

    void FadeIn()
    {
        if (fade == null) return;

        fade.gameObject.SetActive(true);
        fade.color = Color.black;
        fade.raycastTarget = true;

        fade.DOFade(0f, fadeTime).OnComplete(() =>
        {
            fade.raycastTarget = false;
            fade.gameObject.SetActive(false);
        });
    }

    void Back()
    {
        if (leaving) return;
        leaving = true;

        if (fade == null)
        {
            SceneManager.LoadScene(menuScene);
            return;
        }

        fade.gameObject.SetActive(true);
        fade.raycastTarget = true;
        fade.DOKill();
        fade.DOFade(1f, fadeTime).OnComplete(() => SceneManager.LoadScene(menuScene));
    }

    void OnDestroy()
    {
        DOTween.Kill(this);
        if (fade != null) fade.DOKill();
    }

    // ==================== BESLEME ====================

    void FeedAll()
    {
        if (dragon == null) return;
        Feed(Mathf.Min(SaveSystem.DragonEgg, dragon.EggsToMax - fed));
    }

    void Feed(int count)
    {
        if (dragon == null || celebrating || leaving || Naming) return;

        count = Mathf.Min(count, SaveSystem.DragonEgg, dragon.EggsToMax - fed);

        if (count <= 0)
        {
            // Yumurta yok ya da ejderha tam büyüdü: ipucu yazısı titresin
            if (hintText != null)
            {
                hintText.transform.DOComplete();
                hintText.transform.DOPunchPosition(Vector3.right * 18f, 0.35f, 10);
            }

            return;
        }

        if (!SaveSystem.FeedDragon(dragon.id, count)) return;

        fed += count;
        RefreshCounts();

        int target = fed;
        FlyEgg(count, () => Arrive(target));
    }

    // Yumurta ejderhaya vardı: ya bir yudum ya da aşama atlama
    void Arrive(int target)
    {
        if (target <= shownFed) return;

        int before = dragon.StageAt(shownFed);
        int after = dragon.StageAt(target);

        shownFed = target;

        if (after != before)
        {
            StartCoroutine(StageUp(after));
            return;
        }

        if (view != null)
        {
            view.PlayFeed();
            view.Show(dragon, shownFed, true);
        }

        RefreshTexts(true);

        if (HapticManager.Instance != null) HapticManager.Instance.Play(HapticStrength.Light);
    }

    // Sayaçtan ejderhaya uçan yumurta. Birden fazla yenirse de tek yumurta
    // uçuyor - "Tümünü Ver" zaten ne yaptığını söylüyor.
    void FlyEgg(int count, System.Action onArrive)
    {
        if (eggSprite == null || eggSource == null || flyLayer == null || sceneCamera == null || eggTarget == null)
        {
            onArrive();
            return;
        }

        GameObject obj = new GameObject("FlyingEgg", typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(flyLayer, false);

        RectTransform rt = (RectTransform)obj.transform;
        rt.sizeDelta = Vector2.one * eggSize;
        rt.position = eggSource.position;

        Image img = obj.GetComponent<Image>();
        img.sprite = eggSprite;
        img.preserveAspect = true;
        img.raycastTarget = false;

        // Ejderhanın görünen ortasına in: aşamalar çok farklı boyda ve kamera
        // yakınlaşabiliyor, sabit nokta ekran dışına düşebilir. Model yoksa sabit nokta.
        Vector3 world = view != null && view.TryGetFocus(out Vector3 focus, out _) ? focus : eggTarget.position;

        // Overlay canvas: arayüz koordinatı = ekran pikseli
        Vector3 target = sceneCamera.WorldToScreenPoint(world);
        target.z = 0f;

        Sequence seq = DOTween.Sequence().SetTarget(this);
        seq.Append(rt.DOJump(target, 180f, 1, eggFlightTime).SetEase(Ease.InQuad));
        seq.Join(rt.DORotate(new Vector3(0, 0, count > 1 ? 720f : 360f), eggFlightTime, RotateMode.FastBeyond360));
        seq.Insert(eggFlightTime * 0.6f, rt.DOScale(0.4f, eggFlightTime * 0.4f));
        seq.OnComplete(() =>
        {
            Destroy(obj);
            onArrive();
        });
    }

    // ==================== AŞAMA KUTLAMASI ====================

    IEnumerator StageUp(int stage)
    {
        celebrating = true;
        SetFeedButtons(false);

        if (flash != null)
        {
            flash.gameObject.SetActive(true);
            flash.DOKill();

            Color c = flash.color;
            c.a = 0f;
            flash.color = c;

            flash.DOFade(0.85f, 0.12f).OnComplete(() => flash.DOFade(0f, 0.6f)
                 .OnComplete(() => flash.gameObject.SetActive(false)));
        }

        if (view != null) view.PlayStageUp(dragon, shownFed);

        if (AudioManager.Instance != null) AudioManager.Instance.PlayWin();
        if (HapticManager.Instance != null) HapticManager.Instance.Play(HapticStrength.Heavy);

        if (stageUpText != null)
        {
            stageUpText.gameObject.SetActive(true);
            stageUpText.text = dragon.stages[stage].name.ToUpper(Turkish) + "!";

            stageUpText.transform.DOKill();
            stageUpText.transform.localScale = Vector3.zero;
            stageUpText.transform.DOScale(1f, 0.45f).SetEase(Ease.OutBack, 2f);

            stageUpText.DOKill();
            stageUpText.alpha = 1f;
        }

        RefreshTexts(true);

        yield return new WaitForSeconds(1.5f);

        if (stageUpText != null)
            stageUpText.DOFade(0f, 0.35f).OnComplete(() => stageUpText.gameObject.SetActive(false));

        celebrating = false;
        RefreshTexts(false);

        // Yumurtadan çıktı: kutlama bitince isim sor
        AskNameIfNeeded();
    }

    // ==================== İSİM ====================

    bool Naming => namePanel != null && namePanel.IsOpen;

    // Yumurtadan çıkmış (aşama 1+) ve henüz ismi yoksa pencereyi aç. İsim bir
    // kez veriliyor; kaydedilince bir daha sorulmuyor, değiştirilemiyor.
    void AskNameIfNeeded()
    {
        if (dragon == null || namePanel == null || Naming || leaving) return;
        if (dragon.StageAt(shownFed) < 1 || SaveSystem.HasDragonName(dragon.id)) return;

        SetFeedButtons(false);
        namePanel.Show(OnNamed);
    }

    void OnNamed(string dragonName)
    {
        SaveSystem.SetDragonName(dragon.id, dragonName);

        RefreshTexts(false);

        if (nameText != null)
        {
            nameText.transform.DOComplete();
            nameText.transform.DOPunchScale(Vector3.one * 0.25f, 0.45f, 6);
        }

        if (HapticManager.Instance != null) HapticManager.Instance.Play(HapticStrength.Medium);
    }

    void SetFeedButtons(bool on)
    {
        bool canFeed = on && !Naming && dragon != null && !dragon.IsGrown(fed) && SaveSystem.DragonEgg > 0;

        if (feedButton != null) feedButton.interactable = canFeed;
        if (feedAllButton != null) feedAllButton.interactable = canFeed;
    }

    // ==================== YAZILAR ====================

    void RefreshTexts(bool animate)
    {
        if (dragon == null) return;

        int stage = dragon.StageAt(shownFed);
        bool grown = dragon.IsGrown(shownFed);

        if (nameText != null)
        {
            string given = SaveSystem.GetDragonName(dragon.id);
            nameText.text = string.IsNullOrEmpty(given) ? unnamedText : given;
        }
        if (stageText != null) stageText.text = dragon.stages[stage].name;

        if (progressText != null)
        {
            if (grown)
            {
                progressText.text = "Tam büyüdü";
            }
            else
            {
                int need = dragon.stages[stage].eggsToNext;
                int have = shownFed - dragon.StageStart(stage);
                progressText.text = $"{have} / {need}";
            }
        }

        if (progressFill != null)
        {
            float fill = dragon.ProgressInStage(shownFed);

            progressFill.DOKill();

            if (animate) progressFill.DOFillAmount(fill, 0.35f).SetEase(Ease.OutQuad);
            else progressFill.fillAmount = fill;
        }

        RefreshCounts();

        if (!celebrating) SetFeedButtons(true);
    }

    void RefreshCounts()
    {
        int eggs = SaveSystem.DragonEgg;

        if (eggCountText != null) eggCountText.text = eggs.ToString();

        if (hintText != null)
        {
            string hint = dragon != null && dragon.IsGrown(fed) ? grownHint
                        : eggs <= 0 ? noEggsHint
                        : string.Empty;

            hintText.text = hint;
            hintText.gameObject.SetActive(hint.Length > 0);
        }
    }
}
