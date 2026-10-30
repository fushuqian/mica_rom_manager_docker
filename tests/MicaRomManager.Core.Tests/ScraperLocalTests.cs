using MicaRomManager.Core.Models;
using MicaRomManager.Core.Services;
using Xunit;

namespace MicaRomManager.Core.Tests;

public sealed class ScraperLocalTests
{
    [Fact]
    public void Scrape_ParsesRegionLanguageAndYear()
    {
        var game = new Game { Title = "Super Metroid", RomPath = "/roms/Super Metroid (Japan) (1994).sfc" };
        var result = FileNameScraper.Scrape(game);

        Assert.Equal("Super Metroid", result.Title);
        Assert.Equal("日版", result.Region);
        Assert.Equal(1994, result.ReleaseYear);
    }

    [Fact]
    public void Scrape_KeepsVersionAsNote()
    {
        var game = new Game { RomPath = "/roms/Game (Rev 1) (USA).nes" };
        var result = FileNameScraper.Scrape(game);

        Assert.Equal("Game", result.Title);
        Assert.Equal("美版", result.Region);
        Assert.Contains("Rev 1", result.Summary);
    }

    [Fact]
    public void NormalizeRegion_MapsEnglishToChinese()
    {
        Assert.Equal("欧版", FileNameScraper.NormalizeRegion("Europe"));
        Assert.Equal("简体中文", FileNameScraper.NormalizeLanguage("zh"));
        Assert.Equal("未知", FileNameScraper.NormalizeRegion("未知"));
    }

    [Fact]
    public void ExtensionMap_DetectsPlatformsAndArchives()
    {
        Assert.Equal("NES", ExtensionMap.Detect("/a/b.nes").Platform);
        Assert.True(ExtensionMap.Detect("/a/b.iso").NeedsSpecify);
        Assert.True(ExtensionMap.IsArchive("/a/b.zip"));
        Assert.True(ExtensionMap.IsKnownOrArchive("/a/b.rar"));
    }

    [Fact]
    public void PlatformFormats_ResolveHandlesUniquenessAndConflicts()
    {
        var formats = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["NES"] = new[] { ".nes" },
            ["SNES"] = new[] { ".sfc" },
            ["MultiA"] = new[] { ".rom" },
            ["MultiB"] = new[] { ".rom" },
        };

        Assert.Equal("NES", PlatformFormats.Resolve("/a/b.nes", ExtensionMap.Detect("/a/b.nes"), formats).Platform);
        Assert.Equal("未指定", PlatformFormats.Resolve("/a/b.iso", ExtensionMap.Detect("/a/b.iso"), formats).Platform);
        Assert.True(PlatformFormats.Resolve("/a/b.rom", ExtensionMap.Detect("/a/b.rom"), formats).NeedsSpecify);
    }

    [Fact]
    public void PlatformFormats_ParseAndNormalize()
    {
        Assert.Equal(new[] { ".nes", ".unf" }, PlatformFormats.Parse("nes, .unf"));
        Assert.Equal("nes unf", PlatformFormats.Normalize("NES，.UNF;nes"));
        Assert.True(PlatformFormats.Supports("nes unf", ".NES"));
    }
}
