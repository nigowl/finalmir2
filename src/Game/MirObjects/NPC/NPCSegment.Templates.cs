using System.Globalization;
using System.Text.RegularExpressions;

namespace Server.MirObjects
{
    public partial class NPCSegment
    {
        private static readonly Regex TemplatePattern = new Regex(
            @"<\$(?<name>[A-Z_][A-Z0-9_]*)(?<call>\((?<args>[^<>()]*)\))?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex TemplateVariable = new Regex(@"^%?[A-Z][0-9]+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public string ReplaceValue(PlayerObject player, string text) => string.IsNullOrEmpty(text) ? text :
            TemplatePattern.Replace(text, match => PlayerTemplateValue(player, match));

        public string ReplaceValue(MonsterObject monster, string text) => string.IsNullOrEmpty(text) ? text :
            TemplatePattern.Replace(text, match => MonsterTemplateValue(monster, match));

        private static bool TryTemplateArguments(Match match, out string key, out string[] arguments)
        {
            var function = match.Groups["call"].Success;
            key = match.Groups["name"].Value.ToUpperInvariant() + (function ? "()" : "");
            arguments = function ? match.Groups["args"].Value.Split(',').Select(value => value.Trim()).ToArray() : [];
            var count = key switch
            {
                "OUTPUT()" or "MONSTERCOUNT()" or "CONQUESTOWNER()" or "CONQUESTGOLD()" or
                    "CONQUESTRATE()" or "CONQUESTSCHEDULE()" => 1,
                "CONQUESTGUARD()" or "CONQUESTGATE()" or "CONQUESTWALL()" or "CONQUESTSIEGE()" => 2,
                _ => function ? -1 : 0
            };
            return arguments.Length == count && arguments.All(value => value.Length > 0) &&
                (key != "OUTPUT()" || TemplateVariable.IsMatch(arguments[0]));
        }

        private static string TemplateArgumentKey(string value) => TemplateVariable.IsMatch(value)
            ? "%" + value.TrimStart('%').ToUpperInvariant() : value;

        private string TemplateArgument(MapObject actor, string value) => FindVariable(actor, TemplateArgumentKey(value));

        private static string CastleWarDate()
        {
            // 旧模板没有城池参数，只显示当前版本已实际排定的最近一场，不编造“两天后”。
            var now = Envir.Now;
            DateTime? next = null;
            foreach (var conquest in Envir.Conquests)
            {
                if (conquest.WarIsOn && conquest.StartType == ConquestType.Forced && conquest.WarEndTime > now)
                    return conquest.WarStartTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                var info = conquest.Info;
                if (info.Type == ConquestType.Forced || info.StartHour > 23 || info.WarLength <= 0 ||
                    info.Type == ConquestType.Request && conquest.GuildInfo.AttackerID == -1) continue;
                var days = new[] { info.Sunday, info.Monday, info.Tuesday, info.Wednesday, info.Thursday, info.Friday, info.Saturday };
                for (var offset = 0; offset <= 7; offset++)
                {
                    var date = now.Date.AddDays(offset).AddHours(info.StartHour);
                    if (!days[(int)date.DayOfWeek] || date < now && !(conquest.WarIsOn && date.AddMinutes(info.WarLength) > now)) continue;
                    if (!next.HasValue || date < next.Value) next = date;
                    break;
                }
            }
            return next?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ??
                GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.NoWarScheduled);
        }
    }
}
