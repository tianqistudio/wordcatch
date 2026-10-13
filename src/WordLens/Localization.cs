using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WordLens;

internal enum UiLanguage
{
    SimplifiedChinese,
    English,
}

internal interface ILanguagePreference
{
    UiLanguage Load();
    void Save(UiLanguage language);
}

internal sealed class LocalLanguagePreference : ILanguagePreference
{
    private readonly string DirectoryPath;
    private readonly string FilePath;
    internal LocalLanguagePreference(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TianqiStudio", "WordLens");
        FilePath = Path.Combine(DirectoryPath, "language.txt");
    }

    public UiLanguage Load()
    {
        try
        {
            var value = File.ReadAllText(FilePath).Trim();
            return value.Equals("en", StringComparison.OrdinalIgnoreCase)
                ? UiLanguage.English
                : UiLanguage.SimplifiedChinese;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return UiLanguage.SimplifiedChinese;
        }
    }

    public void Save(UiLanguage language)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, language == UiLanguage.English ? "en\n" : "zh-CN\n");
        File.Move(temporary, FilePath, overwrite: true);
    }
}

/// <summary>
/// Lightweight UI localization for the settings window. Chinese remains the
/// source language so existing persisted values and automation checks remain
/// stable. Runtime-created WPF controls are attached as they are created.
/// </summary>
internal static class Localization
{
    private sealed class ElementState
    {
        internal readonly Dictionary<DependencyProperty, string> Sources = new();
        internal readonly Dictionary<DependencyProperty, string> Rendered = new();
        internal readonly Dictionary<DependencyProperty, EventHandler> Handlers = new();
        internal bool Updating;
        internal bool ItemsHooked;
    }

    private sealed class ColumnState
    {
        internal string? Source;
    }

    private static readonly ConditionalWeakTable<DependencyObject, ElementState> States = new();
    private static readonly ConditionalWeakTable<DataGridColumn, ColumnState> ColumnStates = new();
    private static readonly Dictionary<string, string> ChineseToEnglish = CreateTranslations();
    private static readonly Dictionary<string, string> EnglishToChinese = ChineseToEnglish
        .GroupBy(pair => pair.Value, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

    internal static UiLanguage CurrentLanguage { get; private set; } = UiLanguage.SimplifiedChinese;

    internal static event Action? LanguageChanged;
    internal static void SetLanguage(UiLanguage language)
    {
        CurrentLanguage = language;
        if (Application.Current != null)
            foreach (Window window in Application.Current.Windows) Attach(window);
        LanguageChanged?.Invoke();
    }

    internal static string Translate(string? source)
    {
        if (string.IsNullOrEmpty(source) || CurrentLanguage == UiLanguage.SimplifiedChinese)
            return source ?? string.Empty;
        if (ChineseToEnglish.TryGetValue(source, out var translated)) return translated;
        foreach (var pair in ChineseToEnglish.Where(pair => pair.Key.Contains('{')))
        {
            var tokens = Regex.Matches(pair.Key, @"\{[^{}]+\}").Cast<Match>().ToArray();
            var pattern = "^" + Regex.Escape(pair.Key);
            for (int i = 0; i < tokens.Length; i++)
                pattern = pattern.Replace(Regex.Escape(tokens[i].Value), $"(?<v{i}>.*?)");
            var match = Regex.Match(source, pattern + "$", RegexOptions.Singleline);
            if (!match.Success) continue;
            var result = pair.Value;
            for (int i = 0; i < tokens.Length; i++) result = result.Replace(tokens[i].Value, match.Groups[$"v{i}"].Value);
            return result;
        }
        // Status text often contains a measured value or timestamp. Translate
        // known fragments while preserving the value supplied by the backend.
        foreach (var pair in ChineseToEnglish.OrderByDescending(pair => pair.Key.Length))
            if (pair.Key.Length >= 3 && source.Contains(pair.Key, StringComparison.Ordinal))
                source = source.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        return source;
    }

    internal static void Attach(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>();
        AttachNode(root, visited);
    }

    private static void AttachNode(DependencyObject node, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node)) return;
        // Raw input and dictionary/translation results are data, never UI strings.
        if (node is FrameworkElement raw && raw.Name is "Definition" or "Phonetic" or "Word" or "Query" or "Region" or "ApiKey")
        {
            Watch(node, States.GetOrCreateValue(node), AutomationProperties.NameProperty);
            return;
        }
        var state = States.GetOrCreateValue(node);
        foreach (var property in LocalizedProperties(node))
            Watch(node, state, property);
        if (node is DataGrid grid)
            AttachColumns(grid);
        if (node is ItemsControl items && !state.ItemsHooked)
        {
            state.ItemsHooked = true;
            items.ItemContainerGenerator.StatusChanged += (_, _) =>
            {
                var generated = new HashSet<DependencyObject>();
                for (var i = 0; i < items.Items.Count; i++)
                {
                    if (items.ItemContainerGenerator.ContainerFromIndex(i) is DependencyObject child)
                        AttachNode(child, generated);
                }
            };
        }

