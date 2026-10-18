using System;
using System.Collections.Generic;

namespace TableManager
{
    /// <summary>Прямоугольник в физических пикселях экрана.</summary>
    public struct Box
    {
        public int X, Y, W, H;

        public Box(int x, int y, int w, int h)
        {
            X = x; Y = y; W = w; H = h;
        }

        public int Right { get { return X + W; } }
        public int Bottom { get { return Y + H; } }
        public double CenterX { get { return X + W / 2.0; } }
        public double CenterY { get { return Y + H / 2.0; } }

        public static Box From(RECT r)
        {
            return new Box(r.Left, r.Top, r.Width, r.Height);
        }

        public bool Contains(Box other, int tolerance)
        {
            return other.X >= X - tolerance && other.Y >= Y - tolerance
                && other.Right <= Right + tolerance && other.Bottom <= Bottom + tolerance;
        }

        public bool Near(Box other, int tolerance)
        {
            return Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance
                && Math.Abs(W - other.W) <= tolerance && Math.Abs(H - other.H) <= tolerance;
        }

        public override string ToString()
        {
            return string.Format("{0},{1} {2}x{3}", X, Y, W, H);
        }
    }

    /// <summary>Чистая геометрия раскладки: ячейки зоны и место стола в ячейке.</summary>
    public static class Layout
    {
        /// <summary>Сетка columns × rows в области; gap — зазор между ячейками.
        /// Порядок ячеек — по строкам слева направо.</summary>
        public static List<Box> Cells(Box area, int columns, int rows, int gap)
        {
            var cells = new List<Box>();
            if (columns < 1 || rows < 1) return cells;
            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < columns; c++)
                {
                    // Границы считаются от краёв области, чтобы сумма ячеек
                    // ровно покрывала её без накопления округлений.
                    var x0 = area.X + (int)Math.Round((double)area.W * c / columns);
                    var x1 = area.X + (int)Math.Round((double)area.W * (c + 1) / columns);
                    var y0 = area.Y + (int)Math.Round((double)area.H * r / rows);
                    var y1 = area.Y + (int)Math.Round((double)area.H * (r + 1) / rows);
                    var left = c == 0 ? 0 : gap / 2;
                    var right = c == columns - 1 ? 0 : gap - gap / 2;
                    var top = r == 0 ? 0 : gap / 2;
                    var bottom = r == rows - 1 ? 0 : gap - gap / 2;
                    cells.Add(new Box(x0 + left, y0 + top, x1 - x0 - left - right, y1 - y0 - top - bottom));
                }
            }
            return cells;
        }

        /// <summary>Ячейки зоны в порядке заполнения (см. Zone.Order).
        /// rowMajor — как вернул Cells: по строкам слева направо.</summary>
        public static List<Box> Ordered(List<Box> rowMajor, int columns, int rows, string order)
        {
            if (rowMajor.Count != columns * rows) return new List<Box>(rowMajor);
            var result = new List<Box>(rowMajor.Count);
            switch ((order ?? "rows").ToLowerInvariant())
            {
                case "rows-rtl":
                    for (var r = 0; r < rows; r++)
                        for (var c = columns - 1; c >= 0; c--) result.Add(rowMajor[r * columns + c]);
                    break;
                case "columns":
                    for (var c = 0; c < columns; c++)
                        for (var r = 0; r < rows; r++) result.Add(rowMajor[r * columns + c]);
                    break;
                case "columns-rtl":
                    for (var c = columns - 1; c >= 0; c--)
                        for (var r = 0; r < rows; r++) result.Add(rowMajor[r * columns + c]);
                    break;
                default:
                    result.AddRange(rowMajor);
                    break;
            }
            return result;
        }

        /// <summary>Наибольший прямоугольник с пропорцией aspect (ширина/высота),
        /// вписанный в ячейку. align: "center" или "topleft".</summary>
        public static Box Fit(Box cell, double aspect, string align)
        {
            if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect)) aspect = 4.0 / 3.0;
            var w = cell.W;
            var h = (int)Math.Round(w / aspect);
            if (h > cell.H)
            {
                h = cell.H;
                w = (int)Math.Round(h * aspect);
            }
            if (align == "topleft") return new Box(cell.X, cell.Y, w, h);
            return new Box(cell.X + (cell.W - w) / 2, cell.Y + (cell.H - h) / 2, w, h);
        }

        /// <summary>Ячейка, в которую попадает точка (центр брошенного стола), или -1.</summary>
        public static int SlotAt(IList<Box> slots, double x, double y)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (x >= s.X && x < s.Right && y >= s.Y && y < s.Bottom) return i;
            }
            return -1;
        }
    }

    /// <summary>Постоянные ячейки: стол занимает ячейку, пока открыт; новый стол
    /// встаёт в первую свободную, уже стоящие не двигаются.</summary>
    public class SlotTracker
    {
        private readonly Dictionary<IntPtr, int> _slotOf = new Dictionary<IntPtr, int>();

        public int Count { get { return _slotOf.Count; } }

        public bool TryGet(IntPtr window, out int slot)
        {
            return _slotOf.TryGetValue(window, out slot);
        }

        public bool IsTaken(int slot)
        {
            return _slotOf.ContainsValue(slot);
        }

        /// <summary>Чьё окно в ячейке, или IntPtr.Zero.</summary>
        public IntPtr OwnerOf(int slot)
        {
            foreach (var pair in _slotOf)
            {
                if (pair.Value == slot) return pair.Key;
            }
            return IntPtr.Zero;
        }

        /// <summary>Переставить окно в ячейку; если она занята — окна меняются
        /// местами. Возвращает окно, которое переехало на старое место, или Zero.</summary>
        public IntPtr MoveTo(IntPtr window, int slot)
        {
            int was;
            if (!_slotOf.TryGetValue(window, out was) || was == slot) return IntPtr.Zero;
            var other = OwnerOf(slot);
            _slotOf[window] = slot;
            if (other != IntPtr.Zero) _slotOf[other] = was;
            return other;
        }

        /// <summary>Первая свободная ячейка или -1, если свободных нет.</summary>
        public int AssignFirstFree(IntPtr window, int slotCount)
        {
            int existing;
            if (_slotOf.TryGetValue(window, out existing)) return existing;
            for (var s = 0; s < slotCount; s++)
            {
                if (!IsTaken(s))
                {
                    _slotOf[window] = s;
                    return s;
                }
            }
            return -1;
        }

        public void Release(IntPtr window)
        {
            _slotOf.Remove(window);
        }

        /// <summary>Забыть закрытые окна и ячейки, которых больше нет.</summary>
        public void Prune(ICollection<IntPtr> alive, int slotCount)
        {
            var dead = new List<IntPtr>();
            foreach (var pair in _slotOf)
            {
                if (!alive.Contains(pair.Key) || pair.Value >= slotCount) dead.Add(pair.Key);
            }
            foreach (var w in dead) _slotOf.Remove(w);
        }

        public void Clear()
        {
            _slotOf.Clear();
        }

        public void Set(IntPtr window, int slot)
        {
            _slotOf[window] = slot;
        }

        public SlotTracker Clone()
        {
            var copy = new SlotTracker();
            foreach (var pair in _slotOf) copy._slotOf[pair.Key] = pair.Value;
            return copy;
        }

        /// <summary>Смена сетки (другой уровень): окно, чья ячейка есть и в новой
        /// сетке (та же геометрия), остаётся в ней; остальные сжимаются по порядку —
        /// в порядке старых ячеек занимают первые свободные новые ячейки (порядок
        /// заполнения зон сохраняется). Возвращает окна, которым нужно переехать;
        /// окна без места остаются без ячейки.</summary>
        public List<IntPtr> Remap(IList<Box> oldSlots, IList<Box> newSlots, IList<IntPtr> windows)
        {
            var old = new Dictionary<IntPtr, int>(_slotOf);
            _slotOf.Clear();
            var pending = new List<KeyValuePair<IntPtr, Box>>();
            foreach (var w in windows)
            {
                int s;
                if (!old.TryGetValue(w, out s) || s < 0 || s >= oldSlots.Count) continue;
                var same = -1;
                for (var j = 0; j < newSlots.Count; j++)
                {
                    if (newSlots[j].Near(oldSlots[s], 1) && !IsTaken(j))
                    {
                        same = j;
                        break;
                    }
                }
                if (same >= 0) _slotOf[w] = same;
                else pending.Add(new KeyValuePair<IntPtr, Box>(w, oldSlots[s]));
            }
            pending.Sort((a, b) => old[a.Key].CompareTo(old[b.Key]));
            var next = 0;
            foreach (var p in pending)
            {
                while (next < newSlots.Count && IsTaken(next)) next++;
                if (next >= newSlots.Count) break;
                _slotOf[p.Key] = next++;
            }
            var moved = new List<IntPtr>();
            foreach (var p in pending)
            {
                if (_slotOf.ContainsKey(p.Key)) moved.Add(p.Key);
            }
            return moved;
        }

        /// <summary>Как AssignNearest, но занятые ячейки и уже назначенные окна
        /// не трогаются.</summary>
        public void AssignNearestFree(IList<KeyValuePair<IntPtr, Box>> windows, IList<Box> slots)
        {
            var pairs = new List<Tuple<double, int, int>>();
            for (var w = 0; w < windows.Count; w++)
            {
                if (_slotOf.ContainsKey(windows[w].Key)) continue;
                for (var s = 0; s < slots.Count; s++)
                {
                    if (IsTaken(s)) continue;
                    var dx = windows[w].Value.CenterX - slots[s].CenterX;
                    var dy = windows[w].Value.CenterY - slots[s].CenterY;
                    pairs.Add(Tuple.Create(dx * dx + dy * dy, w, s));
                }
            }
            pairs.Sort((a, b) =>
            {
                var byDistance = a.Item1.CompareTo(b.Item1);
                if (byDistance != 0) return byDistance;
                var bySlot = a.Item3.CompareTo(b.Item3);
                return bySlot != 0 ? bySlot : a.Item2.CompareTo(b.Item2);
            });
            var usedWindow = new HashSet<int>();
            var usedSlot = new HashSet<int>();
            foreach (var p in pairs)
            {
                if (usedWindow.Contains(p.Item2) || usedSlot.Contains(p.Item3)) continue;
                usedWindow.Add(p.Item2);
                usedSlot.Add(p.Item3);
                _slotOf[windows[p.Item2].Key] = p.Item3;
            }
        }

        /// <summary>Расставить заново все окна: каждое — в ближайшую свободную
        /// ячейку к его нынешнему центру, чтобы столы сдвигались как можно меньше.
        /// Окна без места остаются без ячейки.</summary>
        public void AssignNearest(IList<KeyValuePair<IntPtr, Box>> windows, IList<Box> slots)
        {
            _slotOf.Clear();
            // Сначала пары с наименьшим расстоянием — жадно, но устойчиво.
            var pairs = new List<Tuple<double, int, int>>();
            for (var w = 0; w < windows.Count; w++)
            {
                for (var s = 0; s < slots.Count; s++)
                {
                    var dx = windows[w].Value.CenterX - slots[s].CenterX;
                    var dy = windows[w].Value.CenterY - slots[s].CenterY;
                    pairs.Add(Tuple.Create(dx * dx + dy * dy, w, s));
                }
            }
            pairs.Sort((a, b) =>
            {
                var byDistance = a.Item1.CompareTo(b.Item1);
                if (byDistance != 0) return byDistance;
                var bySlot = a.Item3.CompareTo(b.Item3);
                return bySlot != 0 ? bySlot : a.Item2.CompareTo(b.Item2);
            });
            var usedWindow = new HashSet<int>();
            var usedSlot = new HashSet<int>();
            foreach (var p in pairs)
            {
                if (usedWindow.Contains(p.Item2) || usedSlot.Contains(p.Item3)) continue;
                usedWindow.Add(p.Item2);
                usedSlot.Add(p.Item3);
                _slotOf[windows[p.Item2].Key] = p.Item3;
            }
        }
    }
}

namespace TableManager
{
    /// <summary>Выбор уровня раскладки по числу столов.</summary>
    public static class Levels
    {
        /// <summary>Сколько столов вмещает уровень: MaxTables, но не больше ячеек.</summary>
        public static int Capacity(int maxTables, int slotCount)
        {
            return maxTables > 0 ? Math.Min(maxTables, slotCount) : slotCount;
        }

        /// <summary>Первый уровень, куда помещаются все столы (иначе последний).</summary>
        public static int For(int tables, IList<int> capacities)
        {
            for (var i = 0; i < capacities.Count; i++)
            {
                if (tables <= capacities[i]) return i;
            }
            return capacities.Count - 1;
        }

        /// <summary>Уровень после изменения числа столов. Вверх — как только не
        /// помещаются; вниз — только когда столов стало на hysteresis меньше
        /// вместимости нижнего уровня, чтобы на границе сетка не прыгала.</summary>
        public static int Next(int current, int tables, IList<int> capacities, int hysteresis)
        {
            var level = current;
            while (level < capacities.Count - 1 && tables > capacities[level]) level++;
            if (level == current && level > 0 && tables <= capacities[level - 1] - hysteresis) level--;
            return level;
        }
    }
}
