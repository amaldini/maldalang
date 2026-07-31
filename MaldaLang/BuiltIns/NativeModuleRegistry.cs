namespace MaldaLang.BuiltIns;

using System.Reflection;

public static class NativeModuleRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, object> LoadedModules = new(StringComparer.OrdinalIgnoreCase);

    public static object LoadModule(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Native module name cannot be empty.", nameof(moduleName));

        EnsureRequiredAssembliesLoaded(moduleName);

        lock (Gate)
        {
            if (LoadedModules.TryGetValue(moduleName, out var cached))
                return cached;

            var module = CreateModule(moduleName);
            LoadedModules[moduleName] = module;
            return module;
        }
    }

    public static bool TryLoadModule(string moduleName, out object? module)
    {
        try
        {
            module = LoadModule(moduleName);
            return true;
        }
        catch
        {
            module = null;
            return false;
        }
    }

    private static object CreateModule(string moduleName)
    {
        foreach (var assembly in CandidateAssemblies(moduleName))
        {
            var module = CreateModuleInstance(assembly, moduleName);
            if (module != null)
                return module;
        }

        throw new InvalidOperationException($"Native module '{moduleName}' could not be loaded. Ensure the optional plugin DLL is available.");
    }

    private static void EnsureRequiredAssembliesLoaded(string moduleName)
    {
        if (moduleName.Equals("trading", StringComparison.OrdinalIgnoreCase))
        {
            EnsureAssemblyLoaded("MaldaLang.Trading.Abstractions", CandidateFilePaths("MaldaLang.Trading.Abstractions.dll"));
            EnsureAssemblyLoaded("MaldaLang.Trading.Core", CandidateFilePaths("MaldaLang.Trading.Core.dll"));
            EnsureAssemblyLoaded("MaldaLang.Trading.Plugin", CandidateFilePaths("MaldaLang.Trading.Plugin.dll"));
            return;
        }

        if (moduleName.Equals("timeseries", StringComparison.OrdinalIgnoreCase))
        {
            EnsureAssemblyLoaded("MaldaLang.Timeseries", CandidateFilePaths("MaldaLang.Timeseries.dll"));
        }
    }

    private static void EnsureAssemblyLoaded(string assemblySimpleName, IEnumerable<string> candidatePaths)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                string.Equals(assembly.GetName().Name, assemblySimpleName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        foreach (var candidatePath in candidatePaths)
        {
            if (!File.Exists(candidatePath))
                continue;

            try
            {
                Assembly.LoadFrom(candidatePath);
                return;
            }
            catch
            {
                // Keep probing until one copy loads successfully.
            }
        }
    }

    private static object? CreateModuleInstance(Assembly assembly, string moduleName)
    {
        foreach (var type in SafeGetTypes(assembly))
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            var moduleNameProperty = type.GetProperty("ModuleName", BindingFlags.Instance | BindingFlags.Public);
            var createModuleMethod = type.GetMethod("CreateModule", BindingFlags.Instance | BindingFlags.Public, Array.Empty<Type>());
            if (moduleNameProperty == null || moduleNameProperty.PropertyType != typeof(string) || createModuleMethod == null)
                continue;

            var factory = Activator.CreateInstance(type);
            if (factory == null)
                continue;

            var discoveredModuleName = moduleNameProperty.GetValue(factory) as string;
            if (!string.Equals(discoveredModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            return createModuleMethod.Invoke(factory, null);
        }

        return null;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null)!;
        }
    }

    private static IEnumerable<Assembly> CandidateAssemblies(string moduleName)
    {
        var current = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in current)
        {
            yield return assembly;
        }

        foreach (var candidatePath in CandidateAssemblyPaths(moduleName))
        {
            if (!File.Exists(candidatePath))
                continue;

            var loaded = current.FirstOrDefault(assembly =>
                string.Equals(assembly.Location, candidatePath, StringComparison.OrdinalIgnoreCase));
            if (loaded != null)
            {
                yield return loaded;
                continue;
            }

            Assembly? assemblyInstance = null;
            try
            {
                TryLoadCompanionAbstractions(candidatePath, current);
                assemblyInstance = Assembly.LoadFrom(candidatePath);
            }
            catch
            {
                // Keep probing other locations.
            }

            if (assemblyInstance != null)
                yield return assemblyInstance;
        }
    }

    private static void TryLoadCompanionAbstractions(string candidatePath, Assembly[] loadedAssemblies)
    {
        var candidateFileName = Path.GetFileName(candidatePath);
        if (!string.Equals(candidateFileName, "MaldaLang.Trading.Plugin.dll", StringComparison.OrdinalIgnoreCase))
            return;

        if (loadedAssemblies.Any(assembly => string.Equals(assembly.GetName().Name, "MaldaLang.Trading.Abstractions", StringComparison.OrdinalIgnoreCase)))
            return;

        var candidateDirectory = Path.GetDirectoryName(candidatePath);
        if (string.IsNullOrWhiteSpace(candidateDirectory))
            return;

        var abstractionsPath = Path.Combine(candidateDirectory, "MaldaLang.Trading.Abstractions.dll");
        if (File.Exists(abstractionsPath))
            Assembly.LoadFrom(abstractionsPath);
    }

    private static IEnumerable<string> CandidateAssemblyPaths(string moduleName)
    {
        var fileNames = moduleName.ToLowerInvariant() switch
        {
            "trading" => new[] { "MaldaLang.Trading.Plugin.dll" },
            "timeseries" => new[] { "MaldaLang.Timeseries.dll" },
            _ => new[] { $"MaldaLang.{moduleName}.Plugin.dll", $"{moduleName}.dll", $"MaldaLang.{moduleName}.dll" }
        };

        var envSpecific = moduleName.Equals("trading", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetEnvironmentVariable("MALDA_TRADING_PLUGIN_PATH")
            : null;
        if (!string.IsNullOrWhiteSpace(envSpecific))
            yield return Path.GetFullPath(envSpecific);

        var envDir = Environment.GetEnvironmentVariable("MALDA_NATIVE_MODULE_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            foreach (var fileName in fileNames)
                yield return Path.Combine(envDir, fileName);
        }

        var baseDir = AppContext.BaseDirectory;
        foreach (var fileName in fileNames)
            yield return Path.Combine(baseDir, fileName);

        var currentDir = Environment.CurrentDirectory;
        foreach (var fileName in fileNames)
            yield return Path.Combine(currentDir, fileName);

        foreach (var root in WalkParents(baseDir).Concat(WalkParents(currentDir)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var fileName in fileNames)
            {
                yield return Path.Combine(root, fileName);
                yield return Path.Combine(root, "bin", "Debug", "net8.0", fileName);
                yield return Path.Combine(root, "bin", "Release", "net8.0", fileName);
                yield return Path.Combine(root, "MaldaLang.Trading.Plugin", "bin", "Debug", "net8.0", fileName);
                yield return Path.Combine(root, "MaldaLang.Trading.Plugin", "bin", "Release", "net8.0", fileName);
            }
        }
    }

    private static IEnumerable<string> CandidateFilePaths(string fileName)
    {
        var envSpecific = fileName.Equals("MaldaLang.Trading.Plugin.dll", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetEnvironmentVariable("MALDA_TRADING_PLUGIN_PATH")
            : null;
        if (!string.IsNullOrWhiteSpace(envSpecific))
            yield return Path.GetFullPath(envSpecific);

        var envDir = Environment.GetEnvironmentVariable("MALDA_NATIVE_MODULE_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
            yield return Path.Combine(envDir, fileName);

        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, fileName);

        var currentDir = Environment.CurrentDirectory;
        yield return Path.Combine(currentDir, fileName);

        foreach (var root in WalkParents(baseDir).Concat(WalkParents(currentDir)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(root, fileName);
            yield return Path.Combine(root, "bin", "Debug", "net8.0", fileName);
            yield return Path.Combine(root, "bin", "Release", "net8.0", fileName);
            yield return Path.Combine(root, "MaldaLang.Trading.Abstractions", "bin", "Debug", "net8.0", fileName);
            yield return Path.Combine(root, "MaldaLang.Trading.Abstractions", "bin", "Release", "net8.0", fileName);
            yield return Path.Combine(root, "MaldaLang.Trading.Plugin", "bin", "Debug", "net8.0", fileName);
            yield return Path.Combine(root, "MaldaLang.Trading.Plugin", "bin", "Release", "net8.0", fileName);
        }
    }

    private static IEnumerable<string> WalkParents(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        var dir = new DirectoryInfo(path);
        if (!dir.Exists && dir.Parent != null)
            dir = dir.Parent;

        while (dir != null)
        {
            yield return dir.FullName;
            dir = dir.Parent;
        }
    }
}
