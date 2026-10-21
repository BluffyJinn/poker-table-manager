using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TableManager;

/// <summary>Проверки геометрии и ячеек без окон. Код выхода 0 — всё прошло.</summary>
public static class LayoutTests
{
    private static int _failed;
    private static int _passed;

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            _passed++;
        }
        else
        {
            _failed++;
            Console.WriteLine("FAIL: " + name);
        }
    }

    private static int _nextHandle = 1000;

    private static TableWindow Win(string process, string cls, string title, int w = 1200, int h = 900, bool minimized = false)
    {
        return new TableWindow
        {
            Handle = new IntPtr(_nextHandle++),
            Process = process,
            ClassName = cls,
            Title = title,
            Frame = new RECT { Left = 0, Top = 0, Right = w, Bottom = h },
            Minimized = minimized,
            Area = (long)w * h,
        };
    }

    /// <summary>Правило из «Указать стол» берёт указанный стол и не берёт остальные окна.</summary>
    private static RoomRule Picked(TableWindow table, List<TableWindow> siblings, string name)
    {
        string description;
        var rule = RuleBuilder.ForTable(table, siblings, out description);
        Check(rule != null, name + ": правило построено");
        if (rule == null) return null;
        var compiled = CompiledRule.From(new[] { rule });
        var all = new List<TableWindow>(siblings);
        all.Add(table);
        var tables = Desktop.Classify(all, compiled, null, null, null);
        Check(tables.Count == 1 && tables[0].Handle == table.Handle, name + ": стол берётся, остальные окна — нет");
        return rule;
    }

    private static bool Takes(RoomRule rule, TableWindow w)
    {
        return Desktop.Classify(new List<TableWindow> { w }, CompiledRule.From(new[] { rule }), null, null, null).Count == 1;
    }

    public static int Main()
    {
        // Сетка 4×3 на рабочей области 4K (без панели задач) покрывает её целиком.
        var work = new Box(0, 0, 3840, 2088);
        var cells = Layout.Cells(work, 4, 3, 0);
        Check(cells.Count == 12, "12 ячеек");
        Check(cells[0].X == 0 && cells[0].Y == 0 && cells[0].W == 960 && cells[0].H == 696, "первая ячейка 960×696");
        Check(cells[11].Right == 3840 && cells[11].Bottom == 2088, "последняя ячейка у края");
        var cover = 0L;
        foreach (var c in cells) cover += (long)c.W * c.H;
        Check(cover == 3840L * 2088, "ячейки без дыр и нахлёстов");

        // Второй монитор слева с отрицательными координатами.
        var left = Layout.Cells(new Box(-3440, 0, 3440, 1392), 4, 2, 0);
        Check(left[0].X == -3440 && left[3].Right == 0, "монитор слева");
        Check(left[0].W == 860 && left[0].H == 696, "ячейка ультраширокого 860×696");

        // Зазор между ячейками, но не у краёв области.
        var gapped = Layout.Cells(new Box(0, 0, 1000, 500), 2, 1, 10);
        Check(gapped[0].X == 0 && gapped[0].W == 495 && gapped[1].X == 505 && gapped[1].Right == 1000, "зазор 10px");

        // Стол 4:3 в ячейке 960×696 — по высоте, по центру.
        var fit = Layout.Fit(cells[0], 4.0 / 3.0, "center");
        Check(fit.H == 696 && fit.W == 928 && fit.X == 16 && fit.Y == 0, "вписан 928×696 по центру");
        var topLeft = Layout.Fit(cells[0], 4.0 / 3.0, "topleft");
        Check(topLeft.X == 0 && topLeft.Y == 0, "в углу ячейки");
        // Широкий стол упирается в ширину.
        var wide = Layout.Fit(new Box(0, 0, 800, 800), 2.0, "center");
        Check(wide.W == 800 && wide.H == 400 && wide.Y == 200, "широкий стол по ширине");
        Check(Layout.Fit(new Box(0, 0, 400, 300), 0, "center").W == 400, "плохая пропорция → 4:3");

        // Постоянные ячейки: новый стол — в первую свободную, закрытый освобождает свою.
        var tracker = new SlotTracker();
        var a = new IntPtr(1);
        var b = new IntPtr(2);
        var c3 = new IntPtr(3);
        Check(tracker.AssignFirstFree(a, 3) == 0, "a → 0");
        Check(tracker.AssignFirstFree(b, 3) == 1, "b → 1");
        Check(tracker.AssignFirstFree(a, 3) == 0, "повторный запрос — та же ячейка");
        tracker.Prune(new HashSet<IntPtr> { b }, 3);
        Check(tracker.AssignFirstFree(c3, 3) == 0, "c занимает освободившуюся 0");
        Check(tracker.AssignFirstFree(new IntPtr(4), 3) == 2, "следующий → 2");
        Check(tracker.AssignFirstFree(new IntPtr(5), 3) == -1, "мест нет → -1");
        tracker.Prune(new HashSet<IntPtr> { b, c3 }, 1);
        int slot;
        Check(!tracker.TryGet(b, out slot) && tracker.TryGet(c3, out slot) && slot == 0, "ячейки сверх числа забываются");

        // Общая расстановка: каждое окно — в ближайшую ячейку.
        var slots = Layout.Cells(new Box(0, 0, 2000, 1000), 2, 2, 0);
        var windows = new List<KeyValuePair<IntPtr, Box>>
        {
            new KeyValuePair<IntPtr, Box>(new IntPtr(10), new Box(1100, 600, 800, 300)), // внизу справа
            new KeyValuePair<IntPtr, Box>(new IntPtr(11), new Box(50, 40, 800, 300)),    // вверху слева
            new KeyValuePair<IntPtr, Box>(new IntPtr(12), new Box(60, 50, 800, 300)),    // тоже вверху слева
        };
        var nearest = new SlotTracker();
        nearest.AssignNearest(windows, slots);
        int s10, s11, s12;
        nearest.TryGet(new IntPtr(10), out s10);
        nearest.TryGet(new IntPtr(11), out s11);
        nearest.TryGet(new IntPtr(12), out s12);
        Check(s10 == 3, "окно справа внизу → ячейка 4");
        Check(s11 == 0 || s12 == 0, "одно из левых → ячейка 1");
        Check(s11 != s12, "два окна не в одной ячейке");

        // Настройки по умолчанию: румы и зоны, JSON туда и обратно.
        var config = Config.Default();
        Check(config.Rooms.Count >= 7 && config.Levels.Count == 2, "настройки по умолчанию");
        var path = System.IO.Path.GetTempFileName();
        Config.Save(path, config);
        var text = System.IO.File.ReadAllText(path);
        Check(text.Contains("Стол") && !text.Contains("\\u04") && text.Contains("\n"), "кириллица и отступы в config.json");
        var loaded = Config.Load(path);
        Check(loaded.Rooms.Count == config.Rooms.Count && loaded.Rooms[1].Table == config.Rooms[1].Table, "чтение config.json");
        System.IO.File.Delete(path);

        // Порядок заполнения: ультраширокий справа налево по столбцам.
        var uw = Layout.Cells(new Box(-3440, 0, 3440, 1392), 4, 2, 0);
        var rtl = Layout.Ordered(uw, 4, 2, "columns-rtl");
        Check(rtl[0].X == uw[3].X && rtl[0].Y == 0, "первая — верхняя правая");
        Check(rtl[1].X == uw[3].X && rtl[1].Y == uw[7].Y, "вторая — нижняя правая");
        Check(rtl[2].X == uw[2].X && rtl[2].Y == 0, "третья — верхняя в следующем столбце");
        var rowsRtl = Layout.Ordered(uw, 4, 2, "rows-rtl");
        Check(rowsRtl[1].X == uw[2].X && rowsRtl[1].Y == 0, "по строкам справа налево");
        Check(Layout.Ordered(uw, 4, 2, null)[1].X == uw[1].X, "по умолчанию — по строкам слева");

        // Уровни: до 14 — 3×2 + 4×2, от 15 — 4×3 + 4×2; обратно — только с запасом.
        var caps = new List<int> { 14, 20 };
        Check(Levels.For(6, caps) == 0 && Levels.For(14, caps) == 0 && Levels.For(15, caps) == 1, "уровень по числу столов");
        Check(Levels.For(25, caps) == 1, "больше всех — последний уровень");
        Check(Levels.Next(0, 15, caps, 2) == 1, "15-й стол — вверх");
        Check(Levels.Next(1, 14, caps, 2) == 1 && Levels.Next(1, 13, caps, 2) == 1, "на границе не прыгает");
        Check(Levels.Next(1, 12, caps, 2) == 0, "вниз при 12");
        Check(Levels.Capacity(14, 30) == 14 && Levels.Capacity(0, 20) == 20 && Levels.Capacity(14, 10) == 10, "вместимость уровня");

        // Смена сетки 3×2 → 4×3: столы ультраширокого остаются, основного — в ближайшие.
        var monitors = new List<MonitorInfo>
        {
            new MonitorInfo { Device = "4K", Primary = true, Bounds = new Box(0, 0, 3840, 2160), Work = new Box(0, 0, 3840, 2088) },
            new MonitorInfo { Device = "UW", Primary = false, Bounds = new Box(-3440, 0, 3440, 1440), Work = new Box(-3440, 0, 3440, 1392) },
        };
        var level0 = Desktop.Slots(config.Levels[0].Zones, 0, monitors);
        var level1 = Desktop.Slots(config.Levels[1].Zones, 0, monitors);
        Check(level0.Count == 14 && level1.Count == 20, "ячейки уровней 14 и 20");
        Check(level0[0].W == 1280 && level0[0].H == 1044, "3×2 на 4K: 1280×1044");
        Check(level0[6].X == -860 && level0[6].Y == 0, "7-й стол — ультраширокий, правый верхний");
        // Основной — по столбцам слева направо, в столбце сверху вниз.
        Check(level0[0].X == 0 && level0[0].Y == 0, "1-й — левый верхний");
        Check(level0[1].X == 0 && level0[1].Y == 1044, "2-й — левый нижний");
        Check(level0[2].X == 1280 && level0[2].Y == 0, "3-й — верх по центру");
        Check(level0[5].X == 2560 && level0[5].Y == 1044, "6-й — правый нижний");
        Check(level0[7].X == -860 && level0[7].Y == 696, "8-й — ультраширокий, правый нижний");
        Check(level0[8].X == -1720 && level0[8].Y == 0, "9-й — следующий столбец влево, верх");
        Check(level1[1].X == 0 && level1[1].Y == 696 && level1[2].Y == 1392 && level1[3].X == 960, "4×3: левый столбец сверху вниз, потом следующий");
        var remap = new SlotTracker();
        var handles = new List<IntPtr>();
        for (var i = 0; i < 14; i++)
        {
            remap.Set(new IntPtr(100 + i), i);
            handles.Add(new IntPtr(100 + i));
        }
        var moved = remap.Remap(level0, level1, handles);
        Check(moved.Count == 6, "переставлены только 6 столов основного");
        var used = new HashSet<int>();
        var allPlaced = true;
        foreach (var h in handles)
        {
            int ns;
            if (!remap.TryGet(h, out ns) || !used.Add(ns)) allPlaced = false;
        }
        Check(allPlaced, "все 14 столов на разных ячейках");
        int firstNew;
        remap.TryGet(new IntPtr(100), out firstNew);
        Check(firstNew == 0, "левый верхний стол остаётся левым верхним");
        int uwNew;
        remap.TryGet(new IntPtr(106), out uwNew);
        Check(level1[uwNew].Near(level0[6], 0), "стол ультраширокого не двигается");
        // Сжатие по порядку: 6 столов основного занимают первые 6 ячеек 4×3
        // (левый столбец сверху вниз, затем второй), 15-й — следующую.
        var packed = true;
        for (var i = 0; i < 6; i++)
        {
            int ns;
            remap.TryGet(new IntPtr(100 + i), out ns);
            if (ns != i) packed = false;
        }
        Check(packed, "столы основного сжаты по порядку в ячейки 1–6");
        Check(remap.AssignFirstFree(new IntPtr(999), level1.Count) == 6, "15-й стол — 7-я ячейка основного (верх 3-го столбца)");
        Check(level1[6].X == 1920 && level1[6].Y == 0, "7-я ячейка 4×3 — верх третьего столбца");

        // Правила румов: столы распознаются, лобби — нет.
        var rules = CompiledRule.From(config.Rooms);
        Func<string, string, string, bool> isWindowTable = (process, cls, title) =>
        {
            foreach (var r in rules) if (r.Matches(process, cls, title)) return true;
            return false;
        };
        Func<string, string, bool> isTable = (process, title) => isWindowTable(process, "Chrome_WidgetWin_1", title);
        // С Jurojin (он дописывает к заголовкам своё) и без него, турниры и кэш.
        Check(isTable("GGnet", "Bounty Hunters Deepstack Turbo $5.40 : Бай-ин $5.40 - Блайнды 500 | 1,000 - Стол 214/!)@(#*$&%^|6550872221"), "GG стол");
        Check(isTable("GGnet", "Bounty Hunters Deepstack Turbo $5.40 : Бай-ин $5.40 - Блайнды 500 | 1,000 - Стол 214"), "GG стол без Jurojin");
        Check(!isTable("GGnet", "ПОКЕРОК"), "GG лобби");
        Check(isTable("Winamax", "Winamax QUANTUM(1186428354)(#0053)"), "Winamax стол");
        Check(isTable("Winamax", "Winamax PLASMA - SPACE KO(1186675162)(#010)"), "Winamax стол с дефисом");
        Check(!isTable("Winamax", "Winamax"), "Winamax лобби");
        Check(isTable("AKs Poker", "$3.30 Bounty King Baby | $500 GTD - Blinds 75/150"), "AKs стол");
        Check(isTable("AKs Poker", "HL8121 - 0.02/0.05/0.10(0.02) - NLHE"), "AKs кэш");
        Check(!isTable("AKs Poker", "22:59 Moscow Standard Time"), "AKs лобби");
        Check(!isTable("AKs Poker", "02:36 Russia TZ 2 Standard Time"), "AKs лобби, другой пояс");
        Check(isWindowTable("CoinPoker", "UnityWndClass", "CoinPoker"), "CoinPoker стол без Jurojin");
        Check(isWindowTable("CoinPoker", "UnityWndClass", "CoinPoker Table | ₮3.30 Micro Clash PKO 2147843 | 2147843"), "CoinPoker стол");
        Check(!isWindowTable("CoinPoker", "Chrome_WidgetWin_1", "CoinPoker"), "CoinPoker лобби");
        Check(isTable("PokerClient", "Late Bounty XL Mini 1181632803 | NL Hold'em | Level 6 | 800/1,600 | Ante 250     v.26.1.1.34"), "Redstar стол");
        Check(isTable("PokerClient", "Late Bounty XL Mini 1181631711 | NL Hold'em , Ante 120     v.26.1.1.34"), "Redstar стол до уровней");
        Check(isTable("PokerClient", "Abades 817033444 | FL Hold'em | €0.02/€0.04     v.26.1.1.34"), "Redstar кэш");
        Check(!isTable("PokerClient", "Redstar Poker"), "Redstar лобби");
        Check(isTable("GameClient", "#6 Maximizer Low Triple Threat - $3.30 NL Hold'em - Break"), "TigerGaming стол");
        Check(isTable("GameClient", "Bomb Pot NLHE $10: 6-Max - $0.05/$0.10, Bomb pot 2-5 BB ~ every 15 hand NL Hold'em - Hand: 1681691702"), "TigerGaming кэш");
        Check(isTable("GameClient", "Григора #3765 - 1 р./2 р., Взрывной банк 2-5 ББ ~ каждые 14 рук БЛ Холдем - Рука: 2889909856"), "Pokerdom кэш");
        Check(!isTable("GameClient", "TigerGaming Lobby | Nickname: x | Balance: $1"), "TigerGaming лобби");
        Check(!isTable("GameClient", "Pokerdom Лобби | Ник: x | Баланс: 1 р."), "Pokerdom лобби");
        Check(isTable("PokerPlanets", " NL Hold'em Blinds: $1/$2"), "PokerPlanets стол");
        Check(!isTable("PokerPlanets", "PokerPlanets - user smokonaj"), "PokerPlanets лобби");
        Check(!isTable("chrome", "Hold'em - Google Chrome"), "браузер не стол");

        var qqDefault = Desktop.Classify(new List<TableWindow>
        {
            Win("QQPK", "Chrome_WidgetWin_1", "QQPK", 2049, 1434),
            Win("QQPK", "Chrome_WidgetWin_1", "QQPK", 1329, 930),
        }, rules, null, null, null);
        Check(qqDefault.Count == 1 && qqDefault[0].Area == 1329L * 930, "QQPK по умолчанию: стол да, лобби нет");

        // «Указать стол»: правило по указанному столу и остальным окнам программы.
        var unity = Picked(Win("CoinPoker", "UnityWndClass", "CoinPoker"),
            new List<TableWindow> { Win("CoinPoker", "Chrome_WidgetWin_1", "CoinPoker", 1920, 1167) }, "CoinPoker");
        Check(unity != null && unity.Class == "^UnityWndClass$" && unity.Table == "", "CoinPoker: по типу окна");
        Check(unity != null && Takes(unity, Win("CoinPoker", "UnityWndClass", "CoinPoker")), "CoinPoker: второй стол тоже");

        var aks = Picked(Win("AKs Poker", "Chrome_WidgetWin_1", "HL8121 - 0.02/0.05/0.10(0.02) - NLHE"),
            new List<TableWindow> { Win("AKs Poker", "Chrome_WidgetWin_1", "02:36 Moscow Standard Time", 1900, 1200, true) }, "AKs");
        Check(aks != null && aks.Table == RuleBuilder.GameMarkers, "AKs: по игре в заголовке");
        Check(aks != null && Takes(aks, Win("AKs Poker", "Chrome_WidgetWin_1", "$3.30 Bounty King Baby | $500 GTD - Blinds 75/150")), "AKs: турнирный стол тоже");
        Check(aks != null && !Takes(aks, Win("AKs Poker", "Chrome_WidgetWin_1", "23:10 Moscow Standard Time")), "AKs: лобби с другим временем — нет");

        var wina = Picked(Win("Winamax", "Chrome_WidgetWin_1", "Winamax Kill The Fish(1186678909)(#005)"),
            new List<TableWindow> { Win("Winamax", "Chrome_WidgetWin_1", "Winamax", 2049, 1152) }, "Winamax");
        Check(wina != null && wina.Exclude == "^Winamax$", "Winamax: все окна, кроме лобби");
        Check(wina != null && Takes(wina, Win("Winamax", "Chrome_WidgetWin_1", "Winamax CYBER(1186000001)(#137)")), "Winamax: другой турнир тоже");

        var tigerLobby = Win("GameClient", "Antares Game Client Window Class",
            "TigerGaming Lobby | Nickname: CheckMEights | Balance: $72.22 (In play: $0; Available: $72.22)", 1926, 1205);
        var tiger = Picked(Win("GameClient", "Antares Game Client Window Class", "Late Night Special 5"),
            new List<TableWindow> { tigerLobby }, "Tiger без игры в заголовке");
        Check(tiger != null && !Takes(tiger, Win("GameClient", "Antares Game Client Window Class",
            "TigerGaming Lobby | Nickname: CheckMEights | Balance: $1,234.50 (In play: $10; Available: $1,224.50)")),
            "лобби с другим балансом — не стол");

        var qqLobby = Win("QQPK", "Chrome_WidgetWin_1", "QQPK", 2049, 1434);
        var qq = Picked(Win("QQPK", "Chrome_WidgetWin_1", "QQPK", 1329, 930), new List<TableWindow> { qqLobby }, "QQPK");
        Check(qq != null && qq.Lobby == "largest", "QQPK: лобби — самое большое окно");
        if (qq != null)
        {
            var qqRule = CompiledRule.From(new[] { qq });
            var qqTables = Desktop.Classify(new List<TableWindow>
            {
                Win("QQPK", "Chrome_WidgetWin_1", "QQPK", 2049, 1434, true),
                Win("QQPK", "Chrome_WidgetWin_1", "QQPK", 1280, 896),
                Win("QQPK", "Chrome_WidgetWin_1", "QQPK", 1280, 896),
            }, qqRule, null, null, null);
            Check(qqTables.Count == 2, "QQPK: свёрнутое лобби всё равно лобби, два стола");
        }
        string none;
        Check(RuleBuilder.ForTable(Win("QQPK", "Chrome_WidgetWin_1", "QQPK", 2049, 1434),
            new List<TableWindow> { Win("QQPK", "Chrome_WidgetWin_1", "QQPK", 1329, 930) }, out none) == null,
            "указано самое большое окно — правило не строится");

        // Указанные вручную и «не стол» окна.
        var plain = Win("Some", "X", "Some");
        var pinned = Desktop.Classify(new List<TableWindow> { plain }, new List<CompiledRule>(),
            new HashSet<IntPtr> { plain.Handle }, null, null);
        Check(pinned.Count == 1, "указанное окно — стол без правила");
        var ggTable = Win("GGnet", "ApolloRuntimeContentWindow", "Daily $5.40 - Стол 3");
        Check(Desktop.Classify(new List<TableWindow> { ggTable }, rules, null, new HashSet<IntPtr> { ggTable.Handle }, null).Count == 0,
            "окно «не стол» не берётся");
        Check(Desktop.Classify(new List<TableWindow> { Win("GGnet", "ApolloRuntimeContentWindow", "Daily $5.40 - Стол 3", 900, 700, true) },
            rules, null, null, null).Count == 0, "свёрнутый стол не расставляется");

        // «Расставить все» разворачивает свёрнутые столы — только их.
        var folded = Win("GGnet", "ApolloRuntimeContentWindow", "Daily $5.40 - Стол 5", 900, 700, true);
        var foldedLobby = Win("GGnet", "ApolloRuntimeContentWindow", "Лобби", 900, 700, true);
        var open = Win("GGnet", "ApolloRuntimeContentWindow", "Daily $5.40 - Стол 6");
        var unfold = Desktop.Classify(new List<TableWindow> { folded, foldedLobby, open }, rules, null, null, null, true);
        Check(unfold.Count == 1 && unfold[0].Handle == folded.Handle, "разворачиваются свёрнутые столы, не лобби и не открытые");
        Check(Desktop.Classify(new List<TableWindow> { folded }, rules, null, new HashSet<IntPtr> { folded.Handle }, null, true).Count == 0,
            "свёрнутое «не стол» не разворачивается");

        Check(Regex.IsMatch("Balance: $1,234.50", RuleBuilder.Exact("Balance: $72.22")), "числа в исключениях — любые");
        Check(!Regex.IsMatch("Winamax Kill", RuleBuilder.Exact("Winamax")), "исключение — весь заголовок");

        // Перенос стола в другую ячейку и обмен местами.
        var swap = new SlotTracker();
        var ta = new IntPtr(1);
        var tb = new IntPtr(2);
        swap.Set(ta, 0);
        swap.Set(tb, 1);
        Check(swap.MoveTo(ta, 1) == tb, "в занятую ячейку — обмен");
        int sa, sb;
        swap.TryGet(ta, out sa);
        swap.TryGet(tb, out sb);
        Check(sa == 1 && sb == 0, "столы поменялись ячейками");
        Check(swap.MoveTo(ta, 5) == IntPtr.Zero && swap.OwnerOf(5) == ta && !swap.IsTaken(1), "в свободную — просто переезд");
        var grid = Layout.Cells(new Box(0, 0, 3840, 2088), 3, 2, 0);
        Check(Layout.SlotAt(grid, 1500, 300) == 1 && Layout.SlotAt(grid, 100, 1500) == 3 && Layout.SlotAt(grid, -5, 10) == -1,
            "ячейка под центром брошенного стола");

        // Окно под курсором: HUD трекера и оверлеи — насквозь, основное окно трекера — нет.
        Check(Desktop.SeeThrough("Hand2Note", "Hand2Note HUD", Native.WS_EX_LAYERED | 0x4), "HUD Hand2Note — насквозь");
        Check(!Desktop.SeeThrough("Hand2Note", "Hand2Note", 0x40000), "основное окно Hand2Note закрывает стол");
        Check(Desktop.SeeThrough("PokerTracker4", "", Native.WS_EX_TOOLWINDOW), "служебное окно трекера — насквозь");
        Check(Desktop.SeeThrough("NVIDIA Overlay", "NVIDIA GeForce Overlay", 0), "оверлей видеокарты — насквозь");
        Check(!Desktop.SeeThrough("Telegram", "Telegram", Native.WS_EX_LAYERED), "другая программа закрывает стол");

#if ACTION_KEYS
        // Клавиши за столами: кнопка в долях клиентской области переживает изменение размера стола.
        double fx, fy;
        var bigTable = new Box(1280, 68, 1280, 908);
        Check(ActionKeys.ToFraction(bigTable, 1980, 918, out fx, out fy) && fx == 0.5469 && fy == 0.9361,
            "кнопка: доли клиентской области стола");
        var same = ActionKeys.ToScreen(bigTable, fx, fy);
        Check(same.X == 1980 && same.Y == 918, "кнопка на том же столе — в пиксель");
        var smaller = ActionKeys.ToScreen(new Box(100, 50, 640, 454), fx, fy);
        Check(smaller.X == 450 && smaller.Y == 475, "кнопка на уменьшенном столе — на том же месте");
        var edge = ActionKeys.ToScreen(bigTable, 1, 1);
        Check(edge.X == bigTable.Right - 1 && edge.Y == bigTable.Bottom - 1, "кнопка не выходит за стол");
        Check(!ActionKeys.ToFraction(bigTable, 100, 100, out fx, out fy), "точка мимо стола — не кнопка");

        var keys = ActionKeysConfig.Fill(null);
        Check(keys.Enabled && keys.OnlyWhenPokerActive && keys.Actions.Count == 3
            && keys.Actions[0].Key == "Space" && keys.Actions[1].Key == "S" && keys.Actions[2].Key == "D",
            "клавиши по умолчанию: пробел — фолд, S — чек/колл, D — ставка");
        foreach (var tableAction in keys.Actions)
        {
            uint mods;
            System.Windows.Forms.Keys key;
            Check(HotkeyWindow.Parse(tableAction.Key, out mods, out key) && mods == 0,
                "клавиша «" + tableAction.Key + "» понятна, без модификаторов");
            Check(HotkeyWindow.Parse(tableAction.RecordHotkey, out mods, out key) && mods == (Native.MOD_CONTROL | Native.MOD_ALT),
                "клавиша записи «" + tableAction.RecordHotkey + "» понятна");
        }
        Check(keys.FocusUnderMouse == true && keys.FocusBorder == true, "по умолчанию: стол под курсором активный, рамка");
        // Раздел клавиш из первой версии сборки (без способов нажатия и фокуса) дополняется.
        var partial = ActionKeysConfig.Fill(new ActionKeysConfig { Enabled = true });
        Check(partial.Actions.Count == 3 && partial.Buttons != null && partial.ExcludeRooms != null
            && partial.ClickDelayMs > 0 && partial.ToggleHotkey != null && partial.FocusUnderMouse == true
            && partial.ClickMode == "direct" && partial.RoomClickModes != null && partial.ClientKeys != null,
            "неполный раздел клавиш дополняется");
        Check(ActionKeysConfig.Fill(new ActionKeysConfig { FocusUnderMouse = false }).FocusUnderMouse == false,
            "выключенный фокус под мышкой не включается сам");
        keys.ExcludeRooms.Add("QQPK");
        Check(keys.IsExcluded("qqpk") && !keys.IsExcluded("Redstar"), "рум без клавиш");

        // GG: на столах программа ничего не нажимает — по умолчанию для GG ничего не задано,
        // а GG из прежнего config.json (свои клавиши клиента) убирается.
        Check(keys.RoomClickModes.Count == 0 && keys.ClientKeys.Count == 0, "по умолчанию для GG ничего не задано");
        Check(ActionKeysConfig.IsNoKeys("GGnet") && ActionKeysConfig.IsNoKeys("ggnet") && !ActionKeysConfig.IsNoKeys("PokerClient")
            && !ActionKeysConfig.IsNoKeys(null), "GG — без клавиш, по программе");
        var oldGG = ActionKeysConfig.Fill(new ActionKeysConfig
        {
            RoomClickModes = new Dictionary<string, string> { { "GGnet", "keys" }, { "CoinPoker", "cursor" } },
            ClientKeys = new List<ClientKey>
            {
                new ClientKey { Room = "GGnet", Action = "Фолд", Keys = "Ctrl+F1" },
                new ClientKey { Room = "Winamax", Action = "Фолд", Keys = "F1" },
            },
        });
        Check(!oldGG.RoomClickModes.ContainsKey("GGnet") && oldGG.ModeOf("CoinPoker", "CoinPoker") == "cursor"
            && oldGG.ClientKeyOf("GG / PokerOK", "GGnet", "Фолд") == null && oldGG.ClientKeyOf("Winamax", "Winamax", "Фолд").Keys == "F1",
            "GG из прежнего config.json убран, остальные румы как были");

        // Способ нажатия: общий — клик без курсора; для рума — из меню, для программы — из config.json.
        Check(keys.ModeOf("Redstar", "PokerClient") == "direct", "по умолчанию — клик без курсора");
        keys.RoomClickModes["PokerApp"] = "keys";
        keys.ClientKeys.Add(new ClientKey { Room = "PokerApp", Action = "Фолд", Keys = "F1" });
        Check(keys.ModeOf("PokerApp турниры", "PokerApp") == "keys" && keys.ModeOf("PokerApp (указан)", "PokerApp") == "keys",
            "свои клавиши клиента — по программе");
        Check(keys.ClientKeyOf("PokerApp (указан)", "PokerApp", "Фолд").Keys == "F1"
            && keys.ClientKeyOf("Redstar", "PokerClient", "Фолд") == null, "клавиши клиента — по программе");
        keys.SetMode("Redstar", "cursor");
        keys.SetMode("pokerapp турниры", "direct");
        Check(keys.ModeOf("Redstar", "PokerClient") == "cursor" && keys.ModeOf("PokerApp турниры", "PokerApp") == "direct"
            && keys.ModeOf("PokerApp (указан)", "PokerApp") == "keys", "способ из меню — для рума, остальные румы программы как были");
        keys.RoomClickModes["AKs Poker"] = "непонятно";
        Check(keys.ModeOf("AKs Poker", "AKs Poker") == "direct", "непонятный способ — общий");

        // Кнопки: для рума, а если рум другой, но программа та же — тоже.
        keys.Buttons.Add(new ActionButton { Room = "Redstar", Process = "PokerClient", Action = "Фолд", X = 0.5, Y = 0.9 });
        Check(keys.ButtonOf("Redstar", "PokerClient", "Фолд") != null && keys.ButtonOf("Redstar", "PokerClient", "Ставка") == null
            && keys.ButtonOf("PokerApp турниры", "PokerApp", "Фолд") == null, "кнопки — по руму и действию");
        Check(keys.ButtonOf("PokerClient (указан)", "PokerClient", "Фолд") != null, "кнопка подходит другому руму той же программы");
        keys.Buttons.Add(new ActionButton { Room = "AKs Poker", Action = "Фолд", X = 0.66, Y = 0.92 });
        Check(keys.ButtonOf("AKs Poker", "AKs Poker", "Фолд").X == 0.66, "кнопка из первой версии (без программы) — по руму");
        var lp = ActionKeys.MouseLParam(10, 20).ToInt64();
        Check(lp == 0x0014000A, "координаты для сообщений мыши");

        // config.json: раздел клавиш сохраняется и читается обратно, старый файл дополняется.
        var keysPath = System.IO.Path.GetTempFileName();
        try
        {
            var withKeys = Config.Default();
            withKeys.ActionKeys = keys;
            Config.Save(keysPath, withKeys);
            var keysLoaded = Config.Load(keysPath);
            var fold = keysLoaded.ActionKeys.ButtonOf("Redstar", "PokerClient", "Фолд");
            Check(fold != null && fold.X == 0.5 && fold.Y == 0.9 && keysLoaded.ActionKeys.IsExcluded("QQPK"),
                "кнопки и румы без клавиш — в config.json");
            Check(keysLoaded.ActionKeys.ModeOf("Redstar", "PokerClient") == "cursor"
                && keysLoaded.ActionKeys.ModeOf("PokerApp (указан)", "PokerApp") == "keys"
                && keysLoaded.ActionKeys.FocusUnderMouse == true, "способы нажатия и фокус — в config.json");
            System.IO.File.WriteAllText(keysPath, "{ \"Gap\": 0 }");
            Check(Config.Load(keysPath).ActionKeys.Actions.Count == 3 && System.IO.File.ReadAllText(keysPath).Contains("ActionKeys"),
                "config.json без клавиш дополняется разделом");
        }
        finally
        {
            System.IO.File.Delete(keysPath);
        }
#endif

        Console.WriteLine(_failed == 0 ? "OK: " + _passed + " проверок" : _failed + " из " + (_passed + _failed) + " не прошли");
        return _failed == 0 ? 0 : 1;
    }
}
