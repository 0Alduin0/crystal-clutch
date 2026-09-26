using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LevelLab;
using UnityEditor;
using UnityEngine;

// Level üretici. İki kullanım:
//
//   TEK SLOT - Bir level numarası seç, "Aday Üret". Üretici o slotun
//   hedefine (LevelPlan) uyan adayları botla ölçüp en iyi 6'sını gösterir.
//   Beğendiğine "Kullan" de: asset oluşur, LevelSet'e girer.
//
//   TOPLU - Bir aralık ver (ör. 21-100). Her slot için en iyi adayı kendisi
//   seçip yazar. Sonra Level Analizi'nde gözden geçirip beğenmediğini tek
//   slot modunda yeniden üretirsin.
//
// Açmak için: üst menü -> Match3 -> Level Üretici
//
// Zorluk eğrisi, mekanik takvimi ve "3 hamlede bitmesin" kuralı LevelPlan'da.
public class LevelGeneratorWindow : EditorWindow
{
    const ulong Seed = 20260925;
    const int ShownCandidates = 6;
    const float Cell = 13f;

    static readonly Color NoneColor = new Color(0.22f, 0.22f, 0.25f);
    static readonly Color TransparentColor = new Color(0.55f, 0.85f, 1f);
    static readonly Color SolidColor = new Color(0.4f, 0.75f, 0.3f);
    static readonly Color LavaColor = new Color(1f, 0.45f, 0.15f);
    static readonly Color IceColor = new Color(0.8f, 0.97f, 1f);

    LevelSet levelSet;

    // --- Tek slot ---
    int slot = 21;
    List<GeneratedLevel> candidates;
    string candidateSummary;
    int candidateSlot;

    // --- Toplu ---
    int rangeFrom = 21;
    int rangeTo = 100;
    bool overwrite;

    // --- Çalışma durumu ---
    // Arka plan iş parçacığı üretir, ana iş parçacığı asset yazar (AssetDatabase
    // sadece ana iş parçacığından çağrılabilir).
    Task task;
    CancellationTokenSource cancel;
    volatile string status = "";
    readonly ConcurrentQueue<GeneratedLevel> toWrite = new ConcurrentQueue<GeneratedLevel>();
    readonly List<string> batchLog = new List<string>();
    int batchTotal;
    int batchDone;
    string error;

    Vector2 scroll;

    bool Running => task != null;

    [MenuItem("Match3/Level Üretici")]
    static void Open()
    {
        GetWindow<LevelGeneratorWindow>("Level Üretici");
    }

    void OnEnable()
    {
        if (levelSet == null)
        {
            string[] guids = AssetDatabase.FindAssets("t:LevelSet");
            if (guids.Length > 0)
                levelSet = AssetDatabase.LoadAssetAtPath<LevelSet>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        if (levelSet != null) slot = Mathf.Max(1, levelSet.Count + 1);

        AssemblyReloadEvents.beforeAssemblyReload += Cancel;
    }

    void OnDisable()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
        Cancel();
    }

    void Update()
    {
        // Toplu üretimin bitirdiği slotları ana iş parçacığında yaz
        while (toWrite.TryDequeue(out GeneratedLevel g))
        {
            try
            {
                LevelAssetWriter.Write(levelSet, g.Spec.Index, g);
                batchLog.Add($"Level {g.Spec.Index}: {g.Level.MaxMoves} hamle, can {g.Level.TotalHitPoints} - {g.Recipe}");
            }
            catch (Exception e)
            {
                batchLog.Add($"Level {g.Spec.Index}: YAZILAMADI - {e.Message}");
            }

            batchDone++;
        }

        if (task == null) return;

        Repaint();

        if (!task.IsCompleted) return;

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
        status = "";
    }

    // ==================== ÇİZİM ====================

