using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using WebAppForge.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FieldInfo = WebAppForge.Core.Model.FieldInfo;
using WebAppForge.Desktop.Localization;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>One row of the form pages list. It writes through to a <see cref="FormPageDef"/>; renaming goes through the owner so fields and grids follow.</summary>
public sealed class FormPageRow : ObservableObject
{
    private readonly FormPagesViewModel _owner;

    public FormPageRow(FormPageDef model, FormPagesViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    public FormPageDef Model { get; }

    public string Name
    {
        get => Model.Name;
        set => _owner.Rename(this, value);
    }

    public string Caption
    {
        get => Model.Caption;
        set => Write(Model.Caption, value, v => Model.Caption = v);
    }

    public string Icon
    {
        get => Model.Icon;
        set => Write(Model.Icon, (value ?? string.Empty).Trim().Replace("bi-", string.Empty, StringComparison.Ordinal), v => Model.Icon = v);
    }

    public string Description
    {
        get => Model.Description;
        set => Write(Model.Description, value, v => Model.Description = v);
    }

    internal void Raise(string name) => OnPropertyChanged(name);

    private void Write(string current, string? value, Action<string> store, [CallerMemberName] string? name = null)
    {
        var text = value ?? string.Empty;
        if (current != text)
        {
            store(text);
            OnPropertyChanged(name);
            _owner.Touched();
        }
    }
}

/// <summary>One row of the field assignment grid. <see cref="PageChoice"/> writes <see cref="FieldUi.FormPage"/>.</summary>
public sealed class FieldPageRow : ObservableObject
{
    private readonly FormPagesViewModel _owner;
    private bool _isSelected;

    public FieldPageRow(FieldInfo field, FormPagesViewModel owner)
    {
        Field = field;
        _owner = owner;
    }

    public FieldInfo Field { get; }

    public string Name => Field.Name;

    public string TypeText => Field.Type.ToString();

    /// <summary>Ticked for the bulk actions.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>The page label shown in the picker: "(first page)" for an empty value, otherwise the page name (even when that page does not exist).</summary>
    public string PageChoice
    {
        get => _owner.ChoiceOf(Field);
        set
        {
            // A combo box reports null while its item list is rebuilt; that is not a choice.
            if (value is not null)
            {
                _owner.Assign(Field, value);
            }
        }
    }

    internal void RaisePage() => OnPropertyChanged(nameof(PageChoice));
}

/// <summary>
/// Multi-page forms of the selected table (hosted by the Pages tab): the list of form pages, the page mode of the View, Add, Copy
/// and Edit pages and the assignment of fields to pages. Everything writes into <see cref="TableInfo.FormPages"/>,
/// <see cref="FieldUi.FormPage"/> and <see cref="PageDesign.MultiPage"/> of the table, so Save persists it.
/// </summary>
public partial class FormPagesViewModel : EditorViewModelBase
{
    /// <summary>The picker entry that stands for an empty <see cref="FieldUi.FormPage"/>: the field goes on the first page.</summary>
    public static string FirstPageLabel => Localizer.Instance["FormPages_FirstPage"];

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly MainViewModel _main;
    private readonly Action _changed;
    private bool _loading;
    private FormPageRow? _selectedPage;
    private string _bulkTarget = FirstPageLabel;

