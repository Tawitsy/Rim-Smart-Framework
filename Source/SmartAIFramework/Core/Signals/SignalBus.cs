using System;
using Verse;

namespace SmartAIFramework.Core.Signals
{
    public enum SignalPhase
    {
        Perception = 0,
        Reasoning  = 1,
        Telemetry  = 2,
    }

    public delegate void SignalHandler<T>(in T signal) where T : struct, ISignal;

    public static class SignalBus
    {
        private const int MaxDepth = 8;
        private static int depth;

        public static bool HasListeners<T>() where T : struct, ISignal => Channel<T>.handlers.Length > 0;

        public static void Subscribe<T>(SignalHandler<T> handler, SignalPhase phase = SignalPhase.Reasoning)
            where T : struct, ISignal
        {
            if (handler == null) return;
            var oldH = Channel<T>.handlers;
            var oldP = Channel<T>.phases;
            var newH = new SignalHandler<T>[oldH.Length + 1];
            var newP = new SignalPhase[oldP.Length + 1];

            int insert = oldH.Length;
            for (int i = 0; i < oldP.Length; i++)
                if (oldP[i] > phase) { insert = i; break; }

            for (int i = 0, j = 0; i < newH.Length; i++)
            {
                if (i == insert) { newH[i] = handler; newP[i] = phase; continue; }
                newH[i] = oldH[j]; newP[i] = oldP[j]; j++;
            }
            Channel<T>.phases = newP;
            Channel<T>.handlers = newH;
        }

        public static void Unsubscribe<T>(SignalHandler<T> handler) where T : struct, ISignal
        {
            var oldH = Channel<T>.handlers;
            int idx = Array.IndexOf(oldH, handler);
            if (idx < 0) return;
            var oldP = Channel<T>.phases;
            var newH = new SignalHandler<T>[oldH.Length - 1];
            var newP = new SignalPhase[oldP.Length - 1];
            for (int i = 0, j = 0; i < oldH.Length; i++)
            {
                if (i == idx) continue;
                newH[j] = oldH[i]; newP[j] = oldP[i]; j++;
            }
            Channel<T>.phases = newP;
            Channel<T>.handlers = newH;
        }

        public static void Publish<T>(in T signal) where T : struct, ISignal
        {
            var hs = Channel<T>.handlers;
            if (hs.Length == 0) return;

            if (depth >= MaxDepth)
            {
                Log.ErrorOnce($"[SmartAI] Signal depth limit hit while publishing {typeof(T).Name}. Possible signal loop.", typeof(T).GetHashCode());
                return;
            }

            depth++;
            try
            {
                for (int i = 0; i < hs.Length; i++)
                {
                    try { hs[i](in signal); }
                    catch (Exception e)
                    {
                        Log.ErrorOnce($"[SmartAI] Handler {hs[i].Method.DeclaringType?.Name}.{hs[i].Method.Name} threw on {typeof(T).Name}: {e}",
                            hs[i].Method.GetHashCode());
                    }
                }
            }
            finally { depth--; }
        }

        private static class Channel<T> where T : struct, ISignal
        {
            public static SignalHandler<T>[] handlers = new SignalHandler<T>[0];
            public static SignalPhase[] phases = new SignalPhase[0];
        }
    }
}
