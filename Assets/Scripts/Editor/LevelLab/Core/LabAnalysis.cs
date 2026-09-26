using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LevelLab
{
    // Bir levelin bot tarafından N kez oynanmasının sonucu
    public sealed class LevelStats
    {
        public string Name = "";
        public int MaxMoves;
        public int Runs;
        public int Cap;              // bot en fazla bu kadar hamle oynadı
        public int Obstacles;
        public int HitPoints;
        public int Variety;
        public BotSkill Skill;

        // Oyun başına: kaçıncı hamlede kazandı (-1 = Cap içinde kazanamadı)
        public int[] MovesToWin = Array.Empty<int>();

        // Oyun başına: maxMoves bittiğinde kalan engel (o ana kadar kazandıysa 0)
        public int[] LeftAtMax = Array.Empty<int>();

        // Oyun başına her hamleden sonra kalan engel: Trace[run * (Cap + 1) + hamle].
        // Hamle sayısı SONRADAN ayarlanırken (üretici) "o sınırda kıl payı" sorusu
        // buradan cevaplanıyor. Oyun bitince kalan hamleler son değerle dolu.
        public short[] Trace = Array.Empty<short>();

        public int LeftAt(int run, int moves)
        {
            moves = Math.Max(0, Math.Min(Cap, moves));
            return Trace[run * (Cap + 1) + moves];
        }

        public double StuckRate;       // oyuncunun kilitlendiği oyunlar
        public double AvgRefreshes;    // "hamle kalmadı" yenilemesi
        public double AvgPowers;       // oyun başına doğan güç
        public double AvgEmptyCells;   // hamle sonrası ortalama boş (ölü) kare
        public double AvgClearedPerMove;  // zincirin büyüklüğü: hamle başına patlayan kristal

        // m hamle verilseydi kazanma oranı
        public double WinRate(int moves)
        {
            if (Runs == 0) return 0;

            int wins = 0;
            foreach (int m in MovesToWin)
                if (m >= 0 && m <= moves) wins++;

            return wins / (double)Runs;
        }

        public double WinAtMax => WinRate(MaxMoves);

        // Kaybedip altınla +extra hamle alsaydı
        public double WinWithExtra(int extra) => WinRate(MaxMoves + extra);

        // Kaybedenlerin ne kadarı engelin <= threshold'u kala kaybetti.
        // +5 hamle teklifinin cazibesi bu: "az kaldı" hissi.
        public double NearMissRate(int threshold) => NearMissRate(threshold, MaxMoves);

        // Hamle sınırı 'moves' olsaydı
        public double NearMissRate(int threshold, int moves)
        {
            int losses = 0, near = 0;

            for (int r = 0; r < Runs; r++)
            {
                bool wonInTime = MovesToWin[r] >= 0 && MovesToWin[r] <= moves;
                if (wonInTime) continue;

                losses++;
                if (LeftAt(r, moves) <= threshold) near++;
            }

            return losses == 0 ? 0 : near / (double)losses;
        }

        // Kazanılan oyunların p. yüzdelik hamlesi. Kazanılamayanlar sonsuz sayılır;
        // o dilime yetmiyorsa -1.
        public int Percentile(double p)
        {
            int[] sorted = new int[Runs];

            for (int r = 0; r < Runs; r++)
                sorted[r] = MovesToWin[r] >= 0 ? MovesToWin[r] : int.MaxValue;

            Array.Sort(sorted);

            int index = (int)Math.Ceiling(p * Runs) - 1;
            index = Math.Max(0, Math.Min(Runs - 1, index));

            return sorted[index] == int.MaxValue ? -1 : sorted[index];
        }

        // Hedef kazanma oranına ulaşan en az hamle. Cap içinde ulaşılamıyorsa -1.
        public int MovesFor(double targetWinRate)
        {
            for (int m = 1; m <= Cap; m++)
                if (WinRate(m) >= targetWinRate) return m;

            return -1;
        }

        // Kazanma oranı hedefe EN YAKIN hamle. MovesFor hep yukarı yuvarlıyor:
        // kısa levellerde bir hamle oranı %10 oynattığından hedefi sistematik
        // olarak aşıyordu. Eşitlikte hedefin üstündeki (oyuncu lehine) seçilir.
        // Cap içinde hedefe ulaşılamıyorsa -1.
        public int MovesClosest(double targetWinRate)
        {
            int above = MovesFor(targetWinRate);
            if (above < 0) return -1;
            if (above == 1) return 1;

            double over = WinRate(above) - targetWinRate;
            double under = targetWinRate - WinRate(above - 1);

            return under < over ? above - 1 : above;
        }
    }

    public static class LevelAnalyzer
    {
        // Botun oynayacağı en fazla hamle. maxMoves'un çok üstüne çıkıyoruz ki
        // "kaç hamle verseydik ne olurdu" sorusu cevaplanabilsin.
        public static int CapFor(LabLevel level) => Math.Max(level.MaxMoves * 3, 60);

        // runDone: her oyun bittiğinde çağrılır (ilerleme çubuğu için), herhangi
        // bir iş parçacığından.
        // maxThreads: -1 = bütün çekirdekler. Editörde bir çekirdek boş bırakılıyor
        // ki Unity analiz sürerken donmasın.
        // moveLimit: > 0 ise oyun en fazla bu kadar hamle sürer. "3 hamlede
        // bitiyor mu" gibi sorular için oyunun tamamını oynamaya gerek yok.
        public static LevelStats Analyze(LabLevel level, int runs, BotSkill skill, ulong seed,
                                         CancellationToken cancel = default,
                                         Action runDone = null,
                                         int maxThreads = -1,
                                         int moveLimit = -1)
        {
            int cap = moveLimit > 0 ? moveLimit : CapFor(level);

            LevelStats stats = new LevelStats
            {
                Name = level.Name,
                MaxMoves = level.MaxMoves,
                Runs = runs,
                Cap = cap,
                Obstacles = level.CountAllObstacles(),
                HitPoints = level.TotalHitPoints,
                Variety = level.Variety,
                Skill = skill,
                MovesToWin = new int[runs],
                LeftAtMax = new int[runs],
                Trace = new short[runs * (cap + 1)]
            };

            int[] refreshes = new int[runs];
            int[] powers = new int[runs];
            double[] empty = new double[runs];
            double[] clearedPerMove = new double[runs];
            bool[] stuck = new bool[runs];

            ParallelOptions options = new ParallelOptions
            {
                CancellationToken = cancel,
                MaxDegreeOfParallelism = maxThreads
            };

            Parallel.For(0, runs, options,
                () => (board: new LabBoard(), bot: new LabBot(skill, 0)),
                (run, state, local) =>
                {
                    LabBoard board = local.board;
                    LabBot bot = local.bot;

                    board.Init(level, LabRng.Seed(seed, (ulong)run, 1));
                    bot.Reset(skill, LabRng.Seed(seed, (ulong)run, 2));

                    int leftAtMax = -1;
                    long emptySum = 0;
                    int samples = 0;

                    int traceBase = run * (cap + 1);
                    stats.Trace[traceBase] = (short)board.Remaining;

                    while (!board.Won && !board.Stuck && board.MovesUsed < cap)
                    {
                        if (!bot.PlayOne(board))
                        {
                            board.Stuck = true;
                            break;
                        }

                        emptySum += board.CountEmptyCells();
                        samples++;

                        stats.Trace[traceBase + board.MovesUsed] = (short)board.Remaining;

                        if (board.MovesUsed == level.MaxMoves && leftAtMax < 0)
                            leftAtMax = board.Remaining;
                    }

                    // maxMoves'a varmadan bitti: kazandıysa 0, kilitlendiyse kalan
                    if (leftAtMax < 0) leftAtMax = board.Won ? 0 : board.Remaining;

                    for (int m = board.MovesUsed + 1; m <= cap; m++)
                        stats.Trace[traceBase + m] = (short)board.Remaining;

                    stats.MovesToWin[run] = board.Won ? board.MovesUsed : -1;
                    stats.LeftAtMax[run] = leftAtMax;

                    refreshes[run] = board.Refreshes;
                    powers[run] = board.CrossMade + board.BombMade + board.ColorBombMade;
                    empty[run] = samples > 0 ? emptySum / (double)samples : 0;
                    clearedPerMove[run] = board.MovesUsed > 0 ? board.Cleared / (double)board.MovesUsed : 0;
                    stuck[run] = board.Stuck;

                    runDone?.Invoke();
                    return local;
                },
                _ => { });

            double stuckCount = 0, refreshSum = 0, powerSum = 0, emptyTotal = 0, clearedTotal = 0;

            for (int r = 0; r < runs; r++)
            {
                if (stuck[r]) stuckCount++;
                refreshSum += refreshes[r];
                powerSum += powers[r];
                emptyTotal += empty[r];
                clearedTotal += clearedPerMove[r];
            }

            stats.StuckRate = stuckCount / runs;
            stats.AvgRefreshes = refreshSum / runs;
            stats.AvgPowers = powerSum / runs;
            stats.AvgEmptyCells = emptyTotal / runs;
            stats.AvgClearedPerMove = clearedTotal / runs;

            return stats;
        }
    }

    // Raporu metne döker. Editör penceresi de konsol aracı da aynı biçimi kullanıyor.
    public static class LabReport
    {
        public const int NearMissThreshold = 3;
        public const int ContinueMoves = 5;   // GameManager.continueMovesGranted

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Percent(double v) => (v * 100).ToString("0", Inv) + "%";

        static string MovesOrDash(int m) => m < 0 ? "-" : m.ToString(Inv);

        public static string ToMarkdown(IList<LevelStats> all)
        {
            StringBuilder sb = new StringBuilder();

            if (all.Count > 0)
                sb.AppendLine($"Bot: {all[0].Skill}, level başına {all[0].Runs} oyun\n");

            sb.AppendLine("| # | Level | Engel (can) | Renk | Hamle | Kazanma | +5 ile | Kıl payı | Medyan | %90 için | %70 için | %50 için | Takılma | Ölü kare |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

            for (int i = 0; i < all.Count; i++)
            {
                LevelStats s = all[i];

                sb.Append("| ").Append(i + 1)
                  .Append(" | ").Append(s.Name)
                  .Append(" | ").Append(s.Obstacles).Append(" (").Append(s.HitPoints).Append(')')
                  .Append(" | ").Append(s.Variety)
                  .Append(" | ").Append(s.MaxMoves)
                  .Append(" | ").Append(Percent(s.WinAtMax))
                  .Append(" | ").Append(Percent(s.WinWithExtra(ContinueMoves)))
                  .Append(" | ").Append(Percent(s.NearMissRate(NearMissThreshold)))
                  .Append(" | ").Append(MovesOrDash(s.Percentile(0.5)))
                  .Append(" | ").Append(MovesOrDash(s.MovesFor(0.9)))
                  .Append(" | ").Append(MovesOrDash(s.MovesFor(0.7)))
                  .Append(" | ").Append(MovesOrDash(s.MovesFor(0.5)))
                  .Append(" | ").Append(Percent(s.StuckRate))
                  .Append(" | ").Append(s.AvgEmptyCells.ToString("0.0", Inv))
                  .AppendLine(" |");
            }

            sb.AppendLine();
            sb.AppendLine("- **Engel (can)**: engel sayısı, parantez içinde hepsini kırmak için gereken toplam vuruş.");
            sb.AppendLine("- **Renk**: oyunda gerçekten kullanılan kristal çeşidi (prefab sayısıyla kırpılmış).");
            sb.AppendLine("- **Kazanma**: levelin şu anki hamle sayısıyla botun kazanma oranı.");
            sb.AppendLine($"- **+5 ile**: kaybedince altınla +{ContinueMoves} hamle alsaydı.");
            sb.AppendLine($"- **Kıl payı**: kaybedenlerin kaçı {NearMissThreshold} ya da daha az engel kala kaybetti.");
            sb.AppendLine("- **Medyan**: botun kazanmak için harcadığı hamlenin ortancası (hamle sınırı olmadan).");
            sb.AppendLine("- **%90 / %70 / %50 için**: o kazanma oranına ulaşmak için verilmesi gereken hamle.");
            sb.AppendLine("- **Takılma**: tahta yenilendikten sonra da hamle kalmayıp kilitlenen oyunlar. 0 olmalı.");
            sb.AppendLine("- **Ölü kare**: hamle sonrası ortalama boş kare (katı blokların altındaki ulaşılamayan cepler).");

            return sb.ToString();
        }

        public static string ToCsv(IList<LevelStats> all)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("index,level,obstacles,hitpoints,variety,maxMoves,winAtMax,winPlus5,nearMiss,median,p90,movesFor90,movesFor70,movesFor50,stuck,avgRefreshes,avgPowers,avgEmptyCells");

            for (int i = 0; i < all.Count; i++)
            {
                LevelStats s = all[i];

                sb.Append(i + 1).Append(',')
                  .Append(s.Name).Append(',')
                  .Append(s.Obstacles).Append(',')
                  .Append(s.HitPoints).Append(',')
                  .Append(s.Variety).Append(',')
                  .Append(s.MaxMoves).Append(',')
                  .Append(s.WinAtMax.ToString("0.000", Inv)).Append(',')
                  .Append(s.WinWithExtra(ContinueMoves).ToString("0.000", Inv)).Append(',')
                  .Append(s.NearMissRate(NearMissThreshold).ToString("0.000", Inv)).Append(',')
                  .Append(s.Percentile(0.5)).Append(',')
                  .Append(s.Percentile(0.9)).Append(',')
                  .Append(s.MovesFor(0.9)).Append(',')
                  .Append(s.MovesFor(0.7)).Append(',')
                  .Append(s.MovesFor(0.5)).Append(',')
                  .Append(s.StuckRate.ToString("0.000", Inv)).Append(',')
                  .Append(s.AvgRefreshes.ToString("0.00", Inv)).Append(',')
                  .Append(s.AvgPowers.ToString("0.00", Inv)).Append(',')
                  .Append(s.AvgEmptyCells.ToString("0.00", Inv))
                  .AppendLine();
            }

            return sb.ToString();
        }
    }
}
