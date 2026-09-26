// OTOMATİK YAZILIYOR - elle değiştirme. Match3 -> Build'e Hazırla yazıyor.
//
// Boş değilse bu build, telefonda ilk açılışta bütün kayıtları BİR KEZ siler
// (SaveSystem.ApplyBuildReset) - test build'i telefonda sıfırdan başlasın diye.
//
// MAĞAZAYA ÇIKACAK BUILD'DE BOŞ OLMALI: yoksa güncellemeyi alan herkesin
// ilerlemesi silinir. Build'e Hazırla -> "Sadece bilgisayar" boşaltır; dolu
// damgayla build alırken de ayrıca sorulur (BuildPrep).
public static class SaveResetStamp
{
    public const string Value = "";
}
