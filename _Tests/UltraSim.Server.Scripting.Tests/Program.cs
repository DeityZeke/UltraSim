using UltraSim.ECS;
using UltraSim.Server;
using UltraSim.Server.Scripting;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(RuntimeTemplate_CreatesExpectedFolders), RuntimeTemplate_CreatesExpectedFolders);
        Run(nameof(ScriptDefinitions_BuildInheritedArchetypeAndPrefabDescriptors), ScriptDefinitions_BuildInheritedArchetypeAndPrefabDescriptors);
        Run(nameof(ScriptDefinitions_PerSpawnFactoriesRollAtSpawnTime), ScriptDefinitions_PerSpawnFactoriesRollAtSpawnTime);
        Run(nameof(ScriptDefinitions_RegistryRequiresArchetypeBeforePrefab), ScriptDefinitions_RegistryRequiresArchetypeBeforePrefab);
        Run(nameof(ScriptCompiler_CompilesLoadsCommandAndPanelModule), ScriptCompiler_CompilesLoadsCommandAndPanelModule);
        Run(nameof(ScriptCompiler_CompilesLoadsArchetypeAndPrefabModule), ScriptCompiler_CompilesLoadsArchetypeAndPrefabModule);
        Run(nameof(ScriptCompiler_UsesCacheOnCleanSecondCompile), ScriptCompiler_UsesCacheOnCleanSecondCompile);
        Run(nameof(ScriptCompiler_RecompilesWhenSourceChanges), ScriptCompiler_RecompilesWhenSourceChanges);
        Run(nameof(ScriptCompiler_ReturnsStructuredDiagnosticsOnCompileFailure), ScriptCompiler_ReturnsStructuredDiagnosticsOnCompileFailure);
        Run(nameof(ScriptCompiler_WarningsAsErrorsCanFailCompile), ScriptCompiler_WarningsAsErrorsCanFailCompile);
        Run(nameof(ScriptCompiler_CorruptCacheFallsBackToCleanCompile), ScriptCompiler_CorruptCacheFallsBackToCleanCompile);
        Run(nameof(ScriptCompiler_DoesNotTouchSavesOrBackups), ScriptCompiler_DoesNotTouchSavesOrBackups);
        Run(nameof(ScriptCompiler_DoesNotExposeGodotReferencesByDefault), ScriptCompiler_DoesNotExposeGodotReferencesByDefault);
        Run(nameof(ScriptStartup_ProducesRunUoStylePhaseSummaryFromRealResults), ScriptStartup_ProducesRunUoStylePhaseSummaryFromRealResults);

        Console.WriteLine("All Server scripting tests passed.");
    }

    private static void RuntimeTemplate_CreatesExpectedFolders()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);

        Assert.True(Directory.Exists(temp.Layout.ScriptsPath));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Mobiles")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Items")));
        Assert.True(Directory.Exists(temp.Layout.DataPrefabsPath));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.DataPrefabsPath, "items")));
        Assert.True(Directory.Exists(temp.Layout.ScriptAssemblyCachePath));
        Assert.True(Directory.Exists(temp.Layout.ScriptManifestCachePath));
        Assert.True(Directory.Exists(temp.Layout.ScriptDiagnosticsCachePath));
        Assert.True(Directory.Exists(temp.Layout.SavesPath));
        Assert.True(Directory.Exists(temp.Layout.BackupsPath));
    }

    private static void ScriptDefinitions_BuildInheritedArchetypeAndPrefabDescriptors()
    {
        ArchetypeDescriptor archetype = new TestBaseBladedWeapon().BuildDescriptor();

        Assert.Equal("items/weapons/bladed/base", archetype.Key);
        Assert.Equal(4, archetype.Components.Count);
        Assert.True(archetype.ContainsComponent<TestItemIdentity>());
        Assert.True(archetype.ContainsComponent<TestInventoryItem>());
        Assert.True(archetype.ContainsComponent<TestWeaponData>());
        Assert.True(archetype.ContainsComponent<TestBladedData>());

        PrefabDescriptor prefab = new TestIronDagger().BuildDescriptor();

        Assert.Equal("items/weapons/bladed/iron_dagger", prefab.Key);
        Assert.Equal("items/weapons/bladed/base", prefab.ArchetypeKey);
        Assert.Equal(typeof(TestBaseBladedWeapon), prefab.ArchetypeShapeType);
        Assert.Equal(4, prefab.ComponentValues.Count);
        Assert.True(prefab.TryGetComponentValue(out TestWeaponData weapon));
        Assert.Equal(3, weapon.DamageMin);
        Assert.Equal(7, weapon.DamageMax);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => new TestInvalidWeaponPrefab().BuildDescriptor());
        Assert.True(ex.Message.Contains(nameof(TestWeaponData), StringComparison.Ordinal));
    }

    private static void ScriptDefinitions_PerSpawnFactoriesRollAtSpawnTime()
    {
        PrefabDescriptor prefab = new TestRandomResistDagger().BuildDescriptor();

        PrefabComponentValue resistanceValue = prefab.ComponentValues.Single(static value => value.ComponentType == typeof(TestFireResistance));
        Assert.Equal(PrefabComponentValueKind.PerSpawnFactory, resistanceValue.Kind);
        Assert.False(prefab.TryGetComponentValue(out TestFireResistance _));

        var firstContext = new PrefabRollContext(new Random(2), spawnSequence: 1, prefab.Key);
        var secondContext = new PrefabRollContext(new Random(19), spawnSequence: 2, prefab.Key);

        Assert.True(prefab.TryCreateComponentValue(firstContext, out TestFireResistance firstRoll));
        Assert.True(prefab.TryCreateComponentValue(secondContext, out TestFireResistance secondRoll));
        Assert.True(firstRoll.Value >= 1 && firstRoll.Value <= 10);
        Assert.True(secondRoll.Value >= 1 && secondRoll.Value <= 10);
        Assert.NotEqual(firstRoll, secondRoll);

        Assert.True(prefab.TryGetComponentValue(out TestWeaponData weapon));
        Assert.Equal(3, weapon.DamageMin);
    }

    private static void ScriptDefinitions_RegistryRequiresArchetypeBeforePrefab()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        var context = new DefaultUltraSimScriptContext(temp.Layout);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => context.Definitions.RegisterPrefab<TestIronDagger>());
        Assert.True(ex.Message.Contains("unregistered archetype", StringComparison.Ordinal));

        Assert.True(context.Definitions.RegisterArchetype<TestBaseBladedWeapon>());
        Assert.False(context.Definitions.RegisterArchetype<TestBaseBladedWeapon>());
        Assert.True(context.Definitions.RegisterPrefab<TestIronDagger>());
        Assert.False(context.Definitions.RegisterPrefab<TestIronDagger>());
        Assert.Equal(1, context.RegisteredArchetypes.Count);
        Assert.Equal(1, context.RegisteredPrefabs.Count);
        Assert.Equal("items/weapons/bladed/iron_dagger", context.RegisteredPrefabs.Single().Key);
    }

    private static void ScriptCompiler_CompilesLoadsCommandAndPanelModule()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        WriteScript(temp.Layout, "Commands/HelloModule.cs", HelloModuleScript("hello"));

        var compiler = new UltraSimScriptCompiler();
        UltraSimScriptCompileResult compile = compiler.Compile(new UltraSimScriptCompilerOptions(temp.Layout));
        Assert.Equal(UltraSimScriptCompileResultKind.Compiled, compile.Kind);
        Assert.True(File.Exists(compile.AssemblyPath!));

        var context = new DefaultUltraSimScriptContext(temp.Layout);
        UltraSimScriptLoadResult load = new UltraSimScriptAssemblyHost().LoadConfigureAndInitialize(compile.AssemblyPath!, context);
        Assert.True(load.Succeeded, string.Join('\n', load.Errors));
        Assert.Equal(1, load.ModuleCount);
        Assert.Equal(1, context.RegisteredCommands.Count);
        Assert.Equal(1, context.RegisteredPanelProviders.Count);
        Assert.Contains(context.LogEntries, entry => entry.Message == "hello module initialized");

        var serverCommands = new ServerCommandRegistry();
        context.RegisterCommandsInto(serverCommands);
        string reply = string.Empty;
        bool handled = serverCommands.TryDispatch(
            new ServerCommandRequest(ServerCommandSource.ServerConsole, "/hello traveler", Reply: message => reply = message),
            out ServerCommandDispatchResult dispatch);
        Assert.True(handled);
        Assert.Equal(ServerCommandDispatchStatus.Executed, dispatch.Status);
        Assert.Equal("hello traveler", reply);

        UltraSimScriptUiPanelProviderDescriptor provider = context.RegisteredPanelProviders.Single();
        var panel = provider.Provider();
        Assert.Equal("script.message", panel.Schema.Metadata.PanelType);
        Assert.Equal(1, panel.Model.Values.Count);
    }

    private static void ScriptCompiler_CompilesLoadsArchetypeAndPrefabModule()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        WriteScript(temp.Layout, "Items/IronDaggerModule.cs", IronDaggerModuleScript());

        var compiler = new UltraSimScriptCompiler();
        UltraSimScriptCompileResult compile = compiler.Compile(new UltraSimScriptCompilerOptions(temp.Layout));
        Assert.Equal(UltraSimScriptCompileResultKind.Compiled, compile.Kind);

        var context = new DefaultUltraSimScriptContext(temp.Layout);
        UltraSimScriptLoadResult load = new UltraSimScriptAssemblyHost().LoadConfigureAndInitialize(compile.AssemblyPath!, context);
        Assert.True(load.Succeeded, string.Join('\n', load.Errors));
        Assert.Equal(1, load.ModuleCount);
        Assert.Equal(1, context.RegisteredArchetypes.Count);
        Assert.Equal(1, context.RegisteredPrefabs.Count);

        PrefabDescriptor prefab = context.RegisteredPrefabs.Single();
        Assert.Equal("items/weapons/bladed/iron_dagger", prefab.Key);
        Assert.Equal("items/weapons/bladed/base", prefab.ArchetypeKey);
        PrefabComponentValue weapon = prefab.ComponentValues.Single(static value => value.ComponentType.Name == "ScriptWeaponData");
        object weaponValue = weapon.CreateValue(new PrefabRollContext(new Random(1), prefabKey: prefab.Key));
        Assert.Equal(3, (int)weapon.ComponentType.GetProperty("DamageMin")!.GetValue(weaponValue)!);
        Assert.Equal(7, (int)weapon.ComponentType.GetProperty("DamageMax")!.GetValue(weaponValue)!);
    }

    private static void ScriptCompiler_UsesCacheOnCleanSecondCompile()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        WriteScript(temp.Layout, "Commands/HelloModule.cs", HelloModuleScript("hello"));

        var compiler = new UltraSimScriptCompiler();
        UltraSimScriptCompileResult first = compiler.Compile(new UltraSimScriptCompilerOptions(temp.Layout));
        UltraSimScriptCompileResult second = compiler.Compile(new UltraSimScriptCompilerOptions(temp.Layout));

        Assert.Equal(UltraSimScriptCompileResultKind.Compiled, first.Kind);
        Assert.Equal(UltraSimScriptCompileResultKind.CacheHit, second.Kind);
        Assert.Equal(first.CacheKey, second.CacheKey);
        Assert.Equal(first.AssemblyPath, second.AssemblyPath);
    }

    private static void ScriptCompiler_RecompilesWhenSourceChanges()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        string scriptPath = WriteScript(temp.Layout, "Commands/HelloModule.cs", HelloModuleScript("hello"));

        var compiler = new UltraSimScriptCompiler();
        UltraSimScriptCompileResult first = compiler.Compile(new UltraSimScriptCompilerOptions(temp.Layout));
        File.AppendAllText(scriptPath, "\n// source change\n");
        UltraSimScriptCompileResult second = compiler.Compile(new UltraSimScriptCompilerOptions(temp.Layout));

        Assert.Equal(UltraSimScriptCompileResultKind.Compiled, first.Kind);
        Assert.Equal(UltraSimScriptCompileResultKind.Compiled, second.Kind);
        Assert.NotEqual(first.CacheKey, second.CacheKey);
    }

    private static void ScriptCompiler_ReturnsStructuredDiagnosticsOnCompileFailure()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        WriteScript(temp.Layout, "Broken.cs", "public sealed class Broken { public void Nope( }");

        UltraSimScriptCompileResult result = new UltraSimScriptCompiler().Compile(new UltraSimScriptCompilerOptions(temp.Layout));

        Assert.Equal(UltraSimScriptCompileResultKind.Failed, result.Kind);
        Assert.True(result.Diagnostics.Any(static diagnostic => diagnostic.Severity == UltraSimScriptDiagnosticSeverity.Error));
        Assert.True(result.Diagnostics.Any(static diagnostic => diagnostic.Line > 0));
    }

    private static void ScriptCompiler_WarningsAsErrorsCanFailCompile()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        WriteScript(temp.Layout, "WarningModule.cs", WarningModuleScript());

        var compiler = new UltraSimScriptCompiler();
        UltraSimScriptCompileResult relaxed = compiler.Compile(new UltraSimScriptCompilerOptions(temp.Layout, TreatWarningsAsErrors: false));
        UltraSimScriptCompileResult strict = compiler.Compile(new UltraSimScriptCompilerOptions(temp.Layout, TreatWarningsAsErrors: true));

        Assert.Equal(UltraSimScriptCompileResultKind.Compiled, relaxed.Kind);
        Assert.Equal(UltraSimScriptCompileResultKind.Failed, strict.Kind);
        Assert.True(strict.Diagnostics.Any(static diagnostic => diagnostic.Severity == UltraSimScriptDiagnosticSeverity.Error));
    }

    private static void ScriptCompiler_CorruptCacheFallsBackToCleanCompile()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        WriteScript(temp.Layout, "Commands/HelloModule.cs", HelloModuleScript("hello"));

        var compiler = new UltraSimScriptCompiler();
        UltraSimScriptCompileResult first = compiler.Compile(new UltraSimScriptCompilerOptions(temp.Layout));
        Assert.Equal(UltraSimScriptCompileResultKind.Compiled, first.Kind);

        File.WriteAllText(Path.Combine(temp.Layout.ScriptManifestCachePath, "current.json"), "{not-json");
        UltraSimScriptCompileResult second = compiler.Compile(new UltraSimScriptCompilerOptions(temp.Layout));

        Assert.Equal(UltraSimScriptCompileResultKind.Compiled, second.Kind);
        Assert.Equal(first.CacheKey, second.CacheKey);
    }

    private static void ScriptCompiler_DoesNotTouchSavesOrBackups()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        string saveSentinel = Path.Combine(temp.Layout.SavesPath, "world.sav");
        string backupSentinel = Path.Combine(temp.Layout.BackupsPath, "world.bak");
        File.WriteAllText(saveSentinel, "save-state");
        File.WriteAllText(backupSentinel, "backup-state");
        string[] beforeSaves = Directory.GetFiles(temp.Layout.SavesPath, "*", SearchOption.AllDirectories);
        string[] beforeBackups = Directory.GetFiles(temp.Layout.BackupsPath, "*", SearchOption.AllDirectories);

        WriteScript(temp.Layout, "Commands/HelloModule.cs", HelloModuleScript("hello"));
        UltraSimScriptCompileResult result = new UltraSimScriptCompiler().Compile(new UltraSimScriptCompilerOptions(temp.Layout));

        Assert.True(result.Succeeded);
        Assert.Equal("save-state", File.ReadAllText(saveSentinel));
        Assert.Equal("backup-state", File.ReadAllText(backupSentinel));
        Assert.Equal(beforeSaves, Directory.GetFiles(temp.Layout.SavesPath, "*", SearchOption.AllDirectories));
        Assert.Equal(beforeBackups, Directory.GetFiles(temp.Layout.BackupsPath, "*", SearchOption.AllDirectories));
    }

    private static void ScriptCompiler_DoesNotExposeGodotReferencesByDefault()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        WriteScript(
            temp.Layout,
            "GodotLeak.cs",
            """
            using Godot;
            using UltraSim.Server.Scripting;

            public sealed class GodotLeak : IUltraSimScriptModule
            {
                public void Configure(IUltraSimScriptContext context)
                {
                    _ = new Node();
                }
            }
            """);

        UltraSimScriptCompileResult result = new UltraSimScriptCompiler().Compile(new UltraSimScriptCompilerOptions(temp.Layout));

        Assert.Equal(UltraSimScriptCompileResultKind.Failed, result.Kind);
        Assert.True(result.Diagnostics.Any(static diagnostic => diagnostic.Message.Contains("Godot", StringComparison.OrdinalIgnoreCase)));
    }

    private static void ScriptStartup_ProducesRunUoStylePhaseSummaryFromRealResults()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        WriteScript(temp.Layout, "Commands/HelloModule.cs", HelloModuleScript("hello"));

        UltraSimScriptStartupResult startup = new UltraSimScriptStartupService().Run(new UltraSimScriptStartupOptions(temp.Layout));
        IReadOnlyList<ServerApplicationEvent> events = UltraSimScriptStartupLog.CreateEvents(startup);
        string joined = string.Join('\n', events.Select(static evt => evt.Message));

        Assert.True(startup.Succeeded);
        Assert.Equal(1, startup.ModuleCount);
        Assert.Equal(1, startup.CommandCount);
        Assert.Equal(1, startup.PanelProviderCount);
        Assert.True(joined.Contains("Scripts: Compiling C# scripts... - done", StringComparison.Ordinal));
        Assert.True(joined.Contains("Scripts: Configuring... - done (1 module)", StringComparison.Ordinal));
        Assert.True(joined.Contains("Scripts: Initializing... - done (1 module, 1 command, 1 panel)", StringComparison.Ordinal));
    }

    private static string HelloModuleScript(string commandName) =>
        $$"""
        using UltraSim.Server.Scripting;
        using UltraSim.UI;

        public sealed class HelloModule : IUltraSimScriptModule
        {
            public void Configure(IUltraSimScriptContext context)
            {
                context.Commands.Register("{{commandName}}", command =>
                {
                    command.Reply("hello " + command.RawArguments);
                }, "Replies with a greeting.");

                context.UiPanels.RegisterPanelProvider("hello.panel", () =>
                    UiPanelBuilder
                        .Create("script-test", "script.message", 1)
                        .Panel(panel =>
                        {
                            panel.Column(column =>
                            {
                                column.DynamicText("message", "Message", "Hello from scripts");
                                column.Button("ok", "OK");
                            });
                        })
                        .Build());
            }

            public void Initialize(IUltraSimScriptContext context)
            {
                context.Logger.Info("hello module initialized");
            }
        }
        """;

    private static string WarningModuleScript() =>
        """
        #warning script warning
        using UltraSim.Server.Scripting;

        public sealed class WarningModule : IUltraSimScriptModule
        {
            public void Configure(IUltraSimScriptContext context)
            {
            }
        }
        """;

    private static string IronDaggerModuleScript() =>
        """
        using UltraSim.ECS;
        using UltraSim.Server.Scripting;

        public readonly record struct ScriptItemIdentity(int DefinitionId) : IComponent<ScriptItemIdentity>;
        public readonly record struct ScriptInventoryItem(int Width, int Height, int StackLimit) : IComponent<ScriptInventoryItem>;
        public readonly record struct ScriptWeaponData(int DamageMin, int DamageMax) : IComponent<ScriptWeaponData>;
        public readonly record struct ScriptBladedData(int BleedChance) : IComponent<ScriptBladedData>;

        public class ScriptBaseItem : ArchetypeShape
        {
            public override string Key => "items/base";

            protected override void Configure(ArchetypeBuilder builder)
            {
                builder.AddComponent<ScriptItemIdentity>();
                builder.AddComponent<ScriptInventoryItem>();
            }
        }

        public class ScriptBaseWeapon : ScriptBaseItem
        {
            public override string Key => "items/weapons/base";

            protected override void Configure(ArchetypeBuilder builder)
            {
                base.Configure(builder);
                builder.AddComponent<ScriptWeaponData>();
            }
        }

        public class ScriptBaseBladedWeapon : ScriptBaseWeapon
        {
            public override string Key => "items/weapons/bladed/base";

            protected override void Configure(ArchetypeBuilder builder)
            {
                base.Configure(builder);
                builder.AddComponent<ScriptBladedData>();
            }
        }

        public sealed class ScriptIronDagger : PrefabDefinition<ScriptBaseBladedWeapon>
        {
            public override string Key => "items/weapons/bladed/iron_dagger";

            protected override void Configure(PrefabBuilder builder)
            {
                builder.Set(new ScriptItemIdentity(1001));
                builder.Set(new ScriptInventoryItem(1, 2, 1));
                builder.Set(new ScriptWeaponData(3, 7));
                builder.Set(new ScriptBladedData(12));
            }
        }

        public sealed class IronDaggerModule : IUltraSimScriptModule
        {
            public void Configure(IUltraSimScriptContext context)
            {
                context.Definitions.RegisterArchetype<ScriptBaseBladedWeapon>();
                context.Definitions.RegisterPrefab<ScriptIronDagger>();
            }
        }
        """;

    private static string WriteScript(UltraSimRuntimeRootLayout layout, string relativePath, string source)
    {
        string path = Path.Combine(layout.ScriptsPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {name}: {ex}");
            throw;
        }
    }
}

