// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Metadata;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RulealizeStudio.Server;

/// <summary>
/// An application's screen designed without its XAML being read: a window drawn by Avalonia from
/// its XAML — <c>MainWindow.axaml</c>, or any other of the application's — as it is written now, the controls the application's build loads to place
/// on it, and what is placed moved, taken out and given its words — each the smallest edit to the
/// text, which is the only document there is.
/// </summary>
/// <remarks>
/// <para>
/// The window is the application's own: the text loaded as the application loads it, with the
/// application's assembly and every assembly its build wrote beside it, under the theme
/// <see cref="Hosting.RuleApplication"/> gives it, and drawn with Skia as it draws itself. What the
/// person sees is a picture of it, and where they point is asked of its controls, never of the
/// picture. To know which element a control was made from, each control of the copy drawn carries
/// the element's number (<see cref="Placed"/>); the file never does.
/// </para>
/// <para>
/// The controls to place are every control those assemblies hold that can be made with nothing — no
/// list of them is written here. A control's words are the first of its <c>Title</c>, <c>Text</c>,
/// <c>Header</c> and <c>Content</c> it has, which is the one place a property is named for them; what
/// holds other controls is told by what it is: a panel holds many, and so does a list of items
/// written in it — a tab control's tabs, say — a decorator or a content control one.
/// </para>
/// <para>
/// A control can hold one control in a part of it besides its content: any property that takes
/// anything and is drawn by a template of its own, as a split view's pane is by its
/// <c>PaneTemplate</c> or a tab's header by its <c>HeaderTemplate</c> — no list of them is written
/// here. Each is listed under its control as a part of it, written as a property element once
/// something is put in it, and not where it is written as an attribute.
/// </para>
/// <para>
/// A control chosen that its list of items shows one at a time — a tab and what is in it — is shown,
/// on the window drawn and never in the text.
/// </para>
/// <para>
/// Where a control goes in what holds it is written the way Visual Studio's WPF designer writes it.
/// In a <c>Grid</c>, or a bare <c>Panel</c>, which lay their children over each other, a control is
/// put where it is dropped — aligned to the top left of its cell, with the distance from there as its
/// <c>Margin</c>, and its <c>Grid.Row</c> and <c>Grid.Column</c> where the grid has more than one — and
/// moved by dragging it; in a <c>Canvas</c> the same is its <c>Canvas.Left</c> and <c>Canvas.Top</c>.
/// In any other panel it goes before or after the others, which the panel lays out. Its size is its
/// <c>Width</c> and <c>Height</c>, set by dragging its corner. The window holds one control, so a
/// control dropped on it empty is put in a grid, as a new window in that designer has one.
/// </para>
/// <para>
/// Every property a control can be given in XAML is given here too, as Visual Studio's property
/// window gives it: those Avalonia has registered for its type, and the attached ones of what holds
/// it — no list of them is written here — each that is written as text in XAML, said with the kind
/// of value it takes: words, a number, yes or no, one of a set, a colour, or anything else XAML reads
/// from text. Set, it is written as the control's attribute; set to nothing, the attribute goes.
/// </para>
/// <para>
/// Nothing placed is bound: what a control stands for is the specification's to say and the agent's
/// to bind, once there are rules. A property that is bound is shown as bound, and not given here.
/// </para>
/// </remarks>
public sealed class Designer
{
    private const string Marker = "rulealizeStudioPlaced";

    private static readonly string[] WordsProperties = ["Title", "Text", "Header", "Content"];

    /// <summary>What placing a control where it was dropped writes, which nothing but such a panel reads.</summary>
    private static readonly string[] PositionProperties = ["HorizontalAlignment", "VerticalAlignment", "Margin", "Canvas.Left", "Canvas.Top", "Grid.Row", "Grid.Column"];

