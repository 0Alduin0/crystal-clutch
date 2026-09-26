// Bir karenin üstünde duran engel.
//
// Kırıldıkça BİR ALT kademeye iner:
//   Solid -> Transparent -> None     katı tek seferde yok olmaz, önce şeffafa döner
//   Block -> None                    blok çatlaya çatlaya dayanır, sonunda doğrudan boşalır
//
// ÖNEMLİ: Katı ve blok karede kristal BULUNMAZ. Onun dışında (None ve Transparent)
// her karede kristal vardır - şeffafın "içinde" de kristal durur, arkadan görünür
// ve oynanabilir.
//
// Sayılar SABİT: level asset'lerinde ve LevelLab simülasyonunda (LabObstacle)
// aynı değerler kullanılıyor.
public enum ObstacleType
{
    None = 0,          // engel yok - normal kare
    Transparent = 1,   // şeffaf kapak: arkasındaki kristal görünür, kare oynanır
    Solid = 2,         // katı blok: kristal yok, kare oynanamaz, üstünden kristal geçmez
    Block = 3          // çok vuruşlu blok: katı gibi kapatır, her komşu patlamada çatlar,
                       // son vuruşta boşalır. Görünüşü levelin temasından (BlockTheme)
}

// Blokların görünüşü. Kural ikisinde de aynı - sadece level teması.
public enum BlockTheme
{
    Lav = 0,
    Buz = 1
}

// Bir engele vurulduğunda ne olduğu. Board bunun üstünden efekt/puan seçiyor.
public enum ObstacleHit
{
    None,          // o karede engel yok - hiçbir şey olmadı
    Damaged,       // canı azaldı ama kademe değişmedi (çok vuruşlu engeller)
    Downgraded,    // katı -> şeffafa döndü
    Broken         // tamamen kırıldı, kare normale döndü
}
