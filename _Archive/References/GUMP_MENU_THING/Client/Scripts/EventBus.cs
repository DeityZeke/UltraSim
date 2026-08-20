
#nullable enable

using System;

using Godot;

namespace Client
{
    public static class EventBus
    {
        public static event Action? OnLoginSuccess;
        public static event Action<string>? OnLoginFailure;

        public static void RaiseLoginSuccess() => OnLoginSuccess?.Invoke();
        public static void RaiseLoginFailure(string reason) => OnLoginFailure?.Invoke(reason);
    }
}