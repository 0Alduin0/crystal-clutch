using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LevelLab;
using UnityEditor;
using UnityEngine;

// Levellerin zorluğunu ölçen pencere. LevelSet'teki her leveli bot yüzlerce
// kez oynuyor; kazanma oranını, kaç hamlede bittiğini ve "şu kazanma oranı
// için kaç hamle vermeli" sorusunun cevabını tablo olarak gösteriyor.
//
// Açmak için: üst menü -> Match3 -> Level Analizi
//
// Simülasyon LevelLab/Core'da: Board.cs kurallarının Unity'siz, animasyonsuz
// kopyası. Arka planda çalışıyor, editörü kilitlemiyor.
// Board.cs'te bir kural değişirse LabBoard da değişmeli.
public class LevelLabWindow : EditorWindow
{
    // Sabit tohum: aynı level aynı ayarla her seferinde aynı sonucu versin.
    // Yoksa bir değişikliğin etkisi zar gürültüsüne karışır.
    const ulong Seed = 20260925;

    // Tablo renklendirme eşikleri
    const double GenerousWinRate = 0.97;     // bu kadar kazanıyor ve...
    const double GenerousMedianRatio = 0.5;  // ...hamlenin yarısını bile kullanmıyorsa "cömert"
    const double HardWinRate = 0.3;

    static readonly Color GenerousTint = new Color(0.3f, 0.6f, 1f, 0.12f);
    static readonly Color HardTint = new Color(1f, 0.55f, 0.1f, 0.15f);
    static readonly Color StuckTint = new Color(1f, 0.2f, 0.2f, 0.25f);
    static readonly Color BarBack = new Color(0.5f, 0.5f, 0.5f, 0.35f);
    static readonly Color BarFront = new Color(0.35f, 0.75f, 0.45f, 1f);

    LevelSet levelSet;
    int runs = 300;
    BotSkill skill = BotSkill.Ortalama;

    // --- Çalışma durumu ---
    // Arka plan iş parçacığı results'a ve doneRuns'a yazıyor, ana iş parçacığı
    // okuyor. results'ın her elemanına tek yazan var, kilide gerek yok.
    Task task;
    CancellationTokenSource cancel;
    int totalRuns;
    int doneRuns;
    LevelStats[] results;
    LevelData[] analyzed;
    BotSkill analyzedSkill;
    int analyzedRuns;
    string error;

    Vector2 scroll;

    bool Running => task != null;

    [MenuItem("Match3/Level Analizi")]
    static void Open()
    {
        GetWindow<LevelLabWindow>("Level Analizi");
    }

    void OnEnable()
    {
        if (levelSet == null) levelSet = FindLevelSet();

        // Script derlenirken domain yeniden yükleniyor; arka plandaki iş
        // yarıda ölür. Temiz kapansın.
        AssemblyReloadEvents.beforeAssemblyReload += Cancel;
    }

