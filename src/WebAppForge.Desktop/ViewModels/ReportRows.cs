using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>
/// The designer panel of one report: the shared plumbing of the five report types. Every property writes straight into the
/// <see cref="ReportDef"/> and then tells the Reports tab (<see cref="Changed"/>), which redraws the schematic, re-runs the
/// validation and marks the project dirty (through the fingerprint).
/// </summary>
public abstract class ReportDesignerBase : EditorViewModelBase
{
    protected ReportDesignerBase(ReportsViewModel owner, ReportDef report)
    {
        Owner = owner;
        Report = report;
    }

    public ReportsViewModel Owner { get; }

    public ReportDef Report { get; }

    /// <summary>The source table of the report, looked up live (null for a dashboard or an unknown table).</summary>
    public TableInfo? Table => Owner.TableByName(Report.Table);

    public bool HasTable => Table is not null;

    protected override void Changed() => Owner.NotifyChanged();

    /// <summary>Called by the row view models after they wrote their model.</summary>
    internal void Touched() => Changed();

    internal void Notice(string text) => Message = text;

    protected static bool Whole(string? text, int min, int max, out int value)
        => ReportRowBase.WholeNumber(text, min, max, out value);

    /// <summary>A text box of a whole number: invalid text stays in the box, the model keeps the last valid value.</summary>
    protected void AcceptWhole(string name, string? text, string label, int min, int max, Action<int> store)
    {
        var typed = text ?? string.Empty;
        var ok = Whole(typed, min, max, out var number);
        Accept(name, typed, ok ? null : RangeMessage(label, min, max), () => store(number));
    }
}

/// <summary>Shared plumbing of the row view models: the owning designer, typed text kept while it is invalid and number parsing.</summary>
public abstract class ReportRowBase : ObservableObject
{
    private readonly Dictionary<string, string> _raw = [];

    protected ReportRowBase(ReportDesignerBase owner) => Owner = owner;

    protected ReportDesignerBase Owner { get; }

    public static bool WholeNumber(string? text, int min, int max, out int value)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    protected string Shown(string name, string modelText) => _raw.TryGetValue(name, out var raw) ? raw : modelText;

    /// <summary>Stores <paramref name="store"/> when <paramref name="error"/> is null; otherwise keeps the typed text and shows the message.</summary>
    protected void AcceptText(string name, string text, string? error, Action store)
    {
        _raw[name] = text;
        Owner.Notice(error ?? string.Empty);
        if (error is null)
        {
            store();
            _raw.Remove(name);
            Owner.Touched();
        }

        OnPropertyChanged(name);
    }

    /// <summary>Writes a changed value and tells the designer. A combo box reports null while its items are rebuilt; that is not a choice.</summary>
    protected bool Write<T>(T current, T value, Action<T> store, string name)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        store(value);
        OnPropertyChanged(name);
        Owner.Touched();
        return true;
    }

    internal static void SetItems<T>(ObservableCollection<T> target, IEnumerable<T> items)
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
}

/// <summary>A grouping field row: field, interval (with the range size), sort direction and sort basis.</summary>
public sealed class ReportFieldRow : ReportRowBase
{
    private readonly Func<TableInfo?> _table;
    private readonly bool _optional;

    public ReportFieldRow(ReportField model, ReportDesignerBase owner, Func<TableInfo?>? table = null, bool optional = false)
        : base(owner)
    {
        Model = model;
        _table = table ?? (() => owner.Table);
        _optional = optional;
        RefreshChoices();
    }

    public ReportField Model { get; }

    /// <summary>The label of an empty optional field.</summary>
    public string EmptyLabel => ReportFieldRules.NoneLabel;

    public ObservableCollection<string> FieldChoices { get; } = [];

    /// <summary>Only the intervals that fit the type of the chosen field: date intervals for dates, Range for numbers.</summary>
    public ObservableCollection<GroupInterval> IntervalChoices { get; } = [];

    public string Field
    {
        get => ReportFieldRules.Display(Model.Field, _optional ? EmptyLabel : string.Empty);
        set
        {
            if (value is null)
            {
                return;
            }

            var stored = ReportFieldRules.Stored(value, EmptyLabel);
            if (Write(Model.Field, stored, v => Model.Field = v, nameof(Field)))
            {
                if (!ReportFieldRules.IntervalsFor(SourceType).Contains(Model.Interval))
                {
                    Model.Interval = GroupInterval.None;
                }

                RefreshChoices();
                OnPropertyChanged(nameof(Interval));
                OnPropertyChanged(nameof(TypeText));
                OnPropertyChanged(nameof(ShowRange));
            }
        }
    }

