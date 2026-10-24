using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WebAppForge.Desktop.Tests.TestSupport;

namespace WebAppForge.Desktop.Tests;

public class GenerateTests
{
    [NorthwindFact]
    public async Task GenerateWritesNorthwindProjectIntoOutputFolder()
    {
        using var folder = new TempFolder();
        var main = await Harness.NorthwindProjectAsync(folder);

        await main.Generate.GenerateCommand.ExecuteAsync(null);

        var generatedTables = main.Current.Tables.Count(t => t.Generate);
        var minimumFiles = generatedTables * 3 + 4;
        var outputFolder = folder.File("generated");

        var match = Regex.Match(main.Status, @"^Generated (\d+) file\(s\) into (.+)\.$");
        Assert.True(match.Success, $"Unexpected status: {main.Status}");
        var reported = int.Parse(match.Groups[1].Value);
        Assert.True(reported >= minimumFiles, $"Expected at least {minimumFiles} files, reported {reported}.");
        Assert.Equal(Path.GetFullPath(outputFolder), Path.GetFullPath(match.Groups[2].Value), ignoreCase: true);

        var onDisk = Directory.GetFiles(outputFolder, "*", SearchOption.AllDirectories).Length;
        Assert.Equal(reported, onDisk);
        Assert.Equal(generatedTables, Directory.GetFiles(Path.Combine(outputFolder, "Models")).Length);
        Assert.Equal(generatedTables, Directory.GetFiles(Path.Combine(outputFolder, "Pages")).Length);
        Assert.True(File.Exists(Path.Combine(outputFolder, "appsettings.json")));
        Assert.DoesNotContain("Exception", main.Status);
    }

    [Fact]
    public async Task GenerateReportsNotSupportedMessageForNonSqliteProvider()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        main.Current.Database.Provider = "postgres";

        await main.Generate.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(
            "Not supported: Provider 'postgres' is not supported. Use one of: sqlite, sqlserver, azuresql, mysql.",
            main.Status);
        Assert.False(Directory.Exists(folder.File("generated")));
    }
}
