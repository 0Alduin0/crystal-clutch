// Alt bardaki bir power-up'ın tahtada NE YAPTIĞI.
//
// Hangi slotun hangi etkiyi taşıdığı koda gömülü DEĞİL: PowerUpController'ın
// Inspector'ında slot slot seçiliyor. Böylece "2. power-up karıştırsın" demek
// bir dropdown değiştirmek kadar; kod değişmiyor, market satırları ve stok
// indeksleri bozulmuyor.
//
// Bu enum TAHTADAKİ buff'larla (PowerType) karıştırılmamalı: oradakiler
// eşleşmeden doğuyor, buradakiler marketten satın alınıyor.
//
// Yeni bir etki eklemek: buraya bir eleman ekle, karşılığını Board'da
// CollectPowerUpTargets (hedefliyse) ya da RunPowerUpEffect (hedefsizse)
// içine yaz. Hedefsizse NeedsTarget'a da ekle.
public enum PowerUpEffect
{
    BlokKirma = 0,          // seçilen TEK kareyi patlatır, engele vurur
    Karistir = 1,           // hedefsiz - tahtayı yeniden dizer
    HamleEkle = 2,          // hedefsiz - hamle ekler
    MultiColorOlustur = 3   // seçilen kristali multicolor buff'a çevirir
}

public static class PowerUpEffectExtensions
{
    // Hedefsiz etkiler tahtada kare beklemiyor; PowerUpHintUI onlarda KULLAN
    // butonunu gösteriyor. Hedefliler için oyuncu bir kareye dokunuyor.
    public static bool NeedsTarget(this PowerUpEffect effect)
    {
        return effect != PowerUpEffect.Karistir
            && effect != PowerUpEffect.HamleEkle;
    }
}
