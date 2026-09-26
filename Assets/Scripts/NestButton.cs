using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Ana menüdeki YUVA butonu: ejderha sahnesine (Nest) götürür. Üstündeki rozet
// ejderhaya yedirilebilecek yumurta sayısını gösterir - oyuncu level geçip
// menüye dönünce "yuvada beni bekleyen bir şey var" diye görsün.
[RequireComponent(typeof(Button))]
public class NestButton : MonoBehaviour
{
    [SerializeField] string nestScene = "Nest";

    [Tooltip("Rozet 'ejderha hâlâ büyüyebilir mi' diye buna bakıyor")]
    [SerializeField] DragonDefinition dragon;

    [SerializeField] GameObject badge;
    [SerializeField] TextMeshProUGUI badgeText;

    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() => SceneManager.LoadScene(nestScene));
    }

    void Start()
    {
        if (badge == null) return;

        int eggs = SaveSystem.DragonEgg;
        bool show = dragon != null && eggs > 0 && !dragon.IsGrown(SaveSystem.GetDragonFed(dragon.id));

        badge.SetActive(show);

        if (show && badgeText != null)
            badgeText.text = eggs > 99 ? "99+" : eggs.ToString();
    }
}