internal sealed class TempRuntimeRoot : IDisposable
{
    private TempRuntimeRoot(string path)
    {
        Path = path;
        Layout = UltraSimRuntimeRootResolver.FromRoot(path);
    }

    public string Path { get; }
    public UltraSimRuntimeRootLayout Layout { get; }

    public static TempRuntimeRoot Create()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ultrasim-scripting-tests", Guid.NewGuid().ToString("N"));
        return new TempRuntimeRoot(path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
        }
    }
}

internal readonly record struct TestItemIdentity(int DefinitionId) : IComponent<TestItemIdentity>;
internal readonly record struct TestInventoryItem(int Width, int Height, int StackLimit) : IComponent<TestInventoryItem>;
internal readonly record struct TestWeaponData(int DamageMin, int DamageMax) : IComponent<TestWeaponData>;
internal readonly record struct TestBladedData(int BleedChance) : IComponent<TestBladedData>;
internal readonly record struct TestFireResistance(int Value) : IComponent<TestFireResistance>;

internal class TestBaseItem : ArchetypeShape
{
    public override string Key => "items/base";

    protected override void Configure(ArchetypeBuilder builder)
    {
        builder.AddComponent<TestItemIdentity>();
        builder.AddComponent<TestInventoryItem>();
    }
}

internal class TestBaseWeapon : TestBaseItem
{
    public override string Key => "items/weapons/base";

