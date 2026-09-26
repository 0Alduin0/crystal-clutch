using System;

namespace LevelLab
{
    // Testere dişinin kademesi
    public enum Tier
    {
        Nefes,   // zor levelin ardından - oyuncu soluklansın
        Orta,
        Zor      // her 5. level
    }

    // Bir level slotu için üreticinin hedefi: ne kadar zor olacak, hangi
    // mekanikler açık, kaç "can" (engel vuruşu) içerecek.
    public sealed class LevelSpec
    {
        public int Index;                 // 1'den başlayan level numarası
        public Tier Tier;

        // "Ortalama" botun bu oranda kazanacağı hamle sayısı verilir
        public double TargetWinRate;

        public bool UseSolid;
        public bool UseBlock;
        public bool RequireBlock;         // blok bu levelde mutlaka olsun (tanıtım levelleri)
        public bool BlockClusters;        // 2x2 ve üstü blok kümeleri serbest mi
        public int BlockTheme;            // 0 = Lav, 1 = Buz (runtime'daki BlockTheme ile aynı sayılar)
        public int BlockHits = 3;

        public double RefillLuck;
        public int Variety = 4;

        // Toplam vuruş bütçesi (LabLevel.TotalHitPoints)
        public int MinHitPoints;
        public int MaxHitPoints;

        // Kabul edilen hamle aralığı. Bunun dışına düşen aday elenir: 10'dan
        // az hamle nefes aldırmıyor, 40'tan fazlası sıkıcı.
        public int MinMoves = 10;
        public int MaxMoves = 40;

        // İyi bir oyuncu (Usta bot) bu kadar ya da daha az hamlede
        // bitirememeli - "tek hamlede level geçme" yasağı. Kural %2; eşik
        // %1.5 çünkü ölçüm gürültüsü ±%1: üretimde %2'nin hemen altında
        // geçen bir level bağımsız doğrulamada %2.7 çıkmıştı.
        public int ShortWinMoves = 3;
        public double ShortWinLimit = 0.015;

        public string Describe()
        {
            string mech = UseBlock ? (BlockTheme == 1 ? "buz blok" : "lav blok") + $" x{BlockHits}"
                        : UseSolid ? "katı" : "şeffaf";

            if (BlockClusters) mech += ", küme";

            return $"{Tier} %{TargetWinRate * 100:0} | {mech} | şans {RefillLuck:0.00} | can {MinHitPoints}-{MaxHitPoints}";
        }
    }

    // 100 levelin zorluk eğrisi ve mekanik takvimi. Kullanıcının onayladığı
    // varsayılanlar (2026-09-25):
    //   - Testere dişi: her 5 levelden biri zor, ardından nefes, arası orta.
    //     Hedefler oyunun ilk bölümünde yumuşak, 31'den sonra %90 / %70 / %45.
    //   - Katı 1. levelden itibaren (4'ten başlaması planlanmıştı ama sadece
    //     şeffaf engelli bir tahtayı iyi oyuncu zincirle 3 hamlede temizliyor),
    //     21'de lav bloğu, 35'te blok kümeleri, 41'de buz teması, 50'den sonra lav/buz onar levelde bir
    //     dönüşümlü ve zor levellerde 4-5 vuruşlu bloklar.
    //   - Dolum şansı 0.25'ten 60. levele kadar 0.1'e iner (0.4 planlanmıştı, bkz. LuckFor).
    //   - 4 renk (oyunda 4 kristal prefab var).
    public static class LevelPlan
    {
        public static Tier TierFor(int index)
        {
            if (index % 5 == 0) return Tier.Zor;
            if (index % 5 == 1 && index > 1) return Tier.Nefes;
            return Tier.Orta;
        }

        public static LevelSpec For(int index)
        {
            Tier tier = TierFor(index);

            LevelSpec s = new LevelSpec
            {
                Index = index,
                Tier = tier,
                TargetWinRate = TargetFor(index, tier),
                UseSolid = index >= 1,
                UseBlock = index >= 21,
                RequireBlock = index >= 21 && index <= 25,
                BlockClusters = index >= 35,
                BlockTheme = ThemeFor(index),
                BlockHits = BlockHitsFor(index, tier),
                RefillLuck = LuckFor(index),
                Variety = 4
            };

            // Can bütçesi levelle büyüyor, zor level daha dolu. Nefes leveli az
            // engelle değil bol hamleyle (yüksek hedef oran) kolay: seyrek bir
            // tahta "3 hamlede bitmesin" kuralını geçemiyordu.
            double baseHp = 22 + index * 0.5;
            double mult = tier == Tier.Zor ? 1.35 : 1.0;

            s.MinHitPoints = (int)Math.Round(baseHp * mult * 0.85);
            s.MaxHitPoints = (int)Math.Round(baseHp * mult * 1.4);

            return s;
        }

        static double TargetFor(int index, Tier tier)
        {
            // İlk levellerde oyuncuyu kaçırmayalım: aynı testere, daha yumuşak dişler
            if (index <= 10)
                return tier == Tier.Zor ? 0.70 : tier == Tier.Nefes ? 0.95 : 0.88;

            if (index <= 30)
                return tier == Tier.Zor ? 0.55 : tier == Tier.Nefes ? 0.92 : 0.78;

            return tier == Tier.Zor ? 0.45 : tier == Tier.Nefes ? 0.90 : 0.70;
        }

        static int ThemeFor(int index)
        {
            if (index < 41) return 0;          // lav
            if (index < 50) return 1;          // buz tanıtımı
            return ((index - 50) / 10) % 2 == 0 ? 0 : 1;   // onar levelde bir dönüşüm
        }

        static int BlockHitsFor(int index, Tier tier)
        {
            if (index >= 75)
                return tier == Tier.Zor ? 5 : tier == Tier.Orta ? 4 : 3;

            if (index >= 50 && tier == Tier.Zor) return 4;

            return 3;
        }

        static double LuckFor(int index)
        {
            // 0.4 ile başlamak planlanmıştı ama sadece şeffaf engelli ilk
            // levellerde "3 hamlede bitmesin" kuralıyla çelişiyordu: iyi oyuncu
            // %3-44 oranında bitiriyordu. 0.25'ten başlıyor.
            double t = Math.Min(1.0, (index - 1) / 59.0);
            return Math.Round(0.25 - 0.15 * t, 2);
        }
    }
}
