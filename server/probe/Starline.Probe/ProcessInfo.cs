using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Starline.Probe
{
    /// <summary>
    /// Tells a cold start from a warm one: the first call in a fresh process sees invocation 1
    /// and a small uptime. Static state lives as long as the function instance does.
    /// </summary>
    public static class ProcessInfo
    {
        private static readonly Stopwatch Uptime = Stopwatch.StartNew();
        private static int _invocations;

        public static void Stamp(ProbeReport report, string probe)
        {
            int n = Interlocked.Increment(ref _invocations);
            report.Probe = probe;
            report.InvocationInProcess = n;
            report.ColdStart = n == 1;
            report.ProcessUptimeMs = Uptime.ElapsedMilliseconds;
            report.Runtime = RuntimeInformation.FrameworkDescription + " / " + RuntimeInformation.OSDescription
                + " / " + RuntimeInformation.ProcessArchitecture + " / " + Environment.ProcessorCount + " CPU";
        }
    }
}