    void OnDisable()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
        Cancel();
    }

    // Editör pencereleri saniyede birkaç kez Update alıyor - ilerleme çubuğu
    // ve bitiş kontrolü burada
    void Update()
    {
        if (task == null) return;

        Repaint();

        if (!task.IsCompleted) return;

        // İptal bir hata değil
        if (task.IsFaulted && task.Exception != null)
        {
            Exception e = task.Exception.GetBaseException();

            if (!(e is OperationCanceledException))
            {
                error = e.Message;
                Debug.LogException(e);
            }
        }

        task = null;
    }

    static LevelSet FindLevelSet()
    {
        string[] guids = AssetDatabase.FindAssets("t:LevelSet");
        if (guids.Length == 0) return null;

        return AssetDatabase.LoadAssetAtPath<LevelSet>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    // ==================== ÇİZİM ====================

    void OnGUI()
    {
        EditorGUILayout.Space(4);

        using (new EditorGUI.DisabledScope(Running))
        {
            levelSet = (LevelSet)EditorGUILayout.ObjectField("Level Set", levelSet, typeof(LevelSet), false);

            runs = EditorGUILayout.IntSlider(
                new GUIContent("Level başına oyun", "Fazlası daha kesin ama daha yavaş. 300 çoğu iş için yeterli."),
                runs, 50, 1000);

            skill = (BotSkill)EditorGUILayout.EnumPopup(
                new GUIContent("Bot", "Ortalama: zorluk ölçümü için olan. Usta ve Acemi aralığı görmek için. " +
                                      "Rastgele: hiç düşünmeden oynasa bile geçiliyor mu?"),
                skill);
        }

        EditorGUILayout.Space(4);

        DrawButtons();

        if (!string.IsNullOrEmpty(error))
            EditorGUILayout.HelpBox(error, MessageType.Error);

        DrawResults();
    }

    void DrawButtons()
    {
        if (Running)
        {
            float progress = totalRuns > 0 ? Volatile.Read(ref doneRuns) / (float)totalRuns : 0f;

            Rect bar = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(bar, progress, $"Oynanıyor... {progress * 100:0}%");

            if (GUILayout.Button("İptal")) Cancel();
            return;
        }

        EditorGUILayout.BeginHorizontal();

        using (new EditorGUI.DisabledScope(levelSet == null || levelSet.Count == 0))
        {
            if (GUILayout.Button("Analiz Et", GUILayout.Height(26))) StartAnalysis();
        }

        using (new EditorGUI.DisabledScope(!HasResults()))
        {
            if (GUILayout.Button("Raporu Kaydet", GUILayout.Height(26), GUILayout.Width(120))) SaveReport();
        }

        // Eğri "Ortalama" bota göre tanımlı - başka botla yapılan analizle ayarlamak yanlış olur
        using (new EditorGUI.DisabledScope(!HasResults() || analyzedSkill != BotSkill.Ortalama))
        {
            if (GUILayout.Button(new GUIContent("Hamleleri eğriye ayarla",
                    "Her levelin hamlesini, o slotun hedef kazanma oranına (LevelPlan) en yakın " +
                    "değere çeker. Önce neyin değişeceğini gösterir, onay ister."),
                    GUILayout.Height(26), GUILayout.Width(170)))
                FitMovesToCurve();
        }

        EditorGUILayout.EndHorizontal();
    }

    bool HasResults()
    {
        if (results == null) return false;

        foreach (LevelStats s in results)
            if (s != null) return true;

        return false;
    }

    // Sütun genişlikleri - başlık ve satırlar aynı diziden okuyor
    static readonly (string title, float width, string tip)[] Columns =
    {
        ("#", 28, ""),
        ("Level", 90, "Tıkla: asset'i seç"),
        ("Engel", 64, "Engel sayısı, parantez içinde hepsini kırmak için gereken toplam vuruş"),
        ("Renk", 38, "Oyunda gerçekten kullanılan kristal çeşidi (prefab sayısıyla kırpılmış)"),
        ("Hamle", 48, "Levelin şu anki maxMoves değeri"),
        ("Kazanma", 60, "Şu anki hamle sayısıyla kazanma oranı"),
        ("+5 ile", 52, "Kaybedince altınla +5 hamle alsaydı"),
        ("Kıl payı", 58, "Kaybedenlerin kaçı 3 ya da daha az engel kala kaybetti - +5 teklifini cazip yapan şey"),
        ("Medyan", 52, "Kazanmak için harcanan hamlenin ortancası (hamle sınırı olmadan)"),
        ("%90", 40, "%90 kazanma için gereken hamle"),
        ("%70", 40, "%70 kazanma için gereken hamle"),
        ("%50", 40, "%50 kazanma için gereken hamle"),
        ("Takılma", 56, "Tahta yenilendikten sonra da hamle kalmayıp kilitlenen oyunlar. 0 olmalı."),
        ("Ölü", 36, "Hamle sonrası ortalama boş kare - katı blokların altındaki ulaşılamayan cepler"),
        ("Medyan / Hamle", 140, "Gri: verilen hamle. Yeşil: medyan. Yeşil grinin çok altındaysa hamle cömert."),
    };

    void DrawResults()
    {
        if (!HasResults()) return;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField($"Bot: {analyzedSkill}, level başına {analyzedRuns} oyun",
                                   EditorStyles.miniLabel);

        DrawSummary();
        DrawLegend();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.BeginHorizontal();
        foreach (var c in Columns)
            GUILayout.Label(new GUIContent(c.title, c.tip), EditorStyles.boldLabel, GUILayout.Width(c.width));
        EditorGUILayout.EndHorizontal();

        int barScale = 1;
        foreach (LevelStats s in results)
            if (s != null) barScale = Math.Max(barScale, Math.Max(s.MaxMoves, Math.Max(0, s.Percentile(0.5))));

        for (int i = 0; i < results.Length; i++)
            if (results[i] != null) DrawRow(i, results[i], barScale);

        EditorGUILayout.EndScrollView();
    }

    void DrawRow(int i, LevelStats s, int barScale)
    {
        Rect row = EditorGUILayout.BeginHorizontal();

        Color tint = RowTint(s);
        if (tint.a > 0f) EditorGUI.DrawRect(row, tint);

        int median = s.Percentile(0.5);
        int col = 0;

        Cell(ref col, (i + 1).ToString());

        if (GUILayout.Button(s.Name, EditorStyles.linkLabel, GUILayout.Width(Columns[col++].width)) &&
            analyzed[i] != null)
        {
            Selection.activeObject = analyzed[i];
            EditorGUIUtility.PingObject(analyzed[i]);
        }

        Cell(ref col, $"{s.Obstacles} ({s.HitPoints})");
        Cell(ref col, s.Variety.ToString());
        Cell(ref col, s.MaxMoves.ToString());
        Cell(ref col, LabReport.Percent(s.WinAtMax));
        Cell(ref col, LabReport.Percent(s.WinWithExtra(LabReport.ContinueMoves)));
        Cell(ref col, LabReport.Percent(s.NearMissRate(LabReport.NearMissThreshold)));
        Cell(ref col, Moves(median));
        Cell(ref col, Moves(s.MovesFor(0.9)));
        Cell(ref col, Moves(s.MovesFor(0.7)));
        Cell(ref col, Moves(s.MovesFor(0.5)));
        Cell(ref col, LabReport.Percent(s.StuckRate));
        Cell(ref col, s.AvgEmptyCells.ToString("0.0"));

        // Medyan / hamle çubuğu: ikisi aynı ölçekte
        Rect bar = GUILayoutUtility.GetRect(Columns[col].width, 16, GUILayout.Width(Columns[col].width));
        bar.y += 2;
        bar.height -= 4;

        EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * s.MaxMoves / barScale, bar.height), BarBack);

        if (median > 0)
            EditorGUI.DrawRect(new Rect(bar.x, bar.y + 3, bar.width * median / barScale, bar.height - 6), BarFront);

        EditorGUILayout.EndHorizontal();
    }

    static void Cell(ref int col, string text)
    {
        GUILayout.Label(text, GUILayout.Width(Columns[col++].width));
    }

    static string Moves(int m) => m < 0 ? "-" : m.ToString();

    static Color RowTint(LevelStats s)
    {
        if (s.StuckRate > 0) return StuckTint;
        if (s.WinAtMax < HardWinRate) return HardTint;

        int median = s.Percentile(0.5);

        if (s.WinAtMax >= GenerousWinRate && median > 0 && median <= s.MaxMoves * GenerousMedianRatio)
            return GenerousTint;

        return Color.clear;
    }

    void DrawSummary()
    {
        int count = 0, generous = 0, hard = 0, stuck = 0;

        foreach (LevelStats s in results)
        {
            if (s == null) continue;
            count++;

            Color tint = RowTint(s);
            if (tint == StuckTint) stuck++;
            else if (tint == HardTint) hard++;
            else if (tint == GenerousTint) generous++;
        }

        string text = $"{count} levelin {generous} tanesinde hamle cömert (neredeyse hep kazanılıyor, hamlenin yarısı bile kullanılmıyor). " +
                      $"{hard} tanesi çok zor (kazanma < %{HardWinRate * 100:0}).";

        if (stuck > 0) text += $" {stuck} levelde oyuncu kilitlenebiliyor - tasarımı düzelt.";

        EditorGUILayout.HelpBox(text, stuck > 0 ? MessageType.Warning : MessageType.Info);
    }

    static void DrawLegend()
    {
        EditorGUILayout.BeginHorizontal();
        LegendItem(GenerousTint, "Hamle cömert");
        LegendItem(HardTint, "Çok zor");
        LegendItem(StuckTint, "Kilitleniyor");
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    static void LegendItem(Color color, string label)
    {
        Rect r = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
        r.y += 3;
        EditorGUI.DrawRect(r, new Color(color.r, color.g, color.b, Mathf.Max(color.a * 3f, 0.5f)));
        GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(90));
    }

    // ==================== ANALİZ ====================

    void StartAnalysis()
    {
        error = null;

        // LevelData okumak Unity API'si: ana iş parçacığında, arka plana
        // geçmeden önce düz veriye çeviriyoruz.
        int crystalKinds = Enum.GetValues(typeof(CrystalType)).Length;

        int count = levelSet.Count;
        LabLevel[] levels = new LabLevel[count];
        analyzed = new LevelData[count];

        for (int i = 0; i < count; i++)
        {
            LevelData d = levelSet.Get(i);
            analyzed[i] = d;

            if (d == null) continue;

            levels[i] = ToLab(d, crystalKinds);
        }

        results = new LevelStats[count];
        analyzedSkill = skill;
        analyzedRuns = runs;

        totalRuns = count * runs;
        doneRuns = 0;

        cancel = new CancellationTokenSource();
        CancellationToken token = cancel.Token;

        int runsPerLevel = runs;
        BotSkill botSkill = skill;
        int threads = Math.Max(1, Environment.ProcessorCount - 1);

        task = Task.Run(() =>
        {
            for (int i = 0; i < levels.Length; i++)
            {
                token.ThrowIfCancellationRequested();

                if (levels[i] == null)
                {
                    Interlocked.Add(ref doneRuns, runsPerLevel);
                    continue;
                }

                results[i] = LevelAnalyzer.Analyze(levels[i], runsPerLevel, botSkill, Seed, token,
                                                   () => Interlocked.Increment(ref doneRuns), threads);
            }
        }, token);
    }

    // LevelData'nın simülasyonun anladığı hâli. Board'un okuduğu her alan burada olmalı.
    public static LabLevel ToLab(LevelData d, int crystalKinds)
    {
        LabLevel l = LabLevel.FromRows(d.name, d.width, d.height, d.obstacleRows);

        l.Variety = LabLevel.EffectiveVariety(d.crystalVariety, crystalKinds);
        l.MaxMoves = d.maxMoves;
        l.TransparentHits = d.transparentHits;
        l.SolidHits = d.solidHits;
        l.BlockHits = d.blockHits;
        l.RefillLuck = d.refillLuck;

        return l;
    }

    void Cancel()
    {
        if (cancel == null) return;

        cancel.Cancel();
        cancel = null;
    }

    // ==================== HAMLE AYARI ====================

    // Analiz sonucundan her level için hedefe en yakın hamleyi bul, listeyi
    // göster, onay gelirse yaz. Onaysız hiçbir asset'e dokunmuyor.
    void FitMovesToCurve()
    {
        List<(LevelData data, int from, int to)> changes = new List<(LevelData, int, int)>();
        List<string> lines = new List<string>();
        int unreachable = 0;

        for (int i = 0; i < results.Length; i++)
        {
            LevelStats s = results[i];
            LevelData d = analyzed[i];
            if (s == null || d == null) continue;

            LevelSpec spec = LevelPlan.For(i + 1);
            int moves = s.MovesClosest(spec.TargetWinRate);

            if (moves < 0) { unreachable++; continue; }
            if (moves == d.maxMoves) continue;

            changes.Add((d, d.maxMoves, moves));
            lines.Add($"Level {i + 1}: {d.maxMoves} -> {moves} (hedef %{spec.TargetWinRate * 100:0})");
        }

        if (changes.Count == 0)
        {
            EditorUtility.DisplayDialog("Hamleleri eğriye ayarla",
                "Bütün leveller zaten hedefe en yakın hamlede." +
                (unreachable > 0 ? $"\n\n{unreachable} level hedef orana hiç ulaşamıyor - onlara dokunulmadı." : ""),
                "Tamam");
            return;
        }

        // Diyalog uzun listeyi taşıyamıyor - ilk 25 satır yeter, fikir versin
        string preview = string.Join("\n", lines.GetRange(0, Math.Min(25, lines.Count)));
        if (lines.Count > 25) preview += $"\n... ve {lines.Count - 25} level daha";
        if (unreachable > 0) preview += $"\n\n{unreachable} level hedef orana ulaşamıyor, dokunulmayacak.";

        if (!EditorUtility.DisplayDialog("Hamleleri eğriye ayarla",
                $"{changes.Count} levelin hamlesi değişecek:\n\n{preview}", "Uygula", "Vazgeç"))
            return;

        foreach (var c in changes)
        {
            Undo.RecordObject(c.data, "Hamleleri eğriye ayarla");
            c.data.maxMoves = c.to;
            EditorUtility.SetDirty(c.data);
        }

        AssetDatabase.SaveAssets();

        // Tablodaki sayılar eski hamleye göre - yeniden analiz edilsin
        results = null;
    }

    // ==================== RAPOR ====================

    // Proje kökündeki LevelLab klasörüne (Assets'in DIŞINA - Unity import etmesin)
    void SaveReport()
    {
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "LevelLab");
        Directory.CreateDirectory(dir);

        List<LevelStats> list = new List<LevelStats>();
        foreach (LevelStats s in results)
            if (s != null) list.Add(s);

        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        string md = Path.Combine(dir, $"rapor_{stamp}.md");
        string csv = Path.Combine(dir, $"rapor_{stamp}.csv");

        File.WriteAllText(md, LabReport.ToMarkdown(list));
        File.WriteAllText(csv, LabReport.ToCsv(list));

        EditorUtility.RevealInFinder(md);
    }
}
