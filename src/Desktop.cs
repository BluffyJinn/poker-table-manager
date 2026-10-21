using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace TableManager
{
    public class MonitorInfo
    {
        public string Device;
        public bool Primary;
        public Box Bounds;
        public Box Work;
    }

    public class TableWindow
    {
        public IntPtr Handle;
        public uint ProcessId;
        public string Process;
        public string ClassName;
        public string Title;
        public string Room;
        public RECT Window;

        /// <summary>Видимая рамка — по ней считаются ячейки.</summary>
        public RECT Frame;

        public bool Minimized;

        /// <summary>Площадь окна в обычном состоянии (и у свёрнутого).</summary>
        public long Area;
    }

    /// <summary>Правило рума с готовыми регулярками.</summary>
    public class CompiledRule
    {
        public string Name;
        public Regex Process;
        public Regex Class;
        public Regex Table;
        public Regex Exclude;

        /// <summary>Самое большое окно рума — лобби (Lobby = "largest").</summary>
        public bool LargestIsLobby;

        public static List<CompiledRule> From(IEnumerable<RoomRule> rules)
        {
            var list = new List<CompiledRule>();
            foreach (var r in rules)
            {
                if (string.IsNullOrEmpty(r.Process)) continue;
                const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
                list.Add(new CompiledRule
                {
                    Name = string.IsNullOrEmpty(r.Name) ? r.Process : r.Name,
                    Process = new Regex(r.Process, options),
                    Class = string.IsNullOrEmpty(r.Class) ? null : new Regex(r.Class, options),
                    Table = string.IsNullOrEmpty(r.Table) ? null : new Regex(r.Table, options),
                    Exclude = string.IsNullOrEmpty(r.Exclude) ? null : new Regex(r.Exclude, options),
                    LargestIsLobby = string.Equals(r.Lobby, "largest", StringComparison.OrdinalIgnoreCase),
                });
            }
            return list;
        }

        /// <summary>Окно этого рума (процесс и класс подходят) — стол или нет.</summary>
        public bool Owns(string process, string className)
        {
            return Process.IsMatch(process) && (Class == null || Class.IsMatch(className));
        }

        public bool Matches(string process, string className, string title)
        {
            if (!Owns(process, className)) return false;
            if (Exclude != null && Exclude.IsMatch(title)) return false;
            return Table == null ? title.Trim().Length > 0 : Table.IsMatch(title);
        }

        public bool Matches(TableWindow w)
        {
            return Matches(w.Process, w.ClassName, w.Title);
        }
    }

    /// <summary>Мониторы и окна столов. Только чтение.</summary>
    public static class Desktop
    {
        private static readonly Dictionary<uint, string> ProcessNames = new Dictionary<uint, string>();

        public static List<MonitorInfo> Monitors()
        {
            var list = new List<MonitorInfo>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                var info = new MONITORINFOEX();
                info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFOEX));
                if (Native.GetMonitorInfo(hMonitor, ref info))
                {
                    list.Add(new MonitorInfo
                    {
                        Device = info.szDevice,
                        Primary = (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                        Bounds = Box.From(info.rcMonitor),
                        Work = Box.From(info.rcWork),
                    });
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>Ячейки зон уровня в порядке заполнения: сначала первая зона,
        /// внутри зоны — по её Order.</summary>
        public static List<Box> Slots(IList<Zone> zones, int gap, List<MonitorInfo> monitors)
        {
            var slots = new List<Box>();
            foreach (var zone in zones)
            {
                var monitor = Resolve(zone.Monitor, monitors);
                if (monitor == null) continue;
                var work = monitor.Work;
                var area = new Box(
                    work.X + (int)Math.Round(work.W * Clamp(zone.X)),
                    work.Y + (int)Math.Round(work.H * Clamp(zone.Y)),
                    (int)Math.Round(work.W * Clamp(zone.W <= 0 ? 1 : zone.W)),
                    (int)Math.Round(work.H * Clamp(zone.H <= 0 ? 1 : zone.H)));
                var cells = Layout.Cells(area, zone.Columns, zone.Rows, gap);
                slots.AddRange(Layout.Ordered(cells, zone.Columns, zone.Rows, zone.Order));
            }
            return slots;
        }

        private static double Clamp(double v)
        {
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        public static MonitorInfo Resolve(string name, List<MonitorInfo> monitors)
        {
            if (string.IsNullOrEmpty(name) || name == "primary")
            {
                return monitors.Find(m => m.Primary);
            }
            if (name == "secondary")
            {
                return monitors.Find(m => !m.Primary);
            }
            return monitors.Find(m => string.Equals(m.Device, name, StringComparison.OrdinalIgnoreCase));
        }

        public static string ProcessName(uint pid)
        {
            string name;
            if (ProcessNames.TryGetValue(pid, out name)) return name;
            try
            {
                using (var p = Process.GetProcessById((int)pid)) name = p.ProcessName;
            }
            catch (Exception)
            {
                name = "";
            }
            ProcessNames[pid] = name;
            return name;
        }

        /// <summary>Сброс кэша имён процессов (pid могут переиспользоваться).</summary>
        public static void ForgetProcesses()
        {
            ProcessNames.Clear();
        }

        private static string Text(IntPtr hWnd, bool className)
        {
            var sb = new StringBuilder(512);
            if (className) Native.GetClassName(hWnd, sb, sb.Capacity);
            else Native.GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static TableWindow Describe(IntPtr hWnd)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hWnd, out pid);
            RECT window;
            Native.GetWindowRect(hWnd, out window);
            var normal = Native.NormalRect(hWnd);
            return new TableWindow
            {
                Handle = hWnd,
                ProcessId = pid,
                Process = ProcessName(pid),
                ClassName = Text(hWnd, true),
                Title = Text(hWnd, false),
                Window = window,
                Frame = Native.FrameOf(hWnd),
                Minimized = Native.IsIconic(hWnd),
                Area = (long)Math.Max(0, normal.Width) * Math.Max(0, normal.Height),
            };
        }

        /// <summary>Все видимые окна верхнего уровня с заголовком, и свёрнутые тоже
        /// (в порядке сверху вниз). Только чтение.</summary>
        public static List<TableWindow> Windows()
        {
            var result = new List<TableWindow>();
            Native.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!Native.IsWindowVisible(hWnd)) return true;
                var w = Describe(hWnd);
                if (w.Title.Length > 0) result.Add(w);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        /// <summary>Какие окна — столы. Стол — не свёрнутое окно, подходящее под
        /// правило рума, или указанное вручную (pinned); ignored — указано «не стол».
        /// У правила с Lobby = "largest" самое большое подходящее окно (по обычному
        /// размеру, и свёрнутое тоже) — лобби. В others — остальные не свёрнутые
        /// окна румов. Room у столов заполняется. minimized — наоборот, только свёрнутые
        /// окна, которые развёрнутыми были бы столами.</summary>
        public static List<TableWindow> Classify(List<TableWindow> windows, List<CompiledRule> rules,
            ICollection<IntPtr> pinned, ICollection<IntPtr> ignored, List<TableWindow> others, bool minimized = false)
        {
            var lobbies = new HashSet<IntPtr>();
            foreach (var rule in rules)
            {
                if (!rule.LargestIsLobby) continue;
                TableWindow largest = null;
                foreach (var w in windows)
                {
                    if (!rule.Matches(w) || (ignored != null && ignored.Contains(w.Handle))) continue;
                    if (largest == null || w.Area > largest.Area) largest = w;
                }
                if (largest != null) lobbies.Add(largest.Handle);
            }

            var tables = new List<TableWindow>();
            foreach (var w in windows)
            {
                if (w.Minimized != minimized) continue;
                CompiledRule owner = null;
                CompiledRule matched = null;
                foreach (var rule in rules)
                {
                    if (owner == null && rule.Owns(w.Process, w.ClassName)) owner = rule;
                    if (matched == null && rule.Matches(w) && !(rule.LargestIsLobby && lobbies.Contains(w.Handle))) matched = rule;
                }
                var isIgnored = ignored != null && ignored.Contains(w.Handle);
                if (pinned != null && pinned.Contains(w.Handle) && !isIgnored)
                {
                    w.Room = owner != null ? owner.Name : w.Process + " (указан)";
                    tables.Add(w);
                }
                else if (matched != null && !isIgnored)
                {
                    w.Room = matched.Name;
                    tables.Add(w);
                }
                else if (owner != null && others != null)
                {
                    w.Room = owner.Name;
                    others.Add(w);
                }
            }
            return tables;
        }

        /// <summary>Видимые, не свёрнутые окна столов по правилам румов. В others —
        /// остальные окна тех же румов (лобби и прочее), чтобы по журналу было
        /// видно заголовки, если стол не распознался.</summary>
        public static List<TableWindow> FindTables(List<CompiledRule> rules, List<TableWindow> others = null,
            ICollection<IntPtr> pinned = null, ICollection<IntPtr> ignored = null)
        {
            return Classify(Windows(), rules, pinned, ignored, others);
        }

        /// <summary>Окна, которые лежат поверх столов, но сами не столы: оверлеи
        /// видеокарты и системы.</summary>
        private static readonly Regex Overlays = new Regex(
            @"^(NVIDIA Overlay|RazerAppEngine|TextInputHost|ShellExperienceHost|StartMenuExperienceHost|SearchHost|SearchApp|LockApp)$",
            RegexOptions.IgnoreCase);

        /// <summary>Трекеры: их HUD лежит на столах, а основное окно — обычная программа.</summary>
        private static readonly Regex Trackers = new Regex(
            @"^(Hand2Note.*|h2n.*|HoldemManager.*|HM3.*|PokerTracker.*|PT4.*|Jurojin.*)$",
            RegexOptions.IgnoreCase);

        private static readonly Regex DesktopClasses = new Regex(@"^(Progman|WorkerW|Shell_TrayWnd|Shell_SecondaryTrayWnd)$");

        /// <summary>Сквозь окно видно стол под ним: оверлей или HUD трекера (окно
        /// слоистое, служебное, без активации или «HUD» в заголовке). Основное окно
        /// трекера закрывает стол, как любая другая программа.</summary>
        public static bool SeeThrough(string process, string title, int exStyle)
        {
            if (Overlays.IsMatch(process)) return true;
            if (!Trackers.IsMatch(process)) return false;
            return (exStyle & (Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE)) != 0
                || title.IndexOf("HUD", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Верхнее окно в точке экрана — то, что игрок видит под курсором.
        /// HUD и прозрачные оверлеи пропускаются. Только чтение.</summary>
        public static TableWindow WindowAt(int x, int y)
        {
            var self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            TableWindow found = null;
            Native.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!Native.IsWindowVisible(hWnd) || Native.IsIconic(hWnd) || Native.IsCloaked(hWnd)) return true;
                var exStyle = Native.GetWindowLong(hWnd, Native.GWL_EXSTYLE);
                if ((exStyle & Native.WS_EX_TRANSPARENT) != 0) return true;
                var frame = Native.FrameOf(hWnd);
                if (x < frame.Left || x >= frame.Right || y < frame.Top || y >= frame.Bottom) return true;
                var w = Describe(hWnd);
                if (w.ProcessId == self || w.Title.Length == 0) return true;
                if (SeeThrough(w.Process, w.Title, exStyle) || DesktopClasses.IsMatch(w.ClassName)) return true;
                found = w;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Все видимые окна — чтобы добавить рум.</summary>
        public static List<string> DescribeWindows(List<CompiledRule> rules)
        {
            var windows = Windows();
            var rooms = new Dictionary<IntPtr, string>();
            foreach (var t in Classify(windows, rules, null, null, null)) rooms[t.Handle] = t.Room;
            var lines = new List<string>();
            foreach (var w in windows)
            {
                string room;
                var mark = rooms.TryGetValue(w.Handle, out room) ? "   <= СТОЛ " + room : (w.Minimized ? "   (свёрнуто)" : "");
                lines.Add(string.Format("{0,-22} | {1,-30} | {2} | {3}{4}", w.Process, w.ClassName, w.Frame, w.Title, mark));
            }
            lines.Sort(StringComparer.OrdinalIgnoreCase);
            return lines;
        }
    }
}