    protected override void Configure(ArchetypeBuilder builder)
    {
        base.Configure(builder);
        builder.AddComponent<TestWeaponData>();
    }
}

internal class TestEffectBladedWeapon : TestBaseBladedWeapon
{
    public override string Key => "items/weapons/bladed/effect";

    protected override void Configure(ArchetypeBuilder builder)
    {
        base.Configure(builder);
        builder.AddComponent<TestFireResistance>();
    }
}

internal class TestBaseBladedWeapon : TestBaseWeapon
{
    public override string Key => "items/weapons/bladed/base";

    protected override void Configure(ArchetypeBuilder builder)
    {
        base.Configure(builder);
        builder.AddComponent<TestBladedData>();
    }
}

internal sealed class TestIronDagger : PrefabDefinition<TestBaseBladedWeapon>
{
    public override string Key => "items/weapons/bladed/iron_dagger";

    protected override void Configure(PrefabBuilder builder)
    {
        builder.Set(new TestItemIdentity(1001));
        builder.Set(new TestInventoryItem(1, 2, 1));
        builder.Set(new TestWeaponData(3, 7));
        builder.Set(new TestBladedData(12));
    }
}

internal sealed class TestInvalidWeaponPrefab : PrefabDefinition<TestBaseItem>
{
    public override string Key => "items/invalid_weapon";