    private FieldType? SourceType => ReportFieldRules.Find(_table(), Model.Field)?.Type;

    public string TypeText => SourceType?.ToString() ?? string.Empty;

    public GroupInterval Interval
    {
        get => Model.Interval;
        set
        {
            if (Write(Model.Interval, value, v => Model.Interval = v, nameof(Interval)))
            {
                OnPropertyChanged(nameof(ShowRange));
            }
        }
    }

    public bool ShowRange => Model.Interval == GroupInterval.Range;

    public string RangeSizeText
    {
        get => Shown(nameof(RangeSizeText), Model.RangeSize.ToString("0.##", CultureInfo.InvariantCulture));
        set
        {
            var text = value ?? string.Empty;
            var ok = double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0 && double.IsFinite(size);
            AcceptText(nameof(RangeSizeText), text, ok ? null : "Range size must be a number greater than 0. The last valid value was kept.", () => Model.RangeSize = size);
        }
    }

    public SortDirection Sort
    {
        get => Model.Sort;
        set => Write(Model.Sort, value, v => Model.Sort = v, nameof(Sort));
    }

    public GroupSortBy SortBy
    {
        get => Model.SortBy;
        set => Write(Model.SortBy, value, v => Model.SortBy = v, nameof(SortBy));
    }

    public string Caption
    {
        get => Model.Caption;
        set => Write(Model.Caption, value ?? string.Empty, v => Model.Caption = v, nameof(Caption));
    }

    /// <summary>"Order date (Month)" for lists and the schematic.</summary>
    public string Text
    {
        get
        {
            var name = string.IsNullOrEmpty(Model.Caption) ? Model.Field : Model.Caption;
            return Model.Interval == GroupInterval.None ? name : $"{name} ({Model.Interval})";
        }
    }

    internal void RefreshChoices()
    {
        var fields = ReportFieldRules.FieldsFor(_table(), FieldRole.Group);
        SetItems(FieldChoices, _optional ? [EmptyLabel, .. fields] : fields);
        SetItems(IntervalChoices, ReportFieldRules.IntervalsFor(SourceType));
    }
}

/// <summary>An aggregate row: function, field (only fields that fit the function), caption and format string.</summary>
public sealed class AggregateRow : ReportRowBase
{
    private readonly Func<TableInfo?> _table;

    public AggregateRow(ReportAggregate model, ReportDesignerBase owner, Func<TableInfo?>? table = null)
        : base(owner)
    {
        Model = model;
        _table = table ?? (() => owner.Table);
        RefreshChoices();
    }

    public ReportAggregate Model { get; }

    public ObservableCollection<string> FieldChoices { get; } = [];

    public AggregateFunction Function
    {
        get => Model.Function;
        set
        {
            if (!Write(Model.Function, value, v => Model.Function = v, nameof(Function)))
            {
                return;
            }

            RefreshChoices();
            if (!ValidChoice(Model.Field))
            {
                Model.Field = ReportFieldRules.AllowsEmptyField(value) ? string.Empty : ReportFieldRules.AggregateFields(_table(), value).FirstOrDefault() ?? string.Empty;
            }

            OnPropertyChanged(nameof(Field));
            OnPropertyChanged(nameof(Text));
        }
    }

    public string Field
    {
        get => ReportFieldRules.Display(Model.Field, ReportFieldRules.AllowsEmptyField(Model.Function) ? ReportFieldRules.AllRowsLabel : string.Empty);
        set
        {
            if (value is null)
            {
                return;
            }

            if (Write(Model.Field, ReportFieldRules.Stored(value, ReportFieldRules.AllRowsLabel), v => Model.Field = v, nameof(Field)))
            {
                OnPropertyChanged(nameof(Text));
            }
        }
    }

    public string Caption
    {
        get => Model.Caption;
        set => Write(Model.Caption, value ?? string.Empty, v => Model.Caption = v, nameof(Caption));
    }

    public string FormatString
    {
        get => Model.FormatString;
        set => Write(Model.FormatString, value ?? string.Empty, v => Model.FormatString = v, nameof(FormatString));
    }

