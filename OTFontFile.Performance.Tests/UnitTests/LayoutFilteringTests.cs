using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile;
using OTFontFile.Subsetting;

namespace OTFontFile.Performance.Tests.UnitTests;

[TestClass]
public class LayoutFilteringTests
{
    private const string TestFontPath = @"TestResources\SampleFonts\SourceHanSansCN-Regular.otf";

    [TestMethod]
    public void Subset_WithAllFeatures_ProducesValidFont()
    {
        // Arrange
        var options = new SubsetOptions()
            .AddText("中文");
        options.LayoutFeatures = new HashSet<string> { "*" };

        // Act
        var subsetFont = Subset(options);

        // Assert
        Assert.IsNotNull(subsetFont, "Subset with all features should produce valid font");
        Assert.IsTrue(subsetFont.GetNumTables() > 0, "Subset should have tables");
    }

    [TestMethod]
    public void Subset_WithEmptyFeatures_ProducesValidFont()
    {
        // Arrange
        var options = new SubsetOptions()
            .AddText("中文");
        options.LayoutFeatures = new HashSet<string>(); // Drop all

        // Act
        var subsetFont = Subset(options);

        // Assert - font should still work
        Assert.IsNotNull(subsetFont, "Subset with empty features should produce valid font");
    }

    [TestMethod]
    public void Subset_WithKernOnly_ProducesSmallerFont()
    {
        // Arrange - compare default vs kern-only
        var defaultOptions = new SubsetOptions().AddText("中文");
        var kernOnlyOptions = new SubsetOptions().AddText("中文");
        kernOnlyOptions.LayoutFeatures = new HashSet<string> { "kern" };

        // Act
        var defaultPath = Path.GetTempFileName();
        var kernPath = Path.GetTempFileName();
        
        var defaultFont = Subset(defaultOptions);
        var kernFont = Subset(kernOnlyOptions);
        
        using (var fs = new FileStream(defaultPath, FileMode.Create))
            OTFile.WriteSfntFile(fs, defaultFont);
        using (var fs = new FileStream(kernPath, FileMode.Create))
            OTFile.WriteSfntFile(fs, kernFont);

        var defaultSize = new FileInfo(defaultPath).Length;
        var kernSize = new FileInfo(kernPath).Length;

        File.Delete(defaultPath);
        File.Delete(kernPath);

        // Assert - kern-only should be <= default size
        Assert.IsTrue(kernSize <= defaultSize, 
            $"Kern-only font ({kernSize}) should be <= default ({defaultSize})");
    }

    [TestMethod]
    public void Subset_WithSpecificScripts_ProducesValidFont()
    {
        // Arrange
        var options = new SubsetOptions()
            .AddText("中文");
        options.LayoutScripts = new HashSet<string> { "hani" };
        options.LayoutFeatures = new HashSet<string> { "*" };

        // Act
        var subsetFont = Subset(options);

        // Assert
        Assert.IsNotNull(subsetFont, "Subset with specific scripts should work");
    }

    [TestMethod]
    public void Subset_WithDefaultFeatures_MatchesPyftsubsetBehavior()
    {
        // Arrange - null LayoutFeatures = use defaults
        var options = new SubsetOptions()
            .AddText("中文");
        // LayoutFeatures = null

        // Act
        var subsetFont = Subset(options);

        // Assert
        Assert.IsNotNull(subsetFont);
        
        // Default features set should be used
        Assert.IsNotNull(SubsetOptions.DefaultLayoutFeatures);
        Assert.IsTrue(SubsetOptions.DefaultLayoutFeatures.Contains("kern"));
        Assert.IsTrue(SubsetOptions.DefaultLayoutFeatures.Contains("liga"));
        Assert.IsTrue(SubsetOptions.DefaultLayoutFeatures.Contains("mark"));
    }

    private OTFont Subset(SubsetOptions options)
    {
        using var file = new OTFile();
        file.open(GetTestFontPath());
        var font = file.GetFont(0)!;

        var subsetter = new Subsetter(options);
        return subsetter.Subset(font);
    }

    private string GetTestFontPath()
    {
        return Path.Combine(AppContext.BaseDirectory, TestFontPath);
    }
}
