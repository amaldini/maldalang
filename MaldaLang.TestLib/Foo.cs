// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.TestLib;

public class Foo
{
    public string Name { get; set; } = "default";
    public object? Payload { get; set; }

    public int Add(int a, int b) => a + b;

    public List<object?> CreateRouteItems()
    {
        return new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["path"] = "/reports",
                ["label"] = "Reports"
            },
            new
            {
                path = "/settings",
                label = "Settings"
            }
        };
    }

    public Dictionary<string, object?> CreateNestedPayload()
    {
        return new Dictionary<string, object?>
        {
            ["items"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = 7,
                    ["name"] = "alpha"
                }
            },
            ["meta"] = new
            {
                count = 1,
                title = "payload"
            }
        };
    }
}
