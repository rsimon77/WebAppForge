using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>One row of the column editor. It writes through to a <see cref="ListColumn"/> of the list page design.</summary>
public sealed class ColumnRow : ObservableObject
{
    private readonly Action _changed;

    public ColumnRow(ListColumn model, Action changed)
    {
        Model = model;
        _changed = changed;
    }

    public ListColumn Model { get; }

    public string Field
    {
        get => Model.Field;
        set
        {
            // A combo box reports null while its item list is rebuilt; that is not a choice.
            if (value is not null && Model.Field != value)
            {
                Model.Field = value;
                OnPropertyChanged();
                _changed();
            }
        }
    }

    public string Width
    {
        get => Model.Width;
        set
        {
            if (Model.Width != (value ?? string.Empty).Trim())
            {
                Model.Width = (value ?? string.Empty).Trim();
                OnPropertyChanged();
                _changed();
            }
        }
    }

    public bool Visible
    {
        get => Model.Visible;
        set
        {
            if (Model.Visible != value)
            {
                Model.Visible = value;
                OnPropertyChanged();
                _changed();
            }
        }
    }
}

/// <summary>One row of the row style rules grid. It writes through to a <see cref="RowStyleRule"/> of the table.</summary>
public sealed class RowStyleRow : ObservableObject
{
    public RowStyleRow(RowStyleRule model)
    {
        Model = model;
    }

    public RowStyleRule Model { get; }

    public string Field
    {
        get => Model.Field;
        set
        {
            if (value is not null && Model.Field != value)
            {
                Model.Field = value;
                OnPropertyChanged();
            }
        }
    }

    public ConditionOperator Operator
    {
        get => Model.Operator;
        set
        {
            if (Model.Operator != value)
            {
                Model.Operator = value;
                OnPropertyChanged();
            }
        }
    }

    public string Value
    {
        get => Model.Value;
        set
        {
            if (value is not null && Model.Value != value)
            {
                Model.Value = value;
                OnPropertyChanged();
            }
        }
    }

    public string Style
    {
        get => Model.Style;
        set
        {
            if (value is not null && Model.Style != value)
            {
                Model.Style = value;
                OnPropertyChanged();
            }
        }
    }
}