    /// <summary>The column heading the report shows: the caption, else "Sum of Total".</summary>
    public string Heading => AggregateText(Model);

    public string Text => Heading;

    private bool ValidChoice(string field)
        => field.Length == 0 ? ReportFieldRules.AllowsEmptyField(Model.Function) : ReportFieldRules.AggregateFields(_table(), Model.Function).Contains(field);

    internal void RefreshChoices()
    {
        var fields = ReportFieldRules.AggregateFields(_table(), Model.Function);
        SetItems(FieldChoices, ReportFieldRules.AllowsEmptyField(Model.Function) ? [ReportFieldRules.AllRowsLabel, .. fields] : fields);
    }

    internal void RefreshAll()
    {
        RefreshChoices();
        OnPropertyChanged(string.Empty);
    }

    public static string AggregateText(ReportAggregate aggregate)
    {
        if (!string.IsNullOrWhiteSpace(aggregate.Caption))
        {
            return aggregate.Caption;
        }

        return aggregate.Function switch
        {
            AggregateFunction.Count when aggregate.Field.Length == 0 => "Count",
            AggregateFunction.CountDistinct => $"Distinct {aggregate.Field}",
            _ => $"{aggregate.Function} of {aggregate.Field}".TrimEnd(),
        };
    }
}

/// <summary>A filter row: the field (any field of the table), its caption and, for dates, the relative ranges.</summary>
public sealed class FilterRow : ReportRowBase
{
    public FilterRow(ReportFilter model, ReportDesignerBase owner)
        : base(owner)
    {
        Model = model;
        RefreshChoices();
    }

    public ReportFilter Model { get; }

    public ObservableCollection<string> FieldChoices { get; } = [];

    public string Field
    {
        get => Model.Field;
        set
        {
            if (value is not null && Write(Model.Field, value, v => Model.Field = v, nameof(Field)))
            {
                OnPropertyChanged(nameof(IsDate));
            }
        }
    }

    public string Caption
    {
        get => Model.Caption;
        set => Write(Model.Caption, value ?? string.Empty, v => Model.Caption = v, nameof(Caption));
    }

    public bool RelativeDates
    {
        get => Model.RelativeDates;
        set => Write(Model.RelativeDates, value, v => Model.RelativeDates = v, nameof(RelativeDates));
    }

    /// <summary>Relative ranges only apply to date and date-time fields.</summary>
    public bool IsDate => ReportFieldRules.Find(Owner.Table, Model.Field) is { } f && ReportFieldRules.Matches(f.Type, FieldRole.Date);

    public string Text => string.IsNullOrEmpty(Model.Caption) ? Model.Field : Model.Caption;

    internal void RefreshChoices() => SetItems(FieldChoices, ReportFieldRules.FieldsFor(Owner.Table, FieldRole.Group));
}

/// <summary>One name of a field list (detail fields, popover fields).</summary>
public sealed class FieldNameRow : ReportRowBase
{
    private readonly Action _sync;
    private string _field;

    public FieldNameRow(string field, ReportDesignerBase owner, Action sync)
        : base(owner)
    {
        _field = field;
        _sync = sync;
    }

    public ObservableCollection<string> FieldChoices { get; } = [];

    public string Field
    {
        get => _field;
        set
        {
            if (value is not null && _field != value)
            {
                _field = value;
                OnPropertyChanged();
                _sync();
            }
        }
    }
}

