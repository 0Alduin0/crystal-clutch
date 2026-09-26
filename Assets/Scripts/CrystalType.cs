// Tahtada eşleşen kristal çeşitleri.
//
// ÖNEMLİ: Buradaki sıra, sahnedeki CrystalPool'un "Crystal Prefabs" dizisiyle
// BİREBİR aynı olmak zorunda - CrystalPool.Get() prefabı dizi indeksiyle
// seçip Type'ı da o indeksten yazıyor, prefabın içindeki Type değerini ezer.
//
// Sıra bozulursa oyun çökmez, sadece sessizce yanlış çalışır: mor kristal
// patlarken yeşil parça saçar, hedefte "Mavi" yazarken başka renk sayılır.
// Prefab dizisini oynatırsan burayı da oynat.
//
// Sayılar SABİT: enum'lar int olarak serileşiyor.
public enum CrystalType
{
    Kizil = 0,   // KızılKristal  - kırmızı yıldız
    Mor   = 1,   // MorKristal    - mor hilal
    Mavi  = 2,   // MaviKristal   - mavi elmas
    Yesil = 3,   // YeşilKristal  - yeşil lotus
}
