using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Bir power-up "kurulduğunda" tahtanın üstünde çıkan yönlendirme.
//
// İki farklı power-up ailesi var ve ikisi aynı pencereyi kullanıyor:
//
//   HEDEFLİ   (Ejder Pençesi, Kurtuluş Kanadı)
//     Sadece ikon + açıklama görünür. Oyuncu tahtada bir kare seçer.
//     İptal: tahta DIŞINDA herhangi bir yere dokunmak.
//
//   HEDEFSİZ  (Kum Saati, Rün Çemberi)
//     Ayrıca ortada bir KULLAN butonu çıkar, etki ona basınca uygulanır.
//     İptal: o buton DIŞINDA herhangi bir yere dokunmak.
//
// İkinci ailede iptali tam ekran bir "blocker" yakalıyor. Blocker, KULLAN
// butonunun ARKASINDA duruyor: butona basınca üstteki buton olayı yutuyor,
// başka yere basınca arkadaki blocker iptal ediyor. Böylece iki dokunuşu
// ayırmak için EventSystem sorgulamaya gerek kalmıyor.
public class PowerUpHintUI : MonoBehaviour
{
    [Tooltip("İkon + başlık + açıklamayı taşıyan kök. Boş bırakırsan bu " +
             "objenin kendisi kullanılır.")]
    [SerializeField] GameObject root;

    [Tooltip("Açılışta zıplayan kutu - boşsa root'un kendisi ölçeklenir")]
    [SerializeField] RectTransform box;

    [SerializeField] Image icon;
    [SerializeField] TextMeshProUGUI titleText;

    [Tooltip("Ne işe yaradığı ve nasıl kullanıldığı")]
    [SerializeField] TextMeshProUGUI descriptionText;

    [Header("Hedefsiz Power-up'lar")]
    [Tooltip("KULLAN butonu - sadece hedefsiz power-up'larda görünür")]
    [SerializeField] Button useButton;

    [Tooltip("Tam ekran şeffaf iptal alanı. Hierarchy'de KULLAN butonunun " +
             "ÜSTÜNDE değil ALTINDA olmalı - yoksa butona basmak da iptal eder.")]
    [SerializeField] Button cancelBlocker;

    public event Action Used;
    public event Action Cancelled;

    bool wired;
    bool opening;

    GameObject Root => root != null ? root : gameObject;

    public bool IsOpen => Root.activeSelf;

    void Awake()
    {
        EnsureWired();

        // NotEnoughPanel'deki tuzağın aynısı: panel sahnede kapalı başlarsa
        // Awake ilk Show'un SetActive'inde çalışır ve açılışı yerdi.
        if (!opening && Root.activeSelf) Root.SetActive(false);
    }

    void EnsureWired()
    {
        if (wired) return;
        wired = true;

        if (useButton != null) useButton.onClick.AddListener(() => Used?.Invoke());
        if (cancelBlocker != null) cancelBlocker.onClick.AddListener(() => Cancelled?.Invoke());
    }

    // needsUseButton: hedefsiz power-up mı. Hedefliyse buton da blocker da
    // kapalı kalıyor - o ailede iptali tahta dışına dokunmak hallediyor ve
    // tam ekran bir blocker tahtaya dokunmayı da yutardı.
    public void Show(Sprite sprite, string title, string description, bool needsUseButton)
    {
        EnsureWired();

        if (icon != null)
        {
            icon.sprite = sprite;
            icon.gameObject.SetActive(sprite != null);
        }

        if (titleText != null) titleText.text = title;
        if (descriptionText != null) descriptionText.text = description;

        if (useButton != null) useButton.gameObject.SetActive(needsUseButton);
        if (cancelBlocker != null) cancelBlocker.gameObject.SetActive(needsUseButton);

        opening = true;
        Root.SetActive(true);

        Transform pop = box != null ? (Transform)box : Root.transform;
        pop.DOKill();
        pop.localScale = Vector3.zero;
        pop.DOScale(1f, 0.25f).SetEase(Ease.OutBack);
    }

    public void Hide()
    {
        opening = false;

        if (box != null) box.DOKill();

        Root.transform.DOKill();
        Root.SetActive(false);
    }

    void OnDisable()
    {
        if (box != null) box.DOKill();

        transform.DOKill();
    }
}
