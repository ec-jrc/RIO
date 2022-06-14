using System;
using System.Collections.Generic;
using System.Linq;

namespace RIO
{
    internal class CronParser
    {
        private static string ParseNumber(string s, string name)
        {
            if (s.Contains('-'))
            {   // Range
                string[] parts = s.Split('-');
                if (parts.Length == 2 && parts[0].ToInt(out int low) && parts[1].ToInt(out int high))
                    return $"{name} >= {low} and {name} <= {high}";
            }
            if (s.Contains('/'))
            {   // Range
                string[] parts = s.Split('/');
                if (parts[0] == "*") parts[0] = "0";
                if (parts.Length == 2 && parts[0].ToInt(out int start) && parts[1].ToInt(out int interval))
                    return $"{name} % {interval} = {start % interval}";
            }
            return $"{name} = {s}";
        }
        public static (Rule, string) Parse(string cronLine)
        {
            TimeSpan timeTrigger = TimeSpan.FromDays(300);
            string[] parts = cronLine.Split(' ');
            if (parts.Length < 8)
                return (null, null);
            List<string> clauses = new List<string>();
            if (parts[0] != "*") // second match
            {
                clauses.Add(string.Join(" OR ", parts[0].Split(',').Select(s => ParseNumber(s, "utc.second"))));
                timeTrigger = TimeSpan.FromSeconds(1);
            }
            if (parts[1] != "*") // minute match
            {
                clauses.Add(string.Join(" OR ", parts[1].Split(',').Select(s => ParseNumber(s, "utc.minute"))));
                timeTrigger = TimeSpan.FromSeconds(Math.Min(timeTrigger.TotalSeconds, 60));
            }
            if (parts[2] != "*") // hour match
            {
                clauses.Add(string.Join(" OR ", parts[2].Split(',').Select(s => ParseNumber(s, "utc.hour"))));
                timeTrigger = TimeSpan.FromSeconds(Math.Min(timeTrigger.TotalSeconds, 3600));
            }
            if (parts[3] != "*") // day-of-week match
            {
                List<string> patterns = new List<string>();
                foreach (string pattern in parts[3].Split(','))
                {
                    if(pattern.TryToDayOfWeek(true, out DayOfWeek day))
                        patterns.Add($"equal(utc.dayofweek, {(int)day})");
                    else throw new Exception($"Unrecognized day of week: {pattern}");
                }
                clauses.Add(string.Join(" OR ", patterns));
                timeTrigger = TimeSpan.FromSeconds(Math.Min(timeTrigger.TotalSeconds, 86400));
            }
            if (parts[4] != "*") // day number match
            {
                clauses.Add(string.Join(" OR ", parts[2].Split(',').Select(s => ParseNumber(s, "utc.day"))));
                timeTrigger = TimeSpan.FromSeconds(Math.Min(timeTrigger.TotalSeconds, 86400));
            }
            if (parts[5] != "*") // month match
            {
                clauses.Add(string.Join(" OR ", parts[2].Split(',').Select(s => $"utc.month = {s}")));
                timeTrigger = TimeSpan.FromSeconds(Math.Min(timeTrigger.TotalSeconds, TimeSpan.FromDays(31).TotalSeconds));
            }
            if (parts[6] != "*" && parts[6].ToInt(out int secondsWait)) // explicit timeTrigger
            {
                timeTrigger = TimeSpan.FromSeconds(Math.Min(timeTrigger.TotalSeconds, secondsWait));
            }
            string command = parts[parts.Length - 1];
            if (parts.Length > 8)
                clauses.Add(string.Join(" ", parts.Skip(7).Take(parts.Length - 8)));

            Rule rule = new Rule()
            {
                Id = System.Guid.NewGuid().ToString("N"),
                Expression = string.Format("({0})", string.Join(") AND (", clauses)),
                TimeTrigger = timeTrigger
            };
            return (rule, command);
        }
    }
}
