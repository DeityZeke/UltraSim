using UltraSim.ECS;
using UltraSim.Server;
using UltraSim.Server.Gameplay;
using UltraSim.Server.Scripting;
using UltraSim.UI;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(RuntimeTemplate_CreatesExpectedFolders), RuntimeTemplate_CreatesExpectedFolders);
        Run(nameof(RuntimeTemplate_AdmissionPackageCompilesAgainstScriptApi), RuntimeTemplate_AdmissionPackageCompilesAgainstScriptApi);
        Run(nameof(RuntimeTemplate_AdmissionPackageLoadsModuleAndRegistersProviders), RuntimeTemplate_AdmissionPackageLoadsModuleAndRegistersProviders);
        Run(nameof(RuntimeTemplate_DefaultScriptsCompileLoadAndBuildMobilePrefabs), RuntimeTemplate_DefaultScriptsCompileLoadAndBuildMobilePrefabs);
        Run(nameof(AdmissionRegistry_CustomStepReplacesDefaultForSameEvent), AdmissionRegistry_CustomStepReplacesDefaultForSameEvent);
        Run(nameof(ScriptDefinitions_BuildInheritedArchetypeAndPrefabDescriptors), ScriptDefinitions_BuildInheritedArchetypeAndPrefabDescriptors);
        Run(nameof(ScriptDefinitions_PerSpawnFactoriesRollAtSpawnTime), ScriptDefinitions_PerSpawnFactoriesRollAtSpawnTime);
        Run(nameof(ScriptDefinitions_RegistryRequiresArchetypeBeforePrefab), ScriptDefinitions_RegistryRequiresArchetypeBeforePrefab);
        Run(nameof(ReplaceableProviderRegistry_ReplacesDisablesAndFallsBackDeterministically), ReplaceableProviderRegistry_ReplacesDisablesAndFallsBackDeterministically);
        Run(nameof(ScriptPrefabCatalogBuilder_TranslatesDescriptorsToEcsCatalog), ScriptPrefabCatalogBuilder_TranslatesDescriptorsToEcsCatalog);
        Run(nameof(ScriptPrefabCatalogBuilder_WorldCreatePrefabAppliesConstantsAndPerSpawnValues), ScriptPrefabCatalogBuilder_WorldCreatePrefabAppliesConstantsAndPerSpawnValues);
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
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Accounts")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Commands")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "ContextMenus")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Customs")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Engines")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Mobiles")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Packages")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Items")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Misc")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Properties")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Regions")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Skills")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Spells")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "Targets")));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.ScriptsPath, "UI")));
        Assert.True(Directory.Exists(temp.Layout.DataPrefabsPath));
        Assert.True(Directory.Exists(Path.Combine(temp.Layout.DataPrefabsPath, "items")));
        Assert.True(Directory.Exists(temp.Layout.ScriptAssemblyCachePath));
        Assert.True(Directory.Exists(temp.Layout.ScriptManifestCachePath));
        Assert.True(Directory.Exists(temp.Layout.ScriptDiagnosticsCachePath));
        Assert.True(Directory.Exists(temp.Layout.SavesPath));
        Assert.True(Directory.Exists(temp.Layout.BackupsPath));
    }

    private static void RuntimeTemplate_AdmissionPackageCompilesAgainstScriptApi()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        string sourceRoot = Path.Combine(
            FindRepositoryRoot(),
            "Server",
            "RuntimeTemplate",
            "scripts",
            "Packages",
            "Admission");
        string destinationRoot = Path.Combine(temp.Layout.ScriptsPath, "Packages", "Admission");
        CopyDirectory(sourceRoot, destinationRoot);

        UltraSimScriptCompileResult result = new UltraSimScriptCompiler().Compile(new UltraSimScriptCompilerOptions(temp.Layout));

        Assert.Equal(UltraSimScriptCompileResultKind.Compiled, result.Kind);
        Assert.True(result.SourceCount >= 5);
        Assert.False(result.Diagnostics.Any(static diagnostic => diagnostic.Severity == UltraSimScriptDiagnosticSeverity.Error));
    }

    private static void RuntimeTemplate_AdmissionPackageLoadsModuleAndRegistersProviders()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        CopyDirectory(
            Path.Combine(FindRepositoryRoot(), "Server", "RuntimeTemplate", "scripts", "Packages", "Admission"),
            Path.Combine(temp.Layout.ScriptsPath, "Packages", "Admission"));

        UltraSimScriptCompileResult compile = new UltraSimScriptCompiler().Compile(new UltraSimScriptCompilerOptions(temp.Layout));
        Assert.Equal(UltraSimScriptCompileResultKind.Compiled, compile.Kind);

        var context = new DefaultUltraSimScriptContext(temp.Layout);
        UltraSimScriptLoadResult load = new UltraSimScriptAssemblyHost().LoadConfigureAndInitialize(compile.AssemblyPath!, context);

        Assert.True(load.Succeeded, string.Join('\n', load.Errors));
        Assert.Equal(1, load.ModuleCount);
        Assert.Equal(3, context.RegisteredPanelProviders.Count);
        Assert.Equal(3, context.RegisteredAdmissionSteps.Count);
        Assert.True(context.TryGetPanelProvider("admission.panel.login", out UltraSimScriptUiPanelProviderDescriptor loginProvider));
        Assert.True(context.TryGetPanelProvider("admission.panel.character_select", out UltraSimScriptUiPanelProviderDescriptor selectProvider));
        Assert.True(context.TryGetPanelProvider("admission.panel.character_create", out UltraSimScriptUiPanelProviderDescriptor createProvider));
        Assert.True(context.TryGetAdmissionStep("admission.client_connected", out UltraSimScriptAdmissionFlowStepDescriptor loginStep));
        Assert.Equal("admission.panel.login", loginStep.PanelProviderKey);
        Assert.Equal("admission.login_verified", loginStep.CompletesWithEvent);
        Assert.True(context.TryGetAdmissionStep("admission.login_verified", out UltraSimScriptAdmissionFlowStepDescriptor selectStep));
        Assert.True(selectStep.TryGetActionRoute("character_select.create", UiActionContextKind.Panel, out UltraSimScriptAdmissionActionRouteDescriptor createRoute));
        Assert.Equal("admission.character_create_requested", createRoute.CompletesWithEvent);
        Assert.True(selectStep.TryGetActionRoute("character_select.play", UiActionContextKind.Row, out UltraSimScriptAdmissionActionRouteDescriptor playRoute));
        Assert.Equal("admission.enter_world", playRoute.CompletesWithEvent);
        Assert.True(context.TryGetAdmissionStep("admission.character_create_requested", out UltraSimScriptAdmissionFlowStepDescriptor createStep));
        Assert.True(createStep.TryGetActionRoute("character_create.submit", UiActionContextKind.Panel, out UltraSimScriptAdmissionActionRouteDescriptor submitRoute));
        Assert.Equal("admission.enter_world", submitRoute.CompletesWithEvent);
        Assert.True(createStep.TryGetActionRoute("character_create.cancel", UiActionContextKind.Panel, out UltraSimScriptAdmissionActionRouteDescriptor cancelRoute));
        Assert.Equal("admission.login_verified", cancelRoute.CompletesWithEvent);
        Assert.Equal("admission.login", loginProvider.Provider().Schema.Identity.PanelType);
        Assert.Equal("admission.character_select", selectProvider.Provider().Schema.Identity.PanelType);
        Assert.Equal("admission.character_create", createProvider.Provider().Schema.Identity.PanelType);
        Assert.True(context.LogEntries.Any(static entry => entry.Message.Contains("Default admission package configured.", StringComparison.Ordinal)));
    }

    private static void RuntimeTemplate_DefaultScriptsCompileLoadAndBuildMobilePrefabs()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        CopyDirectory(
            Path.Combine(FindRepositoryRoot(), "Server", "RuntimeTemplate", "scripts"),
            temp.Layout.ScriptsPath);

        UltraSimScriptCompileResult compile = new UltraSimScriptCompiler().Compile(new UltraSimScriptCompilerOptions(temp.Layout));
        Assert.Equal(UltraSimScriptCompileResultKind.Compiled, compile.Kind);
        Assert.True(compile.SourceCount >= 6);

        var context = new DefaultUltraSimScriptContext(temp.Layout);
        UltraSimScriptLoadResult load = new UltraSimScriptAssemblyHost().LoadConfigureAndInitialize(compile.AssemblyPath!, context);

        Assert.True(load.Succeeded, string.Join('\n', load.Errors));
        Assert.Equal(2, load.ModuleCount);
        Assert.True(context.RegisteredArchetypes.Any(static archetype =>
            archetype.Key == "mobiles/base" &&
            archetype.ContainsComponent<ServerMovementBody>() &&
            archetype.ContainsComponent<BiologicalProfile>()));
        Assert.True(context.RegisteredArchetypes.Any(static archetype =>
            archetype.Key == "mobiles/humanoid/base" &&
            archetype.ContainsComponent<ServerMovementBody>() &&
            archetype.ContainsComponent<BiologicalProfile>() &&
            archetype.ContainsComponent<HumanoidProfile>()));
        Assert.True(context.RegisteredArchetypes.Any(static archetype =>
            archetype.Key == "mobiles/human/base" &&
            archetype.ContainsComponent<ServerMovementBody>() &&
            archetype.ContainsComponent<BiologicalProfile>() &&
            archetype.ContainsComponent<HumanoidProfile>() &&
            archetype.ContainsComponent<HumanProfile>()));
        Assert.True(context.RegisteredPrefabs.Any(static prefab => prefab.Key == "mobiles/human"));

        var schemaBuilder = new EcsSchemaBuilder();
        ScriptPrefabCatalogBuildResult catalog = ScriptPrefabCatalogBuilder.Build(
            context.RegisteredArchetypes,
            context.RegisteredPrefabs,
            schemaBuilder);
        var world = new World(schemaBuilder.Build(), new EcsWorldServices
        {
            PrefabResolver = catalog.Catalog,
        });

        Assert.True(world.TryCreatePrefab("mobiles/human", out Entity human));
        Assert.True(world.TryGetComponent(human, out ServerMovementBody movement));
        Assert.True(world.TryGetComponent(human, out BiologicalProfile biology));
        Assert.True(world.TryGetComponent(human, out HumanoidProfile humanoid));
        Assert.True(world.TryGetComponent(human, out HumanProfile humanProfile));
        Assert.Equal(new ServerMovementBody(0f, 0f, 0f, 0f), movement);
        Assert.Equal(new BiologicalProfile(BiologicalSex.Male, CanBreed: true, Maturity: 1f, LineageSeed: 0UL), biology);
        Assert.Equal(new HumanoidProfile(SkeletonKind: 0), humanoid);
        Assert.Equal(new HumanProfile(Variant: 0), humanProfile);
    }

    private static void AdmissionRegistry_CustomStepReplacesDefaultForSameEvent()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        UltraSimRuntimeTemplate.Ensure(temp.Layout);
        var context = new DefaultUltraSimScriptContext(temp.Layout);

        Assert.True(context.Admission.RegisterStep(new UltraSimScriptAdmissionFlowStepDescriptor(
            "core.login",
            "admission.client_connected",
            "admission.login_verified",
            "admission.panel.login",
            Priority: 100)));
        Assert.True(context.Admission.RegisterStep(new UltraSimScriptAdmissionFlowStepDescriptor(
            "custom.direct_world",
            "admission.client_connected",
            "admission.enter_world",
            "custom.panel.direct_world",
            Priority: 200)));

        Assert.True(context.TryGetAdmissionStep("admission.client_connected", out UltraSimScriptAdmissionFlowStepDescriptor step));
        Assert.Equal("custom.direct_world", step.OwnerKey);
        Assert.Equal("admission.enter_world", step.CompletesWithEvent);
        Assert.Equal("custom.panel.direct_world", step.PanelProviderKey);
        Assert.Equal(1, context.RegisteredAdmissionSteps.Count);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            context.Admission.RegisterStep(new UltraSimScriptAdmissionFlowStepDescriptor(
                "other.custom",
                "admission.client_connected",
                "admission.other",
                "other.panel",
                Priority: 200)));
        Assert.True(ex.Message.Contains("already has owner 'custom.direct_world'", StringComparison.Ordinal));
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
        Assert.Equal(typeof(TestIronDagger).FullName, prefab.SourceName);
        Assert.Equal("items/weapons/bladed/base", prefab.ArchetypeKey);
        Assert.Equal(typeof(TestBaseBladedWeapon), prefab.ArchetypeShapeType);
        Assert.Equal(4, prefab.ComponentValues.Count);
        Assert.True(prefab.TryGetComponentValue(out TestWeaponData weapon));
        Assert.Equal(3, weapon.DamageMin);
        Assert.Equal(7, weapon.DamageMax);

        PrefabDescriptor defaultKeyPrefab = new TestDefaultKeyDagger().BuildDescriptor();
        Assert.Equal(nameof(TestDefaultKeyDagger), defaultKeyPrefab.Key);
        Assert.Equal(typeof(TestDefaultKeyDagger).FullName, defaultKeyPrefab.SourceName);

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
        InvalidOperationException duplicate = Assert.Throws<InvalidOperationException>(() => context.Definitions.RegisterPrefab<TestDuplicateIronDagger>());
        Assert.True(duplicate.Message.Contains("Duplicate prefab key 'items/weapons/bladed/iron_dagger'", StringComparison.Ordinal));
        Assert.True(duplicate.Message.Contains(typeof(TestIronDagger).FullName!, StringComparison.Ordinal));
        Assert.True(duplicate.Message.Contains(typeof(TestDuplicateIronDagger).FullName!, StringComparison.Ordinal));
        Assert.Equal(1, context.RegisteredArchetypes.Count);
        Assert.Equal(1, context.RegisteredPrefabs.Count);
        Assert.Equal("items/weapons/bladed/iron_dagger", context.RegisteredPrefabs.Single().Key);
    }

    private static void ReplaceableProviderRegistry_ReplacesDisablesAndFallsBackDeterministically()
    {
        var registry = new UltraSimReplaceableProviderRegistry<Func<string>>();

        UltraSimReplaceableProviderSetResult<Func<string>> core = registry.SetDefault(
            "chat.surface",
            "core.chat",
            static () => "core");
        Assert.True(core.Accepted);
        Assert.True(registry.TryGetActive("chat.surface", out UltraSimReplaceableProviderRegistration<Func<string>> active));
        Assert.Equal("core.chat", active.Owner);
        Assert.Equal("core", active.Provider!());

        UltraSimReplaceableProviderSetResult<Func<string>> rich = registry.Replace(
            "chat.surface",
            "packages.chat.rich",
            static () => "rich",
            priority: 100);
        Assert.True(rich.Accepted);
        Assert.True(registry.TryGetActive("chat.surface", out active));
        Assert.Equal("packages.chat.rich", active.Owner);
        Assert.Equal("rich", active.Provider!());
        Assert.True(registry.TryGetState("chat.surface", out UltraSimReplaceableProviderState<Func<string>>? state));
        Assert.True(state is not null);
        Assert.Equal(1, state!.SuppressedRegistrations.Count);
        Assert.Equal("core.chat", state.SuppressedRegistrations[0].Owner);

        UltraSimReplaceableProviderSetResult<Func<string>> conflict = registry.Replace(
            "chat.surface",
            "packages.chat.other",
            static () => "other",
            priority: 100);
        Assert.False(conflict.Accepted);
        Assert.Equal(UltraSimReplaceableProviderSetStatus.RejectedConflict, conflict.Status);
        Assert.True(conflict.Error.Contains("packages.chat.rich", StringComparison.Ordinal));
        Assert.True(registry.TryGetActive("chat.surface", out active));
        Assert.Equal("packages.chat.rich", active.Owner);

        UltraSimReplaceableProviderSetResult<Func<string>> disable = registry.Disable(
            "chat.surface",
            "operator.disable_chat",
            priority: 1000);
        Assert.True(disable.Accepted);
        Assert.False(registry.TryGetActive("chat.surface", out _));
        Assert.True(registry.TryGetState("chat.surface", out state));
        Assert.True(state!.IsDisabled);
        Assert.Equal("operator.disable_chat", state.ActiveRegistration!.Owner);

        Assert.True(registry.RemoveOwner("chat.surface", "operator.disable_chat"));
        Assert.True(registry.TryGetActive("chat.surface", out active));
        Assert.Equal("packages.chat.rich", active.Owner);

        Assert.True(registry.RemoveOwner("chat.surface", "packages.chat.rich"));
        Assert.True(registry.TryGetActive("chat.surface", out active));
        Assert.Equal("core.chat", active.Owner);

        UltraSimReplaceableProviderSetResult<Func<string>> coreReplacement = registry.SetDefault(
            "chat.surface",
            "core.chat",
            static () => "core_v2");
        Assert.True(coreReplacement.Accepted);
        Assert.True(coreReplacement.ReplacedRegistration is not null);
        Assert.True(registry.TryGetActive("chat.surface", out active));
        Assert.Equal("core_v2", active.Provider!());
    }

    private static void ScriptPrefabCatalogBuilder_TranslatesDescriptorsToEcsCatalog()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        var context = new DefaultUltraSimScriptContext(temp.Layout);
        Assert.True(context.Definitions.RegisterArchetype<TestBaseBladedWeapon>());
        Assert.True(context.Definitions.RegisterPrefab<TestIronDagger>());

        var schemaBuilder = new EcsSchemaBuilder();
        ScriptPrefabCatalogBuildResult result = ScriptPrefabCatalogBuilder.Build(
            context.RegisteredArchetypes,
            context.RegisteredPrefabs,
            schemaBuilder,
            new ScriptPrefabCatalogBuildOptions(new Random(7)));

        Assert.Equal(1, result.Catalog.Count);
        Assert.True(result.Catalog.TryResolvePrefab("items/weapons/bladed/iron_dagger", out UltraSim.ECS.PrefabDefinition prefab));
        Assert.True(result.ArchetypeSignatures.TryGetValue("items/weapons/bladed/base", out ArchetypeSignature? signature));
        Assert.Equal(4, signature!.ComponentCount);
        Assert.Equal(4, prefab.Signature.ComponentCount);
    }

    private static void ScriptPrefabCatalogBuilder_WorldCreatePrefabAppliesConstantsAndPerSpawnValues()
    {
        using TempRuntimeRoot temp = TempRuntimeRoot.Create();
        var context = new DefaultUltraSimScriptContext(temp.Layout);
        Assert.True(context.Definitions.RegisterArchetype<TestEffectBladedWeapon>());
        Assert.True(context.Definitions.RegisterPrefab<TestSequencedResistDagger>());

        var schemaBuilder = new EcsSchemaBuilder();
        ScriptPrefabCatalogBuildResult result = ScriptPrefabCatalogBuilder.Build(
            context.RegisteredArchetypes,
            context.RegisteredPrefabs,
            schemaBuilder,
            new ScriptPrefabCatalogBuildOptions(new Random(7)));
        var world = new World(schemaBuilder.Build(), new EcsWorldServices
        {
            PrefabResolver = result.Catalog,
        });

        Assert.True(world.TryCreatePrefab("items/weapons/bladed/sequenced_resist_dagger", out Entity first));
        Assert.True(world.TryCreatePrefab("items/weapons/bladed/sequenced_resist_dagger", out Entity second));

        Assert.True(world.TryGetComponent(first, out TestWeaponData firstWeapon));
        Assert.True(world.TryGetComponent(second, out TestWeaponData secondWeapon));
        Assert.Equal(new TestWeaponData(3, 7), firstWeapon);
        Assert.Equal(new TestWeaponData(3, 7), secondWeapon);

        Assert.True(world.TryGetComponent(first, out TestFireResistance firstResistance));
        Assert.True(world.TryGetComponent(second, out TestFireResistance secondResistance));
        Assert.Equal(new TestFireResistance(1), firstResistance);
        Assert.Equal(new TestFireResistance(2), secondResistance);
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
        Assert.Equal(0, startup.AdmissionStepCount);
        Assert.True(joined.Contains("Scripts: Compiling C# scripts... - done", StringComparison.Ordinal));
        Assert.True(joined.Contains("Scripts: Configuring... - done (1 module)", StringComparison.Ordinal));
        Assert.True(joined.Contains("Scripts: Initializing... - done (1 module, 1 command, 1 panel, 0 admission steps)", StringComparison.Ordinal));
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

    private static string FindRepositoryRoot()
    {
        string? current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(Path.Combine(current, "Server", "RuntimeTemplate")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from current directory.");
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException(sourceRoot);

        foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
        }

        Directory.CreateDirectory(destinationRoot);
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceRoot, file);
            string destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
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

internal sealed class TestDefaultKeyDagger : PrefabDefinition<TestBaseBladedWeapon>
{
    protected override void Configure(PrefabBuilder builder)
    {
        builder.Set(new TestItemIdentity(1005));
        builder.Set(new TestInventoryItem(1, 2, 1));
        builder.Set(new TestWeaponData(4, 8));
        builder.Set(new TestBladedData(10));
    }
}

internal sealed class TestDuplicateIronDagger : PrefabDefinition<TestBaseBladedWeapon>
{
    public override string Key => "items/weapons/bladed/iron_dagger";

    protected override void Configure(PrefabBuilder builder)
    {
        builder.Set(new TestItemIdentity(1006));
        builder.Set(new TestInventoryItem(1, 2, 1));
        builder.Set(new TestWeaponData(4, 8));
        builder.Set(new TestBladedData(10));
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

internal sealed class TestSequencedResistDagger : PrefabDefinition<TestEffectBladedWeapon>
{
    public override string Key => "items/weapons/bladed/sequenced_resist_dagger";

    protected override void Configure(PrefabBuilder builder)
    {
        builder.Set(new TestItemIdentity(1003));
        builder.Set(new TestInventoryItem(1, 2, 1));
        builder.Set(new TestWeaponData(3, 7));
        builder.Set(new TestBladedData(12));
        builder.SetPerSpawn(context => new TestFireResistance((int)context.SpawnSequence));
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
