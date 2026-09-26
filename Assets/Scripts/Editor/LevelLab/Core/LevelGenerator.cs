using System;
using System.Collections.Generic;
using System.Threading;

namespace LevelLab
{
    // Üreticinin bir slot için önerdiği level ve neden önerdiği
    public sealed class GeneratedLevel
    {
        public LabLevel Level;          // MaxMoves ayarlanmış, kullanıma hazır
        public LevelSpec Spec;
        public string Recipe = "";

        public LevelStats Stats;        // Ortalama bot, tam değerlendirme (Level.MaxMoves ile)
        public double ShortWinRate;     // Usta bot Spec.ShortWinMoves hamlede ne kadar bitiriyor
        public double NearMiss;         // kaybedenlerin kıl payı oranı (ayarlanan hamlede)
        public double Novelty;          // önceki levellere benzemezlik, 0-1
        public double Coherence;        // tek başına duran engel azlığı, 0-1
        public double Score;

        public bool Accepted => Rejection == null;
        public string Rejection;        // null = kabul
    }

    // Bir level slotu için: aday layout üret -> ele -> botla ölç -> hamleyi
    // hedef kazanma oranına göre ayarla -> puanla -> en iyileri döndür.
    //
    // Ölçüm iki aşamalı, yoksa çok yavaş olurdu:
    //   1. Hızlı tarama: her aday az sayıda oyunla. Açık ara kötüler elenir.
    //   2. Tam değerlendirme: en iyi birkaç aday çok oyunla + "3 hamlede
    //      biter mi" kontrolü Usta botla.
    public sealed class LevelGenerator
    {
        public int Candidates = 48;
        public int QuickRuns = 48;
        public int Finalists = 8;
        public int FullRuns = 240;

        // Tam ölçüm en fazla bu kadar adaya yapılır (süre sınırı)
        public int MaxFullEvaluations = 16;
        public int ShortRuns = 400;

        // Önceki levellerden en fazla bu kadar benzer olabilir (0-1)
        public double MaxSimilarity = 0.72;

        // Benzerlik için geriye kaç levele bakılsın
        public int NoveltyWindow = 15;

        // Hamle sonrası ortalama ölü kare sınırı
        public double MaxEmptyCells = 3.0;

        public int MaxThreads = -1;

        // Son Generate çağrısının bütün adayları, elenenler dahil - "neden aday
        // çıkmadı" sorusu için (Rejection alanı)
        public List<GeneratedLevel> LastCandidates { get; private set; } = new List<GeneratedLevel>();

        // Layout üreticisi kurallara uyan yerleşim bulamadıkça artan sayaç
        public int LastLayoutFailures { get; private set; }

        // Hamle için kademe başına tercih: bu civarda olanlar biraz öne çıkar
        static int PreferredMoves(Tier t) => t == Tier.Nefes ? 18 : t == Tier.Zor ? 26 : 22;

