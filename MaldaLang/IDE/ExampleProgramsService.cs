// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Text.Json;

namespace MaldaLang.IDE;

public class ExampleProgramsService
{
    private static List<ExampleProgram>? _cachedExamples;
    
    public static List<ExampleProgram> GetExamples()
    {
        if (_cachedExamples != null)
        {
            return _cachedExamples;
        }
        
        _cachedExamples = new List<ExampleProgram>();
        
        // Get the Examples directory path
        var examplesPath = GetExamplesPath();
        if (!Directory.Exists(examplesPath))
        {
            // Return empty list if Examples directory doesn't exist
            return _cachedExamples;
        }
        
        // Scan subdirectories
        var categoryDirs = Directory.GetDirectories(examplesPath);
        foreach (var categoryDir in categoryDirs)
        {
            var categoryName = Path.GetFileName(categoryDir);
            var metadataPath = Path.Combine(categoryDir, "metadata.json");
            
            if (!File.Exists(metadataPath))
            {
                continue;
            }
            
            try
            {
                var jsonContent = File.ReadAllText(metadataPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var metadata = JsonSerializer.Deserialize<MetadataFile>(jsonContent, options);
                
                if (metadata?.Examples != null)
                {
                    foreach (var exampleMeta in metadata.Examples)
                    {
                        var filePath = Path.Combine(categoryDir, exampleMeta.File);
                        if (File.Exists(filePath))
                        {
                            var code = File.ReadAllText(filePath);
                            var relativePath = Path.Combine(categoryName, exampleMeta.File);
                            
                            _cachedExamples.Add(new ExampleProgram
                            {
                                Name = exampleMeta.Name,
                                Description = exampleMeta.Description,
                                Code = code,
                                Category = categoryName,
                                FilePath = relativePath,
                                AbsoluteFilePath = Path.GetFullPath(filePath),
                                Track = exampleMeta.Track,
                                Difficulty = exampleMeta.Difficulty,
                                Minutes = exampleMeta.Minutes,
                                Concepts = exampleMeta.Concepts ?? new List<string>(),
                                Prerequisites = exampleMeta.Prerequisites ?? new List<string>(),
                                Requires = exampleMeta.Requires ?? new List<string>(),
                                LearningGoal = exampleMeta.LearningGoal,
                                ExpectedOutput = exampleMeta.ExpectedOutput,
                                Next = exampleMeta.Next,
                                DocumentationPath = exampleMeta.DocumentationPath,
                                DocumentationTitle = exampleMeta.DocumentationTitle,
                                Featured = exampleMeta.Featured
                            });
                        }
                    }
                }
            }
            catch
            {
                // Skip invalid metadata files
                continue;
            }
        }
        
        return _cachedExamples;
    }
    
    private static string GetExamplesPath()
    {
        // Helper to check if Examples directory has valid content (subdirectories with metadata.json)
        bool IsValidExamplesPath(string path)
        {
            if (!Directory.Exists(path)) return false;
            var subdirs = Directory.GetDirectories(path);
            foreach (var subdir in subdirs)
            {
                var metadataPath = Path.Combine(subdir, "metadata.json");
                if (File.Exists(metadataPath)) return true;
            }
            return false;
        }
        
        // Try relative to current working directory first (most common case)
        var cwd = Directory.GetCurrentDirectory();
        var examplesPath = Path.Combine(cwd, "Examples");
        if (IsValidExamplesPath(examplesPath))
        {
            return examplesPath;
        }
        
        // Try going up from current directory
        var currentDir = new DirectoryInfo(cwd);
        while (currentDir != null)
        {
            examplesPath = Path.Combine(currentDir.FullName, "Examples");
            if (IsValidExamplesPath(examplesPath))
            {
                return examplesPath;
            }
            currentDir = currentDir.Parent;
        }
        
        // Try relative to executable/assembly location
        var exePath = AppDomain.CurrentDomain.BaseDirectory;
        examplesPath = Path.Combine(exePath, "Examples");
        if (IsValidExamplesPath(examplesPath))
        {
            return examplesPath;
        }
        
        // Try going up from executable directory
        var exeDir = new DirectoryInfo(exePath);
        while (exeDir != null)
        {
            examplesPath = Path.Combine(exeDir.FullName, "Examples");
            if (IsValidExamplesPath(examplesPath))
            {
                return examplesPath;
            }
            exeDir = exeDir.Parent;
        }
        
        // Try relative to assembly location (more reliable for published apps)
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            var assemblyDir = Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                examplesPath = Path.Combine(assemblyDir, "Examples");
                if (IsValidExamplesPath(examplesPath))
                {
                    return examplesPath;
                }
                
                // Try going up from assembly directory
                var assemblyDirInfo = new DirectoryInfo(assemblyDir);
                while (assemblyDirInfo != null)
                {
                    examplesPath = Path.Combine(assemblyDirInfo.FullName, "Examples");
                    if (IsValidExamplesPath(examplesPath))
                    {
                        return examplesPath;
                    }
                    assemblyDirInfo = assemblyDirInfo.Parent;
                }
            }
        }
        
