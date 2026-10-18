using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TableManager
{
    /// <summary>Правило рума по указанному столу: «этот и такие же окна — столы».
    /// Только строки и регулярные выражения — окна сюда приходят уже прочитанными.</summary>
    public static class RuleBuilder
    {
        /// <summary>Признаки покерного стола в заголовке: игра, блайнды, ставки.</summary>
        public const string GameMarkers =
            @"Hold'?em|Холдем|Omaha|Омаха|\bNLH|\bPLO|Blinds|Блайнды|Table \d|Стол \d|\d\s?(?:р\.)?\s?/\s?[$€₽₮£]?\d";

        private static readonly Regex Markers = new Regex(GameMarkers, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Numbers = new Regex(@"\d+(?:[\s.,:]\d+)*");

        /// <summary>Заголовок → шаблон, в котором числа (балансы, время, номера
        /// раздач) могут быть любыми: «Balance: $72.22» подойдёт и для «$1,234.50».</summary>
        public static string Generalize(string title)
        {
            var sb = new StringBuilder();
            var at = 0;
            foreach (Match m in Numbers.Matches(title))
            {
                sb.Append(Regex.Escape(title.Substring(at, m.Index - at)));
                sb.Append(@"[\d\s.,:]+");
                at = m.Index + m.Length;
            }
            sb.Append(Regex.Escape(title.Substring(at)));
            return sb.ToString();
        }

        /// <summary>Шаблон «ровно такой заголовок (числа любые)».</summary>
        public static string Exact(string title)
        {
            return "^" + Generalize(title) + "$";
        }

        public static string NameFor(string process)
        {
            return process + " (указан)";
        }

        /// <summary>Правило для указанного стола. siblings — другие окна той же
        /// программы, которые сейчас не столы (лобби, кассы…), и свёрнутые тоже.
        /// Возвращает null, если стол ничем не отличается от остальных окон —
        /// тогда двигать можно только сам этот стол.</summary>
        public static RoomRule ForTable(TableWindow table, IList<TableWindow> siblings, out string description)
        {
            var rule = new RoomRule
            {
                Name = NameFor(table.Process),
                Process = "^" + Regex.Escape(table.Process) + "$",
                Class = "^" + Regex.Escape(table.ClassName) + "$",
                Table = "",
                Exclude = "",
                Lobby = "",
                Sample = table.Title,
            };
            var sameClass = siblings.Where(w => w.ClassName == table.ClassName).ToList();

            // 1. Остальные окна программы другого типа (CoinPoker: стол — Unity, лобби — Chrome).
            if (sameClass.Count == 0)
            {
                description = "все окна " + table.Process + " типа " + table.ClassName;
                return rule;
            }

            // 2. В заголовке стола есть игра или ставки, а у остальных окон — нет.
            if (Markers.IsMatch(table.Title) && !sameClass.Any(w => Markers.IsMatch(w.Title)))
            {
                rule.Table = GameMarkers;
                rule.Exclude = "Lobby|Лобби";
                description = "окна " + table.Process + " с игрой или ставками в заголовке (Hold'em, NLH, 0.02/0.05…)";
                return rule;
            }

            // 3. Все окна программы, кроме тех, что открыты сейчас и не столы.
            var exclude = string.Join("|", sameClass.Select(w => Exact(w.Title)).Distinct().ToArray());
            if (!Regex.IsMatch(table.Title, exclude, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                rule.Exclude = exclude;
                description = "все окна " + table.Process + ", кроме «"
                    + string.Join("», «", sameClass.Select(w => Short(w.Title)).Distinct().ToArray()) + "»";
                return rule;
            }

            // 4. Заголовки одинаковые (QQPK): лобби — самое большое окно.
            if (sameClass.Any(w => w.Area > table.Area))
            {
                rule.Lobby = "largest";
                description = "все окна " + table.Process + ", кроме самого большого (лобби)";
                return rule;
            }

            description = null;
            return null;
        }

        /// <summary>Добавить шаблон в Exclude правила.</summary>
        public static void AddExclude(RoomRule rule, string pattern)
        {
            rule.Exclude = string.IsNullOrEmpty(rule.Exclude) ? pattern : rule.Exclude + "|" + pattern;
        }

        public static string Short(string title)
        {
            title = title.Trim();
            return title.Length > 50 ? title.Substring(0, 50) + "…" : title;
        }
    }
}
