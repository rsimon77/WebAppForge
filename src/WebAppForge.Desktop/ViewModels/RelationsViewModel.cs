using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FieldInfo = WebAppForge.Core.Model.FieldInfo;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>An enum value with the text a picker shows for it.</summary>
public sealed record RelationChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One entry of the "on delete" picker. A disabled entry (Set null while a link field is required) cannot be chosen.</summary>
public sealed class DeleteRuleOption : ObservableObject
{
    private bool _isEnabled = true;

    public DeleteRuleOption(DeleteRule rule, string label, string explanation)
    {
        Rule = rule;
        Label = label;
        Explanation = explanation;
    }

    public DeleteRule Rule { get; }

    public string Label { get; }

    /// <summary>One line saying what the rule does.</summary>
    public string Explanation { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public override string ToString() => Label;
}

/// <summary>One entry of the detail link list of the master table.</summary>
public sealed class DetailLinkRow : ObservableObject
{
    private readonly TableInfo _master;

    public DetailLinkRow(TableInfo master, DetailLink link)
    {
        _master = master;
        Link = link;
    }

    public DetailLink Link { get; }

    /// <summary>"Orders (Id = CustomerId)".</summary>
    public string Text => string.IsNullOrWhiteSpace(Link.DetailTable)
        ? "(no detail table)"
        : Link.Links.Count == 0 ? Link.DetailTable : $"{Link.DetailTable} ({RelationValidator.PairsText(Link)})";

    internal void Refresh() => OnPropertyChanged(nameof(Text));

    public override string ToString() => $"{_master.Name} {Text}";
}

/// <summary>One key link pair of the selected link. It writes through to a <see cref="KeyLink"/> and shows the types of both fields.</summary>
public sealed class KeyPairRow : ObservableObject
{
    private readonly RelationsViewModel _owner;

    public KeyPairRow(KeyLink model, RelationsViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    public KeyLink Model { get; }

    public string MasterField
    {
        get => Model.MasterField;
        set
        {
            // A combo box reports null while its item list is rebuilt; that is not a choice.
            if (value is not null && Model.MasterField != value)
            {
                Model.MasterField = value;
                RaiseAll();
                _owner.Touched();
            }
        }
    }

    public string DetailField
    {
        get => Model.DetailField;
        set
        {
            if (value is not null && Model.DetailField != value)
            {
                Model.DetailField = value;
                RaiseAll();
                _owner.Touched();
            }
        }
    }

    public string MasterType => _owner.TypeOfMasterField(Model.MasterField);

    public string DetailType => _owner.TypeOfDetailField(Model.DetailField);

    /// <summary>"Types differ" when both fields exist and have different types, otherwise empty.</summary>
    public string TypeNote => MasterType != "?" && DetailType != "?" && MasterType != DetailType ? "Types differ" : string.Empty;

    internal void RaiseAll()
    {
        OnPropertyChanged(nameof(MasterField));
        OnPropertyChanged(nameof(DetailField));
        OnPropertyChanged(nameof(MasterType));
        OnPropertyChanged(nameof(DetailType));
        OnPropertyChanged(nameof(TypeNote));
    }
}

/// <summary>One column of the detail grid.</summary>
public sealed class DetailColumnRow : ObservableObject
{
    private readonly Action _changed;
    private string _field;

    public DetailColumnRow(string field, Action changed)
    {
        _field = field;
        _changed = changed;
    }

    public string Field
    {
        get => _field;
        set
        {
            if (value is not null && _field != value)
            {
                _field = value;
                OnPropertyChanged();
                _changed();
            }
        }
    }
}

/// <summary>One line of the relations overview.</summary>
public sealed record OverviewRow(TableInfo Master, DetailLink Link, string Text)
{
    public override string ToString() => Text;
}

/// <summary>
/// The Relations tab: the master/detail links of the selected (master) table, suggestions from foreign keys, an overview of every link and
/// the validation list. Every property writes straight into <see cref="TableInfo.Details"/> of the master table, so Save persists it.
/// Errors of <see cref="RelationValidator"/> disable Generate through <see cref="SecurityViewModel.CanGenerate"/>.
/// </summary>
public partial class RelationsViewModel : EditorViewModelBase
{
    /// <summary>The form page picker entry for an empty <see cref="DetailLink.FormPage"/>.</summary>
    public const string BelowFormLabel = "(below the form)";

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly MainViewModel _main;
    private bool _loading;
    private DetailLinkRow? _selectedLink;
    private KeyPairRow? _selectedKeyPair;
    private DetailColumnRow? _selectedColumn;
    private OverviewRow? _selectedOverview;
    private string _suggestMessage = string.Empty;