        // Fallback: return path relative to current working directory
        return Path.Combine(cwd, "Examples");
    }
    
    private class MetadataFile
    {
        public List<ExampleMetadata>? Examples { get; set; }
    }
    
    private class ExampleMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Track { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;
        public int? Minutes { get; set; }
        public List<string>? Concepts { get; set; }
        public List<string>? Prerequisites { get; set; }
        public List<string>? Requires { get; set; }
        public string LearningGoal { get; set; } = string.Empty;
        public string ExpectedOutput { get; set; } = string.Empty;
        public string Next { get; set; } = string.Empty;
        public string DocumentationPath { get; set; } = string.Empty;
        public string DocumentationTitle { get; set; } = string.Empty;
        public bool Featured { get; set; }
    }
    
    /// <summary>
    /// Gets the display order for categories, matching the reference manual structure.
    /// Categories are ordered from basics to advanced.
    /// </summary>
    public static int GetCategoryOrder(string category)
    {
        // Language Fundamentals (basics first)
        if (category.Equals("Basics", StringComparison.OrdinalIgnoreCase)) return 1;
        if (category.Equals("OOP", StringComparison.OrdinalIgnoreCase)) return 2;
        if (category.Equals("Prompts", StringComparison.OrdinalIgnoreCase)) return 3;
        if (category.Equals("Plan", StringComparison.OrdinalIgnoreCase)) return 4;

        // Built-in Features
        if (category.Equals("Tools", StringComparison.OrdinalIgnoreCase)) return 10;
        if (category.Equals("Databases", StringComparison.OrdinalIgnoreCase)) return 11;
        if (category.Equals("Web", StringComparison.OrdinalIgnoreCase)) return 12;
        if (category.Equals("Graphs", StringComparison.OrdinalIgnoreCase)) return 13;
        if (category.Equals("VectorDB", StringComparison.OrdinalIgnoreCase)) return 14;
        if (category.Equals("SpectreConsole", StringComparison.OrdinalIgnoreCase)) return 15;
        if (category.Equals("Testing", StringComparison.OrdinalIgnoreCase)) return 16;
        if (category.Equals("LLM_Servers", StringComparison.OrdinalIgnoreCase)) return 17;
        if (category.Equals("MCP", StringComparison.OrdinalIgnoreCase)) return 18;
        
        // AI & Advanced Features
        if (category.Equals("Actors", StringComparison.OrdinalIgnoreCase)) return 20;
        if (category.Equals("Agents", StringComparison.OrdinalIgnoreCase)) return 21;
        if (category.Equals("AI_LLM", StringComparison.OrdinalIgnoreCase)) return 22;
        if (category.Equals("ACP", StringComparison.OrdinalIgnoreCase)) return 23;
        
        // Extended Features
        if (category.Equals("Devices", StringComparison.OrdinalIgnoreCase)) return 31;
        
        // Unknown categories go to the end
        return 100;
    }
    
    /// <summary>
    /// Gets examples sorted by category order (matching reference manual structure).
    /// </summary>
    public static List<ExampleProgram> GetExamplesSorted()
    {
        var examples = GetExamples();
        return examples.OrderBy(e => GetCategoryOrder(e.Category ?? ""))
                      .ThenByDescending(e => e.Featured)
                      .ThenBy(e => e.Name)
                      .ToList();
    }

    public static ExampleProgram? GetExampleByRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalizedPath = NormalizePath(relativePath);
        var metadataExample = GetExamples().FirstOrDefault(example =>
            NormalizePath(example.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (metadataExample != null)
        {
            return metadataExample;
        }

        return TryLoadExampleDirectly(normalizedPath);
    }

    public static ExampleProgram? GetExampleByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return GetExamples().FirstOrDefault(example =>
            example.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static List<ExampleProgram> GetExamplesByTrack(string? track)
    {
        if (string.IsNullOrWhiteSpace(track))
        {
            return GetExamplesSorted();
        }

        return GetExamplesSorted()
            .Where(example => example.Track.Equals(track, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// True when the example is suitable for the browser playground without API keys,
    /// databases, or a long-lived network server. Missing <c>requires</c> counts as offline.
    /// </summary>
    public static bool IsOfflineFriendly(ExampleProgram example)
    {
        if (example.Requires == null || example.Requires.Count == 0)
            return true;

        foreach (var tag in example.Requires)
        {
            if (string.Equals(tag, "offline", StringComparison.OrdinalIgnoreCase))
                continue;
            return false;
        }

        return true;
    }
    
    /// <summary>
    /// Gets unique categories sorted by their display order.
    /// </summary>
    public static List<string> GetCategoriesSorted()
    {
        var examples = GetExamples();
        var categories = examples.Select(e => e.Category ?? "")
                                 .Where(c => !string.IsNullOrEmpty(c))
                                 .Distinct()
                                 .OrderBy(c => GetCategoryOrder(c))
                                 .ToList();
        return categories;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar)
                   .Replace('\\', Path.DirectorySeparatorChar);
    }

    private static ExampleProgram? TryLoadExampleDirectly(string normalizedRelativePath)
    {
        var examplesPath = GetExamplesPath();
        var absolutePath = Path.GetFullPath(Path.Combine(examplesPath, normalizedRelativePath));

        if (!File.Exists(absolutePath))
        {
            return null;
        }

        var category = Path.GetDirectoryName(normalizedRelativePath) ?? string.Empty;
        return new ExampleProgram
        {
            Name = Path.GetFileNameWithoutExtension(normalizedRelativePath).Replace('_', ' '),
            Description = $"Directly loaded example from {normalizedRelativePath}.",
            Code = File.ReadAllText(absolutePath),
            Category = category,
            FilePath = normalizedRelativePath,
            AbsoluteFilePath = absolutePath
        };
    }
}
