using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputHandler : MonoBehaviour
{
    [SerializeField] Board board;
    [SerializeField] float threshold = 0.4f;

    Crystal selectedCrystal;
    Vector2 startPos;
    bool swapDone;

    Camera cam;

    // Raycast sonuçları için tekrar kullanılan tampon - kare başına çöp üretmesin
    readonly List<RaycastHit2D> hitBuffer = new List<RaycastHit2D>(8);
    ContactFilter2D hitFilter;

    void Awake()
    {
        cam = Camera.main;

        hitFilter = ContactFilter2D.noFilter;
        hitFilter.useTriggers = true;
    }

    void Update()
    {
        // Pointer, mouse ve touch'ı tek arayüzde birleştiriyor: editörde Mouse'a,
        // telefonda Touchscreen'e çözülüyor. Tek parmaklı swipe için doğru soyutlama
        // bu - ayrı ayrı Mouse.current / Touchscreen.current yazmaya gerek yok.
        Pointer pointer = Pointer.current;
        if (pointer == null) return;   // işaret cihazı yok (bazı build/test ortamları)

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // Kare başına bir kez oku ve bir kez dünyaya çevir.
        // ReadValue her çağrıda cihazı yeniden sorguluyor, üç yerde çağırmanın anlamı yok.
        Vector2 worldPos = cam.ScreenToWorldPoint(pointer.position.ReadValue());

        // Basma ile swipe arasında cascade bu kristali patlatıp havuza yollamış
        // olabilir. Referans null OLMAZ - obje canlı, sadece kapalı - ama X/Y
        // bayat kalır ve oyuncunun kastetmediği bir kare takas edilir.
        if (selectedCrystal != null && !selectedCrystal.gameObject.activeInHierarchy)
            selectedCrystal = null;

        if (pointer.press.wasPressedThisFrame)
        {
            // Parmak ekrana değdi: ipucu kalksın. Altında kristal zıplarken
            // hamle yapmak rahatsız edici.
            if (board != null) board.NotifyPlayerActivity();

            // Bir power-up kuruluysa bu dokunuş takas değil. Kristal seçmiyoruz
            // ki parmak sürüklenirse takas doğmasın.
            if (PowerUpController.Instance != null && PowerUpController.Instance.IsArmed)
            {
                // Tahtada kare bekleyenler (Ejder Pençesi, Kurtuluş Kanadı)
                // dokunuşu kullanır; tahta dışına düşerse kendisi iptal eder.
                //
                // Hedefsizler (Kum Saati, Rün Çemberi) dokunuşu HİÇ görmemeli:
                // onlarda hem kullanım hem iptal hint penceresinin butonlarına
                // bağlı. Buradan da işleseydik KULLAN'a basmak, butonun onClick'i
                // çalışmadan önce seçimi iptal edebilirdi.
                if (PowerUpController.Instance.IsAwaitingBoardTap)
                    PowerUpController.Instance.OnBoardTapped(worldPos);

                selectedCrystal = null;
                swapDone = true;   // bu basış artık swipe üretemez
                return;
            }

            selectedCrystal = GetCrystalAt(worldPos);
            startPos = worldPos;
            swapDone = false;
        }

        if (pointer.press.isPressed && selectedCrystal != null && !swapDone)
        {
            Vector2 delta = worldPos - startPos;

            if (delta.magnitude >= threshold)
            {
                board.TrySwap(selectedCrystal.X, selectedCrystal.Y, GetDirection(delta));
                swapDone = true;
            }
        }

        if (pointer.press.wasReleasedThisFrame)
        {
            selectedCrystal = null;
            swapDone = false;
        }
    }

    // Sıfır yönlü raycast - "bu noktada ne var" demenin kısa yolu.
    // Kristalnin BoxCollider2D'si kök objede, Crystal script'i de aynı objede.
    //
    // Tek isabetle yetinmiyoruz: aynı karede kristalin ÖNÜNDE duran bir şey
    // (şeffaf engel görseline collider eklenmişse) ilk sırayı kapar ve kristal
    // hiç seçilemez olurdu. Tüm isabetler arasından kristali arıyoruz.
    Crystal GetCrystalAt(Vector2 worldPos)
    {
        // 2D fizik simülasyonu kapalı (AppPerformance): tween'le kayan
        // kristallerin collider'ları kendiliğinden güncellenmiyor. Sorgudan
        // önce eşitle - sadece dokunma anında, kare başına değil.
        Physics2D.SyncTransforms();

        int count = Physics2D.Raycast(worldPos, Vector2.zero, hitFilter, hitBuffer);

        for (int i = 0; i < count; i++)
        {
            Collider2D col = hitBuffer[i].collider;
            if (col == null) continue;

            Crystal f = col.GetComponent<Crystal>();
            if (f != null) return f;
        }

        return null;
    }

    Vector2Int GetDirection(Vector2 delta)
    {
        if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
            return delta.x > 0 ? Vector2Int.right : Vector2Int.left;
        else
            return delta.y > 0 ? Vector2Int.up : Vector2Int.down;
    }
}
