using System.Threading.Tasks;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests.TestSupport;

/// <summary>Builds view models in the state the GUI would reach by the same user actions.</summary>
public static class Harness
{
    /// <summary>A view model whose project has been saved into <paramref name="folder"/>, so relative paths resolve there.</summary>
    public static async Task<MainViewModel> SavedProjectAsync(TempFolder folder, string fileName = "project.xml")
    {
        var dialogs = new FakeDialogService { NextSaveProject = folder.File(fileName) };
        var main = new MainViewModel(dialogs);
        await main.SaveAsCommand.ExecuteAsync(null);
        return main;
    }

    /// <summary>Saved project in <paramref name="folder"/> with a copy of Northwind connected as northwind.db.</summary>
    public static async Task<MainViewModel> NorthwindProjectAsync(TempFolder folder)
    {
        NorthwindDb.CopyInto(folder.Path);
        var main = await SavedProjectAsync(folder);
        main.Database.DatabasePath = "northwind.db";
        await main.Database.ConnectCommand.ExecuteAsync(null);
        return main;
    }
}
