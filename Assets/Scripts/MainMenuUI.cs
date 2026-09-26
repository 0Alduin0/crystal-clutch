using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Açılış ekranı. Oyun TEK YÖNLÜ ilerlediği için level SEÇME diye bir şey yok:
// tek bir "DEVAM ET" butonu oyuncuyu kaldığı levelden başlatır.
public class MainMenuUI : MonoBehaviour
{
    [Header("Sahneler")]
    [SerializeField] string gameSceneName = "Game";

    [Header("Levellar")]
    [Tooltip("GameManager ile AYNI asset olmalı - Levels/LevelSet.asset")]
    [SerializeField] LevelSet levelSet;

    [Header("Devam Et")]
    [SerializeField] Button continueButton;

    [Tooltip("Butonun üstündeki 'Level 7' yazısı - opsiyonel")]
    [SerializeField] TextMeshProUGUI continueLabel;

    [Tooltip("Eldeki bütün levellar bitince beliren panel - opsiyonel")]
    [SerializeField] GameObject allDonePanel;

    void Start()
    {
        // Profildeki "ne zamandan beri oynuyor" bilgisi. Sadece ilk açılışta yazılıyor.
        SaveSystem.MarkFirstPlay();

        SetupContinue();
    }

    // ==================== HİKAYE ====================

    void SetupContinue()
    {
        int levelIndex = SaveSystem.CurrentLevel;
        int total = levelSet != null ? levelSet.Count : 0;

        // Sayaç setin dışına taşmışsa oyuncu eldeki her leveli bitirmiş demek.
        // Sete yeni level eklendiği gün bu kendiliğinden false'a döner ve
        // oyuncu kaldığı yerden devam eder - elle sıfırlanacak bir bayrak yok.
        bool allDone = total <= 0 || levelIndex >= total;

        if (total <= 0)
            Debug.LogError("Menüdeki 'Level Set' alanı boş - " +
                           "Levels/LevelSet.asset'i Inspector'da bu alana sürükle.", this);

        if (allDonePanel != null) allDonePanel.SetActive(allDone);

        if (continueButton != null)
        {
            continueButton.gameObject.SetActive(!allDone);
            continueButton.onClick.AddListener(LoadStory);
        }

        if (continueLabel != null && !allDone)
            continueLabel.text = "Level " + (levelIndex + 1);
    }

    // Canı olmayan levele giremez. Bu kontrol olmadan can sistemi tamamen
    // anlamsızdı: oyuncu 0 canla girip kaybediyor, SpendLife sessizce false
    // dönüyor ve oyun sonsuza kadar oynanıyordu.
    void LoadStory()
    {
        if (LivesUI.Instance != null)
        {
            // Can yoksa "canın bitti" panelini kendisi açıyor
            if (!LivesUI.Instance.TryStartLevel()) return;
        }
        else if (SaveSystem.Lives <= 0)
        {
            WarnNoLivesUI();
            return;
        }

        SceneManager.LoadScene(gameSceneName);
    }

    // LivesUI sahnede yoksa giriş yine kapalı ama ekranda hiçbir şey çıkmıyor -
    // oyuncu butonun bozuk olduğunu sanır. Geliştirirken görelim.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void WarnNoLivesUI()
    {
        Debug.LogWarning("[MainMenuUI] Can bitti ama sahnede LivesUI yok - " +
                         "oyuncuya hiçbir şey gösterilemiyor. Üst bara LivesUI " +
                         "bileşenini ekle.", this);
    }

    // ==================== TEST ====================

    // Bir butona bağlarsan tüm ilerlemeyi siler, oyuncu Level 1'e döner
    public void ResetProgress()
    {
        SaveSystem.ClearAll();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
