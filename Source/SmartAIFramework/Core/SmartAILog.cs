using System;
using System.IO;
using Verse;

namespace SmartAIFramework.Core
{
    public static class SmartAILog
    {
        private static string logFilePath;
        private static readonly object lockObj = new object();

        public static void Initialize(string rootDir)
        {
            try
            {
                logFilePath = Path.Combine(rootDir, "SmartAI_Telemetry.log");
                lock (lockObj)
                {
                    File.WriteAllText(logFilePath, $"================================================================\n" +
                                                  $"[SmartAI Telemetry Session Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n" +
                                                  $"Mod: SmartAIFramework 1.6 (Universal Cognitive AI)\n" +
                                                  $"================================================================\n\n");
                }
            }
            catch (Exception ex)
            {
                Verse.Log.Warning($"[SmartAI] Failed to init SmartAILog: {ex.Message}");
            }
        }

        public static void Log(string tag, string message)
        {
            if (SmartAIMod.Settings?.debugLogging != true) return;

            Verse.Log.Message($"[{tag}] {message}");

            if (string.IsNullOrEmpty(logFilePath)) return;

            try
            {
                lock (lockObj)
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    File.AppendAllText(logFilePath, $"[{timestamp}] [{tag}] {message}\n");
                }
            }
            catch { }
        }

        public static void LogDecision(Pawn pawn, string category, string action, string rationale)
        {
            string pawnName = pawn?.LabelShort ?? "Unknown";
            string faction = pawn?.Faction?.Name ?? "Neutral";
            Log(category, $"Pawn: {pawnName} ({faction}) -> Action: {action} | Rationale: {rationale}");
        }
    }
}
