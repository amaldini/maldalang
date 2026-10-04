// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class ShowcaseTimelineTests
{
    [Fact]
    public void CheckedInPlaylist_ChainsEachCueToTheNextStart()
    {
        var root = RepoRoot();
        var handshake = Path.Combine(Path.GetTempPath(), "malda-showcase-timeline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = ShowcaseSession.Load(Path.Combine(root, "scripts", "showcase", "playlist.json"), handshake);
            var starts = new List<int>();
            var ends = new List<int>();
            foreach (var pick in session.Playlist.Browse)
            {
                starts.Add(pick.StartMs);
                ends.Add(pick.EndMs);
            }

            foreach (var page in session.Playlist.Manual)
            {
                starts.Add(page.StartMs);
                ends.Add(page.EndMs);
            }

            foreach (var scene in session.Playlist.Scenes)
            {
                starts.Add(scene.StartMs);
                ends.Add(scene.EndMs);
            }

            Assert.Equal(session.Playlist.OpeningMs, starts[0]);
            Assert.Equal(session.Playlist.EndMs, ends[^1]);
            for (var i = 0; i < starts.Count - 1; i++)
            {
                Assert.True(starts[i] < starts[i + 1]);
                Assert.Equal(starts[i + 1], ends[i]);
            }
        }
        finally
        {
            if (Directory.Exists(handshake))
            {
                Directory.Delete(handshake, recursive: true);
            }
        }
    }

    [Fact]
    public void Autostart_StartsWithoutTheRecorderFlag()
    {
        var playlist = Path.Combine(RepoRoot(), "scripts", "showcase", "playlist.json");
        var handshake = Path.Combine(Path.GetTempPath(), "malda-showcase-timeline-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(ShowcaseSession.TryParseLaunchArgs(
                new[] { "--demo", playlist, "--handshake", handshake, "--autostart" },
                out var started,
                out var error));
            Assert.Null(error);
            Assert.NotNull(started);
            Assert.True(started!.AutoStart);

            Assert.True(ShowcaseSession.TryParseLaunchArgs(
                new[] { "--demo", playlist, "--handshake", handshake },
                out var recorded,
                out var recordedError));
            Assert.Null(recordedError);
            Assert.NotNull(recorded);
            Assert.False(recorded!.AutoStart);
        }
        finally
        {
            if (Directory.Exists(handshake))
            {
                Directory.Delete(handshake, recursive: true);
            }
        }
    }

    [Fact]
    public void Timeline_RejectsACueThatDoesNotStartLater()
    {
        var playlist = new ShowcasePlaylist
        {
            EndMs = 3000,
            Scenes =
            {
                new ShowcaseScene { File = "a.malda", Caption = "One", StartMs = 1000 },
                new ShowcaseScene { File = "b.malda", Caption = "Two", StartMs = 1000 }
            }
        };

        var error = Assert.Throws<InvalidOperationException>(() => playlist.BindTimeline());
        Assert.Contains("startMs", error.Message, StringComparison.Ordinal);
        Assert.Contains("Two", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_RejectsAnEndOnOrBeforeTheLastCue()
    {
        var playlist = new ShowcasePlaylist
        {
            EndMs = 2000,
            Scenes =
            {
                new ShowcaseScene { File = "a.malda", Caption = "One", StartMs = 2000 }
            }
        };

        var error = Assert.Throws<InvalidOperationException>(() => playlist.BindTimeline());
        Assert.Contains("endMs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SongPrompt_AssignsOneLyricToEachCue()
    {
        var root = RepoRoot();
        var handshake = Path.Combine(Path.GetTempPath(), "malda-showcase-lyrics-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = ShowcaseSession.Load(Path.Combine(root, "scripts", "showcase", "playlist.json"), handshake);
            var lyrics = session.Lyrics;
            Assert.Equal("Malda, let's begin.", lyrics.Intro);
            Assert.Equal(session.Playlist.Browse.Count, lyrics.CatalogSentences.Count);
            Assert.Equal("Hello, confirm.", lyrics.BrowseLyric(0, session.Playlist.Browse.Count));
            Assert.Equal("The king is watched.", lyrics.BrowseLyric(session.Playlist.Browse.Count - 1, session.Playlist.Browse.Count));

            var captions = new List<string>();
            foreach (var page in session.Playlist.Manual)
            {
                captions.Add(page.Caption);
            }

            Assert.Equal("Open the book.", lyrics.ManualLyric(0, captions));
            Assert.Equal("", lyrics.ManualLyric(captions.FindIndex(caption => caption.Contains("Neural", StringComparison.OrdinalIgnoreCase)), captions));
            Assert.Equal("Many agents, one objective.", lyrics.ManualLyric(captions.FindIndex(caption => caption.Contains("Multi-agent", StringComparison.OrdinalIgnoreCase)), captions));
            Assert.Equal("Play it in the browser.", lyrics.ManualLyric(captions.FindIndex(caption => caption.Contains("Browser", StringComparison.OrdinalIgnoreCase)), captions));

            Assert.Equal(session.Playlist.Scenes.Count, lyrics.DreamLines.Count);
            Assert.Equal("Printed clean.", lyrics.SceneLyric(0));
            Assert.Equal("Last move. Objective done.", lyrics.SceneLyric(session.Playlist.Scenes.Count - 1));
        }
        finally
        {
            if (Directory.Exists(handshake))
            {
                Directory.Delete(handshake, recursive: true);
            }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MaldaLang.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find MaldaLang.sln above the test output.");
    }
}
