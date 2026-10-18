using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TableManager
{
    /// <summary>Сердце расстановщика. Каждый тик: находит столы, выбирает уровень
    /// раскладки по их числу, новым даёт первую свободную ячейку, проверяет, что
    /// стол встал, и держит столы в ячейках. Окна только перемещаются
    /// (SetWindowPos без активации) — фокус, ввод и содержимое клиентов не
    /// затрагиваются.</summary>
    public class Manager
    {
        private class State
        {
            public int Slot = -1;
            public Box Target;
            public int Attempts;
            public bool Settled;
            public bool Manual;
            public bool GaveUp;
            public bool OverflowLogged;
            public bool DeniedLogged;
            public DateTime MovedAt;

            /// <summary>Когда стол возвращали в ячейку без участия игрока.</summary>
            public readonly List<DateTime> Returns = new List<DateTime>();
        }

        /// <summary>Сколько раз пробуем вписать стол (клиент может поправить размер,
        /// при переходе на монитор с другим масштабом Windows меняет его сама).</summary>
        private const int MaxAttempts = 4;
        private const int Tolerance = 6;
        private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(300);

        /// <summary>Сдвиг стола в течение этого времени после нажатия левой кнопки —
        /// дело рук игрока (перетаскивание), а не клиента.</summary>
        private static readonly TimeSpan UserMoveWindow = TimeSpan.FromSeconds(1.5);

        /// <summary>Если клиент сам уводит стол из ячейки чаще — перестаём возвращать.</summary>
        private const int MaxReturns = 5;
        private static readonly TimeSpan ReturnsPeriod = TimeSpan.FromMinutes(1);

        private Config _config;
        private List<CompiledRule> _rules;
        private SlotTracker _tracker = new SlotTracker();
        private readonly Dictionary<IntPtr, State> _states = new Dictionary<IntPtr, State>();
        private int _ticks;
        private bool _adopted;
        private int _level;
        private DateTime _mouseDownAt = DateTime.MinValue;

        /// <summary>Окна, указанные столом вручную, когда правило не построить, и
        /// окна, указанные «не стол». Живут, пока открыто окно.</summary>
        private readonly HashSet<IntPtr> _pinned = new HashSet<IntPtr>();
        private readonly HashSet<IntPtr> _ignored = new HashSet<IntPtr>();

        /// <summary>Окна румов, не похожие на стол, уже записанные в журнал.</summary>
        private readonly HashSet<IntPtr> _othersLogged = new HashSet<IntPtr>();

        public event Action<string> Log;

        public bool Paused { get; set; }
        public int TableCount { get; private set; }
        public List<Box> Slots { get; private set; }

        /// <summary>Столы последнего тика (для клавиш за столами).</summary>
        public List<TableWindow> Tables { get; private set; }

        public Manager(Config config)
        {
            Apply(config);
            Slots = new List<Box>();
            Tables = new List<TableWindow>();
        }

        public void Apply(Config config)
        {
            _config = config;
            _rules = CompiledRule.From(config.Rooms);
            // Ячейки могли измениться — столы берём заново.
            _adopted = false;
        }

        /// <summary>Текущая сетка словами: «3×2 + 4×2».</summary>
        public string LevelName
        {
            get { return Describe(_level); }
        }

        private string Describe(int level)
        {
            if (level < 0 || level >= _config.Levels.Count) return "—";
            return string.Join(" + ", _config.Levels[level].Zones.Select(z => z.Columns + "×" + z.Rows).ToArray());
        }

        private void Write(string text)
        {
            var handler = Log;
            if (handler != null) handler(text);
        }

        private static string Short(TableWindow t)
        {
            return t.Room + " «" + RuleBuilder.Short(t.Title) + "»";
        }

        private List<Box> SlotsFor(int level, List<MonitorInfo> monitors)
        {
            return Desktop.Slots(_config.Levels[level].Zones, _config.Gap, monitors);
        }

        private List<int> Capacities(List<MonitorInfo> monitors)
        {
            var list = new List<int>();
            for (var i = 0; i < _config.Levels.Count; i++)
            {
                list.Add(Levels.Capacity(_config.Levels[i].MaxTables, SlotsFor(i, monitors).Count));
            }
            return list;
        }

        /// <summary>Ячейки текущего уровня (для «Показать ячейки»).</summary>
        public List<Box> CurrentSlots()
        {
            return SlotsFor(Math.Min(_level, _config.Levels.Count - 1), Desktop.Monitors());
        }

        private List<TableWindow> FindTables(List<TableWindow> others = null)
        {
            return Desktop.FindTables(_rules, others, _pinned, _ignored);
        }

        public void Tick()
        {
            if (++_ticks % 150 == 0)
            {
                Desktop.ForgetProcesses();
                _pinned.RemoveWhere(h => !Native.IsWindow(h));
                _ignored.RemoveWhere(h => !Native.IsWindow(h));
            }
            var monitors = Desktop.Monitors();
            var others = new List<TableWindow>();
            var tables = FindTables(others);
            Tables = tables;
            TableCount = tables.Count;
            var capacities = Capacities(monitors);
            LogOthers(others);

            // Пока зажата левая кнопка (клик, перетаскивание) — ничего не двигаем,
            // чтобы окно не уехало из-под курсора. Сдвиг сразу после отпускания —
            // это игрок перетащил стол.
            var mouseDown = Native.LeftButtonDown();
            if (mouseDown) _mouseDownAt = DateTime.Now;
            var userActive = DateTime.Now - _mouseDownAt < UserMoveWindow;

            if (!_adopted)
            {
                if (_config.RespectManualMoves)
                {
                    _level = Levels.For(tables.Count, capacities);
                    Slots = SlotsFor(_level, monitors);
                    Adopt(tables, Slots);
                    _adopted = true;
                }
                else if (!mouseDown && !Paused)
                {
                    // Все столы только по ячейкам — сразу расставляем открытые.
                    RelayoutAll(tables, monitors);
                }
                return;
            }

            if (_level >= _config.Levels.Count) _level = _config.Levels.Count - 1;
            var slots = SlotsFor(_level, monitors);
            var alive = new HashSet<IntPtr>(tables.Select(t => t.Handle));
            _tracker.Prune(alive, slots.Count);
            foreach (var dead in _states.Keys.Where(h => !alive.Contains(h)).ToList())
            {
                _states.Remove(dead);
            }
            foreach (var s in _states.Values)
            {
                if (s.Slot >= slots.Count) s.Slot = -1;
            }

            if (!Paused && !mouseDown)
            {
                var next = Levels.Next(_level, tables.Count, capacities, _config.Hysteresis);
                if (next != _level && ChangeLevel(next, tables, slots, monitors))
                {
                    slots = SlotsFor(_level, monitors);
                }
            }
            Slots = slots;
            var byHandle = tables.ToDictionary(t => t.Handle);

            foreach (var table in tables)
            {
                State state;
                if (!_states.TryGetValue(table.Handle, out state))
                {
                    if (Paused || mouseDown) continue;
                    state = new State();
                    _states[table.Handle] = state;
                    Write("Новый стол: " + Short(table) + " " + table.Frame);
                }

                if (state.Slot < 0)
                {
                    if (Paused || mouseDown) continue;
                    state.Slot = _tracker.AssignFirstFree(table.Handle, slots.Count);
                    if (state.Slot < 0)
                    {
                        if (!state.OverflowLogged)
                        {
                            Write("Нет свободной ячейки для " + Short(table) + " — стол оставлен на месте");
                            state.OverflowLogged = true;
                        }
                        continue;
                    }
                    Place(table, state, slots[state.Slot]);
                    continue;
                }

                if (state.Manual || state.GaveUp || Paused) continue;
                var frame = Box.From(table.Frame);
                var cell = slots[state.Slot];

                if (!state.Settled)
                {
                    if (DateTime.Now - state.MovedAt < SettleDelay) continue;
                    // Первая проверка — почти точная: при переходе на монитор с
                    // другим масштабом меняются невидимые края окна, и один
                    // повторный проход ставит стол в пиксель. Дальше допускаем,
                    // что клиент сам подправил размер под свою пропорцию.
                    if (frame.Near(state.Target, 2) || (state.Attempts >= 2 && cell.Contains(frame, Tolerance)))
                    {
                        state.Settled = true;
                        state.Target = frame;
                        continue;
                    }
                    if (mouseDown) continue;
                    if (state.Attempts >= MaxAttempts)
                    {
                        state.GaveUp = true;
                        Write("Не удалось вписать " + Short(table) + " в ячейку " + (state.Slot + 1) + " " + cell
                            + ": окно " + frame + " (клиент не даёт уменьшить?)");
                        continue;
                    }
                    Place(table, state, cell);
                    continue;
                }

                if (frame.Near(state.Target, Tolerance) || mouseDown) continue;

                // Клиент сам чуть поправил размер или место, не выходя из ячейки, —
                // принимаем, иначе расстановщик будет бороться с клиентом.
                if (!userActive && cell.Contains(frame, Tolerance))
                {
                    state.Target = frame;
                    continue;
                }

                if (_config.RespectManualMoves)
                {
                    state.Manual = true;
                    Write("Стол сдвинут вручную, оставляю: " + Short(table) + " " + frame);
                    continue;
                }
                Snap(table, state, frame, slots, userActive, byHandle);
            }
        }

        /// <summary>Все столы только по ячейкам: стол, брошенный игроком в другую
        /// ячейку, встаёт в неё (если там стол — меняются местами); в остальных
        /// случаях возвращается в свою.</summary>
        private void Snap(TableWindow table, State state, Box frame, List<Box> slots, bool byUser,
            Dictionary<IntPtr, TableWindow> byHandle)
        {
            if (byUser)
            {
                var drop = Layout.SlotAt(slots, frame.CenterX, frame.CenterY);
                if (drop >= 0 && drop != state.Slot)
                {
                    var from = state.Slot;
                    var other = _tracker.MoveTo(table.Handle, drop);
                    state.Slot = drop;
                    State otherState;
                    TableWindow otherTable;
                    if (other != IntPtr.Zero && _states.TryGetValue(other, out otherState))
                    {
                        otherState.Slot = from;
                        otherState.Manual = false;
                        otherState.GaveUp = false;
                        otherState.Attempts = 0;
                        if (byHandle.TryGetValue(other, out otherTable))
                        {
                            Write("Столы поменялись местами: " + Short(table) + " → ячейка " + (drop + 1) + ", "
                                + Short(otherTable) + " → ячейка " + (from + 1));
                            Place(otherTable, otherState, slots[from]);
                        }
                    }
                    else
                    {
                        Write("Стол перенесён в ячейку " + (drop + 1) + ": " + Short(table));
                    }
                }
            }
            else
            {
                var now = DateTime.Now;
                state.Returns.RemoveAll(t => now - t > ReturnsPeriod);
                state.Returns.Add(now);
                if (state.Returns.Count > MaxReturns)
                {
                    state.GaveUp = true;
                    Write("Клиент сам уводит стол из ячейки, перестаю возвращать: " + Short(table) + " " + frame);
                    return;
                }
            }
            state.Attempts = 0;
            Place(table, state, slots[state.Slot]);
        }

        private static int MonitorOf(Box box, List<MonitorInfo> monitors)
        {
            return monitors.FindIndex(m => box.CenterX >= m.Bounds.X && box.CenterX < m.Bounds.Right
                && box.CenterY >= m.Bounds.Y && box.CenterY < m.Bounds.Bottom);
        }

        /// <summary>Переход на другой уровень: столы, чьи ячейки есть и в новой
        /// сетке, остаются на месте; остальные переезжают в ближайшие свободные.
        /// Вниз — только если никому не придётся переезжать на другой монитор
        /// и все стоящие столы получат место (иначе ждём).</summary>
        private bool ChangeLevel(int next, List<TableWindow> tables, List<Box> oldSlots, List<MonitorInfo> monitors)
        {
            var newSlots = SlotsFor(next, monitors);
            var handles = tables.Select(t => t.Handle).ToList();
            var trial = _tracker.Clone();
            var moved = trial.Remap(oldSlots, newSlots, handles);

            if (next < _level)
            {
                foreach (var h in handles)
                {
                    int was, now;
                    if (!_tracker.TryGet(h, out was)) continue;
                    if (!trial.TryGet(h, out now)) return false;
                    if (moved.Contains(h) && MonitorOf(oldSlots[was], monitors) != MonitorOf(newSlots[now], monitors))
                    {
                        return false;
                    }
                }
            }

            var from = Describe(_level);
            _tracker = trial;
            _level = next;
            foreach (var table in tables)
            {
                State state;
                if (!_states.TryGetValue(table.Handle, out state)) continue;
                int slot;
                if (!_tracker.TryGet(table.Handle, out slot))
                {
                    state.Slot = -1;
                    continue;
                }
                state.Slot = slot;
                if (moved.Contains(table.Handle))
                {
                    state.Manual = false;
                    state.GaveUp = false;
                    state.Attempts = 0;
                    Place(table, state, newSlots[slot]);
                }
            }
            Write("Сетка: " + from + " → " + Describe(_level) + " (столов " + tables.Count
                + ", переставлено " + moved.Count + ")");
            return true;
        }

        private void Place(TableWindow table, State state, Box cell)
        {
            var frame = table.Frame;
            var aspect = frame.Height > 0 ? (double)frame.Width / frame.Height : 4.0 / 3.0;
            var target = Layout.Fit(cell, aspect, _config.Align);

            // Невидимые края окна: SetWindowPos работает с полным прямоугольником.
            var left = frame.Left - table.Window.Left;
            var top = frame.Top - table.Window.Top;
            var right = table.Window.Right - frame.Right;
            var bottom = table.Window.Bottom - frame.Bottom;

            var ok = Native.SetWindowPos(table.Handle, IntPtr.Zero,
                target.X - left, target.Y - top,
                target.W + left + right, target.H + top + bottom,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | Native.SWP_ASYNCWINDOWPOS);
            if (!ok && !state.DeniedLogged)
            {
                var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                state.DeniedLogged = true;
                Write("Windows не даёт двигать " + Short(table) + " (код " + error + ")"
                    + (error == 5 ? ": клиент запущен от администратора — запустите расстановщик тоже от администратора" : ""));
            }

            state.Target = target;
            state.Attempts++;
            state.Settled = false;
            state.MovedAt = DateTime.Now;
            if (state.Attempts == 1)
            {
                Write("  → ячейка " + (state.Slot + 1) + ": " + target);
            }
        }

        /// <summary>Окна румов, которые не распознаны как стол (лобби, кассы и т. п.),
        /// — один раз в журнал с классом и заголовком: если стол не встаёт в ячейку,
        /// по этой строке видно, что дописать в правило рума.</summary>
        private void LogOthers(List<TableWindow> others)
        {
            if (_othersLogged.Count > 1000) _othersLogged.Clear();
            foreach (var w in others)
            {
                if (!_othersLogged.Add(w.Handle)) continue;
                var frame = Box.From(w.Frame);
                Write("Окно рума, не стол: " + w.Room + " | " + w.ClassName + " | " + frame.W + "x" + frame.H
                    + " | «" + RuleBuilder.Short(w.Title) + "»");
            }
        }

        /// <summary>Столы, открытые до запуска (или до перечитывания настроек), не
        /// двигаются: каждый занимает ближайшую ячейку «как есть», чтобы новые столы
        /// вставали в действительно свободные. Встанут ровно по общей расстановке.
        /// Только при RespectManualMoves.</summary>
        private void Adopt(List<TableWindow> tables, List<Box> slots)
        {
            _tracker.AssignNearest(
                tables.Select(t => new KeyValuePair<IntPtr, Box>(t.Handle, Box.From(t.Frame))).ToList(),
                slots);
            _states.Clear();
            foreach (var table in tables)
            {
                int slot;
                var state = new State { Settled = true, Manual = true, Target = Box.From(table.Frame) };
                state.Slot = _tracker.TryGet(table.Handle, out slot) ? slot : -1;
                state.OverflowLogged = state.Slot < 0;
                _states[table.Handle] = state;
            }
            Write("Открытых столов: " + tables.Count + ", сетка " + Describe(_level) + ", ячеек: " + slots.Count
                + ". Уже открытые столы не двигаю; новые встают в свободные ячейки. "
                + "Расставить все — " + _config.RelayoutHotkey);
        }

        /// <summary>Расставить все столы заново: уровень — по числу столов, каждый
        /// стол — в ближайшую свободную ячейку, ручные сдвиги сбрасываются.</summary>
        public void RelayoutAll()
        {
            RelayoutAll(FindTables(), Desktop.Monitors());
        }

        private void RelayoutAll(List<TableWindow> tables, List<MonitorInfo> monitors)
        {
            TableCount = tables.Count;
            _level = Levels.For(tables.Count, Capacities(monitors));
            var slots = SlotsFor(_level, monitors);
            Slots = slots;
            // Столы занимают первые ячейки в порядке заполнения (основной монитор
            // первым), а среди них каждый — ближайшую к себе.
            _tracker.AssignNearest(
                tables.Select(t => new KeyValuePair<IntPtr, Box>(t.Handle, Box.From(t.Frame))).ToList(),
                slots.Take(Math.Min(tables.Count, slots.Count)).ToList());
            _states.Clear();
            _adopted = true;
            var placed = 0;
            foreach (var table in tables)
            {
                var state = new State();
                _states[table.Handle] = state;
                int slot;
                if (!_tracker.TryGet(table.Handle, out slot))
                {
                    Write("Нет свободной ячейки для " + Short(table));
                    state.OverflowLogged = true;
                    continue;
                }
                state.Slot = slot;
                Place(table, state, slots[slot]);
                placed++;
            }
            Write("Расстановка всех столов: " + placed + " из " + tables.Count + ", сетка " + Describe(_level)
                + ", ячеек " + slots.Count);
        }

        /// <summary>Забыть стол: следующий тик возьмёт его как новый (или отпустит).</summary>
        private void Forget(IntPtr window)
        {
            _tracker.Release(window);
            _states.Remove(window);
        }

        /// <summary>«Указать стол»: окно — стол, и такие же окна тоже. Строит правило
        /// по окнам той же программы, которые сейчас не столы; если стол от них не
        /// отличить — двигается только он. Возвращает сообщение для игрока;
        /// configChanged — правило добавлено в настройки (их нужно сохранить).</summary>
        public string MarkTable(TableWindow window, out bool configChanged)
        {
            configChanged = false;
            _ignored.Remove(window.Handle);
            var all = Desktop.Windows();
            var current = Desktop.Classify(all, _rules, _pinned, _ignored, null);
            var existing = current.FirstOrDefault(t => t.Handle == window.Handle);
            if (existing != null)
            {
                Forget(window.Handle);
                return "Это уже стол: " + Short(existing) + " — ставлю в ячейку";
            }

            var tables = new HashSet<IntPtr>(current.Select(t => t.Handle));
            var siblings = all.Where(w => w.Process == window.Process && w.Handle != window.Handle
                && !tables.Contains(w.Handle)).ToList();
            string description;
            var rule = RuleBuilder.ForTable(window, siblings, out description);
            Forget(window.Handle);
            if (rule == null)
            {
                _pinned.Add(window.Handle);
                return "Стол " + window.Process + " «" + RuleBuilder.Short(window.Title)
                    + "» ничем не отличается от остальных окон программы — двигаю только его. "
                    + "Новые такие столы указывайте так же.";
            }

            _config.Rooms.RemoveAll(r => r.Name == rule.Name);
            _config.Rooms.Add(rule);
            _rules = CompiledRule.From(_config.Rooms);
            configChanged = true;

            // Правило обязано взять сам указанный стол; если почему-то нет — держим его вручную.
            var check = Desktop.Classify(Desktop.Windows(), _rules, _pinned, _ignored, null);
            if (!check.Any(t => t.Handle == window.Handle)) _pinned.Add(window.Handle);
            var count = check.Count(t => t.Room == rule.Name);
            return "Стол указан: " + window.Process + " «" + RuleBuilder.Short(window.Title) + "». Теперь столы — "
                + description + " (сейчас таких окон: " + count + ")";
        }

        /// <summary>«Это не стол»: окно больше не ставится в ячейки и остаётся, где
        /// стоит. Если у окна свой заголовок — он уходит в исключения правила; если
        /// такой же, как у столов, — окно пропускается, пока не закроется.</summary>
        public string MarkNotTable(TableWindow window, out bool configChanged)
        {
            configChanged = false;
            var wasPinned = _pinned.Remove(window.Handle);
            var current = Desktop.Classify(Desktop.Windows(), _rules, _pinned, _ignored, null);
            var table = current.FirstOrDefault(t => t.Handle == window.Handle);
            Forget(window.Handle);
            if (table == null)
            {
                return wasPinned
                    ? "Больше не стол: " + window.Process + " «" + RuleBuilder.Short(window.Title) + "»"
                    : "Это окно и так не стол: " + window.Process + " «" + RuleBuilder.Short(window.Title) + "»";
            }

            var rule = _config.Rooms.FirstOrDefault(r => r.Name == table.Room);
            var pattern = RuleBuilder.Exact(window.Title);
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var hitsTables = current.Any(t => t.Handle != window.Handle && t.Room == table.Room && regex.IsMatch(t.Title))
                || (rule != null && !string.IsNullOrEmpty(rule.Sample) && regex.IsMatch(rule.Sample))
                || (rule != null && string.Equals(rule.Lobby, "largest", StringComparison.OrdinalIgnoreCase));
            if (rule == null || hitsTables)
            {
                _ignored.Add(window.Handle);
                return "Не стол, пока окно открыто: " + Short(table)
                    + " (заголовок как у столов, поэтому правило не меняю)";
            }

            RuleBuilder.AddExclude(rule, pattern);
            _rules = CompiledRule.From(_config.Rooms);
            configChanged = true;
            return "Больше не стол: " + Short(table) + " — заголовок добавлен в исключения правила «" + rule.Name + "»";
        }
    }
}
