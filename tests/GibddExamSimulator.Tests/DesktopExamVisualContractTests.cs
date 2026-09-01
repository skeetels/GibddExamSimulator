using System.Xml.Linq;

namespace GibddExamSimulator.Tests;

public sealed class DesktopExamVisualContractTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void TerminalWindow_IsSeparateBorderlessTahomaSurfaceWithLocalLegacyTheme()
    {
        var document = Load("ExamTerminalWindow.xaml");
        var root = document.Root ?? throw new InvalidDataException("Terminal XAML has no root element.");

        Assert.Equal("None", Attribute(root, "WindowStyle"));
        Assert.Equal("NoResize", Attribute(root, "ResizeMode"));
        Assert.Equal("Tahoma", Attribute(root, "FontFamily"));
        Assert.Equal("16", Attribute(root, "FontSize"));
        Assert.Contains(
            root.Descendants(Presentation + "ResourceDictionary"),
            item => Attribute(item, "Source") == "Resources/LegacyExamTheme.xaml");
        Assert.Contains(root.Descendants(), item => item.Name.LocalName == "ExamView");
        Assert.Contains(root.Descendants(), item => item.Name.LocalName == "SupplementaryIntroView");
    }

    [Fact]
    public void ExamView_PreservesLegacyRowsOverviewAndControlDimensions()
    {
        var document = Load("ExamView.xaml");
        var heights = document.Descendants(Presentation + "RowDefinition")
            .Select(item => Attribute(item, "Height"))
            .ToArray();
        var buttons = document.Descendants(Presentation + "Button").ToArray();

        Assert.Contains("58", heights);
        Assert.Contains("50", heights);
        Assert.Contains("42", heights);
        Assert.Contains(document.Descendants(Presentation + "UniformGrid"), item => Attribute(item, "Rows") == "5");
        Assert.Contains(buttons, item => Attribute(item, "Content") == "ОТВЕТИТЬ" && Attribute(item, "Width") == "230");
        Assert.Contains(buttons, item => Attribute(item, "Content") == "К ПЕРЕЧНЮ" && Attribute(item, "Width") == "175");
        Assert.Equal(2, buttons.Count(item => Attribute(item, "Width") == "62"));
        Assert.DoesNotContain(document.Descendants(), item => item.Attributes().Any(attribute => attribute.Name.LocalName == "CornerRadius"));
    }

    [Fact]
    public void LegacyButtonTheme_HasExactFlatStateColors()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DesktopContract", "LegacyExamTheme.xaml"));

        Assert.Contains("#FFF2F2F2", text, StringComparison.Ordinal);
        Assert.Contains("#FF707070", text, StringComparison.Ordinal);
        Assert.Contains("#FFE1EAF2", text, StringComparison.Ordinal);
        Assert.Contains("#FF125A9C", text, StringComparison.Ordinal);
        Assert.Contains("#FFC8D8E8", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius", text, StringComparison.Ordinal);
    }

    private static XDocument Load(string fileName) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "DesktopContract", fileName));

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;
}
