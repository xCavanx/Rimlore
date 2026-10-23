using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ColonyChronicle
{
    public static class Json
    {
        public static string Serialize(object value)
        {
            var sb = new StringBuilder(256);
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case float f:
                    sb.Append(float.IsNaN(f) || float.IsInfinity(f) ? "null" : f.ToString("0.###", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("0.###", CultureInfo.InvariantCulture));
                    break;
                case int or long or short or byte:
                    sb.Append(System.Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object> dict:
                    sb.Append('{');
                    bool firstKey = true;
                    foreach (var kv in dict)
                    {
                        if (!firstKey) sb.Append(',');
                        firstKey = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    WriteString(sb, value.ToString());
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    case '<': sb.Append("\\u003c"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