    private readonly Assembly _application;
    private readonly IReadOnlyList<Assembly> _built;
    private readonly Dictionary<string, HashSet<string>> _mapped = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), Type?> _resolved = [];
    private readonly Dictionary<Type, string[]> _parts = [];

    private string? _text;
    private Markup? _markup;
    private List<Spot> _spots = [];
    private Window? _window;
    private string? _trouble;

    /// <summary>Initializes a new instance of the <see cref="Designer"/> class.</summary>
    /// <param name="application">The application, as last built, loaded.</param>
    /// <param name="built">What its build wrote beside it, loaded.</param>
    public Designer(Assembly application, IReadOnlyList<Assembly> built)
    {
        _application = application;
        _built = built;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic))
        {
            foreach (XmlnsDefinitionAttribute mapping in assembly.GetCustomAttributes<XmlnsDefinitionAttribute>())
            {
                if (!_mapped.TryGetValue(mapping.XmlNamespace, out HashSet<string>? namespaces))
                {
                    _mapped[mapping.XmlNamespace] = namespaces = new HashSet<string>(StringComparer.Ordinal);
                }

                namespaces.Add(mapping.ClrNamespace);
            }
        }
    }

    /// <summary>What holds other controls, and how many.</summary>
    private enum Holds
    {
        None,
        One,
        Many,
    }

    /// <summary>How a panel says where each control it holds is: by laying them out itself, by each one's margin in its cell, or by each one's place on a canvas.</summary>
    private enum Positions
    {
        None,
        Margin,
        Canvas,
    }

    /// <summary>Answers one thing asked of the designer, as the extension asks it: a line of JSON for a line of JSON.</summary>
    /// <param name="asked">What is asked: <c>op</c>, and the screen's text as it is now.</param>
    /// <remarks>
    /// <c>place</c> and <c>move</c> are asked with a point of the window, <c>x</c> and <c>y</c> — for
    /// <c>move</c> with where in the control it was taken hold of, <c>dx</c> and <c>dy</c> — or with
    /// <c>into</c> and <c>index</c>; <c>size</c> with <c>at</c>, <c>width</c> and <c>height</c>;
    /// <c>properties</c> with <c>at</c>; <c>set</c> with <c>at</c>, <c>name</c> and <c>value</c>, which is
    /// nothing to take the attribute out; <c>show</c> with <c>at</c>, as <c>draw</c> may be with
    /// <c>show</c>.
    /// </remarks>
    /// <returns>The answer.</returns>
    /// <exception cref="FormatException">A point asked about is not two numbers.</exception>
    /// <exception cref="InvalidOperationException">A part of the request is not of the kind it is read as: the op, the text or the words not a string.</exception>
    public JsonObject Answer(JsonObject asked)
    {
        ArgumentNullException.ThrowIfNull(asked);

        string op = (string?)asked["op"] ?? string.Empty;
        if (op == "controls")
        {
            return new JsonObject { ["controls"] = new JsonArray([.. Controls().Select(type => new JsonObject { ["type"] = type.FullName, ["name"] = type.Name })]) };
        }

        if ((string?)asked["text"] is not { } text)
        {
            return new JsonObject { ["trouble"] = "Nothing was said about the screen's text." };
        }

        Load(text);
        if (_markup is null)
        {
            return new JsonObject { ["trouble"] = _trouble };
        }

        return op switch
        {
            "draw" => Drawn(SpotAt(asked["show"])),
            "show" => new JsonObject { ["shown"] = Show(Chosen(asked)) },
            "at" => new JsonObject { ["at"] = At(PointOf(asked)) },
            "place" => Place(Find((string?)asked["type"]), Target(asked, moving: null), TopLeft(asked)),
            "move" => Chosen(asked) is { } moved ? Move(moved, Target(asked, moved), TopLeft(asked)) : Refused("gone"),
            "size" => Chosen(asked) is { } sized ? sized.Part is null ? Size(sized, asked) : Refused("part") : Refused("gone"),
            "properties" => Chosen(asked) is { } shown ? Properties(shown) : Refused("gone"),
            "set" => Chosen(asked) is { } given ? Set(given, (string?)asked["name"] ?? string.Empty, (string?)asked["value"]) : Refused("gone"),
            "remove" => Chosen(asked) is { } removed ? Remove(removed) : Refused("gone"),
            "words" => Chosen(asked) is { } said ? Say(said, (string?)asked["words"] ?? string.Empty) : Refused("gone"),
            _ => new JsonObject { ["trouble"] = $"'{op}' is not something the designer is asked." },
        };
    }

    /// <summary>
    /// Every control the application's build loads that can be placed: a control, made with nothing,
    /// that is not a window of its own nor a part of another control's template.
    /// </summary>
    public IReadOnlyList<Type> Controls() =>
        [.. _built.SelectMany(Exported)
            .Where(type => type.IsPublic && !type.IsAbstract && !type.IsGenericTypeDefinition
                && typeof(Control).IsAssignableFrom(type) && !typeof(TopLevel).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null
                && type.GetCustomAttribute<ObsoleteAttribute>() is null
                && type.Namespace is { } name && !name.EndsWith(".Presenters", StringComparison.Ordinal)
                && !name.EndsWith(".Chrome", StringComparison.Ordinal) && !name.EndsWith(".Embedding", StringComparison.Ordinal))
            .Distinct()
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ThenBy(type => type.FullName, StringComparer.Ordinal)];

    private static Type[] Exported(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (Exception wrong) when (wrong is ReflectionTypeLoadException or FileNotFoundException or FileLoadException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Reads the text, finds what each element is, and loads it as a window, where it is not the text last loaded.</summary>
    private void Load(string text)
    {
        if (text == _text)
        {
            return;
        }

        _text = text;
        _trouble = null;
        _window?.Close();
        _window = null;
        try
        {
            _markup = Markup.Read(text);
        }
        catch (FormatException wrong)
        {
            _markup = null;
            _spots = [];
            _trouble = wrong.Message;
            return;
        }

        _spots = [];
        Walk(_markup.Root, parent: null);
        if (_spots.Count == 0 || !typeof(Window).IsAssignableFrom(_spots[0].Type))
        {
            _trouble = $"'{_markup.Root.Name}' is not a window.";
            return;
        }

        try
        {
            object loaded = AvaloniaRuntimeXamlLoader.Load(
                new RuntimeXamlLoaderDocument(Marked(_markup)),
                new RuntimeXamlLoaderConfiguration { LocalAssembly = _application, UseCompiledBindingsByDefault = true });
            if (loaded is not Window window)
            {
                _trouble = $"'{_markup.Root.Name}' is not a window.";
                return;
            }

            window.Show();
            Dispatcher.UIThread.RunJobs();
            _window = window;
            Dictionary<int, Control> made = window.GetLogicalDescendants().Prepend(window).OfType<Control>()
                .Where(control => Placed.GetAt(control) >= 0)
                .GroupBy(Placed.GetAt)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (Spot spot in _spots)
            {
                spot.Control = made.GetValueOrDefault(spot.Id);
            }
        }
#pragma warning disable CA1031 // Whatever a text somebody is in the middle of writing makes the loader throw is said, and the window is drawn again once it loads.
        catch (Exception wrong)
#pragma warning restore CA1031
        {
            _trouble = wrong.Message;
        }
    }

    /// <summary>
    /// The elements that are placed controls — the window, and every control written inside one —
    /// numbered in the order they are written, each followed by its parts that hold a control and
    /// what is in them, before what it holds.
    /// </summary>
    private void Walk(MarkupElement element, int? parent)
    {
        if (Resolve(element) is not { } type || !typeof(Control).IsAssignableFrom(type))
        {
            return;
        }

        int id = _spots.Count;
        _spots.Add(new Spot(id, element, type, parent));
        foreach (string part in Parts(type))
        {
            MarkupElement? written = PartElement(element, part);
            if (written is null && element.Attribute(part) is not null)
            {
                continue;
            }

            int at = _spots.Count;
            _spots.Add(new Spot(at, written ?? element, typeof(object), id) { Part = part, Written = written is not null });
            foreach (MarkupElement child in written?.Children.Where(child => !child.IsProperty) ?? [])
            {
                Walk(child, at);
            }
        }

        foreach (MarkupElement child in element.Children.Where(child => !child.IsProperty))
        {
            Walk(child, id);
        }
    }

    /// <summary>
    /// The parts of a control that hold one control besides its content: each property that takes
    /// anything and is drawn by a template of its own, its name and <c>Template</c>.
    /// </summary>
    private string[] Parts(Type type)
    {
        if (!_parts.TryGetValue(type, out string[]? parts))
        {
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            _parts[type] = parts = [.. properties
                .Where(property => property.PropertyType == typeof(object) && property.SetMethod is { IsPublic: true }
                    && property.GetIndexParameters().Length == 0
                    && property.GetCustomAttribute<ContentAttribute>() is null
                    && properties.Any(template => template.Name == property.Name + "Template" && typeof(IDataTemplate).IsAssignableFrom(template.PropertyType)))
                .Select(property => property.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];
        }

        return parts;
    }

    /// <summary>The property element a part is written as, where it is.</summary>
    private static MarkupElement? PartElement(MarkupElement element, string part) =>
        element.Children.FirstOrDefault(child => child.IsProperty && child.LocalName[(child.LocalName.IndexOf('.', StringComparison.Ordinal) + 1)..] == part);

    /// <summary>The text with each placed element saying which it is, for the copy that is drawn.</summary>
    private string Marked(Markup markup)
    {
        StringBuilder marked = new(markup.Text);
        foreach (Spot spot in _spots.Where(spot => spot.Part is null).OrderByDescending(spot => spot.Element.AttributesEnd))
        {
            string at = $" {Marker}:Placed.At=\"{spot.Id}\"";
            if (spot.Id == 0)
            {
                at += $" xmlns:{Marker}=\"using:{typeof(Placed).Namespace}\"";
            }

            marked.Insert(spot.Element.AttributesEnd, at);
        }

        return marked.ToString();
    }

    /// <summary>The type an element names, as the namespaces in scope and the assemblies loaded say.</summary>
    private Type? Resolve(MarkupElement element)
    {
        if (element.Namespace is not { } xmlns)
        {
            return null;
        }

        if (_resolved.TryGetValue((xmlns, element.LocalName), out Type? known))
        {
            return known;
        }

        IEnumerable<string> namespaces = xmlns.StartsWith("using:", StringComparison.Ordinal) ? [xmlns["using:".Length..]]
            : xmlns.StartsWith("clr-namespace:", StringComparison.Ordinal) ? [xmlns["clr-namespace:".Length..].Split(';')[0]]
            : _mapped.GetValueOrDefault(xmlns) ?? [];
        Assembly[] assemblies = [.. AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic)];
        Type? type = namespaces.SelectMany(name => assemblies.Select(assembly => assembly.GetType($"{name}.{element.LocalName}")))
            .FirstOrDefault(found => found is not null);
        _resolved[(xmlns, element.LocalName)] = type;
        return type;
    }

    /// <summary>The window as drawn, showing a control where it is asked to: a picture of it, and every placed control with where it is.</summary>
    private JsonObject Drawn(Spot? shown)
    {
        Show(shown);
        if (_window is null)
        {
            return new JsonObject { ["trouble"] = _trouble, ["placed"] = new JsonArray([.. _spots.Select(spot => (JsonNode)Describe(spot))]) };
        }

        Dispatcher.UIThread.RunJobs();
        _window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using MemoryStream png = new();
        try
        {
            using WriteableBitmap? frame = _window.CaptureRenderedFrame();
            frame?.Save(png);
        }
        catch (NotSupportedException)
        {
            // Avalonia started without Skia draws nothing to take a picture of — in a test, say —
            // and where everything is still is still said.
        }

        return new JsonObject
        {
            ["picture"] = Convert.ToBase64String(png.ToArray()),
            ["size"] = new JsonObject { ["width"] = _window.ClientSize.Width, ["height"] = _window.ClientSize.Height },
            ["placed"] = new JsonArray([.. _spots.Select(spot => (JsonNode)Describe(spot))]),
        };
    }

    /// <summary>
    /// A control shown on the window drawn, where a list of items that shows one at a time holds it
    /// or what it is in: that item made the one shown. Nothing of it is written.
    /// </summary>
    /// <returns>Whether anything shown changed.</returns>
    private bool Show(Spot? spot)
    {
        bool changed = false;
        for (Spot? at = spot; at?.Parent is int parent; at = _spots[parent])
        {
            if (_spots[parent].Control is SelectingItemsControl items && at.Control is { } item
                && items.Items.Contains(item) && !ReferenceEquals(items.SelectedItem, item))
            {
                items.SelectedItem = item;
                changed = true;
            }
        }

        if (changed && _window is not null)
        {
            Dispatcher.UIThread.RunJobs();
            _window.UpdateLayout();
        }

        return changed;
    }

    private JsonObject Describe(Spot spot)
    {
        (string? words, bool sayable) = Words(spot);
        Control? control = spot.Control;
        Point? at = control is not null && _window is not null && control.IsEffectivelyVisible ? control.TranslatePoint(default, _window) : null;
        return new JsonObject
        {
            ["id"] = spot.Id,
            ["control"] = spot.Part ?? spot.Element.LocalName,
            ["type"] = spot.Part is { } part ? $"{_spots[spot.Parent!.Value].Type.FullName}.{part}" : spot.Type.FullName,
            ["parent"] = spot.Parent,
            ["part"] = spot.Part is not null,
            ["written"] = spot.Written,
            ["words"] = words,
            ["sayable"] = sayable,
            ["holds"] = Holding(spot) switch { Holds.Many => "many", Holds.One => "one", _ => "none" },
            ["full"] = Full(spot),
            ["box"] = at is { } where
                ? new JsonObject { ["x"] = where.X, ["y"] = where.Y, ["width"] = control!.Bounds.Width, ["height"] = control.Bounds.Height }
                : null,
        };
    }

    /// <summary>The placed control under a point of the window, the innermost one; the window itself where there is none.</summary>
    private int? At(Point point)
    {
        if (_window is null)
        {
            return null;
        }

        // By where each control is laid out, not by Avalonia's hit test, which passes through a panel
        // with no background — and placing into one is what an empty panel is for. Of the controls
        // there, the one drawn last is inside or on top of the others: a split view's pane over its
        // content, a grid's last over its first.
        Dictionary<Visual, int> drawn = [];
        void Order(Visual visual)
        {
            drawn[visual] = drawn.Count;
            foreach (Visual child in visual.GetVisualChildren().OrderBy(child => child.ZIndex))
            {
                Order(child);
            }
        }

        Order(_window);
        return _spots.Where(spot => spot.Control is { IsEffectivelyVisible: true } control
                && control.TranslatePoint(default, _window) is { } at
                && new Rect(at, control.Bounds.Size).Contains(point))
            .MaxBy(spot => drawn.GetValueOrDefault(spot.Control!, -1))?.Id ?? 0;
    }

    private Spot? Chosen(JsonObject asked) => SpotAt(asked["at"]);

    private Spot? SpotAt(JsonNode? asked) =>
        asked is JsonValue at && at.TryGetValue(out int id) && id >= 0 && id < _spots.Count ? _spots[id] : null;

    /// <summary>A point of the window, in its own units, as asked: <c>x</c> and <c>y</c>, whole or not.</summary>
    /// <exception cref="FormatException">Either is not a number.</exception>
    private static Point PointOf(JsonObject asked)
    {
        static double Number(JsonNode? node) =>
            node is JsonValue value && (value.TryGetValue(out double number) || (value.TryGetValue(out int whole) && (number = whole) == whole))
                ? number
                : throw new FormatException("A point is two numbers, x and y.");

        return new Point(Number(asked["x"]), Number(asked["y"]));
    }

    private Type? Find(string? name) => name is null ? null : Controls().FirstOrDefault(type => type.FullName == name);

    /// <summary>Where the top left of what is placed or moved is to be: the point asked, less where in it it was taken hold of; nothing where no point was asked.</summary>
    /// <exception cref="FormatException">A part of it is not a number.</exception>
    private static Point? TopLeft(JsonObject asked)
    {
        if (asked["x"] is null || asked["y"] is null)
        {
            return null;
        }

        Point point = PointOf(asked);
        return asked["dx"] is null || asked["dy"] is null ? point : point - PointOf(new JsonObject { ["x"] = asked["dx"]?.DeepClone(), ["y"] = asked["dy"]?.DeepClone() });
    }

    /// <summary>Where something is to go: a placed control that holds others, and before which of what it holds — said, or found under a point.</summary>
    private (Spot Into, int Index)? Target(JsonObject asked, Spot? moving)
    {
        if (asked["into"] is JsonValue into && into.TryGetValue(out int id) && id >= 0 && id < _spots.Count)
        {
            Spot spot = _spots[id];
            int count = Children(spot).Count;
            return (spot, asked["index"] is JsonValue index && index.TryGetValue(out int at) ? Math.Clamp(at, 0, count) : count);
        }

        if (asked["x"] is null || asked["y"] is null)
        {
            return null;
        }

        Point point = PointOf(asked);
        if (At(point) is not { } hit)
        {
            return null;
        }

        // Dropped on itself, or on something inside it, it goes beside itself.
        Spot under = _spots[hit];
        if (moving is not null && Within(under, moving))
        {
            under = moving;
        }
        else if (Holding(under) == Holds.Many)
        {
            return (under, PositionsOf(under.Type) == Positions.None ? IndexAt(under, point) : Children(under).Count);
        }
        else if (Holding(under) == Holds.One && !Full(under))
        {
            return (under, 0);
        }

        for (Spot child = under; child.Parent is int parentId; child = _spots[parentId])
        {
            Spot parent = _spots[parentId];
            if (Holding(parent) == Holds.Many)
            {
                int index = Children(parent).IndexOf(child);
                return (parent, index + (Past(child, point, Horizontal(parent)) ? 1 : 0));
            }
        }

        return null;
    }

    /// <summary>
    /// Where something put in an item of a list goes, where it is of that item's kind: beside it, in
    /// the list, rather than in it — a tab put after the tab chosen, never in it.
    /// </summary>
    private (Spot Into, int Index)? Beside((Spot Into, int Index)? target, Type type)
    {
        if (target is not { } where || where.Into.Part is not null || where.Into.Parent is not int parent
            || !typeof(ItemsControl).IsAssignableFrom(_spots[parent].Type) || !where.Into.Type.IsAssignableFrom(type))
        {
            return target;
        }

        return (_spots[parent], Children(_spots[parent]).IndexOf(where.Into) + 1);
    }

    /// <summary>Before which of what a panel holds a point is, along the way it lays them out.</summary>
    private int IndexAt(Spot panel, Point point)
    {
        bool horizontal = Horizontal(panel);
        List<Spot> children = Children(panel);
        int index = 0;
        for (int i = 0; i < children.Count; i++)
        {
            if (Past(children[i], point, horizontal))
            {
                index = i + 1;
            }
        }

        return index;
    }

    private bool Past(Spot spot, Point point, bool horizontal)
    {
        if (spot.Control is not { } control || _window is null || control.TranslatePoint(default, _window) is not { } at)
        {
            return false;
        }

        return horizontal ? point.X > at.X + (control.Bounds.Width / 2) : point.Y > at.Y + (control.Bounds.Height / 2);
    }

    /// <summary>
    /// Whether what holds many lays them out across: as its orientation says, or — a list of items,
    /// which may have none — as the first two of them stand.
    /// </summary>
    private bool Horizontal(Spot panel)
    {
        if (panel.Control?.GetType().GetProperty("Orientation") is { } orientation)
        {
            return orientation.GetValue(panel.Control) is global::Avalonia.Layout.Orientation.Horizontal;
        }

        if (panel.Control is not ItemsControl || _window is null)
        {
            return false;
        }

        Point[] at = [.. Children(panel)
            .Select(child => child.Control is { IsEffectivelyVisible: true } control ? control.TranslatePoint(default, _window) : null)
            .OfType<Point>()
            .Take(2)];
        return at.Length == 2 && Math.Abs(at[1].X - at[0].X) > Math.Abs(at[1].Y - at[0].Y);
    }

    private bool Within(Spot spot, Spot ancestor)
    {
        for (Spot? at = spot; at is not null; at = at.Parent is int parent ? _spots[parent] : null)
        {
            if (at == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What a placed control holds, or a part of one, in the order it is written; not its parts.</summary>
    private List<Spot> Children(Spot spot) => [.. _spots.Where(child => child.Parent == spot.Id && child.Part is null)];

    private static Positions PositionsOf(Type type) =>
        typeof(Canvas).IsAssignableFrom(type) ? Positions.Canvas
        : typeof(Grid).IsAssignableFrom(type) || type == typeof(Panel) ? Positions.Margin
        : Positions.None;

    /// <summary>
    /// What a control is written with to have its top left at a point of the window, in a panel that
    /// says where each control it holds is: its cell and its margin in it, or its place on a canvas.
    /// </summary>
    private Dictionary<string, string?> Position(Spot panel, Point topLeft)
    {
        Point origin = panel.Control is { } control && _window is not null ? control.TranslatePoint(default, _window) ?? default : default;
        Point at = topLeft - origin;
        Dictionary<string, string?> said = new(StringComparer.Ordinal);
        if (PositionsOf(panel.Type) == Positions.Canvas)
        {
            said["Canvas.Left"] = Whole(at.X);
            said["Canvas.Top"] = Whole(at.Y);
            return said;
        }

        if (panel.Control is Grid grid)
        {
            (int column, double left) = Cell([.. grid.ColumnDefinitions.Select(definition => definition.ActualWidth)], at.X);
            (int row, double top) = Cell([.. grid.RowDefinitions.Select(definition => definition.ActualHeight)], at.Y);
            at = new Point(at.X - left, at.Y - top);
            said["Grid.Row"] = grid.RowDefinitions.Count > 1 ? row.ToString(CultureInfo.InvariantCulture) : null;
            said["Grid.Column"] = grid.ColumnDefinitions.Count > 1 ? column.ToString(CultureInfo.InvariantCulture) : null;
        }

        said["HorizontalAlignment"] = "Left";
        said["VerticalAlignment"] = "Top";
        said["Margin"] = $"{Whole(at.X)},{Whole(at.Y)},0,0";
        return said;
    }

    /// <summary>Which of a grid's rows or columns a distance from its start falls in, and where that one starts; the first where it has none.</summary>
    private static (int Index, double Start) Cell(IReadOnlyList<double> sizes, double distance)
    {
        double start = 0;
        for (int i = 0; i < sizes.Count; i++)
        {
            if (distance < start + sizes[i] || i == sizes.Count - 1)
            {
                return (i, start);
            }

            start += sizes[i];
        }

        return (0, 0);
    }

    /// <summary>A length as written: whole, and never less than nothing.</summary>
    private static string Whole(double length) => Math.Max(0, Math.Round(length)).ToString(CultureInfo.InvariantCulture);

    /// <summary>The edits that give an element's attributes these values, or take out those given none — each written where it is, the new ones after the last.</summary>
    private List<Edit> Attributes(MarkupElement element, IReadOnlyDictionary<string, string?> set)
    {
        List<Edit> edits = [];
        StringBuilder added = new();
        foreach ((string name, string? value) in set)
        {
            if (element.Attribute(name) is { } written)
            {
                if (value is null)
                {
                    int start = written.Start;
                    while (start > 0 && char.IsWhiteSpace(_markup!.Text[start - 1]) && _markup.Text[start - 1] is not ('\n' or '\r'))
                    {
                        start--;
                    }

                    edits.Add(new Edit(start, written.End - start, string.Empty));
                }
                else if (_markup!.Text[written.ValueStart..written.ValueEnd] != Markup.Attribute(value))
                {
                    edits.Add(new Edit(written.ValueStart, written.ValueEnd - written.ValueStart, Markup.Attribute(value)));
                }
            }
            else if (value is not null)
            {
                added.Append(CultureInfo.InvariantCulture, $" {name}=\"{Markup.Attribute(value)}\"");
            }
        }

        if (added.Length > 0)
        {
            edits.Add(new Edit(element.AttributesEnd, 0, added.ToString()));
        }

        return edits;
    }

    /// <summary>What placing a control where it was dropped wrote, each taken out: for a control moved into a panel that lays out what it holds itself.</summary>
    private static Dictionary<string, string?> Unpositioned() => PositionProperties.ToDictionary(name => name, string? (_) => null, StringComparer.Ordinal);

    private static Holds HoldsOf(Type type) =>
        typeof(Panel).IsAssignableFrom(type) || typeof(ItemsControl).IsAssignableFrom(type) ? Holds.Many
        : typeof(Decorator).IsAssignableFrom(type) || typeof(ContentControl).IsAssignableFrom(type) ? Holds.One
        : Holds.None;

    /// <summary>What a placed control holds, or a part of one: a part holds one.</summary>
    private static Holds Holding(Spot spot) => spot.Part is not null ? Holds.One : HoldsOf(spot.Type);

    /// <summary>
    /// Whether what holds one thing has it already — a control, or content written as words — or a
    /// list of items is given its items by a binding, so that none can be written in it.
    /// </summary>
    private static bool Full(Spot spot) => Holding(spot) switch
    {
        Holds.One when spot.Part is not null => spot.Written && (spot.Element.Children.Any(child => !child.IsProperty) || spot.Element.Words is not null),
        Holds.One => spot.Element.Children.Any(child => !child.IsProperty) || spot.Element.Words is not null
            || spot.Element.Attribute("Content") is not null || spot.Element.Attribute("Child") is not null,
        Holds.Many => spot.Element.Attribute("ItemsSource") is not null,
        _ => false,
    };

    /// <summary>The property a control's words are, where it has one.</summary>
    private static string? WordsProperty(Type type) =>
        WordsProperties.FirstOrDefault(name => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { CanWrite: true } property
            && (property.PropertyType == typeof(string) || property.PropertyType == typeof(object)));

    /// <summary>A control's words as written, and whether it can be given words here: not where they are bound, nor where what it holds is a control, nor where its words are a part that holds one.</summary>
    private static (string? Words, bool Sayable) Words(Spot spot)
    {
        if (spot.Part is not null || WordsProperty(spot.Type) is not { } property || PartElement(spot.Element, property) is not null)
        {
            return (null, false);
        }

        if (spot.Element.Attribute(property) is { } written)
        {
            return written.Value.StartsWith("{}", StringComparison.Ordinal) ? (written.Value[2..], true)
                : written.Value.StartsWith('{') ? (null, false)
                : (written.Value, true);
        }

        bool holdsControl = spot.Element.Children.Any(child => !child.IsProperty);
        return (holdsControl ? null : spot.Element.Words, !holdsControl);
    }

    private static JsonObject Refused(string why) => new() { ["refused"] = why };

    /// <summary>A control placed: written into what holds it, before the one it was put before — or, in a panel that says where each control is, where it was dropped.</summary>
    private JsonObject Place(Type? type, (Spot Into, int Index)? target, Point? topLeft)
    {
        if (type is null)
        {
            return Refused("unknown");
        }

        if (Beside(target, type) is not { } where)
        {
            return Refused("nowhere");
        }

        if (!Accepts(where.Into))
        {
            return Refused(Unaccepted(where.Into));
        }

        List<Edit> edits = [];
        string name = NameFor(type, edits);
        static string Written(Dictionary<string, string?> said) =>
            string.Concat(said.Where(pair => pair.Value is not null).Select(pair => $" {pair.Key}=\"{Markup.Attribute(pair.Value!)}\""));

        // The window holds one control. One that is not a panel, dropped on it empty at a point, is put
        // in a grid there, as a new window in Visual Studio's WPF designer has one, so that more can go
        // beside it.
        if (where.Into.Parent is null && HoldsOf(type) != Holds.Many && topLeft is { } dropped)
        {
            string newLine = _markup!.NewLine;
            string indent = IndentOf(where.Into);
            string grid = NameFor(typeof(Grid), edits);
            string control = $"<{name}{Written(Position(where.Into, dropped))} />";
            (Edit wrap, int at) = Insert(where.Into, 0, $"<{grid}>{newLine}{indent}  {control}{newLine}{indent}</{grid}>");
            edits.Add(wrap);
            return Made(edits, wrap, at + grid.Length + 2 + newLine.Length + indent.Length + 2);
        }

        string attributes = PositionsOf(where.Into.Type) == Positions.None || topLeft is not { } point ? string.Empty
            : Written(Position(where.Into, point));

        // An item of a list, a tab of a tab control say, is given its type's name as its words, as
        // Visual Studio's WPF designer gives one, so that it is drawn and can be pointed at.
        if (typeof(ItemsControl).IsAssignableFrom(where.Into.Type) && WordsProperty(type) is { } words)
        {
            attributes += $" {words}=\"{type.Name}\"";
        }

        (Edit insert, int within) = Insert(where.Into, where.Index, $"<{name}{attributes} />");
        edits.Add(insert);
        return Made(edits, insert, within);
    }

    /// <summary>
    /// A placed control moved: taken out where it is and written where it goes, as it was written. In
    /// a panel that says where each control is, dragged to a point, it is moved there instead — and
    /// moved out of such a panel into one that lays out what it holds, what said where it was goes.
    /// </summary>
    private JsonObject Move(Spot moving, (Spot Into, int Index)? target, Point? topLeft)
    {
        if (moving.Parent is null)
        {
            return Refused("window");
        }

        if (moving.Part is not null)
        {
            return Refused("part");
        }

        if (Beside(target, moving.Type) is not { } where)
        {
            return Refused("nowhere");
        }

        if (Within(where.Into, moving))
        {
            return Refused("inside");
        }

        List<Spot> siblings = Children(where.Into);
        int from = siblings.IndexOf(moving);
        bool positions = PositionsOf(where.Into.Type) != Positions.None;
        if (from >= 0 && positions && topLeft is { } to)
        {
            List<Edit> placing = Attributes(moving.Element, Position(where.Into, to));
            return new JsonObject { ["text"] = Apply(_markup!.Text, placing), ["select"] = moving.Id };
        }

        if (from >= 0 && (where.Index == from || where.Index == from + 1))
        {
            return new JsonObject { ["text"] = _text, ["select"] = moving.Id };
        }

        if (from < 0 && !Accepts(where.Into))
        {
            return Refused(Unaccepted(where.Into));
        }

        Markup markup = _markup!;
        MarkupElement element = moving.Element;
        Edit removal = Removal(element);
        string written = markup.Text[element.Start..element.End];
        if (from < 0)
        {
            // Into another panel: where it is said as that panel says it, or not said where the panel lays it out.
            bool positioned = moving.Parent is int was && PositionsOf(_spots[was].Type) != Positions.None;
            List<Edit> placing = positions && topLeft is { } point ? Attributes(element, Position(where.Into, point))
                : positioned ? Attributes(element, Unpositioned()) : [];
            written = Apply(written, placing.Select(edit => edit with { Start = edit.Start - element.Start }));
        }

        (Edit insert, int within) = Insert(where.Into, where.Index, written);
        string oldIndent = markup.StartsLine(element.Start) ? markup.IndentAt(element.Start) : string.Empty;
        string newIndent = IndentOf(where.Into);
        if (oldIndent != newIndent)
        {
            string reindented = string.Join('\n', written.Split('\n').Select((line, i) =>
                i > 0 && line.StartsWith(oldIndent, StringComparison.Ordinal) ? newIndent + line[oldIndent.Length..] : line));
            insert = insert with { Text = insert.Text.Replace(written, reindented, StringComparison.Ordinal) };
        }

        return Made([removal, insert], insert, within);
    }

    /// <summary>
    /// A placed control given a size: its width and height. In a panel that puts each control in its
    /// cell by its margin, it is held to its top left there too, where it was not, so that it keeps
    /// its place as it is sized.
    /// </summary>
    /// <exception cref="FormatException">The width or the height is not a number.</exception>
    private JsonObject Size(Spot sized, JsonObject asked)
    {
        Point size = PointOf(new JsonObject { ["x"] = asked["width"]?.DeepClone(), ["y"] = asked["height"]?.DeepClone() });
        Dictionary<string, string?> set = new(StringComparer.Ordinal)
        {
            ["Width"] = Whole(Math.Max(1, size.X)),
            ["Height"] = Whole(Math.Max(1, size.Y)),
        };
        if (sized.Parent is int parent && PositionsOf(_spots[parent].Type) == Positions.Margin
            && (sized.Element.Attribute("HorizontalAlignment")?.Value != "Left" || sized.Element.Attribute("VerticalAlignment")?.Value != "Top")
            && sized.Control is { } control && _window is not null && control.TranslatePoint(default, _window) is { } at)
        {
            foreach ((string name, string? value) in Position(_spots[parent], at))
            {
                set[name] = value;
            }
        }

        return new JsonObject { ["text"] = Apply(_markup!.Text, Attributes(sized.Element, set)), ["select"] = sized.Id };
    }

    /// <summary>
    /// Every property a placed control can be given as text in XAML: what kind of value it takes, the
    /// choices where it is one of a set, what is written for it — or that it is bound — and what it
    /// is as drawn.
    /// </summary>
    private JsonObject Properties(Spot spot)
    {
        JsonArray properties = [];
        foreach ((string name, AvaloniaProperty property, string kind, string[]? choices) in Settable(spot))
        {
            string? written = spot.Element.Attribute(name)?.Value;
            bool bound = written is not null && written.StartsWith('{') && !written.StartsWith("{}", StringComparison.Ordinal);
            properties.Add(new JsonObject
            {
                ["name"] = name,
                ["kind"] = kind,
                ["choices"] = choices is null ? null : new JsonArray([.. choices.Select(choice => JsonValue.Create(choice))]),
                ["written"] = bound ? null : written,
                ["bound"] = bound,
                ["now"] = spot.Control is { } control ? Said(control.GetValue(property)) : null,
            });
        }

        return new JsonObject { ["properties"] = properties };
    }

    /// <summary>A property of a placed control given a value, written as its attribute; or the attribute taken out, given none.</summary>
    private JsonObject Set(Spot spot, string name, string? value)
    {
        if (Settable(spot).FirstOrDefault(found => found.Name == name) is not { Property: not null } setting)
        {
            return Refused("noProperty");
        }

        if (spot.Element.Attribute(name)?.Value is { } written && written.StartsWith('{') && !written.StartsWith("{}", StringComparison.Ordinal))
        {
            return Refused("bound");
        }

        if (!string.IsNullOrEmpty(value) && !Takes(setting.Property.PropertyType, setting.Kind, setting.Choices, value))
        {
            return Refused("value");
        }

        Dictionary<string, string?> set = new(StringComparer.Ordinal) { [name] = string.IsNullOrEmpty(value) ? null : value };
        return new JsonObject { ["text"] = Apply(_markup!.Text, Attributes(spot.Element, set)), ["select"] = spot.Id };
    }

    /// <summary>
    /// The properties a placed control can be given here, by the name its attribute has: every one
    /// Avalonia has registered for its type that can be set and is written as text, and the attached
    /// ones of the type that holds it. Not its words, which are given as its words, nor what only
    /// binding gives it.
    /// </summary>
    private List<(string Name, AvaloniaProperty Property, string Kind, string[]? Choices)> Settable(Spot spot)
    {
        List<(string, AvaloniaProperty, string, string[]?)> found = [];
        HashSet<string> named = new(StringComparer.Ordinal);
        string? words = WordsProperty(spot.Type);
        void Add(string name, AvaloniaProperty property)
        {
            if (!property.IsReadOnly && name != words && name is not ("DataContext" or "Theme") && named.Add(name)
                && KindOf(property.PropertyType) is { } kind)
            {
                found.Add((name, property, kind.Kind, kind.Choices));
            }
        }

        if (spot.Part is not null)
        {
            return [];
        }

        foreach (AvaloniaProperty property in AvaloniaPropertyRegistry.Instance.GetRegistered(spot.Type))
        {
            Add(property.Name, property);
        }

        if (spot.Parent is int parent)
        {
            // The attached properties the type that holds it declares, as it declares them.
            foreach (FieldInfo field in _spots[parent].Type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (field.GetValue(null) is AvaloniaProperty { IsAttached: true } property && property.OwnerType.IsAssignableFrom(_spots[parent].Type))
                {
                    Add($"{property.OwnerType.Name}.{property.Name}", property);
                }
            }
        }

        return [.. found.OrderBy(property => property.Item1, StringComparer.Ordinal)];
    }

    /// <summary>The kind of value a property takes, as it is given here, and its choices where it is one of a set; nothing where it is not written as text in XAML.</summary>
    private static (string Kind, string[]? Choices)? KindOf(Type type)
    {
        Type of = Nullable.GetUnderlyingType(type) ?? type;
        if (of == typeof(string))
        {
            return ("text", null);
        }

        if (of == typeof(bool))
        {
            return ("bool", null);
        }

        if (of.IsEnum)
        {
            return ("choice", Enum.GetNames(of));
        }

        if (of == typeof(double) || of == typeof(float) || of == typeof(int) || of == typeof(long) || of == typeof(decimal))
        {
            return ("number", null);
        }

        if (of == typeof(Color) || typeof(IBrush).IsAssignableFrom(of))
        {
            return ("color", null);
        }

        // Anything else XAML reads from text: a thickness, a corner radius, a grid's rows, a font.
        return of.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]) is not null ? ("text", null) : null;
    }

    /// <summary>Whether a value is one a property of this kind can be given: a number for a number, one of the choices, a colour XAML reads, or text its type parses.</summary>
    private static bool Takes(Type type, string kind, string[]? choices, string value)
    {
        Type of = Nullable.GetUnderlyingType(type) ?? type;
        switch (kind)
        {
            case "number":
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            case "bool":
                return value is "True" or "False";
            case "choice":
                return choices is not null && choices.Contains(value, StringComparer.Ordinal);
            case "color":
                return Color.TryParse(value, out _);
            default:
                if (of == typeof(string))
                {
                    return true;
                }

                try
                {
                    of.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)])!.Invoke(null, [value]);
                    return true;
                }
                catch (TargetInvocationException)
                {
                    return false;
                }
        }
    }

    /// <summary>A property's value as drawn, as it would be written: a colour as one, a number without the culture's marks, and nothing where it has none.</summary>
    private static string? Said(object? value) => value switch
    {
        null => null,
        double number when double.IsNaN(number) => null,
        ISolidColorBrush brush => brush.Color.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>A placed control taken out, and with it the line it was on where it was alone there.</summary>
    private JsonObject Remove(Spot removed)
    {
        if (removed.Parent is not int parent)
        {
            return Refused("window");
        }

        if (removed.Part is not null && !removed.Written)
        {
            return Refused("part");
        }

        Edit removal = Removal(removed.Element);
        string text = Apply(_markup!.Text, [removal]);
        return new JsonObject { ["text"] = text, ["select"] = parent };
    }

    /// <summary>A control given its words: the attribute they are written as, set, written or taken out.</summary>
    private JsonObject Say(Spot spot, string words)
    {
        (_, bool sayable) = Words(spot);
        if (!sayable || WordsProperty(spot.Type) is not { } property)
        {
            return Refused("wordless");
        }

        MarkupElement element = spot.Element;
        Edit edit;
        if (element.Attribute(property) is { } written)
        {
            int start = written.Start;
            while (start > 0 && char.IsWhiteSpace(_markup!.Text[start - 1]) && _markup.Text[start - 1] is not ('\n' or '\r'))
            {
                start--;
            }

            edit = words.Length == 0
                ? new Edit(start, written.End - start, string.Empty)
                : new Edit(written.ValueStart, written.ValueEnd - written.ValueStart, Markup.Attribute(words));
        }
        else if (element.Words is not null)
        {
            edit = new Edit(element.OpenEnd, element.CloseStart - element.OpenEnd, Markup.Attribute(words).Replace("&quot;", "\"", StringComparison.Ordinal));
        }
        else if (words.Length > 0)
        {
            edit = new Edit(element.NameEnd, 0, $" {property}=\"{Markup.Attribute(words)}\"");
        }
        else
        {
            return new JsonObject { ["text"] = _text, ["select"] = spot.Id };
        }

        return new JsonObject { ["text"] = Apply(_markup!.Text, [edit]), ["select"] = spot.Id };
    }

    private static bool Accepts(Spot into) => Holding(into) != Holds.None && !Full(into);

    /// <summary>Why something cannot be put in what it was put in.</summary>
    private static string Unaccepted(Spot into) => Holding(into) switch
    {
        Holds.None => "holdsNothing",
        Holds.Many => "listed",
        _ => "full",
    };

    /// <summary>The name a type is written by where it is placed: bare where the default namespace has it, under a prefix declared for it otherwise — declared on the window, by an edit, where none is.</summary>
    private string NameFor(Type type, List<Edit> edits)
    {
        MarkupElement root = _markup!.Root;
        foreach ((string prefix, string xmlns) in root.Scope)
        {
            bool maps = xmlns == $"using:{type.Namespace}"
                || xmlns.StartsWith($"clr-namespace:{type.Namespace};", StringComparison.Ordinal) || xmlns == $"clr-namespace:{type.Namespace}"
                || (_mapped.TryGetValue(xmlns, out HashSet<string>? namespaces) && namespaces.Contains(type.Namespace!));
            if (maps)
            {
                return prefix.Length == 0 ? type.Name : $"{prefix}:{type.Name}";
            }
        }

        string declared = type.Namespace!.Split('.')[^1].ToLowerInvariant();
        string chosen = declared;
        for (int n = 2; root.Scope.ContainsKey(chosen); n++)
        {
            chosen = declared + n.ToString(CultureInfo.InvariantCulture);
        }

        edits.Add(new Edit(root.AttributesEnd, 0, $" xmlns:{chosen}=\"using:{type.Namespace}\""));
        return $"{chosen}:{type.Name}";
    }

    /// <summary>What a control's children are indented by: as the first of them is, or one step in from it.</summary>
    private string IndentOf(Spot into)
    {
        Markup markup = _markup!;
        if (into.Part is not null && !into.Written)
        {
            MarkupElement? any = into.Element.Children.FirstOrDefault();
            return (any is not null && markup.StartsLine(any.Start) ? markup.IndentAt(any.Start) : markup.IndentAt(into.Element.Start) + "  ") + "  ";
        }

        MarkupElement? first = into.Element.Children.FirstOrDefault(child => !child.IsProperty);
        return first is not null && markup.StartsLine(first.Start) ? markup.IndentAt(first.Start) : markup.IndentAt(into.Element.Start) + "  ";
    }

    /// <summary>An element written into another, before the one at an index of what it holds or after them all, on a line of its own; and where in the inserted text the element begins.</summary>
    private (Edit Edit, int Within) Insert(Spot into, int index, string element)
    {
        Markup markup = _markup!;
        string newLine = markup.NewLine;
        string indent = IndentOf(into);
        MarkupElement parent = into.Element;
        if (into.Part is { } part && !into.Written)
        {
            return InsertPart(parent, part, indent, element);
        }

        List<MarkupElement> children = [.. parent.Children.Where(child => !child.IsProperty)];

        if (index < children.Count)
        {
            MarkupElement before = children[index];
            return markup.StartsLine(before.Start)
                ? (new Edit(markup.LineStart(before.Start), 0, indent + element + newLine), indent.Length)
                : (new Edit(before.Start, 0, element + " "), 0);
        }

        if (children.Count > 0)
        {
            return (new Edit(children[^1].End, 0, newLine + indent + element), newLine.Length + indent.Length);
        }

        string closing = markup.IndentAt(parent.Start);
        if (parent.Empty)
        {
            int from = parent.AttributesEnd;
            return (new Edit(from, parent.End - from, $">{newLine}{indent}{element}{newLine}{closing}</{parent.Name}>"), 1 + newLine.Length + indent.Length);
        }

        string inside = markup.Text[parent.OpenEnd..parent.CloseStart];
        if (string.IsNullOrWhiteSpace(inside))
        {
            return (new Edit(parent.OpenEnd, inside.Length, $"{newLine}{indent}{element}{newLine}{closing}"), newLine.Length + indent.Length);
        }

        return markup.StartsLine(parent.CloseStart)
            ? (new Edit(markup.LineStart(parent.CloseStart), 0, indent + element + newLine), indent.Length)
            : (new Edit(parent.CloseStart, 0, newLine + indent + element + newLine + closing), newLine.Length + indent.Length);
    }

    /// <summary>
    /// An element written into a part of a control not written yet: the part written as a property
    /// element holding it, first in the control, on lines of their own; and where in the inserted text
    /// the element begins.
    /// </summary>
    private (Edit Edit, int Within) InsertPart(MarkupElement owner, string part, string indent, string element)
    {
        Markup markup = _markup!;
        string newLine = markup.NewLine;
        string outer = indent[..^2];
        string open = $"<{owner.Name}.{part}>";
        string block = $"{open}{newLine}{indent}{element}{newLine}{outer}</{owner.Name}.{part}>";
        int within = open.Length + newLine.Length + indent.Length;
        string closing = markup.IndentAt(owner.Start);

        if (owner.Children.FirstOrDefault() is { } first)
        {
            return markup.StartsLine(first.Start)
                ? (new Edit(markup.LineStart(first.Start), 0, outer + block + newLine), outer.Length + within)
                : (new Edit(first.Start, 0, block + " "), within);
        }

        if (owner.Empty)
        {
            int from = owner.AttributesEnd;
            return (new Edit(from, owner.End - from, $">{newLine}{outer}{block}{newLine}{closing}</{owner.Name}>"), 1 + newLine.Length + outer.Length + within);
        }

        string inside = markup.Text[owner.OpenEnd..owner.CloseStart];
        return string.IsNullOrWhiteSpace(inside)
            ? (new Edit(owner.OpenEnd, inside.Length, $"{newLine}{outer}{block}{newLine}{closing}"), newLine.Length + outer.Length + within)
            : (new Edit(owner.OpenEnd, 0, $"{newLine}{outer}{block}"), newLine.Length + outer.Length + within);
    }

    /// <summary>An element taken out of the text, with its line where nothing else is on it.</summary>
    private Edit Removal(MarkupElement element)
    {
        Markup markup = _markup!;
        int past = markup.PastLine(element.End);
        if (markup.StartsLine(element.Start) && past != element.End)
        {
            int start = markup.LineStart(element.Start);
            return new Edit(start, past - start, string.Empty);
        }

        return new Edit(element.Start, element.End - element.Start, string.Empty);
    }

    /// <summary>The text with edits made, and the element written by one of them found in it, to be chosen.</summary>
    private JsonObject Made(List<Edit> edits, Edit written, int within)
    {
        string text = Apply(_markup!.Text, edits, written, within, out int at);
        int select = 0;
        try
        {
            Markup made = Markup.Read(text);
            List<Spot> before = _spots;
            _spots = [];
            Walk(made.Root, parent: null);
            select = _spots.FirstOrDefault(spot => spot.Element.Start == at)?.Id ?? 0;
            _spots = before;
        }
        catch (FormatException)
        {
            // The edits leave well-formed text; where they did not, nothing is chosen.
        }

        return new JsonObject { ["text"] = text, ["select"] = select };
    }

    private static string Apply(string text, IEnumerable<Edit> edits) => Apply(text, edits, written: null, within: 0, out _);

    private static string Apply(string text, IEnumerable<Edit> edits, Edit? written, int within, out int at)
    {
        StringBuilder made = new();
        int from = 0;
        at = -1;
        foreach (Edit edit in edits.OrderBy(edit => edit.Start).ThenBy(edit => edit.Length))
        {
            made.Append(text, from, edit.Start - from);
            if (edit == written)
            {
                at = made.Length + within;
            }

            made.Append(edit.Text);
            from = edit.Start + edit.Length;
        }

        made.Append(text, from, text.Length - from);
        return made.ToString();
    }

    /// <summary>Characters of the text replaced.</summary>
    private sealed record Edit(int Start, int Length, string Text);

    /// <summary>
    /// A placed element: its number, the element, the type it makes, the placed element it is in, and
    /// the control drawn from it. A part of a control that holds one is placed too: the property
    /// element it is written as, or the control's own where it is not written yet.
    /// </summary>
    private sealed class Spot(int id, MarkupElement element, Type type, int? parent)
    {
        /// <summary>The name of the part of its control this is, where it is one.</summary>
        public string? Part { get; init; }

        /// <summary>Whether a part is written as a property element.</summary>
        public bool Written { get; init; } = true;

        public int Id { get; } = id;

        public MarkupElement Element { get; } = element;

        public Type Type { get; } = type;

        public int? Parent { get; } = parent;

        public Control? Control { get; set; }
    }
}