        // previous: bu slottan ÖNCEKİ leveller (benzerlik için). progress: durum metni.
        public List<GeneratedLevel> Generate(LevelSpec spec, IList<LabLevel> previous, ulong seed,
                                             CancellationToken cancel = default,
                                             Action<string> progress = null)
        {
            LayoutGenerator layouts = new LayoutGenerator(8, 8, LabRng.Seed(seed, (ulong)spec.Index, 101));
            List<GeneratedLevel> pool = new List<GeneratedLevel>();

            LastCandidates = new List<GeneratedLevel>();
            LastLayoutFailures = 0;

            // ---------- 1. Aday layout'lar ----------
            for (int c = 0; c < Candidates; c++)
            {
                cancel.ThrowIfCancellationRequested();

                LayoutCandidate layout = layouts.Generate(spec);

                if (layout == null)
                {
                    LastLayoutFailures++;
                    continue;
                }

                GeneratedLevel g = new GeneratedLevel
                {
                    Spec = spec,
                    Recipe = layout.Recipe,
                    Level = MakeLevel(spec, layout.Cells)
                };

                g.Novelty = 1.0 - HighestSimilarity(g.Level, previous);
                g.Coherence = Coherence(g.Level);

                LastCandidates.Add(g);

                if (1.0 - g.Novelty > MaxSimilarity)
                {
                    g.Rejection = "önceki levellere çok benziyor";
                    continue;
                }

                pool.Add(g);
            }

            // ---------- 2. Hızlı tarama ----------
            int done = 0;

            foreach (GeneratedLevel g in pool)
            {
                cancel.ThrowIfCancellationRequested();

                done++;
                progress?.Invoke($"Level {spec.Index}: tarama {done}/{pool.Count}");

                LevelStats quick = LevelAnalyzer.Analyze(g.Level, QuickRuns, BotSkill.Ortalama,
                                                         LabRng.Seed(seed, (ulong)spec.Index, 201),
                                                         cancel, null, MaxThreads);

                int moves = quick.MovesFor(spec.TargetWinRate);

                if (quick.StuckRate > 0) g.Rejection = "oyuncu kilitlenebiliyor";
                else if (moves < 0) g.Rejection = "hedef orana hamle sınırı içinde ulaşılamıyor";
                else if (moves < spec.MinMoves * 0.8) g.Rejection = "fazla kolay";
                else if (moves > spec.MaxMoves * 1.15) g.Rejection = "fazla uzun";

                g.Score = QuickScore(g, moves, spec);
            }

            List<GeneratedLevel> finalists = pool.FindAll(g => g.Accepted);
            finalists.Sort((a, b) => b.Score.CompareTo(a.Score));

            // ---------- 3. Tam değerlendirme ----------
            // Taramada öndekiler sırayla ölçülüyor; elenen olursa sıradakine
            // geçiliyor. Finalists kadar kabul ya da MaxFullEvaluations kadar
            // ölçüm yapılınca duruluyor.
            done = 0;
            int acceptedCount = 0;

            foreach (GeneratedLevel g in finalists)
            {
                if (acceptedCount >= Finalists || done >= MaxFullEvaluations)
                {
                    g.Rejection = "ölçülmedi (yeterli aday bulundu)";
                    continue;
                }

                cancel.ThrowIfCancellationRequested();

                // Ayrı satırda: progress null ise ?.Invoke argümanı hiç hesaplamıyor
                done++;
                progress?.Invoke($"Level {spec.Index}: ölçüm {done}");

                // "3 hamlede bitiyor mu" - en ucuz eleme önce
                LevelStats shortRun = LevelAnalyzer.Analyze(g.Level, ShortRuns, BotSkill.Usta,
                                                            LabRng.Seed(seed, (ulong)spec.Index, 301),
                                                            cancel, null, MaxThreads,
                                                            moveLimit: spec.ShortWinMoves);

                g.ShortWinRate = shortRun.WinRate(spec.ShortWinMoves);

                if (g.ShortWinRate > spec.ShortWinLimit)
                {
                    g.Rejection = $"iyi oyuncu {spec.ShortWinMoves} hamlede bitirebiliyor (%{g.ShortWinRate * 100:0})";
                    continue;
                }

                LevelStats full = LevelAnalyzer.Analyze(g.Level, FullRuns, BotSkill.Ortalama,
                                                        LabRng.Seed(seed, (ulong)spec.Index, 401),
                                                        cancel, null, MaxThreads);

                int moves = full.MovesClosest(spec.TargetWinRate);

                if (full.StuckRate > 0) { g.Rejection = "oyuncu kilitlenebiliyor"; continue; }
                if (moves < 0) { g.Rejection = "hedef orana ulaşılamıyor"; continue; }
                if (moves < spec.MinMoves) { g.Rejection = $"fazla kolay ({moves} hamle)"; continue; }
                if (moves > spec.MaxMoves) { g.Rejection = $"fazla uzun ({moves} hamle)"; continue; }
                if (full.AvgEmptyCells > MaxEmptyCells) { g.Rejection = "çok ölü kare"; continue; }

                g.Level.MaxMoves = moves;

                // İstatistikler ayarlanan hamleyle okunsun
                full.MaxMoves = moves;
                g.Stats = full;
                g.NearMiss = full.NearMissRate(LabReport.NearMissThreshold, moves);
                g.Score = FinalScore(g, moves, spec);

                acceptedCount++;
            }

            List<GeneratedLevel> result = finalists.FindAll(g => g.Accepted);
            result.Sort((a, b) => b.Score.CompareTo(a.Score));

            return result;
        }

