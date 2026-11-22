using System;
using System.IO;
using Novashot.Core.Utils;

namespace Novashot.Core.Tests;

public class FilenamePatternsTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novashot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Timestamp_Produces_Deterministic_Name()
    {
        var stamp = new DateTime(2026, 7, 9, 14, 30, 5);
        var name = FilenamePatterns.Generate(FilenamePatterns.Timestamp, ".", ".png", stamp);
        Assert.Equal("novashot_2026-07-09_14-30-05.png", name);
    }

    [Fact]
    public void Timestamp_Ignores_Folder_Collisions()
    {
        var dir = CreateTempDir();
        try
        {
            var stamp = new DateTime(2026, 1, 2, 3, 4, 5);
            var first = FilenamePatterns.Generate(FilenamePatterns.Timestamp, dir, ".png", stamp);
            File.WriteAllText(Path.Combine(dir, first), "");
            // Same second => same name (timestamp collisions are expected to overwrite / caller handles)
            var second = FilenamePatterns.Generate(FilenamePatterns.Timestamp, dir, ".png", stamp);
            Assert.Equal(first, second);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Date_Appends_Suffix_On_Collision()
    {
        var dir = CreateTempDir();
        try
        {
            var stamp = new DateTime(2026, 7, 9);
            var first = FilenamePatterns.Generate(FilenamePatterns.Date, dir, ".png", stamp);
            Assert.Equal("novashot_2026-07-09.png", first);

            File.WriteAllText(Path.Combine(dir, first), "");
            var second = FilenamePatterns.Generate(FilenamePatterns.Date, dir, ".png", stamp);
            Assert.Equal("novashot_2026-07-09_1.png", second);

            File.WriteAllText(Path.Combine(dir, second), "");
            var third = FilenamePatterns.Generate(FilenamePatterns.Date, dir, ".png", stamp);
            Assert.Equal("novashot_2026-07-09_2.png", third);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Increment_Starts_At_001_When_Empty()
    {
        var dir = CreateTempDir();
        try
        {
            var name = FilenamePatterns.Generate(FilenamePatterns.Increment, dir, ".png");
            Assert.Equal("novashot_001.png", name);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Increment_Uses_Max_Existing_Plus_One()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "novashot_001.png"), "");
            File.WriteAllText(Path.Combine(dir, "novashot_007.png"), "");
            File.WriteAllText(Path.Combine(dir, "novashot_042.png"), "");
            // Non-matching should be ignored
            File.WriteAllText(Path.Combine(dir, "other_099.png"), "");

            var next = FilenamePatterns.Generate(FilenamePatterns.Increment, dir, ".png");
            Assert.Equal("novashot_043.png", next);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Increment_Preserves_Wide_Numbers()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "novashot_9999.png"), "");
            var next = FilenamePatterns.Generate(FilenamePatterns.Increment, dir, ".png");
            // Padded to at least 3 digits; larger numbers keep their natural width via D3 default
            Assert.Equal("novashot_10000.png", next);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("png", ".png")]
    [InlineData(".PNG", ".png")]
    [InlineData(".jpeg", ".jpeg")]
    [InlineData("", ".png")]
    [InlineData(null, ".png")]
    public void Extension_Is_Normalized(string? input, string expectedExt)
    {
        var stamp = new DateTime(2026, 7, 9, 0, 0, 0);
        var name = FilenamePatterns.Generate(FilenamePatterns.Timestamp, ".", input!, stamp);
        Assert.EndsWith(expectedExt, name);
    }

    [Fact]
    public void Unknown_Pattern_Falls_Back_To_Timestamp()
    {
        var stamp = new DateTime(2026, 7, 9, 12, 0, 0);
        var name = FilenamePatterns.Generate("bogus", ".", ".png", stamp);
        Assert.Equal("novashot_2026-07-09_12-00-00.png", name);
    }
}