/// <summary>
/// An ordered list of model objects with a row view model each: add, remove, move up and move down write through to the model list
/// and tell the designer. The commands are plain commands so XAML and tests use them the same way.
/// </summary>
public class ModelRowList<TModel, TRow> : ObservableObject
    where TModel : class
    where TRow : class
{
    private readonly List<TModel> _model;
    private readonly Func<TModel, TRow> _makeRow;
    private readonly Func<TRow, TModel> _modelOf;
    private readonly Func<TModel?> _create;
    private readonly ReportDesignerBase _owner;
    private TRow? _selected;

    public ModelRowList(List<TModel> model, ReportDesignerBase owner, Func<TModel, TRow> makeRow, Func<TRow, TModel> modelOf, Func<TModel?> create)
    {
        _model = model;
        _owner = owner;
        _makeRow = makeRow;
        _modelOf = modelOf;
        _create = create;
        foreach (var item in model)
        {
            Rows.Add(makeRow(item));
        }

        Selected = Rows.FirstOrDefault();
        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(Remove);
        MoveUpCommand = new RelayCommand(() => Move(-1));
        MoveDownCommand = new RelayCommand(() => Move(1));
    }

    public ObservableCollection<TRow> Rows { get; } = [];

    public TRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public ICommand AddCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    /// <summary>Raised after a row was added or removed (a designer refreshes what depends on the count).</summary>
    public event EventHandler? Edited;

    public void Add()
    {
        if (_create() is not { } item)
        {
            return;
        }

        _model.Add(item);
        var row = _makeRow(item);
        Rows.Add(row);
        Selected = row;
        Edited?.Invoke(this, EventArgs.Empty);
        _owner.Touched();
    }

    public void Remove()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        _model.Remove(_modelOf(row));
        Rows.Remove(row);
        Selected = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
        Edited?.Invoke(this, EventArgs.Empty);
        _owner.Touched();
    }

    public void Move(int delta)
    {
        if (Selected is not { } row)
        {
            return;
        }

        var from = Rows.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Rows.Count)
        {
            return;
        }

        Rows.Move(from, to);
        _model.RemoveAt(from);
        _model.Insert(to, _modelOf(row));
        _owner.Touched();
    }
}

/// <summary>An ordered list of field names (detail fields, popover fields) kept in step with a list of strings of the model.</summary>
public sealed class FieldNameList : ObservableObject
{
    private readonly List<string> _model;
    private readonly ReportDesignerBase _owner;
    private FieldNameRow? _selected;

    public FieldNameList(List<string> model, ReportDesignerBase owner)
    {
        _model = model;
        _owner = owner;
        foreach (var name in model)
        {
            Rows.Add(MakeRow(name));
        }

        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(Remove);
        MoveUpCommand = new RelayCommand(() => Move(-1));
        MoveDownCommand = new RelayCommand(() => Move(1));
    }

    public ObservableCollection<FieldNameRow> Rows { get; } = [];

    public FieldNameRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public ICommand AddCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    private FieldNameRow MakeRow(string name)
    {
        var row = new FieldNameRow(name, _owner, Sync);
        ReportRowBase.SetItems(row.FieldChoices, ReportFieldRules.FieldsFor(_owner.Table, FieldRole.Group));
        return row;
    }

    /// <summary>Adds the first field of the table that the list does not have yet.</summary>
    public void Add()
    {
        var next = ReportFieldRules.FieldsFor(_owner.Table, FieldRole.Group).FirstOrDefault(n => Rows.All(r => !string.Equals(r.Field, n, StringComparison.OrdinalIgnoreCase)));
        if (next is null)
        {
            _owner.Notice(_owner.Table is null ? "Choose the source table first." : "Every field is already in the list.");
            return;
        }

        _owner.Notice(string.Empty);
        var row = MakeRow(next);
        Rows.Add(row);
        Selected = row;
        Sync();
    }

    public void Remove()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        Rows.Remove(row);
        Selected = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
        Sync();
    }

    public void Move(int delta)
    {
        if (Selected is not { } row)
        {
            return;
        }

        var from = Rows.IndexOf(row);
        var to = from + delta;
        if (from >= 0 && to >= 0 && to < Rows.Count)
        {
            Rows.Move(from, to);
            Sync();
        }
    }

    private void Sync()
    {
        _model.Clear();
        _model.AddRange(Rows.Select(r => r.Field));
        _owner.Touched();
    }
}

/// <summary>The filters list shared by Summary, Crosstab, Chart and Calendar: fields offered above the report.</summary>
public sealed class ReportFilterList : ModelRowList<ReportFilter, FilterRow>
{
    public ReportFilterList(List<ReportFilter> model, ReportDesignerBase owner)
        : base(model, owner, f => new FilterRow(f, owner), r => r.Model, () => Next(model, owner))
    {
    }

    /// <summary>A filter on the first field of the table that no filter uses yet, or null when there is none.</summary>
    private static ReportFilter? Next(List<ReportFilter> model, ReportDesignerBase owner)
    {
        var field = ReportFieldRules.FieldsFor(owner.Table, FieldRole.Group)
            .FirstOrDefault(n => model.All(f => !string.Equals(f.Field, n, StringComparison.OrdinalIgnoreCase)));
        return field is null ? null : new ReportFilter { Field = field };
    }
}
