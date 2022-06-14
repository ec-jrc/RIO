using Flee.PublicTypes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace RIO
{
    /// <summary>
    /// This class defines a set of actions to be taken in case a condition is met.
    /// The condition is a boolean expression, evaluated with a set of variable including the <see cref="Settings"/>,
    /// the telemetry sent by the device itself and the info received through the alert channel.
    /// </summary>
    [DebuggerDisplay("{Expression}, {Actions.Count} actions")]
    public class Rule
    {
        private DateTime retrigger = DateTime.MinValue;
        /// <summary>
        /// The amount of time during which the Rule condition is not evaluated, returning null.
        /// </summary>
        public TimeSpan TimeTrigger { get; set; } = TimeSpan.Zero;
        /// <summary>
        /// An univoque identificator for the Rule, possibily a <see cref="Guid"/>.
        /// </summary>
        public string Id { get; set; }
        string expression;
        /// <summary>
        /// Boolean expression that may include any type of variable and constants and the <see cref="System"/>
        /// classes methods, e.g. <see cref="Math"/> or <see cref="string"/> methods.
        /// </summary>
        public string Expression
        {
            get => expression;
            protected internal set
            {
                expression = value;
            }
        }
        /// <summary>
        /// List of the <see cref="Execution"/> that must be performed in case the condition evaluation returned <code>true</code>.
        /// It is not json serialized.
        /// </summary>
        [JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public IEnumerable<Execution> Actions { get; internal set; }
        /// <summary>
        /// List of the names of the <see cref="Execution"/> list to be performed.
        /// It is json serialized as <code>Actions</code>.
        /// </summary>
        [JsonProperty("Actions")]
        [System.Text.Json.Serialization.JsonPropertyName("Actions")]
        public string ActionList { get; protected internal set; }

        /// <summary>
        /// The last time the rule was updated by the server.
        /// </summary>
        public DateTime Updated { get; set; }

        internal bool? Condition(Dictionary<string, object> knowledge)
        {
            if (DateTime.UtcNow < retrigger)
                return null;

            ExpressionContext context = new ExpressionContext();
            context.Imports.AddType(typeof(CustomFunctions));
            context.Variables.AddRange<string, object>(knowledge);
            context.Variables["utc"] = DateTime.UtcNow;
            context.Variables["local"] = DateTime.Now;
            IDynamicExpression e = context.CompileDynamic(expression);

            object result = e.Evaluate();
            bool retValue = (bool)result;
            if (retValue)
                retrigger = DateTime.UtcNow + TimeTrigger;

            return retValue;
        }
        /// <summary>
        /// Set of functions used in <see cref="Expression"/> to implement more easily <see cref="DateTime"/> comparisons.
        /// </summary>
        public static class CustomFunctions
        {
            /// <summary>
            /// Compares the name of the day with a zero-based index starting from <see cref="DayOfWeek.Sunday"/>.
            /// </summary>
            /// <param name="day">Full name of the day in <see cref="System.Globalization.CultureInfo.InvariantCulture"/></param>
            /// <param name="i">Zero-based index</param>
            /// <returns>True if equals</returns>
            public static bool Equal(DayOfWeek day, int i)
            {
                return ((int)day) == i;
            }
            /// <summary>
            /// Compares the numeric index with another expressed by a string.
            /// </summary>
            /// <param name="s">Index in string format</param>
            /// <param name="i">Numeric index</param>
            /// <returns>True if equals</returns>
            public static bool Equal(string s, int i)
            {
                if (int.TryParse(s, out int e))
                    return e == i;
                else
                    return i.ToString().Equals(s);
            }
            /// <summary>
            /// Compares the numeric index with another expressed by a string.
            /// </summary>
            /// <param name="s">Index in string format</param>
            /// <param name="i">Numeric index</param>
            /// <returns>True if the numeric index is less than the other</returns>
            public static bool GreaterThan(string s, int i)
            {
                if (int.TryParse(s, out int e))
                    return e > i;
                else
                    return i.ToString().CompareTo(s) < 0;
            }
            /// <summary>
            /// Compares the numeric index with another expressed by a string.
            /// </summary>
            /// <param name="s">Index in string format</param>
            /// <param name="i">Numeric index</param>
            /// <returns>True if the numeric index is greater than the other</returns>
            public static bool LessThan(string s, int i)
            {
                if (int.TryParse(s, out int e))
                    return e < i;
                else
                    return i.ToString().CompareTo(s) > 0;
            }
        }
    }
}