    public RelationsViewModel(MainViewModel main)
    {
        _main = main;
        DeleteRuleOptions =
        [
            new DeleteRuleOption(DeleteRule.Restrict, "Restrict", "Refuse to delete a master record while it has detail records, and say so on the page."),
            new DeleteRuleOption(DeleteRule.Cascade, "Cascade", "Delete the detail records together with the master record, in one transaction."),
            new DeleteRuleOption(DeleteRule.SetNull, "Set null", "Keep the detail records and clear their link fields (the fields must allow null)."),
            new DeleteRuleOption(DeleteRule.None, "None", "Do nothing; the database's own foreign key rules, if any, apply."),
        ];
        UpdateChoices =
        [
            new RelationChoice<UpdateRule>(UpdateRule.None, "None"),
            new RelationChoice<UpdateRule>(UpdateRule.Cascade, "Cascade"),
        ];
        PreviewChoices =
        [
            new RelationChoice<DetailPreview>(DetailPreview.None, "None"),
            new RelationChoice<DetailPreview>(DetailPreview.ExpandableRow, "Expandable row"),
            new RelationChoice<DetailPreview>(DetailPreview.Modal, "Modal"),
            new RelationChoice<DetailPreview>(DetailPreview.Offcanvas, "Offcanvas"),
        ];

        _main.Tables.PropertyChanged += OnTablesChanged;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTabIndex))
            {
                Reload();
            }
        };
        Columns.CollectionChanged += (_, _) => SyncColumns();
    }

    public TablesViewModel Tables => _main.Tables;

    /// <summary>The master table: the table selected on the Tables tab.</summary>
    public TableInfo? Table => Tables.SelectedTable;

    public bool HasTable => Table is not null;

    public IReadOnlyList<DeleteRuleOption> DeleteRuleOptions { get; }

    public IReadOnlyList<RelationChoice<UpdateRule>> UpdateChoices { get; }

    public IReadOnlyList<RelationChoice<DetailPreview>> PreviewChoices { get; }

    /// <summary>Detail links of the master table, in order.</summary>
    public ObservableCollection<DetailLinkRow> Links { get; } = [];

    public ObservableCollection<KeyPairRow> KeyPairs { get; } = [];

    public ObservableCollection<DetailColumnRow> Columns { get; } = [];

    /// <summary>Every table of the project: a link may point to any table, the master itself included (a self link, such as a tree).</summary>
    public ObservableCollection<string> DetailTableNames { get; } = [];

    public ObservableCollection<string> MasterFieldNames { get; } = [];

    public ObservableCollection<string> DetailFieldNames { get; } = [];

    /// <summary>"(below the form)" followed by the form pages of the master table.</summary>
    public ObservableCollection<string> FormPageChoices { get; } = [BelowFormLabel];

    /// <summary>The problems of the selected link (errors and warnings).</summary>
    public ObservableCollection<string> LinkIssues { get; } = [];

    public ObservableCollection<OverviewRow> Overview { get; } = [];

    /// <summary>The validation list of the whole project.</summary>
    public ObservableCollection<RelationIssue> Issues { get; } = [];

    public bool HasLinkIssues => LinkIssues.Count > 0;

    public bool HasIssues => Issues.Count > 0;

    public string IssueSummary
    {
        get
        {
            var errors = Issues.Count(i => i.IsError);
            var warnings = Issues.Count - errors;
            return Issues.Count == 0
                ? "No problems found."
                : $"{errors} error(s) (they disable Generate), {warnings} warning(s).";
        }
    }

    public string SuggestMessage
    {
        get => _suggestMessage;
        private set
        {
            if (SetProperty(ref _suggestMessage, value))
            {
                OnPropertyChanged(nameof(HasSuggestMessage));
            }
        }
    }

    public bool HasSuggestMessage => _suggestMessage.Length > 0;

    public DetailLinkRow? SelectedLink
    {
        get => _selectedLink;
        set
        {
            if (SetProperty(ref _selectedLink, value))
            {
                LoadLink();
                _main.RefreshBreadcrumb();
            }
        }
    }

    public KeyPairRow? SelectedKeyPair
    {
        get => _selectedKeyPair;
        set => SetProperty(ref _selectedKeyPair, value);
    }

    public DetailColumnRow? SelectedColumn
    {
        get => _selectedColumn;
        set => SetProperty(ref _selectedColumn, value);
    }

    public OverviewRow? SelectedOverview
    {
        get => _selectedOverview;
        set => SetProperty(ref _selectedOverview, value);
    }

    public bool HasLink => Link is not null;

    public DetailLink? Link => SelectedLink?.Link;

    public TableInfo? DetailTableInfo => RelationValidator.FindTable(_main.Current.Tables, Link?.DetailTable);

    public bool HasMasterNote => MasterNote.Length > 0;

    public bool HasSetNullNote => SetNullNote.Length > 0;

    public bool HasUpdateNote => UpdateNote.Length > 0;

    /// <summary>Explains that a table that is not generated ignores its links.</summary>
    public string MasterNote => Table is { Generate: false }
        ? "This table is not generated (Generate is off), so its links are ignored."
        : string.Empty;

    // ---- loading ----

    public void Reload()
    {
        var previous = Link;
        Links.Clear();
        SelectedLink = null;
        if (Table is { } table)
        {
            foreach (var link in table.Details)
            {
                Links.Add(new DetailLinkRow(table, link));
            }
        }

        SelectedLink = Links.FirstOrDefault(r => ReferenceEquals(r.Link, previous)) ?? Links.FirstOrDefault();
        LoadLink();
        RefreshDerived();
        OnPropertyChanged(string.Empty);
    }

    private void OnTablesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TablesViewModel.SelectedTable))
        {
            Message = string.Empty;
            Reload();
        }
    }

    private void LoadLink()
    {
        _loading = true;
        try
        {
            KeyPairs.Clear();
            Columns.Clear();
            SelectedKeyPair = null;
            SelectedColumn = null;
            SetItems(MasterFieldNames, Table?.Fields.Select(f => f.Name) ?? []);
            SetItems(DetailFieldNames, DetailTableInfo?.Fields.Select(f => f.Name) ?? []);
            SetItems(DetailTableNames, _main.Current.Tables.Select(t => t.Name));
            var choices = new List<string> { BelowFormLabel };
            choices.AddRange(Table?.FormPages.Select(p => p.Name) ?? []);
            SetItems(FormPageChoices, choices);
            if (Link is { } link)
            {
                foreach (var pair in link.Links)
                {
                    KeyPairs.Add(new KeyPairRow(pair, this));
                }

                foreach (var column in link.Columns)
                {
                    Columns.Add(new DetailColumnRow(column, SyncColumns));
                }
            }
        }
        finally
        {
            _loading = false;
        }

        ForgetRawText();
        RefreshDeleteOptions();
        RefreshDerived();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Recomputes the overview, the validation lists and the Generate gate. Called after every change.</summary>
    protected override void Changed()
    {
        RefreshDeleteOptions();
        RefreshDerived();
    }

    internal void Touched()
    {
        SelectedLink?.Refresh();
        Changed();
    }

    private void RefreshDerived()
    {
        var tables = _main.Current.Tables;
        Overview.Clear();
        foreach (var master in tables)
        {
            foreach (var link in master.Details)
            {
                Overview.Add(new OverviewRow(
                    master,
                    link,
                    $"{RelationValidator.LinkName(master, link)}   [{RelationValidator.PairsText(link)}]   delete: {link.OnDelete}, update: {link.OnUpdate}"));
            }
        }

        var issues = RelationValidator.Validate(tables);
        Issues.Clear();
        foreach (var issue in issues)
        {
            Issues.Add(issue);
        }

        LinkIssues.Clear();
        if (Link is { } selected)
        {
            foreach (var issue in issues.Where(i => ReferenceEquals(i.Link, selected)))
            {
                LinkIssues.Add(issue.Text);
            }
        }

        OnPropertyChanged(nameof(HasLinkIssues));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IssueSummary));
        OnPropertyChanged(nameof(MasterNote));
        OnPropertyChanged(nameof(HasMasterNote));
        _main.Security.Refresh();
    }

    private void RefreshDeleteOptions()
    {
        var blocked = RequiredLinkField();
        foreach (var option in DeleteRuleOptions)
        {
            option.IsEnabled = option.Rule != DeleteRule.SetNull || blocked is null;
        }

        OnPropertyChanged(nameof(SetNullNote));
        OnPropertyChanged(nameof(HasSetNullNote));
        OnPropertyChanged(nameof(CanSetNull));
        OnPropertyChanged(nameof(UpdateNote));
        OnPropertyChanged(nameof(HasUpdateNote));
    }

    private static void SetItems(ObservableCollection<string> target, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (target.SequenceEqual(list))
        {
            return;
        }

        target.Clear();
        foreach (var item in list)
        {
            target.Add(item);
        }
    }

    // ---- types shown in the key link grid ----

    internal string TypeOfMasterField(string field) => RelationValidator.FindField(Table, field)?.Type.ToString() ?? "?";

    internal string TypeOfDetailField(string field) => RelationValidator.FindField(DetailTableInfo, field)?.Type.ToString() ?? "?";

    // ---- the link ----

    public string DetailTable
    {
        get => Link?.DetailTable ?? string.Empty;
        set
        {
            if (value is null || Link is not { } link || link.DetailTable == value)
            {
                return;
            }

            // The key pairs and grid columns belong to the old detail table; start over with a guess for the new one.
            link.DetailTable = value;
            link.Links.Clear();
            link.Links.AddRange(GuessPairs(Table, DetailTableInfo));
            link.Columns.Clear();
            LoadLink();
            SelectedLink?.Refresh();
            OnPropertyChanged(nameof(DetailTable));
            Changed();
        }
    }

    public string Caption
    {
        get => Link?.Caption ?? string.Empty;
        set => Set(value ?? string.Empty, l => l.Caption, (l, v) => l.Caption = v);
    }

    public bool ShowOnView
    {
        get => Link?.ShowOnView ?? false;
        set => Set(value, l => l.ShowOnView, (l, v) => l.ShowOnView = v);
    }

    public bool ShowOnEdit
    {
        get => Link?.ShowOnEdit ?? false;
        set => Set(value, l => l.ShowOnEdit, (l, v) => l.ShowOnEdit = v);
    }

    /// <summary>"After add go to edit": after a new master record is saved its edit page opens, so detail rows can be added.</summary>
    public bool AfterAddGoToEdit
    {
        get => Link?.ShowOnAdd ?? false;
        set => Set(value, l => l.ShowOnAdd, (l, v) => l.ShowOnAdd = v);
    }

    public bool AllowAdd
    {
        get => Link?.AllowAdd ?? false;
        set => Set(value, l => l.AllowAdd, (l, v) => l.AllowAdd = v);
    }

    public bool AllowEdit
    {
        get => Link?.AllowEdit ?? false;
        set => Set(value, l => l.AllowEdit, (l, v) => l.AllowEdit = v);
    }

    public bool AllowDelete
    {
        get => Link?.AllowDelete ?? false;
        set => Set(value, l => l.AllowDelete, (l, v) => l.AllowDelete = v);
    }

    public bool FilterAndSort
    {
        get => Link?.FilterAndSort ?? false;
        set => Set(value, l => l.FilterAndSort, (l, v) => l.FilterAndSort = v);
    }

    public DetailPreview Preview
    {
        get => Link?.Preview ?? DetailPreview.None;
        set
        {
            if (Set(value, l => l.Preview, (l, v) => l.Preview = v))
            {
                OnPropertyChanged(nameof(PreviewChoice));
            }
        }
    }

    public RelationChoice<DetailPreview> PreviewChoice
    {
        get => PreviewChoices.First(c => c.Value == Preview);
        set
        {
            if (value is not null)
            {
                Preview = value.Value;
            }
        }
    }

    // ---- delete and update rules ----

    /// <summary>The first detail link field that cannot be null (required or part of the key), or null.</summary>
    public string? RequiredLinkField()
    {
        if (Link is not { } link || DetailTableInfo is not { } detail)
        {
            return null;
        }

        return link.Links
            .Select(p => RelationValidator.FindField(detail, p.DetailField))
            .FirstOrDefault(f => f is { } field && (field.IsRequired || field.IsPrimaryKey))?.Name;
    }

    public bool CanSetNull => RequiredLinkField() is null;

    /// <summary>Why "Set null" is disabled, or empty.</summary>
    public string SetNullNote => RequiredLinkField() is { } name
        ? $"Set null is not available: the link field '{name}' of '{Link!.DetailTable}' is required."
        : string.Empty;

    public DeleteRule OnDelete
    {
        get => Link?.OnDelete ?? DeleteRule.Restrict;
        set
        {
            if (Link is null)
            {
                return;
            }

            if (value == DeleteRule.SetNull && !CanSetNull)
            {
                Message = $"{SetNullNote} The last rule was kept.";
                OnPropertyChanged(nameof(OnDelete));
                OnPropertyChanged(nameof(OnDeleteOption));
                return;
            }

            Message = string.Empty;
            if (Set(value, l => l.OnDelete, (l, v) => l.OnDelete = v))
            {
                OnPropertyChanged(nameof(OnDeleteOption));
                OnPropertyChanged(nameof(OnDeleteExplanation));
            }
        }
    }

    public DeleteRuleOption OnDeleteOption
    {
        get => DeleteRuleOptions.First(o => o.Rule == OnDelete);
        set
        {
            if (value is not null)
            {
                OnDelete = value.Rule;
            }
        }
    }

    /// <summary>One line saying what the chosen delete rule does.</summary>
    public string OnDeleteExplanation => OnDeleteOption.Explanation;

    public UpdateRule OnUpdate
    {
        get => Link?.OnUpdate ?? UpdateRule.None;
        set
        {
            if (Set(value, l => l.OnUpdate, (l, v) => l.OnUpdate = v))
            {
                OnPropertyChanged(nameof(UpdateChoice));
                OnPropertyChanged(nameof(UpdateNote));
            }
        }
    }

    public RelationChoice<UpdateRule> UpdateChoice
    {
        get => UpdateChoices.First(c => c.Value == OnUpdate);
        set
        {
            if (value is not null)
            {
                OnUpdate = value.Value;
            }
        }
    }

    /// <summary>The warning shown with "Cascade" on update.</summary>
    public string UpdateNote => OnUpdate == UpdateRule.Cascade && Link is not null
        ? "Cascade on update only matters for editable, non-key link fields. A key field cannot change, so nothing would be updated for it."
        : string.Empty;

    // ---- grid options ----

    public string PageSizeText
    {
        get => Shown(nameof(PageSizeText), (Link?.PageSize ?? 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
        set
        {
            var text = value ?? string.Empty;
            var ok = TryWholeNumber(text, RelationValidator.MinPageSize, RelationValidator.MaxPageSize, out var size);
            Accept(nameof(PageSizeText), text, ok ? null : RangeMessage("Page size", RelationValidator.MinPageSize, RelationValidator.MaxPageSize),
                () =>
                {
                    if (Link is { } link)
                    {
                        link.PageSize = size;
                    }
                });
        }
    }

    /// <summary>The form page that hosts the grid, or <see cref="BelowFormLabel"/>. A page that no longer exists is shown by its name.</summary>
    public string FormPageChoice
    {
        get => string.IsNullOrEmpty(Link?.FormPage) ? BelowFormLabel : Link!.FormPage;
        set
        {
            if (value is not null)
            {
                Set(value == BelowFormLabel ? string.Empty : value, l => l.FormPage, (l, v) => l.FormPage = v);
            }
        }
    }

    private bool Set<T>(T value, Func<DetailLink, T> get, Action<DetailLink, T> set, [CallerMemberName] string? name = null)
        => Link is { } link && Put(get(link), value, v => set(link, v), name);

    // ---- the link list ----

    [RelayCommand]
    public void AddLink()
    {
        if (Table is not { } table)
        {
            return;
        }

        var tables = _main.Current.Tables;
        var detail = tables.FirstOrDefault(t => !ReferenceEquals(t, table)) ?? table;
        var link = new DetailLink { DetailTable = detail.Name };
        link.Links.AddRange(GuessPairs(table, detail));
        table.Details.Add(link);
        var row = new DetailLinkRow(table, link);
        Links.Add(row);
        SelectedLink = row;
        Message = string.Empty;
        Changed();
    }

    [RelayCommand]
    public void RemoveLink()
    {
        if (Table is not { } table || SelectedLink is not { } row)
        {
            return;
        }

        var index = Links.IndexOf(row);
        table.Details.Remove(row.Link);
        Links.Remove(row);
        SelectedLink = Links.Count == 0 ? null : Links[Math.Min(index, Links.Count - 1)];
        Message = string.Empty;
        Changed();
    }

    [RelayCommand]
    public void MoveLinkUp() => MoveLink(-1);

    [RelayCommand]
    public void MoveLinkDown() => MoveLink(1);

    private void MoveLink(int delta)
    {
        if (Table is not { } table || SelectedLink is not { } row)
        {
            return;
        }

        var from = Links.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Links.Count)
        {
            return;
        }

        Links.Move(from, to);
        table.Details.RemoveAt(from);
        table.Details.Insert(to, row.Link);
        Changed();
    }

    // ---- key pairs ----

    /// <summary>Adds a pair, starting from the next master key field that no pair uses yet.</summary>
    [RelayCommand]
    public void AddKeyPair()
    {
        if (Link is not { } link || Table is not { } table)
        {
            return;
        }

        var used = link.Links.Select(l => l.MasterField).ToHashSet(Names);
        var master = table.PrimaryKeys.FirstOrDefault(f => !used.Contains(f.Name))?.Name
                     ?? table.Fields.FirstOrDefault(f => !used.Contains(f.Name))?.Name
                     ?? string.Empty;
        var pair = new KeyLink { MasterField = master, DetailField = GuessDetailField(table, DetailTableInfo, master) };
        link.Links.Add(pair);
        var row = new KeyPairRow(pair, this);
        KeyPairs.Add(row);
        SelectedKeyPair = row;
        Touched();
    }

    [RelayCommand]
    public void RemoveKeyPair()
    {
        if (Link is not { } link || SelectedKeyPair is not { } row)
        {
            return;
        }

        link.Links.Remove(row.Model);
        KeyPairs.Remove(row);
        SelectedKeyPair = null;
        Touched();
    }

    /// <summary>
    /// Pairs for a new link: from a foreign key of the detail table that references the master, otherwise the master key fields
    /// matched with detail fields named like them ("CustomerId", "Customer_Id"). When nothing matches, one pair with the first key field.
    /// </summary>
    public static List<KeyLink> GuessPairs(TableInfo? master, TableInfo? detail)
    {
        var pairs = new List<KeyLink>();
        if (master is null || detail is null)
        {
            return pairs;
        }

        var key = detail.ForeignKeys.FirstOrDefault(k => Names.Equals(k.ReferencedTable, master.Name) && k.Columns.Count > 0);
        if (key is not null)
        {
            var referenced = key.ReferencedColumns.Count > 0 ? key.ReferencedColumns : master.PrimaryKeys.Select(f => f.Name).ToList();
            if (referenced.Count == key.Columns.Count)
            {
                for (var i = 0; i < referenced.Count; i++)
                {
                    pairs.Add(new KeyLink { MasterField = referenced[i], DetailField = key.Columns[i] });
                }

                return pairs;
            }
        }

        foreach (var primary in master.PrimaryKeys)
        {
            var match = GuessDetailField(master, detail, primary.Name);
            if (match.Length > 0)
            {
                pairs.Add(new KeyLink { MasterField = primary.Name, DetailField = match });
            }
        }

        if (pairs.Count == 0 && master.PrimaryKey is { } first)
        {
            pairs.Add(new KeyLink { MasterField = first.Name });
        }

        return pairs;
    }

    private static string GuessDetailField(TableInfo master, TableInfo? detail, string masterField)
    {
        if (detail is null || masterField.Length == 0)
        {
            return string.Empty;
        }

        var single = master.Name.EndsWith('s') && master.Name.Length > 1 ? master.Name[..^1] : master.Name;
        string[] candidates =
        [
            master.Name + masterField,
            master.Name + "_" + masterField,
            single + masterField,
            single + "_" + masterField,
            masterField,
        ];
        foreach (var candidate in candidates)
        {
            // The master key itself is a fallback only for another table; a self link must not pair a field with itself.
            if (ReferenceEquals(master, detail) && Names.Equals(candidate, masterField))
            {
                continue;
            }

            if (detail.Fields.FirstOrDefault(f => Names.Equals(f.Name, candidate)) is { } field)
            {
                return field.Name;
            }
        }

        return string.Empty;
    }

    // ---- grid columns ----

    /// <summary>Adds the next detail field that is not a column yet. A grid without columns shows the detail fields marked "show in list".</summary>
    [RelayCommand]
    public void AddColumn()
    {
        if (Link is null || DetailTableInfo is not { } detail)
        {
            return;
        }

        var next = detail.Fields.FirstOrDefault(f => Columns.All(c => !Names.Equals(c.Field, f.Name)));
        if (next is null)
        {
            Message = detail.Fields.Count == 0 ? "The detail table has no fields." : "Every field is already a column.";
            return;
        }

        Message = string.Empty;
        var row = new DetailColumnRow(next.Name, SyncColumns);
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

    private void SyncColumns()
    {
        if (_loading || Link is not { } link)
        {
            return;
        }

        link.Columns.Clear();
        link.Columns.AddRange(Columns.Select(c => c.Field));
        Changed();
    }

    // ---- suggestions and overview ----

    /// <summary>
    /// Proposes a link for every foreign key of every table (the foreign key's table becomes the detail of the table it references)
    /// and adds the ones the user ticks. Links that already exist are shown disabled.
    /// </summary>
    [RelayCommand]
    public async Task SuggestFromForeignKeysAsync()
    {
        var suggestions = RelationSuggester.Suggest(_main.Current.Tables);
        if (suggestions.Count == 0)
        {
            Announce("No foreign keys were found. Connect to a database whose tables declare foreign keys on the Database tab, then try again.");
            return;
        }

        var labels = suggestions.Select(s => s.Label).ToList();
        var existing = suggestions.Where(s => s.Exists).Select(s => s.Label).ToList();
        if (existing.Count == labels.Count)
        {
            Announce("Every foreign key already has a master/detail link.");
            return;
        }

        var chosen = await _main.Dialogs.ChooseManyAsync(
            "Suggest from foreign keys",
            "Tick the master/detail links to add. Links that already exist are disabled.",
            labels,
            existing);
        if (chosen is null)
        {
            return;
        }

        var added = new List<(TableInfo Master, DetailLink Link)>();
        foreach (var suggestion in suggestions.Where(s => !s.Exists && chosen.Contains(s.Label)))
        {
            var link = suggestion.ToLink();
            suggestion.Master.Details.Add(link);
            added.Add((suggestion.Master, link));
        }

        if (added.Count == 0)
        {
            Announce("No link was added.");
            return;
        }

        Reload();
        if (added.LastOrDefault(a => ReferenceEquals(a.Master, Table)) is { Link: { } last })
        {
            SelectedLink = Links.FirstOrDefault(r => ReferenceEquals(r.Link, last));
        }

        Announce($"Added {added.Count} master/detail link(s) from foreign keys.");
    }

    private void Announce(string text)
    {
        SuggestMessage = text;
        _main.Status = text;
    }

    /// <summary>Selects the master table and the link of an overview line. The overview list calls this on double-click.</summary>
    [RelayCommand]
    public void OpenOverviewItem(OverviewRow? row)
    {
        if (row is null)
        {
            return;
        }

        Tables.SelectedTable = row.Master;
        Reload();
        SelectedLink = Links.FirstOrDefault(r => ReferenceEquals(r.Link, row.Link));
    }
}
