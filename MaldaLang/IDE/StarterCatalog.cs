// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

public class StarterOption
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Track { get; set; } = string.Empty;
    public string RelativeExamplePath { get; set; } = string.Empty;
    public string LearningGoal { get; set; } = string.Empty;
    public string EstimatedTime { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
}

public class LearningBranch
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RelativeExamplePath { get; set; } = string.Empty;
    public string AudienceTrack { get; set; } = string.Empty;
    public string EstimatedTime { get; set; } = string.Empty;
    public string LearningGoal { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
}

public static class StarterCatalog
{
    private static readonly List<StarterOption> Starters =
    [
        new StarterOption
        {
            Id = "hello-world",
            Title = "1. Hello World",
            Description = "Print your first line of code and learn the smallest runnable MALDA program.",
            Track = "student",
            RelativeExamplePath = @"Basics\hello_world.malda",
            LearningGoal = "Learn the smallest runnable MALDA program.",
            EstimatedTime = "5 min",
            Highlights = ["io.print", "Run button", "first success"]
        },
        new StarterOption
        {
            Id = "variables",
            Title = "2. Variables and Arithmetic",
            Description = "Store values, do calculations, and see how a program remembers and updates data.",
            Track = "student",
            RelativeExamplePath = @"Basics\variables_arithmetic.malda",
            LearningGoal = "Understand variables, expressions, and output.",
            EstimatedTime = "10 min",
            Highlights = ["var", "+", "*"]
        },
        new StarterOption
        {
            Id = "conditionals",
            Title = "3. Conditionals",
            Description = "Make the program choose between different paths with if, else if, and else.",
            Track = "student",
            RelativeExamplePath = @"Basics\conditionals.malda",
            LearningGoal = "Understand how MALDA makes decisions.",
            EstimatedTime = "10 min",
            Highlights = ["if", "else if", "else"]
        },
        new StarterOption
        {
            Id = "loops",
            Title = "4. While Loop",
            Description = "Repeat work with a while loop and watch one variable drive the whole program forward.",
            Track = "student",
            RelativeExamplePath = @"Basics\while_loop.malda",
            LearningGoal = "Learn how MALDA repeats actions until a condition changes.",
            EstimatedTime = "12 min",
            Highlights = ["while", "repetition", "state change"]
        },
        new StarterOption
        {
            Id = "for-loop",
            Title = "5. For Loop",
            Description = "Repeat work with a counted for loop before using for-in over arrays.",
            Track = "student",
            RelativeExamplePath = @"Basics\for_loop.malda",
            LearningGoal = "Use MALDA's for loop with a clear loop variable.",
            EstimatedTime = "10 min",
            Highlights = ["for", "counter", "loop variable"]
        },
        new StarterOption
        {
            Id = "functions",
            Title = "6. First Function",
            Description = "Group reusable logic into a named function and call it with arguments.",
            Track = "student",
            RelativeExamplePath = @"Basics\functions.malda",
            LearningGoal = "Learn how MALDA organizes reusable code.",
            EstimatedTime = "12 min",
            Highlights = ["function", "parameters", "return"]
        },
        new StarterOption
        {
            Id = "complete-starter",
            Title = "7. Complete Starter Program",
            Description = "Put the basics together in one linear example with variables, arrays, a loop, a function, and an if statement.",
            Track = "student",
            RelativeExamplePath = @"Basics\complete_starter_program.malda",
            LearningGoal = "See how the basic syntax fits together in one small real program.",
            EstimatedTime = "12 min",
            Highlights = ["arrays", "for-in", "function", "if"]
        },
        new StarterOption
        {
            Id = "input-output",
            Title = "8. Input and Output",
            Description = "Read from the user, convert values, and print meaningful feedback once the earlier syntax feels familiar.",
            Track = "student",
            RelativeExamplePath = @"Basics\input_example.malda",
            LearningGoal = "Connect program input to visible output.",
            EstimatedTime = "10 min",
            Highlights = ["io.input", "int()", "user interaction"]
        },
        new StarterOption
        {
            Id = "prompt",
            Title = "First Prompt",
            Description = "Try MALDA prompts as a bridge from programming basics into AI-native workflows.",
            Track = "ai-builder",
            RelativeExamplePath = @"Prompts\basic_prompt.malda",
            LearningGoal = "See how MALDA turns a prompt block into a reusable object.",
            EstimatedTime = "8 min",
            Highlights = ["prompt", "structured prompt blocks", "AI-ready syntax"]
        },
        new StarterOption
        {
            Id = "agent",
            Title = "First Agent",
            Description = "Open a simple agent example and explore how MALDA wires prompts into agent behavior.",
            Track = "ai-builder",
            RelativeExamplePath = @"Prompts\prompt_with_agent.malda",
            LearningGoal = "Understand MALDA's jump from prompt blocks to agent workflows.",
            EstimatedTime = "12 min",
            Highlights = ["Agent", "prompt reuse", "AI workflow"]
        },
        new StarterOption
        {
            Id = "local-llm",
            Title = "Local LLM",
            Description = "Run MALDA against a GGUF model on your machine and explore private, offline AI workflows.",
            Track = "ai-builder",
            RelativeExamplePath = @"AI_LLM\local_llm_example.malda",
            LearningGoal = "Learn how MALDA uses `LlamaCppClient` for local completions, conversations, and agents.",
            EstimatedTime = "15 min",
            Highlights = ["LlamaCppClient", "offline inference", "GGUF", "privacy"]
        },
        new StarterOption
        {
            Id = "fullstack",
            Title = "See Full-Stack Demo",
            Description = "Jump straight to a compact showcase of MALDA's full-stack UI controls and runtime.",
            Track = "showcase",
            RelativeExamplePath = @"Web\ui_controls_showcase_minimal.malda",
            LearningGoal = "See the commercial ceiling after the first lessons.",
            EstimatedTime = "15 min",
            Highlights = ["ui.*", "server-driven UI", "full-stack"]
        }
    ];

