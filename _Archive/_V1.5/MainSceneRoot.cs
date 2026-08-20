using Godot;

namespace Client.Godot
{
    [GlobalClass]
    public partial class MainSceneRoot : Node
    {
        [Export(PropertyHint.File, "*.tscn")]
        public string ClientLiteScenePath { get; set; } = "res://Client.Godot/_Scenes/client.tscn";

        [Export(PropertyHint.File, "*.tscn")]
        public string ClientFullScenePath { get; set; } = "res://Client.Godot/_Scenes/client.tscn";

        [Export(PropertyHint.File, "*.tscn")]
        public string ServerScenePath { get; set; } = "res://Server.Godot/_Scenes/server_runtime_host.tscn";

        [Export(PropertyHint.File, "*.tscn")]
        public string LegacyHarnessScenePath { get; set; } = "res://_tmp/island_slice.tscn";

        public override void _Ready()
        {
            CallDeferred(MethodName.SwitchToTargetScene);
        }

        private void SwitchToTargetScene()
        {
            string targetScene = ResolveTargetScene();
            if (!ResourceLoader.Exists(targetScene))
            {
                GD.PushError($"MainSceneRoot could not find target scene: {targetScene}");
                GetTree().Quit(1);
                return;
            }

            Error error = GetTree().ChangeSceneToFile(targetScene);
            if (error != Error.Ok)
            {
                GD.PushError($"MainSceneRoot failed to load scene '{targetScene}': {error}");
                GetTree().Quit(1);
            }
        }

        private string ResolveTargetScene()
        {
            // Server builds always route to the tracked server scene.
            // The legacy harness check must not intercept server builds even if _tmp is included
            // in the export and legacy harness flags (--server, --bot) are present.
            if (OS.HasFeature("server_build"))
                return ServerScenePath;

            if (ShouldUseLegacyHarnessScene() && ResourceLoader.Exists(LegacyHarnessScenePath))
                return LegacyHarnessScenePath;

            if (OS.HasFeature("client_lite"))
                return ClientLiteScenePath;

            if (OS.HasFeature("client_full"))
                return ClientFullScenePath;

            return ClientFullScenePath;
        }

        private static bool ShouldUseLegacyHarnessScene()
        {
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--bot", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "--client", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "--server", System.StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--connect=", System.StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--bot=", System.StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--client=", System.StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--server=", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