        foreach (var child in Children(node))
            AttachNode(child, visited);
    }

    private static IEnumerable<DependencyProperty> LocalizedProperties(DependencyObject node)
    {
        if (node is Window)
            yield return Window.TitleProperty;
        if (node is TextBlock)
            yield return TextBlock.TextProperty;
        if (node is ContentControl)
            yield return ContentControl.ContentProperty;
        if (node is HeaderedContentControl)
            yield return HeaderedContentControl.HeaderProperty;
        if (node is HeaderedItemsControl)
            yield return HeaderedItemsControl.HeaderProperty;
        yield return ToolTipService.ToolTipProperty;
        yield return AutomationProperties.NameProperty;
    }

    private static void Watch(DependencyObject node, ElementState state, DependencyProperty property)
    {
        if (!state.Sources.ContainsKey(property))
        {
            var value = node.GetValue(property);
            if (value is not null && value is not string) return;
            var current = value as string ?? string.Empty;
            state.Sources[property] = SourceFor(current);
            state.Rendered[property] = current;
        }

        if (!state.Handlers.ContainsKey(property))
        {
            try
            {
                var descriptor = DependencyPropertyDescriptor.FromProperty(property, node.GetType());
                if (descriptor is not null)
                {
                    EventHandler handler = (_, _) => Changed(node, state, property);
                    descriptor.AddValueChanged(node, handler);
                    state.Handlers[property] = handler;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                // Some attached properties do not expose a descriptor on every
                // WPF control. They are still translated during each Attach pass.
            }
        }
        Apply(node, state, property);
    }

    private static string SourceFor(string value) => EnglishToChinese.TryGetValue(value, out var source) ? source : value;

    private static void Changed(DependencyObject node, ElementState state, DependencyProperty property)
    {
        if (state.Updating) return;
        var current = node.GetValue(property) as string;
        if (current is null) return;
        if (state.Rendered.TryGetValue(property, out var rendered) && current == rendered) return;
        state.Sources[property] = SourceFor(current);
        Apply(node, state, property);
    }

    private static void Apply(DependencyObject node, ElementState state, DependencyProperty property)
    {
        // Content can change from null/text to a control after initialization.
        // Never replace a newly assigned visual with a cached string.
        if (node.GetValue(property) is not null and not string) return;
        if (!state.Sources.TryGetValue(property, out var source)) return;
        var localized = Translate(source);
        state.Rendered[property] = localized;
        if ((node.GetValue(property) as string) == localized) return;
        state.Updating = true;
        // Localize the displayed value without replacing its binding or style.
        // A later source update must still reach Changed (for example a profile
        // speed slider after switching languages).
        try { node.SetCurrentValue(property, localized); }
        finally { state.Updating = false; }
    }

    private static void AttachColumns(DataGrid grid)
    {
        foreach (var column in grid.Columns)
        {
            if (column.Header is not string header) continue;
            var state = ColumnStates.GetOrCreateValue(column);
            state.Source ??= SourceFor(header);
            var translated = Translate(state.Source);
            if (!Equals(column.Header, translated)) column.Header = translated;
        }
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject node)
    {
        if (node is FrameworkElement element)
        {
            IEnumerable logical;
            try { logical = LogicalTreeHelper.GetChildren(element); }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { logical = Array.Empty<object>(); }
            foreach (var child in logical)
                if (child is DependencyObject dependencyObject) yield return dependencyObject;
        }

        if (node is ItemsControl items)
        {
            for (var i = 0; i < items.Items.Count; i++)
                if (items.ItemContainerGenerator.ContainerFromIndex(i) is DependencyObject child) yield return child;
        }

        var count = 0;
        try { count = VisualTreeHelper.GetChildrenCount(node); }
        catch (InvalidOperationException) { }
        for (var i = 0; i < count; i++)
            yield return VisualTreeHelper.GetChild(node, i);
    }

    private static Dictionary<string, string> CreateTranslations()
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("WordLens.Strings.en.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}