    public FormPagesViewModel(MainViewModel main, Action changed)
    {
        _main = main;
        _changed = changed;
        _main.Tables.PropertyChanged += OnTablesChanged;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTabIndex))
            {
                Reload();
            }
        };
    }

    public static IReadOnlyList<MultiPageMode> Modes { get; } = Enum.GetValues<MultiPageMode>();

    public TableInfo? Table => _main.Tables.SelectedTable;

    public bool HasTable => Table is not null;

    public ObservableCollection<FormPageRow> Pages { get; } = [];

    public ObservableCollection<FieldPageRow> FieldRows { get; } = [];

    /// <summary>"(first page)" followed by the page names, for the pickers of the assignment grid.</summary>
    public ObservableCollection<string> PageChoices { get; } = [FirstPageLabel];

    public ObservableCollection<string> Warnings { get; } = [];

    public bool HasWarnings => Warnings.Count > 0;

    public FormPageRow? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value))
            {
                OnPropertyChanged(nameof(HasSelectedPage));
            }
        }
    }

    public bool HasSelectedPage => _selectedPage is not null;

    /// <summary>The page the bulk "assign" button uses.</summary>
    public string BulkTarget
    {
        get => _bulkTarget;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _bulkTarget, value);
            }
        }
    }

    // ---- page modes ----

    public MultiPageMode ViewMode
    {
        get => Table?.Designs.View.MultiPage ?? MultiPageMode.None;
        set => SetMode(value, t => t.Designs.View);
    }

    public MultiPageMode AddMode
    {
        get => Table?.Designs.Add.MultiPage ?? MultiPageMode.None;
        set => SetMode(value, t => t.Designs.Add);
    }

    public MultiPageMode CopyMode
    {
        get => Table?.Designs.Copy.MultiPage ?? MultiPageMode.None;
        set => SetMode(value, t => t.Designs.Copy);
    }

    public MultiPageMode EditMode
    {
        get => Table?.Designs.Edit.MultiPage ?? MultiPageMode.None;
        set => SetMode(value, t => t.Designs.Edit);
    }

    private void SetMode(MultiPageMode value, Func<TableInfo, PageDesign> pick, [CallerMemberName] string? name = null)
    {
        if (Table is { } table)
        {
            var design = pick(table);
            Put(design.MultiPage, value, v => design.MultiPage = v, name);
        }
    }

    // ---- loading ----

    public void Reload()
    {
        _loading = true;
        try
        {
            var previous = SelectedPage?.Name;
            Pages.Clear();
            FieldRows.Clear();
            SelectedPage = null;
            Message = string.Empty;
            if (Table is { } table)
            {
                foreach (var page in table.FormPages)
                {
                    Pages.Add(new FormPageRow(page, this));
                }

                foreach (var field in table.Fields)
                {
                    FieldRows.Add(new FieldPageRow(field, this));
                }

                SelectedPage = Pages.FirstOrDefault(p => p.Name == previous);
            }

            RefreshChoices();
            RefreshWarnings();
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(string.Empty);
    }

    private void OnTablesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TablesViewModel.SelectedTable))
        {
            Reload();
        }
    }

    private void RefreshChoices()
    {
        var desired = new List<string> { FirstPageLabel };
        desired.AddRange(Pages.Select(p => p.Name));
        if (!PageChoices.SequenceEqual(desired))
        {
            PageChoices.Clear();
            foreach (var choice in desired)
            {
                PageChoices.Add(choice);
            }
        }

        if (!PageChoices.Contains(BulkTarget))
        {
            BulkTarget = FirstPageLabel;
        }

        foreach (var row in FieldRows)
        {
            row.RaisePage();
        }
    }

    private void RefreshWarnings()
    {
        var desired = Table is null
            ? []
            : RelationValidator.ValidateFormPages(Table).Select(i => i.Text).ToList();
        if (!Warnings.SequenceEqual(desired))
        {
            Warnings.Clear();
            foreach (var warning in desired)
            {
                Warnings.Add(warning);
            }
        }

        OnPropertyChanged(nameof(HasWarnings));
    }

    protected override void Changed()
    {
        RefreshWarnings();
        _changed();
    }

    /// <summary>Called by a page row after one of its texts changed.</summary>
    internal void Touched() => Changed();

    // ---- pages ----

    [RelayCommand]
    public void AddPage()
    {
        if (Table is not { } table)
        {
            return;
        }

        var number = table.FormPages.Count + 1;
        string name;
        do
        {
            name = $"Page {number++}";
        }
        while (table.FormPages.Any(p => Names.Equals(p.Name, name)));

        var def = new FormPageDef { Name = name, Caption = name };
        table.FormPages.Add(def);
        var row = new FormPageRow(def, this);
        Pages.Add(row);
        SelectedPage = row;
        Message = string.Empty;
        AfterPagesChanged();
    }

    /// <summary>Removes the selected page. Fields and grids that were on it go back to the first page and below the form.</summary>
    [RelayCommand]
    public void RemovePage()
    {
        if (Table is not { } table || SelectedPage is not { } row)
        {
            return;
        }

        var name = row.Name;
        foreach (var field in table.Fields.Where(f => Names.Equals(f.Ui.FormPage, name)))
        {
            field.Ui.FormPage = string.Empty;
        }

        foreach (var link in table.Details.Where(l => Names.Equals(l.FormPage, name)))
        {
            link.FormPage = string.Empty;
        }

        table.FormPages.Remove(row.Model);
        Pages.Remove(row);
        SelectedPage = null;
        Message = string.Empty;
        AfterPagesChanged();
    }

    [RelayCommand]
    public void MovePageUp() => MovePage(-1);

    [RelayCommand]
    public void MovePageDown() => MovePage(1);

    private void MovePage(int delta)
    {
        if (Table is not { } table || SelectedPage is not { } row)
        {
            return;
        }

        var from = Pages.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Pages.Count)
        {
            return;
        }

        Pages.Move(from, to);
        table.FormPages.RemoveAt(from);
        table.FormPages.Insert(to, row.Model);
        AfterPagesChanged();
    }

    /// <summary>
    /// Renames a page. The new name must be unique (ignoring case) and not empty; otherwise the old name is kept and
    /// <see cref="EditorViewModelBase.Message"/> says why. Fields on the page and grids that sit on it follow the new name.
    /// </summary>
    public bool Rename(FormPageRow row, string? text)
    {
        ArgumentNullException.ThrowIfNull(row);
        var name = (text ?? string.Empty).Trim();
        string? error = null;
        if (name.Length == 0)
        {
            error = Localizer.Instance["FormPages_AFormPageNeedsAName"];
        }
        else if (name.Length > 50)
        {
            error = Localizer.Instance["FormPages_AFormPageNameCanHave"];
        }
        else if (Pages.Any(p => !ReferenceEquals(p, row) && Names.Equals(p.Name, name)))
        {
            error = Localizer.Instance.Format("FormPages_ThereIsAlreadyAFormPage", name);
        }

        if (error is not null)
        {
            Message = error;
            row.Raise(nameof(FormPageRow.Name));
            return false;
        }

        Message = string.Empty;
        var old = row.Model.Name;
        if (old == name)
        {
            return true;
        }

        if (Table is { } table)
        {
            foreach (var field in table.Fields.Where(f => Names.Equals(f.Ui.FormPage, old)))
            {
                field.Ui.FormPage = name;
            }

            foreach (var link in table.Details.Where(l => Names.Equals(l.FormPage, old)))
            {
                link.FormPage = name;
            }
        }

        row.Model.Name = name;
        row.Raise(nameof(FormPageRow.Name));
        if (BulkTarget == old)
        {
            _bulkTarget = name;
        }

        AfterPagesChanged();
        return true;
    }

    private void AfterPagesChanged()
    {
        if (_loading)
        {
            return;
        }

        RefreshChoices();
        OnPropertyChanged(nameof(BulkTarget));
        Changed();
    }

    // ---- field assignment ----

    /// <summary>The picker label of a field: "(first page)" for an empty page, otherwise the stored page name.</summary>
    public string ChoiceOf(FieldInfo field)
        => string.IsNullOrEmpty(field.Ui.FormPage) ? FirstPageLabel : field.Ui.FormPage;

    /// <summary>Puts one field on a page. <see cref="FirstPageLabel"/> clears the page name.</summary>
    public void Assign(FieldInfo field, string choice)
    {
        var value = choice == FirstPageLabel ? string.Empty : choice;
        if (field.Ui.FormPage != value)
        {
            field.Ui.FormPage = value;
            FieldRows.FirstOrDefault(r => ReferenceEquals(r.Field, field))?.RaisePage();
            Changed();
        }
    }

    /// <summary>The index of the page a field is on. An empty or unknown page counts as the first page.</summary>
    public int PageIndexOf(FieldInfo field)
    {
        var index = Pages.ToList().FindIndex(p => Names.Equals(p.Name, field.Ui.FormPage));
        return Math.Max(index, 0);
    }

    private List<FieldPageRow> Ticked() => FieldRows.Where(r => r.IsSelected).ToList();

    [RelayCommand]
    public void SelectAllFields()
    {
        foreach (var row in FieldRows)
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    public void ClearFieldSelection()
    {
        foreach (var row in FieldRows)
        {
            row.IsSelected = false;
        }
    }

    /// <summary>Puts every ticked field on <see cref="BulkTarget"/>.</summary>
    [RelayCommand]
    public void AssignSelected()
    {
        var rows = Ticked();
        if (rows.Count == 0)
        {
            Message = Localizer.Instance["FormPages_TickTheFieldsToAssignFirst"];
            return;
        }

        Message = string.Empty;
        foreach (var row in rows)
        {
            Assign(row.Field, BulkTarget);
        }
    }

    [RelayCommand]
    public void MoveSelectedUp() => MoveSelected(-1);

    [RelayCommand]
    public void MoveSelectedDown() => MoveSelected(1);

    /// <summary>Moves every ticked field to the previous or next page. A field on the first (or last) page stays where it is.</summary>
    private void MoveSelected(int delta)
    {
        var rows = Ticked();
        if (rows.Count == 0)
        {
            Message = Localizer.Instance["FormPages_TickTheFieldsToMoveFirst"];
            return;
        }

        if (Pages.Count == 0)
        {
            Message = Localizer.Instance["FormPages_AddAFormPageFirst"];
            return;
        }

        Message = string.Empty;
        foreach (var row in rows)
        {
            var current = PageIndexOf(row.Field);
            var target = Math.Clamp(current + delta, 0, Pages.Count - 1);
            if (target != current)
            {
                Assign(row.Field, Pages[target].Name);
            }
        }
    }
}
