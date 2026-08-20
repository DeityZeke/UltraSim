using System;
using System.Reflection;

namespace Server.Tests
{
    internal static class ServerTestRuntime
    {
        public static void ResetStatics()
        {
            InvokeStaticClear("UltraSim.ECS.ComponentManager");
            InvokeStaticClear("UltraSim.ECS.PrefabRegistry");
        }

        private static void InvokeStaticClear(string typeName)
        {
            Type type = typeof(UltraSim.ECS.World).Assembly.GetType(typeName, throwOnError: true)!;
            MethodInfo clear = type.GetMethod("Clear", BindingFlags.Static | BindingFlags.NonPublic)!;
            clear.Invoke(null, null);
        }
    }
}