    void OnGUI()
    {
        EditorGUILayout.Space(4);

        using (new EditorGUI.DisabledScope(Running))
            levelSet = (LevelSet)EditorGUILayout.ObjectField("Level Set", levelSet, typeof(LevelSet), false);

        if (levelSet == null)
        {
            EditorGUILayout.HelpBox("Level Set seç.", MessageType.Info);
            return;
        }

        if (!string.IsNullOrEmpty(error))
            EditorGUILayout.HelpBox(error, MessageType.Error);

        if (Running)
        {
            Rect bar = EditorGUILayout.GetControlRect(false, 20);
            float p = batchTotal > 0 ? batchDone / (float)batchTotal : 0f;
            EditorGUI.ProgressBar(bar, p, string.IsNullOrEmpty(status) ? "Çalışıyor..." : status);

            if (GUILayout.Button("İptal")) Cancel();
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawSingle();
        EditorGUILayout.Space(16);
        DrawBatch();

        EditorGUILayout.EndScrollView();
    }

    void DrawSingle()
    {
        EditorGUILayout.LabelField("Tek Slot", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(Running))
        {
            EditorGUILayout.BeginHorizontal();

            slot = Mathf.Clamp(EditorGUILayout.IntField("Level", slot), 1, levelSet.Count + 1);

            if (GUILayout.Button("Aday Üret", GUILayout.Width(100))) StartSingle();

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.LabelField(LevelPlan.For(slot).Describe(), EditorStyles.miniLabel);

        if (slot <= levelSet.Count)
            EditorGUILayout.HelpBox($"Level {slot} dolu. Seçtiğin aday onun yerine geçer; eski asset silinmez.",
                                    MessageType.None);

        if (candidates == null) return;

        if (!string.IsNullOrEmpty(candidateSummary))
            EditorGUILayout.HelpBox(candidateSummary, candidates.Count == 0 ? MessageType.Warning : MessageType.Info);

        int shown = Math.Min(ShownCandidates, candidates.Count);

        EditorGUILayout.BeginHorizontal();

        for (int i = 0; i < shown; i++)
        {
            GeneratedLevel g = candidates[i];

            EditorGUILayout.BeginVertical(GUILayout.Width(Cell * 8 + 12));

            DrawBoard(g.Level, g.Spec.BlockTheme);

            GUILayout.Label($"{g.Level.MaxMoves} hamle, can {g.Level.TotalHitPoints}", EditorStyles.miniLabel);
            GUILayout.Label($"kıl payı %{g.NearMiss * 100:0}, yeni {g.Novelty:0.00}", EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(Running || candidateSlot > levelSet.Count + 1))
            {
                if (GUILayout.Button("Kullan"))
                {
                    LevelData data = LevelAssetWriter.Write(levelSet, candidateSlot, g);
                    EditorGUIUtility.PingObject(data);

                    candidates = null;
                    slot = Mathf.Max(slot, Mathf.Min(levelSet.Count + 1, candidateSlot + 1));
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndHorizontal();
    }

    void DrawBatch()
    {
        EditorGUILayout.LabelField("Toplu Üretim", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(Running))
        {
            EditorGUILayout.BeginHorizontal();
            rangeFrom = Mathf.Max(1, EditorGUILayout.IntField("Başlangıç", rangeFrom));
            rangeTo = Mathf.Max(rangeFrom, EditorGUILayout.IntField("Bitiş", rangeTo));
            EditorGUILayout.EndHorizontal();

            overwrite = EditorGUILayout.Toggle(
                new GUIContent("Dolu slotların yerine yaz", "Kapalıyken sadece LevelSet'in sonuna eklenir. " +
                                                           "Açıkken aralıktaki dolu slotlar da yeniden üretilir " +
                                                           "(eski asset'ler silinmez)."),
                overwrite);

            int first = overwrite ? rangeFrom : Math.Max(rangeFrom, levelSet.Count + 1);
            bool valid = first <= rangeTo && first <= levelSet.Count + 1;

            EditorGUILayout.LabelField(valid
                ? $"Üretilecek: {first}-{rangeTo} ({rangeTo - first + 1} level). Slot başına birkaç saniye."
                : $"Aralık LevelSet'in sonundan ({levelSet.Count + 1}) başlamalı ya da 'yerine yaz' açık olmalı.",
                EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("Toplu Üret", GUILayout.Height(24))) StartBatch(first, rangeTo);
            }
        }

        foreach (string line in batchLog)
            EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
    }

    // 8x8 tahta önizlemesi: üst sıra üstte
    static void DrawBoard(LabLevel l, int theme)
    {
        Rect area = GUILayoutUtility.GetRect(Cell * l.Width + 2, Cell * l.Height + 2,
                                             GUILayout.Width(Cell * l.Width + 2));

        for (int y = 0; y < l.Height; y++)
            for (int x = 0; x < l.Width; x++)
            {
                Rect r = new Rect(area.x + 1 + x * Cell, area.y + 1 + (l.Height - 1 - y) * Cell, Cell - 1, Cell - 1);
                EditorGUI.DrawRect(r, ColorFor(l.Obstacles[y * l.Width + x], theme));
            }
    }

    static Color ColorFor(LabObstacle o, int theme)
    {
        switch (o)
        {
            case LabObstacle.Transparent: return TransparentColor;
            case LabObstacle.Solid: return SolidColor;
            case LabObstacle.Block: return theme == 1 ? IceColor : LavaColor;
        }

        return NoneColor;
    }

    // ==================== ÇALIŞTIRMA ====================

    // Slottan önceki leveller - benzerlik karşılaştırması için. LevelData okumak
    // Unity API'si, arka plana geçmeden düz veriye çeviriyoruz.
    List<LabLevel> PreviousLevels(int upToExclusive)
    {
        int kinds = Enum.GetValues(typeof(CrystalType)).Length;
        List<LabLevel> list = new List<LabLevel>();

        for (int i = 0; i < upToExclusive - 1 && i < levelSet.Count; i++)
        {
            LevelData d = levelSet.Get(i);
            if (d != null) list.Add(LevelLabWindow.ToLab(d, kinds));
        }

        return list;
    }

    void StartSingle()
    {
        error = null;
        candidates = null;
        candidateSlot = slot;

        LevelSpec spec = LevelPlan.For(slot);
        List<LabLevel> previous = PreviousLevels(slot);

        cancel = new CancellationTokenSource();
        CancellationToken token = cancel.Token;

        batchTotal = 0;
        batchDone = 0;

        LevelGenerator generator = NewGenerator();

        task = Task.Run(() =>
        {
            List<GeneratedLevel> result = generator.Generate(spec, previous, Seed ^ (ulong)DateTime.Now.Ticks,
                                                             token, s => status = s);

            candidateSummary = Summary(generator, result);
            candidates = result;
        }, token);
    }

    void StartBatch(int first, int last)
    {
        error = null;
        batchLog.Clear();

        List<LabLevel> previous = PreviousLevels(first);

        cancel = new CancellationTokenSource();
        CancellationToken token = cancel.Token;

        batchTotal = last - first + 1;
        batchDone = 0;

        LevelGenerator generator = NewGenerator();

        task = Task.Run(() =>
        {
            for (int index = first; index <= last; index++)
            {
                token.ThrowIfCancellationRequested();

                LevelSpec spec = LevelPlan.For(index);
                List<GeneratedLevel> result = generator.Generate(spec, previous, Seed, token,
                                                                 s => status = s);

                if (result.Count == 0)
                {
                    // Sıradaki slotlar bu slota bağlı (LevelSet'e boşluk bırakılamaz)
                    status = $"Level {index} için aday bulunamadı - durdu. {Summary(generator, result)}";
                    throw new InvalidOperationException(status);
                }

                previous.Add(result[0].Level);
                toWrite.Enqueue(result[0]);

                // Yazılmadan sıradakine geçme: slot numaraları ardışık olmalı
                while (!toWrite.IsEmpty) Thread.Sleep(50);
            }
        }, token);
    }

    LevelGenerator NewGenerator()
    {
        return new LevelGenerator { MaxThreads = Math.Max(1, Environment.ProcessorCount - 1) };
    }

    static string Summary(LevelGenerator g, List<GeneratedLevel> result)
    {
        Dictionary<string, int> reasons = new Dictionary<string, int>();

        foreach (GeneratedLevel c in g.LastCandidates)
        {
            if (c.Rejection == null || c.Rejection.StartsWith("ölçülmedi")) continue;

            // "fazla kolay (9 hamle)" gibi ayrıntıları grupla
            string key = c.Rejection;
            int paren = key.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) key = key.Substring(0, paren);

            reasons.TryGetValue(key, out int n);
            reasons[key] = n + 1;
        }

        string text = $"{g.LastCandidates.Count} aday denendi, {result.Count} tanesi kabul edildi.";

        foreach (var pair in reasons)
            text += $"\n  {pair.Value} x {pair.Key}";

        return text;
    }

    void Cancel()
    {
        if (cancel == null) return;

        cancel.Cancel();
        cancel = null;
    }
}