        static LabLevel MakeLevel(LevelSpec spec, LabObstacle[] cells)
        {
            return new LabLevel
            {
                Name = "Level_" + spec.Index,
                Width = 8,
                Height = 8,
                Variety = spec.Variety,
                MaxMoves = spec.MaxMoves,     // ölçüm sınırı; sonra ayarlanıyor
                TransparentHits = 1,
                SolidHits = 1,
                BlockHits = spec.BlockHits,
                RefillLuck = spec.RefillLuck,
                Obstacles = (LabObstacle[])cells.Clone()
            };
        }

        // ==================== PUAN ====================

        static double QuickScore(GeneratedLevel g, int moves, LevelSpec spec)
        {
            double fit = moves < 0 ? 0 : 1.0 - Math.Min(1.0, Math.Abs(moves - PreferredMoves(spec.Tier)) / 15.0);
            return 2.0 * g.Novelty + 2.0 * g.Coherence + 1.0 * fit;
        }

        // Kıl payı: zor ve orta levelde "az kaldı" hissi +5 teklifini anlamlı
        // kılıyor. Nefes levelinde önemi az.
        static double FinalScore(GeneratedLevel g, int moves, LevelSpec spec)
        {
            double nearWeight = spec.Tier == Tier.Nefes ? 0.5 : 2.0;
            double fit = 1.0 - Math.Min(1.0, Math.Abs(moves - PreferredMoves(spec.Tier)) / 15.0);

            return nearWeight * g.NearMiss
                 + 2.0 * g.Novelty
                 + 2.0 * g.Coherence
                 + 1.0 * fit
                 - 0.3 * g.Stats.AvgEmptyCells;
        }

        // ==================== BENZERLİK ====================

        // Önceki son N levelle en yüksek benzerlik (tür dahil Jaccard)
        double HighestSimilarity(LabLevel level, IList<LabLevel> previous)
        {
            if (previous == null || previous.Count == 0) return 0;

            double max = 0;
            int from = Math.Max(0, previous.Count - NoveltyWindow);

            for (int i = from; i < previous.Count; i++)
                if (previous[i] != null) max = Math.Max(max, Similarity(level, previous[i]));

            return max;
        }

        public static double Similarity(LabLevel a, LabLevel b)
        {
            if (a.Obstacles.Length != b.Obstacles.Length) return 0;

            int inter = 0, union = 0;

            for (int i = 0; i < a.Obstacles.Length; i++)
            {
                bool ha = a.Obstacles[i] != LabObstacle.None;
                bool hb = b.Obstacles[i] != LabObstacle.None;

                if (ha || hb) union++;
                if (ha && hb && a.Obstacles[i] == b.Obstacles[i]) inter++;
            }

            return union == 0 ? 0 : inter / (double)union;
        }

        // Engellerin ne kadarı en az 4 karelik bağlı bir şeklin parçası.
        // Düşükse tahta "serpilmiş" görünür - şekil değil gürültü. (İlk sürüm
        // "komşusu olan engel" sayıyordu; ikili parçalar da geçtiği için hep 1'di.)
        public static double Coherence(LabLevel l)
        {
            int n = l.Obstacles.Length;
            int[] comp = new int[n];
            for (int i = 0; i < n; i++) comp[i] = -1;

            int total = 0, inShapes = 0;
            Stack<int> stack = new Stack<int>();
            List<int> members = new List<int>();

            for (int start = 0; start < n; start++)
            {
                if (l.Obstacles[start] == LabObstacle.None || comp[start] >= 0) continue;

                members.Clear();
                stack.Push(start);
                comp[start] = start;

                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    members.Add(c);

                    int x = c % l.Width, y = c / l.Width;

                    if (x > 0) Visit(l, comp, stack, c - 1, start);
                    if (x + 1 < l.Width) Visit(l, comp, stack, c + 1, start);
                    if (y > 0) Visit(l, comp, stack, c - l.Width, start);
                    if (y + 1 < l.Height) Visit(l, comp, stack, c + l.Width, start);
                }

                total += members.Count;
                if (members.Count >= 4) inShapes += members.Count;
            }

            return total == 0 ? 0 : inShapes / (double)total;
        }

        static void Visit(LabLevel l, int[] comp, Stack<int> stack, int i, int id)
        {
            if (l.Obstacles[i] == LabObstacle.None || comp[i] >= 0) return;

            comp[i] = id;
            stack.Push(i);
        }
    }
}