    protected override void Configure(PrefabBuilder builder)
    {
        builder.Set(new TestWeaponData(1, 2));
    }
}

internal sealed class TestRandomResistDagger : PrefabDefinition<TestEffectBladedWeapon>
{
    public override string Key => "items/weapons/bladed/random_resist_dagger";

    protected override void Configure(PrefabBuilder builder)
    {
        builder.Set(new TestItemIdentity(1002));
        builder.Set(new TestInventoryItem(1, 2, 1));
        builder.Set(new TestWeaponData(3, 7));
        builder.Set(new TestBladedData(12));
        builder.SetRandom(random => new TestFireResistance(random.Next(1, 11)));
    }
}

internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Expected condition to be true.");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (typeof(T).IsArray && expected is Array expectedArray && actual is Array actualArray)
        {
            if (expectedArray.Length != actualArray.Length)
                throw new InvalidOperationException($"Expected array length {expectedArray.Length}, got {actualArray.Length}.");

            for (int i = 0; i < expectedArray.Length; i++)
            {
                object? expectedValue = expectedArray.GetValue(i);
                object? actualValue = actualArray.GetValue(i);
                if (!Equals(expectedValue, actualValue))
                    throw new InvalidOperationException($"Expected array item {i} to be '{expectedValue}', got '{actualValue}'.");
            }

            return;
        }

        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void NotEqual<T>(T notExpected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            throw new InvalidOperationException($"Did not expect '{actual}'.");
    }

    public static void Contains<T>(IEnumerable<T> values, Func<T, bool> predicate)
    {
        if (!values.Any(predicate))
            throw new InvalidOperationException("Expected matching item.");
    }

    public static void False(bool condition)
    {
        if (condition)
            throw new InvalidOperationException("Expected condition to be false.");
    }

    public static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected exception of type {typeof(TException).Name}.");
    }
}
