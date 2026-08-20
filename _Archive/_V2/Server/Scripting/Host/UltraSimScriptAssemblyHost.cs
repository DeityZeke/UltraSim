using System.Reflection;
using System.Runtime.Loader;

namespace UltraSim.Server.Scripting;

public sealed class UltraSimScriptAssemblyHost : IScriptAssemblyHost
{
    public UltraSimScriptLoadResult LoadConfigureAndInitialize(string assemblyPath, IUltraSimScriptContext context)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new ArgumentException("Assembly path cannot be empty.", nameof(assemblyPath));
        ArgumentNullException.ThrowIfNull(context);

        var errors = new List<string>();
        try
        {
            var loadContext = new ScriptLoadContext(Path.GetFileNameWithoutExtension(assemblyPath));
            Assembly assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            Type[] moduleTypes = assembly
                .GetTypes()
                .Where(static type => !type.IsAbstract && typeof(IUltraSimScriptModule).IsAssignableFrom(type))
                .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            var modules = new List<IUltraSimScriptModule>(moduleTypes.Length);
            foreach (Type moduleType in moduleTypes)
            {
                if (Activator.CreateInstance(moduleType) is not IUltraSimScriptModule module)
                {
                    errors.Add($"Could not create script module '{moduleType.FullName}'. Modules must have a public parameterless constructor.");
                    continue;
                }

                modules.Add(module);
            }

            if (errors.Count > 0)
                return UltraSimScriptLoadResult.Failed(errors);

            foreach (IUltraSimScriptModule module in modules)
            {
                try
                {
                    module.Configure(context);
                }
                catch (Exception ex)
                {
                    errors.Add($"Configure failed for '{module.GetType().FullName}': {ex.Message}");
                }
            }

            if (errors.Count > 0)
                return UltraSimScriptLoadResult.Failed(errors);

            foreach (IUltraSimScriptModule module in modules)
            {
                try
                {
                    module.Initialize(context);
                }
                catch (Exception ex)
                {
                    errors.Add($"Initialize failed for '{module.GetType().FullName}': {ex.Message}");
                }
            }

            return errors.Count == 0
                ? UltraSimScriptLoadResult.Loaded(modules.Count)
                : UltraSimScriptLoadResult.Failed(errors);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return UltraSimScriptLoadResult.Failed(errors);
        }
    }

    private sealed class ScriptLoadContext : AssemblyLoadContext
    {
        public ScriptLoadContext(string name)
            : base($"UltraSimScripts:{name}", isCollectible: false)
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                AssemblyName loadedName = assembly.GetName();
                if (AssemblyName.ReferenceMatchesDefinition(loadedName, assemblyName))
                    return assembly;
            }

            return null;
        }
    }
}