    private static readonly List<LearningBranch> Branches =
    [
        new LearningBranch
        {
            Id = "testing-track",
            Title = "Testing and Quality",
            Description = "Learn how MALDA expresses invariants, runs properties, and turns failures into regressions.",
            RelativeExamplePath = @"Testing\run_property_inline.malda",
            AudienceTrack = "student",
            EstimatedTime = "10-15 min",
            LearningGoal = "Branch from core language lessons into confidence-building test workflows.",
            Highlights = ["property tests", "runProperty()", "regressions"]
        },
        new LearningBranch
        {
            Id = "database-track",
            Title = "Data and Databases",
            Description = "Move from local programs into data-backed workflows with SQLite and then PostgreSQL.",
            RelativeExamplePath = @"Databases\sqlite_basic.malda",
            AudienceTrack = "student",
            EstimatedTime = "12-15 min",
            LearningGoal = "Practice real application data patterns after the fundamentals.",
            Highlights = ["SqliteClient", "queries", "transactions"]
        },
        new LearningBranch
        {
            Id = "browser-track",
            Title = "Browser Apps",
            Description = "Take MALDA into the browser with DOM updates, counters, games, and 3D examples.",
            RelativeExamplePath = @"Web\js\counter.malda",
            AudienceTrack = "showcase",
            EstimatedTime = "12-18 min",
            LearningGoal = "Explore MALDA's browser runtime once the learner wants visible interactive projects.",
            Highlights = ["DOM", "events", "games", "3D"]
        }
    ];

    public static List<StarterOption> GetAll()
    {
        return Starters
            .Select(Clone)
            .ToList();
    }

    public static List<StarterOption> GetByTrack(string? track)
    {
        if (string.IsNullOrWhiteSpace(track))
        {
            return GetAll();
        }

        return Starters
            .Where(starter => starter.Track.Equals(track, StringComparison.OrdinalIgnoreCase))
            .Select(Clone)
            .ToList();
    }

    public static StarterOption? GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var starter = Starters.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return starter == null ? null : Clone(starter);
    }

    public static string NormalizeExamplePath(string? path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim();
    }

    public static StarterOption? GetByRelativeExamplePath(string? relativeExamplePath)
    {
        var normalized = NormalizeExamplePath(relativeExamplePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var starter = Starters.FirstOrDefault(item =>
            NormalizeExamplePath(item.RelativeExamplePath)
                .Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return starter == null ? null : Clone(starter);
    }

    public static StarterOption? GetNextStudentStarter(string? relativeExamplePath)
    {
        var current = GetByRelativeExamplePath(relativeExamplePath);
        if (current == null || !IsStudentTrack(current.Track))
        {
            return null;
        }

        var student = StudentStarters();
        var index = student.FindIndex(item => item.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index >= student.Count - 1)
        {
            return null;
        }

        return Clone(student[index + 1]);
    }

    public static bool IsLastStudentStarter(string? relativeExamplePath)
    {
        var current = GetByRelativeExamplePath(relativeExamplePath);
        if (current == null || !IsStudentTrack(current.Track))
        {
            return false;
        }

        var student = StudentStarters();
        return student.Count > 0 &&
               student[^1].Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStudentTrack(string track)
    {
        return track.Equals("student", StringComparison.OrdinalIgnoreCase);
    }

    private static List<StarterOption> StudentStarters()
    {
        return Starters.Where(item => IsStudentTrack(item.Track)).ToList();
    }

    public static List<LearningBranch> GetBranches()
    {
        return Branches
            .Select(Clone)
            .ToList();
    }

    public static List<LearningBranch> GetBranchesForTrack(string? track)
    {
        if (string.IsNullOrWhiteSpace(track))
        {
            return GetBranches();
        }

        return Branches
            .OrderByDescending(branch => branch.AudienceTrack.Equals(track, StringComparison.OrdinalIgnoreCase))
            .ThenBy(branch => branch.Title)
            .Select(Clone)
            .ToList();
    }

    public static string GetBranchTitleSummary()
    {
        var titles = Branches.Select(branch => branch.Title).ToList();
        return titles.Count switch
        {
            0 => string.Empty,
            1 => titles[0],
            2 => $"{titles[0]} and {titles[1]}",
            _ => $"{string.Join(", ", titles.Take(titles.Count - 1))}, and {titles[^1]}"
        };
    }

    private static StarterOption Clone(StarterOption starter)
    {
        return new StarterOption
        {
            Id = starter.Id,
            Title = starter.Title,
            Description = starter.Description,
            Track = starter.Track,
            RelativeExamplePath = starter.RelativeExamplePath,
            LearningGoal = starter.LearningGoal,
            EstimatedTime = starter.EstimatedTime,
            Highlights = starter.Highlights.ToList()
        };
    }

    private static LearningBranch Clone(LearningBranch branch)
    {
        return new LearningBranch
        {
            Id = branch.Id,
            Title = branch.Title,
            Description = branch.Description,
            RelativeExamplePath = branch.RelativeExamplePath,
            AudienceTrack = branch.AudienceTrack,
            EstimatedTime = branch.EstimatedTime,
            LearningGoal = branch.LearningGoal,
            Highlights = branch.Highlights.ToList()
        };
    }
}
