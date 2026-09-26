using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Liderlik tablosundaki tek satır. Soldan sağa: sıra, profil fotoğrafı,
// isim, ulaşılan bölüm.
//
// Kendi başına veri tutmuyor - LeaderboardUI listeyi kurup her karta Bind
// çağırıyor. Böylece kart, verinin nereden geldiğini (sunucu ya da
// bağlantı yokken sahte liste) hiç bilmiyor.
public class LeaderboardCard : MonoBehaviour
{
    [SerializeField] Image background;
    [SerializeField] TextMeshProUGUI rankText;
    [SerializeField] Image avatar;
    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI levelText;

    [Tooltip("Profil fotoğrafının üstündeki buton - basınca o oyuncunun profil " +
             "paneli açılıyor. Avatar objesine Button ekleyip buraya sürükle.")]
    [SerializeField] Button avatarButton;

    // Kartlar yeniden kullanılıyor; her Bind tıklamanın kimi açacağını yeniliyor
    Action onAvatarClick;
    bool wired;

    public void Bind(int rank, Sprite avatarSprite, string playerName, int level,
                     bool isPlayer, Color normalColor, Color playerColor,
                     Action onAvatarClicked = null)
    {
        if (rankText != null) rankText.text = rank.ToString();
        if (nameText != null) nameText.text = playerName;
        if (levelText != null) levelText.text = level.ToString();

        if (avatar != null && avatarSprite != null) avatar.sprite = avatarSprite;

        // Oyuncunun kendi satırı listede kaybolmasın
        if (background != null) background.color = isPlayer ? playerColor : normalColor;

        onAvatarClick = onAvatarClicked;

        if (avatarButton != null)
        {
            // Dinleyici bir kez bağlanıyor, hedefi onAvatarClick'ten okuyor -
            // her Bind'da AddListener yapsaydık tazelemeler üst üste binerdi
            if (!wired)
            {
                wired = true;
                avatarButton.onClick.AddListener(() => onAvatarClick?.Invoke());
            }

            avatarButton.interactable = onAvatarClicked != null;
        }
    }
}
