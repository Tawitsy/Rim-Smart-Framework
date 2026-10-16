using System;
using System.IO;
using Verse;

namespace SmartAIFramework.Data
{
    public static class SmartLog
    {
        private static string logFilePath;
        private static bool headerWritten;
        private static readonly object syncRoot = new object();

        public static bool IsEnabled => SmartAIMod.Settings != null && SmartAIMod.Settings.debugLogging;

        public static void Initialize(string rootDir)
        {
            if (string.IsNullOrEmpty(rootDir)) return;
            logFilePath = Path.Combine(rootDir, "RimSmartFramework_Debug.log");
        }

        // Callers guard with IsEnabled so the message string is never built when logging is off.
        public static void Decision(string category, Pawn pawn, string text)
        {
            Message(category, $"{pawn?.LabelShort ?? "?"}: {text}");
        }

        public static void Message(string category, string text)
        {
            if (!IsEnabled) return;

            Verse.Log.Message($"[{category}] {text}");

            if (string.IsNullOrEmpty(logFilePath)) return;

            try
            {
                lock (syncRoot)
                {
                    if (!headerWritten)
                    {
                        headerWritten = true;
                        File.WriteAllText(logFilePath,
                            $"=== Rim Smart Framework Debug Log ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ===\n");
                    }
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {text}\n");
                }
            }
            catch
            {
            }
        }
    }
}