/// <summary>
/// The Tables and Pages tab: table look and menu placement, and the design of each page type of the selected table with a
/// schematic preview. Every property writes straight into <see cref="TableInfo.Ui"/> or the <see cref="PageDesign"/> of the
/// chosen page type, so Save persists the edits. A value that is not valid shows <see cref="EditorViewModelBase.Message"/>
/// and the model keeps the last valid value.
/// </summary>
public partial class PageDesignerViewModel : EditorViewModelBase
{
    private static readonly Regex IconName = new("^[a-z0-9-]*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly MainViewModel _main;
    private PageKind _selectedPage = PageKind.List;
    private bool _loading;
    private ColumnRow? _selectedColumn;
    private RowStyleRow? _selectedRowStyle;

    public PageDesignerViewModel(MainViewModel main)
    {
        _main = main;
        Tables.PropertyChanged += OnTablesChanged;
        Tables.Fields.CollectionChanged += (_, _) => RefreshFieldNames();
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTabIndex))
            {
                RefreshFieldNames();
                OnPropertyChanged(nameof(Schematic));
            }
        };
        Columns.CollectionChanged += (_, _) => SyncColumns();
        RowStyles.CollectionChanged += (_, _) => SyncRowStyles();
        FormPages = new FormPagesViewModel(main, () => OnPropertyChanged(nameof(Schematic)));
    }

    /// <summary>The multi-page form editor (form pages, page modes, field assignment) shown on the Pages tab.</summary>
    public FormPagesViewModel FormPages { get; }

    public TablesViewModel Tables => _main.Tables;

    public TableInfo? Table => Tables.SelectedTable;

    public bool HasTable => Table is not null;

    public ObservableCollection<string> FieldNames { get; } = [];

    public ObservableCollection<RowStyleRow> RowStyles { get; } = [];

    public ObservableCollection<ColumnRow> Columns { get; } = [];

    public void Reload()
    {
        OnTableChanged();
        FormPages.Reload();
    }

    private void OnTablesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TablesViewModel.SelectedTable))
        {
            OnTableChanged();
        }
    }

    private void OnTableChanged()
    {
        ForgetRawText();
        Message = string.Empty;
        LoadCollections();
        OnPropertyChanged(string.Empty);
    }

    protected override void Changed() => OnPropertyChanged(nameof(Schematic));

    private void RefreshFieldNames()
    {
        var desired = Table?.Fields.Select(f => f.Name).ToList() ?? [];
        if (FieldNames.SequenceEqual(desired))
        {
            return;
        }

        FieldNames.Clear();
        foreach (var name in desired)
        {
            FieldNames.Add(name);
        }
    }

    private void LoadCollections()
    {
        _loading = true;
        try
        {
            // Clear the rows before their pick lists change, so no old row sees an emptied list.
            RowStyles.Clear();
            Columns.Clear();
            SelectedRowStyle = null;
            SelectedColumn = null;
            RefreshFieldNames();
            if (Table is { } table)
            {
                foreach (var rule in table.Ui.RowStyles)
                {
                    RowStyles.Add(new RowStyleRow(rule));
                }

                foreach (var column in Design!.Columns)
                {
                    Columns.Add(new ColumnRow(column, Changed));
                }
            }
        }
        finally
        {
            _loading = false;
        }
    }

    // ---- table ui ----

    private TableUi? TableUi => Table?.Ui;

    public string Description
    {
        get => TableUi?.Description ?? string.Empty;
        set => SetTable(value ?? string.Empty, u => u.Description, (u, v) => u.Description = v);
    }

    public string Icon
    {
        get => Shown(nameof(Icon), TableUi?.Icon ?? string.Empty);
        set
        {
            var text = (value ?? string.Empty).Trim();
            if (text.StartsWith("bi-", StringComparison.Ordinal))
            {
                text = text[3..];
            }

            Accept(nameof(Icon), text,
                IconName.IsMatch(text) ? null : "An icon name uses lower case letters, digits and dashes, for example people or card-list. The last valid icon was kept.",
                () => SetTable(text, u => u.Icon, (u, v) => u.Icon = v, nameof(Icon)));
        }
    }

    public string MenuGroup
    {
        get => TableUi?.MenuGroup ?? string.Empty;
        set => SetTable((value ?? string.Empty).Trim(), u => u.MenuGroup, (u, v) => u.MenuGroup = v);
    }

    public bool ShowInMenu
    {
        get => TableUi?.ShowInMenu ?? false;
        set => SetTable(value, u => u.ShowInMenu, (u, v) => u.ShowInMenu = v);
    }

    public string MenuOrderText
    {
        get => Shown(nameof(MenuOrderText), (TableUi?.MenuOrder ?? 0).ToString(CultureInfo.InvariantCulture));
        set
        {
            var text = value ?? string.Empty;
            var ok = TryWholeNumber(text, 0, 9999, out var order);
            Accept(nameof(MenuOrderText), text, ok ? null : RangeMessage("Menu order", 0, 9999),
                () => SetTable(order, u => u.MenuOrder, (u, v) => u.MenuOrder = v, nameof(MenuOrderText)));
        }
    }

    public string TableCssClass
    {
        get => TableUi?.CssClass ?? string.Empty;
        set => SetTable(value ?? string.Empty, u => u.CssClass, (u, v) => u.CssClass = v);
    }

    public RowStyleRow? SelectedRowStyle
    {
        get => _selectedRowStyle;
        set => SetProperty(ref _selectedRowStyle, value);
    }

    [RelayCommand]
    public void AddRowStyle()
    {
        if (Table is not { } table)
        {
            return;
        }

        var rule = new RowStyleRow(new RowStyleRule { Field = table.Fields.FirstOrDefault()?.Name ?? string.Empty });
        RowStyles.Add(rule);
        SelectedRowStyle = rule;
    }

    [RelayCommand]
    public void RemoveRowStyle()
    {
        if (SelectedRowStyle is { } rule)
        {
            RowStyles.Remove(rule);
            SelectedRowStyle = null;
        }
    }

    private void SyncRowStyles()
    {
        if (_loading || Table is null)
        {
            return;
        }

        Table.Ui.RowStyles.Clear();
        Table.Ui.RowStyles.AddRange(RowStyles.Select(r => r.Model));
    }

    // ---- page type ----

    public PageKind SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value))
            {
                ForgetRawText();
                LoadCollections();
                OnPropertyChanged(string.Empty);
            }
        }
    }

    public PageDesign? Design => Table is null ? null : PageRules.DesignOf(Table, _selectedPage);

    public bool IsListPage => PageRules.IsListPage(_selectedPage);

    public bool IsFormPage => PageRules.IsFormPage(_selectedPage);

    public bool IsContainerEnabled => PageRules.CanChooseContainer(_selectedPage);

    /// <summary>Everything about the selected page that the preview draws.</summary>
    public PageSchematic? Schematic => Table is null ? null : SchematicBuilder.Build(Table, _selectedPage, _main.Current.Tables);

    // ---- page design: general ----

    public string Title
    {
        get => Design?.Title ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.Title, (d, v) => d.Title = v);
    }

    public string Subtitle
    {
        get => Design?.Subtitle ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.Subtitle, (d, v) => d.Subtitle = v);
    }

    public string HeaderHtml
    {
        get => Design?.HeaderHtml ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.HeaderHtml, (d, v) => d.HeaderHtml = v);
    }

    public string FooterHtml
    {
        get => Design?.FooterHtml ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.FooterHtml, (d, v) => d.FooterHtml = v);
    }

    public string PageCssClass
    {
        get => Design?.CssClass ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.CssClass, (d, v) => d.CssClass = v);
    }

    public string CustomCss
    {
        get => Design?.CustomCss ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.CustomCss, (d, v) => d.CustomCss = v);
    }

    public string CustomScript
    {
        get => Design?.CustomScript ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.CustomScript, (d, v) => d.CustomScript = v);
    }

    public PageContainer Container
    {
        get => Design?.Container ?? PageContainer.Page;
        set => SetDesign(value, d => d.Container, (d, v) => d.Container = v);
    }

    // ---- page design: forms ----

    public FormLayout FormLayout
    {
        get => Design?.FormLayout ?? FormLayout.Vertical;
        set => SetDesign(value, d => d.FormLayout, (d, v) => d.FormLayout = v);
    }

    public string SubmitText
    {
        get => Design?.SubmitText ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.SubmitText, (d, v) => d.SubmitText = v);
    }

    public string CancelText
    {
        get => Design?.CancelText ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.CancelText, (d, v) => d.CancelText = v);
    }

    public bool ShowCancel
    {
        get => Design?.ShowCancel ?? true;
        set => SetDesign(value, d => d.ShowCancel, (d, v) => d.ShowCancel = v);
    }

    public string SubmitCssClass
    {
        get => Design?.SubmitCssClass ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.SubmitCssClass, (d, v) => d.SubmitCssClass = v);
    }

    public ButtonPlacement ButtonPlacement
    {
        get => Design?.Buttons ?? ButtonPlacement.Bottom;
        set => SetDesign(value, d => d.Buttons, (d, v) => d.Buttons = v);
    }

    public string SuccessMessage
    {
        get => Design?.SuccessMessage ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.SuccessMessage, (d, v) => d.SuccessMessage = v);
    }

    public bool ConfirmBeforeSave
    {
        get => Design?.ConfirmBeforeSave ?? false;
        set => SetDesign(value, d => d.ConfirmBeforeSave, (d, v) => d.ConfirmBeforeSave = v);
    }

    // ---- page design: list ----

    public ListLayout ListLayout
    {
        get => Design?.ListLayout ?? ListLayout.Table;
        set => SetDesign(value, d => d.ListLayout, (d, v) => d.ListLayout = v);
    }

    public bool ShowRowNumbers
    {
        get => Design?.ShowRowNumbers ?? false;
        set => SetDesign(value, d => d.ShowRowNumbers, (d, v) => d.ShowRowNumbers = v);
    }

    public bool ShowPager
    {
        get => Design?.ShowPager ?? true;
        set => SetDesign(value, d => d.ShowPager, (d, v) => d.ShowPager = v);
    }

    /// <summary>Page sizes as a comma list such as "10, 25, 50". Empty means no page size selector.</summary>
    public string PageSizeOptionsText
    {
        get => Shown(nameof(PageSizeOptionsText), string.Join(", ", Design?.PageSizeOptions ?? []));
        set
        {
            var text = value ?? string.Empty;
            var (sizes, error) = ParsePageSizes(text);
            Accept(nameof(PageSizeOptionsText), text, error, () =>
            {
                if (Design is { } design)
                {
                    design.PageSizeOptions.Clear();
                    design.PageSizeOptions.AddRange(sizes);
                }
            });
        }
    }

    /// <summary>Parses "10, 25, 50". Each size must be a whole number from 1 to 1000, listed once.</summary>
    public static (List<int> Sizes, string? Error) ParsePageSizes(string? text)
    {
        var sizes = new List<int>();
        foreach (var part in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) || size < 1 || size > 1000)
            {
                return ([], $"Page sizes must be whole numbers from 1 to 1000 separated by commas ('{part}' is not). The last valid list was kept.");
            }

            if (sizes.Contains(size))
            {
                return ([], $"Page size {size} is listed twice. The last valid list was kept.");
            }

            sizes.Add(size);
        }

        return (sizes, null);
    }

    public bool FilterBuilder
    {
        get => Design?.FilterBuilder ?? false;
        set => SetDesign(value, d => d.FilterBuilder, (d, v) => d.FilterBuilder = v);
    }

    public bool QuickFilter
    {
        get => Design?.QuickFilter ?? false;
        set => SetDesign(value, d => d.QuickFilter, (d, v) => d.QuickFilter = v);
    }

    public bool ResetButton
    {
        get => Design?.ResetButton ?? false;
        set => SetDesign(value, d => d.ResetButton, (d, v) => d.ResetButton = v);
    }

    public bool StickyHeader
    {
        get => Design?.StickyHeader ?? false;
        set => SetDesign(value, d => d.StickyHeader, (d, v) => d.StickyHeader = v);
    }

    public bool Striped
    {
        get => Design?.Striped ?? true;
        set => SetDesign(value, d => d.Striped, (d, v) => d.Striped = v);
    }

    public bool Hover
    {
        get => Design?.Hover ?? true;
        set => SetDesign(value, d => d.Hover, (d, v) => d.Hover = v);
    }

    public bool Bordered
    {
        get => Design?.Bordered ?? false;
        set => SetDesign(value, d => d.Bordered, (d, v) => d.Bordered = v);
    }

    public bool ShowActionsFirst
    {
        get => Design?.ShowActionsFirst ?? false;
        set => SetDesign(value, d => d.ShowActionsFirst, (d, v) => d.ShowActionsFirst = v);
    }

    public string AddButtonText
    {
        get => Design?.AddButtonText ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.AddButtonText, (d, v) => d.AddButtonText = v);
    }

    public string EmptyText
    {
        get => Design?.EmptyText ?? string.Empty;
        set => SetDesign(value ?? string.Empty, d => d.EmptyText, (d, v) => d.EmptyText = v);
    }

    // ---- columns ----

    public ColumnRow? SelectedColumn
    {
        get => _selectedColumn;
        set => SetProperty(ref _selectedColumn, value);
    }

    /// <summary>Adds the next field that is not a column yet. A list without columns shows every field marked "show in list".</summary>
    [RelayCommand]
    public void AddColumn()
    {
        if (Table is not { } table || Design is null)
        {
            return;
        }

        var next = table.Fields.FirstOrDefault(f => Columns.All(c => !string.Equals(c.Field, f.Name, StringComparison.OrdinalIgnoreCase)));
        if (next is null)
        {
            Message = table.Fields.Count == 0 ? "This table has no fields." : "Every field is already a column.";
            return;
        }

        Message = string.Empty;
        var row = new ColumnRow(new ListColumn { Field = next.Name }, Changed);
        Columns.Add(row);
        SelectedColumn = row;
    }

    [RelayCommand]
    public void RemoveColumn()
    {
        if (SelectedColumn is { } row)
        {
            Columns.Remove(row);
            SelectedColumn = null;
        }
    }

    [RelayCommand]
    public void MoveColumnUp() => MoveColumn(-1);

    [RelayCommand]
    public void MoveColumnDown() => MoveColumn(1);

    private void MoveColumn(int delta)
    {
        if (SelectedColumn is not { } row)
        {
            return;
        }

        var from = Columns.IndexOf(row);
        var to = from + delta;
        if (from >= 0 && to >= 0 && to < Columns.Count)
        {
            Columns.Move(from, to);
        }
    }

    /// <summary>Starts an explicit column list with every field marked "show in list".</summary>
    [RelayCommand]
    public void UseListFields()
    {
        if (Table is not { } table || Design is null)
        {
            return;
        }

        Columns.Clear();
        foreach (var field in table.Fields.Where(f => f.ShowInList))
        {
            Columns.Add(new ColumnRow(new ListColumn { Field = field.Name }, Changed));
        }
    }

    /// <summary>Removes every column, so the list shows each field marked "show in list" again.</summary>
    [RelayCommand]
    public void ClearColumns() => Columns.Clear();

    private void SyncColumns()
    {
        if (_loading || Design is not { } design)
        {
            return;
        }

        design.Columns.Clear();
        design.Columns.AddRange(Columns.Select(c => c.Model));
        Changed();
    }

    // ---- reset and copy ----

    /// <summary>Puts the selected page design back to its defaults, after a confirmation.</summary>
    [RelayCommand]
    public async Task ResetPageAsync()
    {
        if (Design is not { } design)
        {
            return;
        }

        var confirmed = await _main.Dialogs.ConfirmAsync(
            "Reset page design",
            $"Reset the {_selectedPage} page design of {Table!.Name} to its defaults?",
            "Reset");
        if (!confirmed)
        {
            return;
        }

        DeepCopy.Into(new PageDesign(), design);
        OnTableChanged();
        _main.Status = $"Reset the {_selectedPage} page design of {Table.Name}.";
    }

    /// <summary>Every page design of the project as "Table / Page", except the selected one.</summary>
    public IReadOnlyList<(string Label, TableInfo Table, PageKind Kind)> CopySources()
    {
        var list = new List<(string, TableInfo, PageKind)>();
        foreach (var table in _main.Current.Tables)
        {
            foreach (var kind in CustomizationChoices.PageKinds)
            {
                if (!(ReferenceEquals(table, Table) && kind == _selectedPage))
                {
                    list.Add(($"{table.Name} / {kind}", table, kind));
                }
            }
        }

        return list;
    }

    /// <summary>Gives the list page of every generated table the filter and sort, quick filter and reset settings of this list.</summary>
    [RelayCommand]
    public void ApplyGridToolsToAllLists()
    {
        if (Table is null)
        {
            return;
        }

        var source = Table.Designs.List;
        var changed = 0;
        foreach (var table in _main.Current.Tables.Where(t => t.Generate && !ReferenceEquals(t, Table)))
        {
            var list = table.Designs.List;
            if (list.FilterBuilder != source.FilterBuilder || list.QuickFilter != source.QuickFilter || list.ResetButton != source.ResetButton)
            {
                list.FilterBuilder = source.FilterBuilder;
                list.QuickFilter = source.QuickFilter;
                list.ResetButton = source.ResetButton;
                changed++;
            }
        }

        OnTableChanged();
        _main.Status = changed == 1
            ? "Copied the searching, filtering and sorting options to 1 other list."
            : $"Copied the searching, filtering and sorting options to {changed} other lists.";
    }

    /// <summary>Copies another page design (of this table or another one) over the selected page design.</summary>
    [RelayCommand]
    public async Task CopyDesignFromAsync()
    {
        if (Design is not { } design)
        {
            return;
        }

        var sources = CopySources();
        var choice = await _main.Dialogs.ChooseOneAsync(
            "Copy page design",
            $"Replace the {_selectedPage} page design of {Table!.Name} with a copy of:",
            sources.Select(s => s.Label).ToList());
        if (choice is null)
        {
            return;
        }

        var source = sources.FirstOrDefault(s => s.Label == choice);
        if (source.Table is null)
        {
            return;
        }

        DeepCopy.Into(PageRules.DesignOf(source.Table, source.Kind), design);
        OnTableChanged();
        _main.Status = $"Copied the {source.Kind} page design of {source.Table.Name} to the {_selectedPage} page of {Table.Name}.";
    }

    // ---- plumbing ----

    private bool SetTable<T>(T value, Func<TableUi, T> get, Action<TableUi, T> set, [CallerMemberName] string? name = null)
        => TableUi is { } ui && Put(get(ui), value, v => set(ui, v), name);

    private bool SetDesign<T>(T value, Func<PageDesign, T> get, Action<PageDesign, T> set, [CallerMemberName] string? name = null)
        => Design is { } design && Put(get(design), value, v => set(design, v), name);
}
