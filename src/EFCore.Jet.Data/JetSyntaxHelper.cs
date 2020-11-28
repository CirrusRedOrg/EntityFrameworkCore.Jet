using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EntityFrameworkCore.Jet.Data
{
    static class JetSyntaxHelper
    {
        public static string ReplaceCaseInsensitive(this string s, string oldValue, string newValue)
        {
            return Regex.Replace(s, oldValue, newValue, RegexOptions.IgnoreCase);
        }

        public static string ToSqlString(string value)
        {
            // In Jet everything's unicode
            return $"'{value.Replace("'", "''")}'";
        }

        public static string ToSqlString(int value)
        {
            return value.ToString(NumberFormatInfo.InvariantInfo);
        }

        public static string ToSqlString(byte[] value)
        {
            return " 0x" + ByteArrayToBinaryString((Byte[])value) + " ";
        }

        public static string ToSqlString(bool value)
        {
            return value ? "true" : "false";
        }

        public static string ToSqlStringSwitch(object value)
        {
            string output;
            switch (value)
            {
                case bool b:
                    output = ToSqlString(b);
                    break;
                case int i:
                    output = ToSqlString(i);
                    break;
                case string str:
                    output = ToSqlString(str);
                    break;
                case Guid guid:
                    output = ToSqlString(guid);
                    break;
                case byte[] byteArray:
                    output = ToSqlString(byteArray);
                    break;
                default:
                    throw new NotSupportedException();
            }
            return output;
        }

        static string ByteArrayToBinaryString(byte[] binaryArray)
        {
            StringBuilder sb = new StringBuilder(binaryArray.Length * 2);

            foreach (byte b in binaryArray)
                sb.Append(b.ToString("X2"));

            return sb.ToString();
        }

        public static string ToSqlString(Guid value)
        {
            // In Jet everything's unicode
            return "'P" + value.ToString() + "'";
        }


        /// <summary>
        /// Quotes an identifier
        /// </summary>
        /// <param name="name">Identifier name</param>
        /// <returns>The quoted identifier</returns>
        internal static string QuoteIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            return $"`{name}`";
        }

        /// <summary>
        /// Function to detect wildcard characters * ? [ #) and escape them
        /// This escaping is used when StartsWith, EndsWith and Contains canonical and CLR functions
        /// are translated to their equivalent LIKE expression
        /// </summary>
        /// <param name="text">Original input as specified by the user</param>
        /// <param name="usedEscapeChar">true if the escaping was performed, false if no escaping was required</param>
        /// <returns>The escaped string that can be used as pattern in a LIKE expression</returns>
        internal static string EscapeLikeText(string text, out bool usedEscapeChar)
        {
            usedEscapeChar = false;
            if (
                !(text.Contains("*") || text.Contains("?") || text.Contains("[") || text.Contains("#"))
            )
                return text;


            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (
                    c == '*' || c == '?' || c == '[' || c == '#'
                )
                {
                    sb.AppendFormat("[{0}]", c);
                    usedEscapeChar = true;
                }
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }


    }
}
