using System.Collections.Generic;
using System.Text;

namespace Starline.Probe
{
    /// <summary>One check: whether it passed, how long it took, and what was observed.</summary>
    public sealed class ProbeItem
    {
        public string Name;
        public bool Ok;
        public long Ms;
        public string Detail;
    }

    /// <summary>The result of a probe run. Returned as-is by the cloud function and printed by the local runner.</summary>
    public sealed class ProbeReport
    {
        public string Probe;
        public bool ColdStart;
        public long ProcessUptimeMs;
        public int InvocationInProcess;
        public string Runtime;
        public List<ProbeItem> Items = new List<ProbeItem>();

        public bool AllOk => Items.TrueForAll(i => i.Ok);

        public ProbeItem Add(string name, bool ok, long ms, string detail)
        {
            var item = new ProbeItem { Name = name, Ok = ok, Ms = ms, Detail = detail };
            Items.Add(item);
            return item;
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"probe\": ").Append(Quote(Probe)).Append(",\n");
            sb.Append("  \"allOk\": ").Append(AllOk ? "true" : "false").Append(",\n");
            sb.Append("  \"coldStart\": ").Append(ColdStart ? "true" : "false").Append(",\n");
            sb.Append("  \"processUptimeMs\": ").Append(ProcessUptimeMs).Append(",\n");
            sb.Append("  \"invocationInProcess\": ").Append(InvocationInProcess).Append(",\n");
            sb.Append("  \"runtime\": ").Append(Quote(Runtime)).Append(",\n");
            sb.Append("  \"items\": [\n");
            for (int i = 0; i < Items.Count; i++)
            {
                ProbeItem item = Items[i];
                sb.Append("    { \"name\": ").Append(Quote(item.Name))
                  .Append(", \"ok\": ").Append(item.Ok ? "true" : "false")
                  .Append(", \"ms\": ").Append(item.Ms)
                  .Append(", \"detail\": ").Append(Quote(item.Detail))
                  .Append(i + 1 < Items.Count ? " },\n" : " }\n");
            }
            sb.Append("  ]\n}");
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            if (s == null)
            {
                return "null";
            }
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
