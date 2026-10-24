using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using CommunityToolkit.Mvvm.Input;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>
/// The field designer on the Fields tab: a searchable field list for the selected table and a property panel for the
/// selected field. Every property writes straight into the <see cref="FieldInfo"/> and its <see cref="FieldUi"/>, so Save persists the edits.
/// A value that is not valid shows <see cref="EditorViewModelBase.Message"/> and the model keeps the last valid value.
/// </summary>
public partial class FieldDesignerViewModel : EditorViewModelBase
{
    private static readonly Regex CssLength = new(@"^(auto|\d+(\.\d+)?(px|%|em|rem|vw|ch)?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly MainViewModel _main;
    private string _searchText = string.Empty;
    private string _patternTestText = string.Empty;
    private string _optionsMessage = string.Empty;
    private bool _loading;
    private FieldOption? _selectedOption;
    private ValueRule? _selectedRule;

    public FieldDesignerViewModel(MainViewModel main)
    {
        _main = main;
        Tables.PropertyChanged += OnTablesChanged;
        Tables.Fields.CollectionChanged += (_, _) => RebuildVisible();
        Options.CollectionChanged += (_, _) => SyncOptions();
        BadgeRules.CollectionChanged += (_, _) => SyncRules();
    }

    public TablesViewModel Tables => _main.Tables;

    /// <summary>The fields of the selected table that match the search box, in field order.</summary>
    public ObservableCollection<FieldInfo> VisibleFields { get; } = [];

    public ObservableCollection<FieldOption> Options { get; } = [];

    public ObservableCollection<ValueRule> BadgeRules { get; } = [];

    /// <summary>"(none)" followed by every table of the project.</summary>
    public ObservableCollection<string> LookupTables { get; } = [];

    /// <summary>The fields of the lookup table.</summary>
    public ObservableCollection<string> LookupFields { get; } = [];

    /// <summary>"(none)" followed by the fields of the lookup table.</summary>
    public ObservableCollection<string> LookupOrderChoices { get; } = [];

    public FieldInfo? Field => Tables.SelectedField;

    public bool HasField => Field is not null;

    public string NoFieldText => Tables.SelectedTable is null
        ? "Pick a table, then a field, to design it."
        : "Pick a field in the list to design it.";

    /// <summary>Re-reads the list and the panel from the open project.</summary>
    public void Reload()
    {
        RebuildVisible();
        OnFieldChanged();
    }

    private void OnTablesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TablesViewModel.SelectedField))
        {
            OnFieldChanged();
        }
        else if (e.PropertyName == nameof(TablesViewModel.SelectedTable))
        {
            OnPropertyChanged(nameof(NoFieldText));
        }
    }

    private void OnFieldChanged()
    {
        ForgetRawText();
        Message = string.Empty;
        _patternTestText = string.Empty;
        LoadCollections();
        RefreshLookupLists();
        OnPropertyChanged(string.Empty);
    }

    protected override void Changed()
    {
        RaisePreview();
        OnPropertyChanged(nameof(ShowOptionsSection));
        OnPropertyChanged(nameof(FormatPreview));
        OnPropertyChanged(nameof(PatternTest));
        OnPropertyChanged(nameof(ShowOptions));
        OnPropertyChanged(nameof(IsTextArea));
        OnPropertyChanged(nameof(IsBooleanFormat));
        OnPropertyChanged(nameof(IsBadgeFormat));
    }

    // ---- field list ----

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RebuildVisible();
            }
        }
    }

    private void RebuildVisible()
    {
        var selected = Tables.SelectedField;
        var term = _searchText.Trim();
        var desired = Tables.Fields
            .Where(f => term.Length == 0
                || f.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || f.Caption.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (VisibleFields.SequenceEqual(desired))
        {
            return;
        }

        VisibleFields.Clear();
        foreach (var field in desired)
        {
            VisibleFields.Add(field);
        }

        if (selected is not null && Tables.SelectedField != selected && VisibleFields.Contains(selected))
        {
            Tables.SelectedField = selected;
        }
    }

    public bool CanMoveUp => Field is { } f && Tables.SelectedTable is { } t && t.Fields.IndexOf(f) > 0;

    public bool CanMoveDown => Field is { } f && Tables.SelectedTable is { } t && t.Fields.IndexOf(f) is var i && i >= 0 && i < t.Fields.Count - 1;

    [RelayCommand]
    public void MoveUp() => Move(-1);

    [RelayCommand]
    public void MoveDown() => Move(1);

    /// <summary>
    /// Moves the selected field one place in <see cref="TableInfo.Fields"/>. Key order of a composite key follows the field
    /// order when no key positions are stored, so the positions are fixed first and the key order does not change.
    /// </summary>
    private void Move(int delta)
    {
        if (Tables.SelectedTable is not { } table || Field is not { } field)
        {
            return;
        }

        var from = table.Fields.IndexOf(field);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= table.Fields.Count)
        {
            return;
        }

        NormalizeKeyPositions(table);
        table.Fields.RemoveAt(from);
        table.Fields.Insert(to, field);

        var shown = Tables.Fields.IndexOf(field);
        if (shown >= 0 && to < Tables.Fields.Count)
        {
            Tables.Fields.Move(shown, to);
        }

        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        _main.Status = $"Moved {field.Name} to position {to + 1} of {table.Fields.Count}.";
    }

    /// <summary>Stores explicit key positions (1, 2, ...) for a composite key that relies on field order.</summary>
    public static void NormalizeKeyPositions(TableInfo table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var keys = table.PrimaryKeys;
        if (keys.Count < 2 || keys.All(k => k.KeyPosition > 0))
        {
            return;
        }

        for (var i = 0; i < keys.Count; i++)
        {
            keys[i].KeyPosition = i + 1;
        }
    }

    // ---- basic ----

    public string Caption
    {
        get => Field?.Caption ?? string.Empty;
        set => SetField(value ?? string.Empty, f => f.Caption, (f, v) => f.Caption = v);
    }

    public string TypeText => Field?.Type.ToString() ?? string.Empty;

    public string KeyFlags
    {
        get
        {
            if (Field is not { } f)
            {
                return string.Empty;
            }

            var flags = new List<string>();
            if (f.IsPrimaryKey)
            {
                flags.Add(f.KeyPosition > 0 ? $"Primary key (position {f.KeyPosition})" : "Primary key");
            }

            if (f.IsAutoIncrement)
            {
                flags.Add("Auto increment");
            }

            if (f.HasDefault)
            {
                flags.Add("Has default");
            }

            if (f.IsSecret)
            {
                flags.Add("Secret");
            }

            return flags.Count == 0 ? "None" : string.Join(", ", flags);
        }
    }

    public bool Required
    {
        get => Field?.IsRequired ?? false;
        set => SetField(value, f => f.IsRequired, (f, v) => f.IsRequired = v);
    }

    public bool ShowInList
    {
        get => Field?.ShowInList ?? false;
        set => SetField(value, f => f.ShowInList, (f, v) => f.ShowInList = v);
    }

    public bool ShowInView
    {
        get => Field?.ShowInView ?? false;
        set => SetField(value, f => f.ShowInView, (f, v) => f.ShowInView = v);
    }

    public bool ShowInAdd
    {
        get => Field?.ShowInAdd ?? false;
        set => SetField(value, f => f.ShowInAdd, (f, v) => f.ShowInAdd = v);
    }

    public bool ShowInEdit
    {
        get => Field?.ShowInEdit ?? false;
        set => SetField(value, f => f.ShowInEdit, (f, v) => f.ShowInEdit = v);
    }

    public bool Searchable
    {
        get => Field?.Searchable ?? false;
        set => SetField(value, f => f.Searchable, (f, v) => f.Searchable = v);
    }

    public bool Sortable
    {
        get => Field?.Ui.Sortable ?? false;
        set => SetUi(value, u => u.Sortable, (u, v) => u.Sortable = v);
    }

    // ---- edit control ----

    public EditControl Control
    {
        get => Field?.Ui.Control ?? EditControl.Auto;
        set => SetUi(value, u => u.Control, (u, v) => u.Control = v);
    }

    public string HelpText
    {
        get => Field?.Ui.HelpText ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.HelpText, (u, v) => u.HelpText = v);
    }

    public string Placeholder
    {
        get => Field?.Ui.Placeholder ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.Placeholder, (u, v) => u.Placeholder = v);
    }

    public string Tooltip
    {
        get => Field?.Ui.Tooltip ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.Tooltip, (u, v) => u.Tooltip = v);
    }

    public string DefaultValue
    {
        get => Field?.Ui.DefaultValue ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.DefaultValue, (u, v) => u.DefaultValue = v);
    }

    public bool ReadOnlyOnEdit
    {
        get => Field?.Ui.ReadOnlyOnEdit ?? false;
        set => SetUi(value, u => u.ReadOnlyOnEdit, (u, v) => u.ReadOnlyOnEdit = v);
    }

    public bool AutoFocus
    {
        get => Field?.Ui.AutoFocus ?? false;
        set => SetUi(value, u => u.AutoFocus, (u, v) => u.AutoFocus = v);
    }

    public string RowsText
    {
        get => Shown(nameof(RowsText), (Field?.Ui.Rows ?? 3).ToString(CultureInfo.InvariantCulture));
        set
        {
            var text = value ?? string.Empty;
            var ok = TryWholeNumber(text, 1, 50, out var rows);
            Accept(nameof(RowsText), text, ok ? null : RangeMessage("Rows", 1, 50), () => SetUi(rows, u => u.Rows, (u, v) => u.Rows = v, nameof(RowsText)));
        }
    }

    public string Group
    {
        get => Field?.Ui.Group ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.Group, (u, v) => u.Group = v);
    }

    /// <summary>The rows setting only matters for a text area.</summary>
    public bool IsTextArea => Control == EditControl.TextArea;

    // ---- options ----

    /// <summary>Static options (and the lookup) apply to select and radio controls.</summary>
    public bool ShowOptions => Control is EditControl.Select or EditControl.Radio;

    // ---- sections (the inner tabs of the inspector) ----

    /// <summary>The sections of the inspector, shown one at a time.</summary>
    public static IReadOnlyList<string> Sections { get; } = ["General", "Editing", "Validation", "Display", "Style"];

    private string _section = "General";

    /// <summary>The section on show. Setting an unknown name is ignored.</summary>
    public string Section
    {
        get => _section;
        set
        {
            if (value is null || !Sections.Contains(value) || _section == value)
            {
                return;
            }

            _section = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowGeneral));
            OnPropertyChanged(nameof(ShowEditing));
            OnPropertyChanged(nameof(ShowOptionsSection));
            OnPropertyChanged(nameof(ShowValidation));
            OnPropertyChanged(nameof(ShowDisplay));
            OnPropertyChanged(nameof(ShowStyle));
        }
    }

    public bool ShowGeneral => _section == "General";

    public bool ShowEditing => _section == "Editing";

    /// <summary>Options and lookup belong to the Editing section and only to select and radio controls.</summary>
    public bool ShowOptionsSection => ShowEditing && ShowOptions;

    public bool ShowValidation => _section == "Validation";

    public bool ShowDisplay => _section == "Display";

    public bool ShowStyle => _section == "Style";

    // ---- live preview: the field as the generated form and list show it ----

    /// <summary>The edit control the generated form uses: the chosen one, or the one the field type implies for Auto.</summary>
    public EditControl EffectiveControl => Field is not { } f ? EditControl.TextBox : Control != EditControl.Auto ? Control : f.Type switch
    {
        FieldType.Boolean => EditControl.Checkbox,
        FieldType.Date => EditControl.Date,
        FieldType.DateTime => EditControl.DateTime,
        FieldType.Integer or FieldType.Decimal => EditControl.Number,
        _ => EditControl.TextBox,
    };

    public string PreviewLabel => Field is not { } f ? string.Empty
        : (string.IsNullOrWhiteSpace(f.Caption) ? f.Name : f.Caption) + (f.IsRequired ? " *" : string.Empty);

    /// <summary>The placeholder, or a hint of the input for date and number controls.</summary>
    public string PreviewPlaceholder => Placeholder.Length > 0 ? Placeholder : EffectiveControl switch
    {
        EditControl.Date => "yyyy-mm-dd",
        EditControl.DateTime => "yyyy-mm-dd hh:mm",
        EditControl.Time => "hh:mm",
        EditControl.Number => "0",
        EditControl.Email => "name@example.com",
        EditControl.Url => "https://",
        _ => string.Empty,
    };

    public bool PreviewIsCheck => EffectiveControl == EditControl.Checkbox;

    public bool PreviewIsChoice => EffectiveControl is EditControl.Select or EditControl.Radio;

    public bool PreviewIsArea => EffectiveControl == EditControl.TextArea;

    public bool PreviewIsHidden => EffectiveControl is EditControl.Hidden or EditControl.ReadOnly;

    public bool PreviewIsInput => !PreviewIsCheck && !PreviewIsChoice && !PreviewIsArea && !PreviewIsHidden;

    /// <summary>What the form shows instead of an input for hidden and read-only fields.</summary>
    public string PreviewHiddenText => EffectiveControl == EditControl.Hidden ? "Not shown in the form" : "Shown as text, not editable";

    public bool PreviewHasHelp => HelpText.Length > 0;

    private void RaisePreview()
    {
        foreach (var name in new[]
                 {
                     nameof(EffectiveControl), nameof(PreviewLabel), nameof(PreviewPlaceholder), nameof(PreviewIsCheck), nameof(PreviewIsChoice),
                     nameof(PreviewIsArea), nameof(PreviewIsHidden), nameof(PreviewIsInput), nameof(PreviewHiddenText), nameof(PreviewHasHelp),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    public FieldOption? SelectedOption
    {
        get => _selectedOption;
        set => SetProperty(ref _selectedOption, value);
    }

    public string OptionsMessage
    {
        get => _optionsMessage;
        private set
        {
            if (SetProperty(ref _optionsMessage, value))
            {
                OnPropertyChanged(nameof(HasOptionsMessage));
            }
        }
    }

    public bool HasOptionsMessage => _optionsMessage.Length > 0;

    [RelayCommand]
    public void AddOption()
    {
        if (Field is null)
        {
            return;
        }

        var option = new FieldOption();
        Options.Add(option);
        SelectedOption = option;
    }

    [RelayCommand]
    public void RemoveOption()
    {
        if (SelectedOption is { } option)
        {
            Options.Remove(option);
            SelectedOption = null;
        }
    }

    /// <summary>Checks the option values. The view calls this after a cell was edited.</summary>
    public void RefreshOptionsMessage()
    {
        var empty = Options.Count(o => string.IsNullOrWhiteSpace(o.Value));
        var duplicates = Options.Where(o => !string.IsNullOrWhiteSpace(o.Value))
            .GroupBy(o => o.Value.Trim(), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        OptionsMessage = empty > 0
            ? "Every option needs a value."
            : duplicates.Count > 0 ? $"Option values must be unique: {string.Join(", ", duplicates)}." : string.Empty;
    }

    private void SyncOptions()
    {
        if (_loading || Field is null)
        {
            return;
        }

        Field.Ui.Options.Clear();
        Field.Ui.Options.AddRange(Options);
        RefreshOptionsMessage();
    }

    private void SyncRules()
    {
        if (_loading || Field is null)
        {
            return;
        }

        Field.Ui.BadgeRules.Clear();
        Field.Ui.BadgeRules.AddRange(BadgeRules);
        Changed();
    }

    private void LoadCollections()
    {
        _loading = true;
        try
        {
            Options.Clear();
            BadgeRules.Clear();
            SelectedOption = null;
            SelectedRule = null;
            if (Field is { } field)
            {
                foreach (var option in field.Ui.Options)
                {
                    Options.Add(option);
                }

                foreach (var rule in field.Ui.BadgeRules)
                {
                    BadgeRules.Add(rule);
                }
            }
        }
        finally
        {
            _loading = false;
        }

        RefreshOptionsMessage();
    }

    // ---- lookup ----

    public bool HasLookupTable => Field?.Ui.Lookup is { Table.Length: > 0 };

    public string LookupTable
    {
        get => Field?.Ui.Lookup is { Table.Length: > 0 } lookup ? lookup.Table : CustomizationChoices.NoneLabel;
        set
        {
            // A combo box reports null while its item list is rebuilt; that is not a choice.
            if (value is null || Field is not { } current)
            {
                return;
            }

            var table = value == CustomizationChoices.NoneLabel ? string.Empty : value;
            if (table == (current.Ui.Lookup?.Table ?? string.Empty))
            {
                return;
            }

            if (table.Length == 0)
            {
                if (current.Ui.Lookup is not null)
                {
                    current.Ui.Lookup = null;
                }
            }
            else
            {
                var lookup = current.Ui.Lookup ??= new FieldLookup();
                lookup.Table = table;
                var target = _main.Current.Tables.FirstOrDefault(t => t.Name == table);
                var names = target?.Fields.Select(f => f.Name).ToList() ?? [];
                if (!names.Contains(lookup.KeyField))
                {
                    lookup.KeyField = target?.PrimaryKey?.Name ?? string.Empty;
                }

                if (!names.Contains(lookup.DisplayField))
                {
                    lookup.DisplayField = target?.Fields.FirstOrDefault(f => f.Type == FieldType.Text)?.Name ?? lookup.KeyField;
                }

                if (!names.Contains(lookup.OrderField))
                {
                    lookup.OrderField = string.Empty;
                }
            }

            RefreshLookupLists();
            OnPropertyChanged(nameof(LookupTable));
            OnPropertyChanged(nameof(HasLookupTable));
            OnPropertyChanged(nameof(LookupKeyField));
            OnPropertyChanged(nameof(LookupDisplayField));
            OnPropertyChanged(nameof(LookupOrderField));
            Changed();
        }
    }

    public string LookupKeyField
    {
        get => Field?.Ui.Lookup?.KeyField ?? string.Empty;
        set
        {
            if (value is not null)
            {
                SetLookup(value, l => l.KeyField, (l, v) => l.KeyField = v);
            }
        }
    }

    public string LookupDisplayField
    {
        get => Field?.Ui.Lookup?.DisplayField ?? string.Empty;
        set
        {
            if (value is not null)
            {
                SetLookup(value, l => l.DisplayField, (l, v) => l.DisplayField = v);
            }
        }
    }

    public string LookupFilter
    {
        get => Field?.Ui.Lookup?.Filter ?? string.Empty;
        set => SetLookup(value ?? string.Empty, l => l.Filter, (l, v) => l.Filter = v);
    }

    public string LookupOrderField
    {
        get => Field?.Ui.Lookup is { OrderField.Length: > 0 } lookup ? lookup.OrderField : CustomizationChoices.NoneLabel;
        set
        {
            if (value is not null)
            {
                SetLookup(value == CustomizationChoices.NoneLabel ? string.Empty : value, l => l.OrderField, (l, v) => l.OrderField = v);
            }
        }
    }

    public bool LookupShowDisplayInView
    {
        get => Field?.Ui.Lookup?.ShowDisplayInView ?? true;
        set => SetLookup(value, l => l.ShowDisplayInView, (l, v) => l.ShowDisplayInView = v);
    }

    private bool SetLookup<T>(T value, Func<FieldLookup, T> get, Action<FieldLookup, T> set, [CallerMemberName] string? name = null)
        => Field?.Ui.Lookup is { } lookup && Put(get(lookup), value, v => set(lookup, v), name);

    private void RefreshLookupLists()
    {
        LookupTables.Clear();
        LookupTables.Add(CustomizationChoices.NoneLabel);
        foreach (var table in _main.Current.Tables)
        {
            LookupTables.Add(table.Name);
        }

        LookupFields.Clear();
        LookupOrderChoices.Clear();
        LookupOrderChoices.Add(CustomizationChoices.NoneLabel);
        if (Field?.Ui.Lookup is { Table.Length: > 0 } lookup
            && _main.Current.Tables.FirstOrDefault(t => t.Name == lookup.Table) is { } target)
        {
            foreach (var name in target.Fields.Select(f => f.Name))
            {
                LookupFields.Add(name);
                LookupOrderChoices.Add(name);
            }
        }
    }

    // ---- validation ----

    public string MinLengthText
    {
        get => Shown(nameof(MinLengthText), (Field?.Ui.MinLength ?? 0).ToString(CultureInfo.InvariantCulture));
        set
        {
            var text = value ?? string.Empty;
            var ok = TryWholeNumber(text, 0, 100000, out var length);
            string? error = ok ? null : RangeMessage("Minimum length", 0, 100000);
            if (ok && Field is { } f && f.Ui.MaxLength > 0 && length > f.Ui.MaxLength)
            {
                error = "Minimum length cannot be larger than the maximum length. The last valid value was kept.";
            }

            Accept(nameof(MinLengthText), text, error, () => SetUi(length, u => u.MinLength, (u, v) => u.MinLength = v, nameof(MinLengthText)));
        }
    }

    public string MaxLengthText
    {
        get => Shown(nameof(MaxLengthText), (Field?.Ui.MaxLength ?? 0).ToString(CultureInfo.InvariantCulture));
        set
        {
            var text = value ?? string.Empty;
            var ok = TryWholeNumber(text, 0, 100000, out var length);
            string? error = ok ? null : RangeMessage("Maximum length", 0, 100000);
            if (ok && Field is { } f && length > 0 && length < f.Ui.MinLength)
            {
                error = "Maximum length cannot be smaller than the minimum length. The last valid value was kept.";
            }

            Accept(nameof(MaxLengthText), text, error, () => SetUi(length, u => u.MaxLength, (u, v) => u.MaxLength = v, nameof(MaxLengthText)));
        }
    }

    public string MinText
    {
        get => Shown(nameof(MinText), Field?.Ui.Min ?? string.Empty);
        set => SetBound(nameof(MinText), "Minimum", value, u => u.Min, (u, v) => u.Min = v, isStep: false);
    }

    public string MaxText
    {
        get => Shown(nameof(MaxText), Field?.Ui.Max ?? string.Empty);
        set => SetBound(nameof(MaxText), "Maximum", value, u => u.Max, (u, v) => u.Max = v, isStep: false);
    }

    public string StepText
    {
        get => Shown(nameof(StepText), Field?.Ui.Step ?? string.Empty);
        set => SetBound(nameof(StepText), "Step", value, u => u.Step, (u, v) => u.Step = v, isStep: true);
    }

    private void SetBound(string name, string label, string? value, Func<FieldUi, string> get, Action<FieldUi, string> set, bool isStep)
    {
        var text = (value ?? string.Empty).Trim();
        var error = BoundError(label, text, isStep);
        Accept(name, text, error, () => SetUi(text, get, set, name));
    }

    private string? BoundError(string label, string text, bool isStep)
    {
        if (Field is not { } field || text.Length == 0)
        {
            return null;
        }

        string? kind = null;
        switch (field.Type)
        {
            case FieldType.Integer:
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    kind = "a whole number";
                }

                break;
            case FieldType.Decimal:
                if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    kind = "a number";
                }

                break;
            case FieldType.Date:
            case FieldType.DateTime:
                if (isStep)
                {
                    if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        kind = "a number";
                    }
                }
                else if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    kind = "a date such as 2026-03-14";
                }

                break;
        }

        if (kind is not null)
        {
            return $"{label} must be {kind}. The last valid value was kept.";
        }

        var numeric = field.Type is FieldType.Integer or FieldType.Decimal;
        if (isStep && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var step) && step <= 0)
        {
            return "Step must be greater than 0. The last valid value was kept.";
        }

        if (numeric && !isStep && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            var ui = field.Ui;
            if (label == "Minimum" && decimal.TryParse(ui.Max, NumberStyles.Float, CultureInfo.InvariantCulture, out var max) && number > max)
            {
                return "Minimum cannot be larger than the maximum. The last valid value was kept.";
            }

            if (label == "Maximum" && decimal.TryParse(ui.Min, NumberStyles.Float, CultureInfo.InvariantCulture, out var min) && number < min)
            {
                return "Maximum cannot be smaller than the minimum. The last valid value was kept.";
            }
        }

        return null;
    }

    public string Pattern
    {
        get => Shown(nameof(Pattern), Field?.Ui.Pattern ?? string.Empty);
        set
        {
            var text = value ?? string.Empty;
            var problem = RegexTester.Validate(text);
            var error = problem is null ? null : $"The pattern is not a valid regular expression ({problem}) The last valid pattern was kept.";
            Accept(nameof(Pattern), text, error, () => SetUi(text, u => u.Pattern, (u, v) => u.Pattern = v, nameof(Pattern)));
            OnPropertyChanged(nameof(PatternTest));
        }
    }

    public string PatternMessage
    {
        get => Field?.Ui.PatternMessage ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.PatternMessage, (u, v) => u.PatternMessage = v);
    }

    public string RequiredMessage
    {
        get => Field?.Ui.RequiredMessage ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.RequiredMessage, (u, v) => u.RequiredMessage = v);
    }

    /// <summary>Sample text typed into the pattern tester. It is not stored in the project.</summary>
    public string PatternTestText
    {
        get => _patternTestText;
        set
        {
            if (SetProperty(ref _patternTestText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(PatternTest));
            }
        }
    }

    /// <summary>The sample against the pattern as typed (the whole value must match, like the HTML pattern attribute).</summary>
    public RegexTestResult PatternTest => RegexTester.Test(Pattern, _patternTestText);

    // ---- display ----

    public ViewFormat Format
    {
        get => Field?.Ui.Format ?? ViewFormat.Auto;
        set => SetUi(value, u => u.Format, (u, v) => u.Format = v);
    }

    public string FormatString
    {
        get => Field?.Ui.FormatString ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.FormatString, (u, v) => u.FormatString = v);
    }

    public string TrueText
    {
        get => Field?.Ui.TrueText ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.TrueText, (u, v) => u.TrueText = v);
    }

    public string FalseText
    {
        get => Field?.Ui.FalseText ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.FalseText, (u, v) => u.FalseText = v);
    }

    /// <summary>The sample value of the field type with the chosen format applied.</summary>
    public string FormatPreview => Field is { } f ? FormatPreviewer.Preview(f.Type, f.Ui) : string.Empty;

    public bool IsBooleanFormat => Field is { } f && (f.Type == FieldType.Boolean || f.Ui.Format == ViewFormat.Boolean);

    public bool IsBadgeFormat => Format == ViewFormat.Badge;

    /// <summary>Only number columns can hold dates as numbers; date and text columns hold them as text.</summary>
    public bool CanStoreDates => Field is { Type: FieldType.Integer or FieldType.Decimal };

    /// <summary>How a number column holds dates, used by the reports to group by month, filter by date and fill calendars.</summary>
    public DateStorage DateStorage
    {
        get => Field?.DateStorage ?? DateStorage.Text;
        set
        {
            if (Field is { } selected && CanStoreDates)
            {
                Put(selected.DateStorage, value, v => selected.DateStorage = v);
            }
        }
    }

    public ValueRule? SelectedRule
    {
        get => _selectedRule;
        set => SetProperty(ref _selectedRule, value);
    }

    [RelayCommand]
    public void AddBadgeRule()
    {
        if (Field is null)
        {
            return;
        }

        var rule = new ValueRule();
        BadgeRules.Add(rule);
        SelectedRule = rule;
    }

    [RelayCommand]
    public void RemoveBadgeRule()
    {
        if (SelectedRule is { } rule)
        {
            BadgeRules.Remove(rule);
            SelectedRule = null;
        }
    }

    // ---- style ----

    public string Width
    {
        get => Shown(nameof(Width), Field?.Ui.Width ?? string.Empty);
        set
        {
            var text = (value ?? string.Empty).Trim();
            Accept(nameof(Width), text,
                text.Length == 0 || CssLength.IsMatch(text) ? null : "Width must be a CSS length such as 120px, 20% or auto. The last valid value was kept.",
                () => SetUi(text, u => u.Width, (u, v) => u.Width = v, nameof(Width)));
        }
    }

    public TextAlign Align
    {
        get => Field?.Ui.Align ?? TextAlign.Auto;
        set => SetUi(value, u => u.Align, (u, v) => u.Align = v);
    }

    public string CssClass
    {
        get => Field?.Ui.CssClass ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.CssClass, (u, v) => u.CssClass = v);
    }

    public string InlineStyle
    {
        get => Field?.Ui.Style ?? string.Empty;
        set => SetUi(value ?? string.Empty, u => u.Style, (u, v) => u.Style = v);
    }

    // ---- reset and copy ----

    /// <summary>Puts every customization setting of the selected field back to its default, after a confirmation.</summary>
    [RelayCommand]
    public async Task ResetAsync()
    {
        if (Field is not { } field)
        {
            return;
        }

        var confirmed = await _main.Dialogs.ConfirmAsync(
            "Reset field",
            $"Reset every design setting of {field.Name} to its default? Its caption, type and key settings are not changed.",
            "Reset");
        if (!confirmed)
        {
            return;
        }

        DeepCopy.Into(new FieldUi(), field.Ui);
        OnFieldChanged();
        _main.Status = $"Reset the design settings of {field.Name}.";
    }

    /// <summary>The fields (as "Table.Field") of the project that have the type of the selected field, except the field itself.</summary>
    public IReadOnlyList<(string Label, FieldInfo Field)> CopyTargets()
    {
        if (Field is not { } field)
        {
            return [];
        }

        return _main.Current.Tables
            .SelectMany(t => t.Fields.Where(f => f.Type == field.Type && !ReferenceEquals(f, field)).Select(f => ($"{t.Name}.{f.Name}", f)))
            .ToList();
    }

    /// <summary>Copies the design settings of the selected field to the fields chosen in a dialog. Caption, type and key flags are not copied.</summary>
    [RelayCommand]
    public async Task CopySettingsAsync()
    {
        if (Field is not { } field)
        {
            return;
        }

        var targets = CopyTargets();
        if (targets.Count == 0)
        {
            _main.Status = $"There is no other {field.Type} field to copy the settings of {field.Name} to.";
            return;
        }

        var chosen = await _main.Dialogs.ChooseManyAsync(
            "Copy settings",
            $"Copy the design settings of {field.Name} to these {field.Type} fields:",
            targets.Select(t => t.Label).ToList());
        if (chosen is null || chosen.Count == 0)
        {
            return;
        }

        var copied = 0;
        foreach (var (label, target) in targets.Where(t => chosen.Contains(t.Label)))
        {
            DeepCopy.Into(field.Ui, target.Ui);
            copied++;
        }

        _main.Status = $"Copied the design settings of {field.Name} to {copied} field(s).";
    }

    // ---- plumbing ----

    private bool SetField<T>(T value, Func<FieldInfo, T> get, Action<FieldInfo, T> set, [CallerMemberName] string? name = null)
        => Field is { } f && Put(get(f), value, v => set(f, v), name);

    private bool SetUi<T>(T value, Func<FieldUi, T> get, Action<FieldUi, T> set, [CallerMemberName] string? name = null)
        => Field is { } f && Put(get(f.Ui), value, v => set(f.Ui, v), name);
}
