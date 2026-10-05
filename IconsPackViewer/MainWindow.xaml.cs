using MahApps.Metro.IconPacks;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace IconBrowser;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly List<IconPackViewModel> _allPacks = new();

    public ObservableCollection<IconPackViewModel> Packs { get; }
        = new();


    // ================================================================
    // SEARCH
    // ================================================================

    private string _searchText = string.Empty;

    public string SearchText
    {
        get => _searchText;

        set
        {
            if (_searchText == value)
                return;

            _searchText = value;

            OnPropertyChanged(nameof(SearchText));

            Refresh();
        }
    }


    // ================================================================
    // ICON SIZE
    // ================================================================

    private double _iconSize = 48;

    public double IconSize
    {
        get => _iconSize;

        set
        {
            if (Math.Abs(_iconSize - value) < 0.01)
                return;

            _iconSize = value;

            OnPropertyChanged(nameof(IconSize));
        }
    }


    // ================================================================
    // STATUS
    // ================================================================

    private string _statusText = string.Empty;

    public string StatusText
    {
        get => _statusText;

        private set
        {
            _statusText = value;

            OnPropertyChanged(nameof(StatusText));
        }
    }


    // ================================================================
    // COMMANDS
    // ================================================================

    public ICommand CopyCommand { get; }

    public ICommand TogglePackCommand { get; }


    // ================================================================
    // CONSTRUCTOR
    // ================================================================

    public MainWindow()
    {
        InitializeComponent();

        CopyCommand =
            new RelayCommand<IconViewModel>(CopyIcon);

        TogglePackCommand =
            new RelayCommand<IconPackViewModel>(TogglePack);

        DataContext = this;

        LoadIconPacks();
    }


    // ================================================================
    // FIND ICONPACK ASSEMBLY
    // ================================================================
    private static List<Assembly> FindIconPackAssemblies()
    {
        var result = new List<Assembly>();

        // Сначала берем уже загруженные сборки
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name;

            if (name != null &&
                name.StartsWith("MahApps.Metro.IconPacks", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(assembly);
            }
        }

        // Затем ищем DLL непосредственно рядом с exe.
        // Это важно: NuGet-пакеты отдельных наборов могут еще
        // не быть загружены CLR.
        var baseDirectory = AppContext.BaseDirectory;

        foreach (var file in Directory.EnumerateFiles(
                     baseDirectory,
                     "MahApps.Metro.IconPacks*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(file);

                if (!result.Any(a =>
                        string.Equals(
                            a.FullName,
                            assembly.FullName,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(assembly);
                }
            }
            catch
            {
                // Игнорируем DLL, которые не удалось загрузить
            }
        }

        return result;
    }

    private static Assembly? FindIconPackAssembly()
    {
        return typeof(PackIconBase).Assembly;
    }

    //private static Assembly? FindIconPackAssembly()
    //{
    //    /*
    //     * Мы не используем typeof(PackIconMaterial),
    //     * typeof(PackIconFontAwesome) и т.д.
    //     *
    //     * Вместо этого ищем загруженную сборку,
    //     * имя которой содержит IconPacks.
    //     */

    //    var assembly = AppDomain.CurrentDomain
    //        .GetAssemblies()
    //        .FirstOrDefault(a =>
    //            a.GetName()
    //             .Name?
    //             .Contains(
    //                 "MahApps.Metro.IconPacks",
    //                 StringComparison.OrdinalIgnoreCase)
    //             == true);

    //    return assembly;
    //}


    // ================================================================
    // LOAD ALL PACKS
    // ================================================================

    private void LoadIconPacks()
    {
        var assemblies = FindIconPackAssemblies();

        if (assemblies.Count == 0)
        {
            StatusText = "Не удалось найти сборки MahApps.Metro.IconPacks";
            return;
        }

        var types = assemblies
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch
                {
                    return Array.Empty<Type>();
                }
            })
            .ToList();

        var packTypes = types
            .Where(IsIconPackType)
            .OrderBy(t => t.Name)
            .ToList();


        foreach (var type in packTypes)
        {
            try
            {
                var pack = CreatePack(type);

                if (pack != null)
                    _allPacks.Add(pack);
            }
            catch
            {
                /*
                 * Один проблемный набор не должен
                 * ломать загрузку остальных.
                 */
            }
        }


        foreach (var pack in _allPacks)
        {
            Packs.Add(pack);
        }


        StatusText =
            $"Найдено наборов: {_allPacks.Count}    |    " +
            $"Всего иконок: {_allPacks.Sum(x => x.TotalCount)}";
    }


    // ================================================================
    // CHECK PACK TYPE
    // ================================================================

    private static bool IsIconPackType(Type type)
    {
        if (!type.IsClass)
            return false;

        if (type.IsAbstract)
            return false;

        if (!type.Name.StartsWith("PackIcon"))
            return false;


        var kindProperty = type.GetProperty(
            "Kind",
            BindingFlags.Public |
            BindingFlags.Instance);


        if (kindProperty == null)
            return false;


        return kindProperty.PropertyType.IsEnum;
    }


    // ================================================================
    // CREATE PACK
    // ================================================================

    private static IconPackViewModel? CreatePack(Type packType)
    {
        var kindProperty = packType.GetProperty(
            "Kind",
            BindingFlags.Public |
            BindingFlags.Instance);


        if (kindProperty == null)
            return null;


        var values =
            Enum.GetValues(kindProperty.PropertyType)
                .Cast<object>()
                .ToList();


        if (values.Count == 0)
            return null;


        var pack = new IconPackViewModel
        {
            Name = FormatPackName(packType.Name),
            TypeName = packType.Name,
            IconType = packType,
            KindProperty = kindProperty,
            TotalCount = values.Count
        };


        /*
         * Сначала пытаемся подобрать
         * репрезентативные иконки.
         */

        var selected = SelectRepresentativeIcons(values);


        /*
         * Если подходящих названий мало,
         * добиваем список обычными элементами enum.
         */

        foreach (var value in values)
        {
            if (selected.Contains(value))
                continue;

            selected.Add(value);

            if (selected.Count >= 10)
                break;
        }


        foreach (var kind in selected.Take(10))
        {
            var icon = CreateIcon(
                packType,
                kindProperty,
                kind);

            if (icon == null)
                continue;


            pack.AllIcons.Add(
                new IconViewModel
                {
                    KindName = kind.ToString()!,
                    Icon = icon,
                    PackType = packType
                });
        }


        /*
         * Первоначально показываем только 10.
         */

        foreach (var icon in pack.AllIcons)
            pack.Icons.Add(icon);


        return pack;
    }


    // ================================================================
    // REPRESENTATIVE ICONS
    // ================================================================

    private static List<object> SelectRepresentativeIcons(
        List<object> values)
    {
        /*
         * Это не строгий список.
         *
         * Мы проверяем различные варианты написания,
         * потому что разные IconPack используют
         * разные названия:
         *
         * Home
         * House
         * HomeOutline
         * HomeOutline
         * HomeSolid
         * ...
         */

        string[] preferred =
        {
            "Home",
            "House",

            "User",
            "Person",
            "Account",

            "Search",
            "Magnify",

            "Settings",
            "Cog",
            "Gear",

            "Heart",

            "Star",

            "Bell",
            "Notification",

            "Check",
            "Checkmark",

            "Close",
            "X",

            "Menu",
            "Bars"
        };


        var result = new List<object>();


        foreach (var keyword in preferred)
        {
            var match = values.FirstOrDefault(
                value =>
                    value.ToString()?
                        .Equals(
                            keyword,
                            StringComparison.OrdinalIgnoreCase)
                    == true);


            if (match != null &&
                !result.Contains(match))
            {
                result.Add(match);
            }


            if (result.Count >= 10)
                break;
        }


        return result;
    }


    // ================================================================
    // CREATE ICON INSTANCE
    // ================================================================

    private static object? CreateIcon(
        Type packType,
        PropertyInfo kindProperty,
        object kind)
    {
        try
        {
            var icon =
                Activator.CreateInstance(packType);

            if (icon == null)
                return null;


            kindProperty.SetValue(icon, kind);

            return icon;
        }
        catch
        {
            return null;
        }
    }


    // ================================================================
    // NAME
    // ================================================================

    private static string FormatPackName(string name)
    {
        if (name.StartsWith("PackIcon"))
            name = name["PackIcon".Length..];


        /*
         * Небольшое улучшение читаемости.
         */

        return name switch
        {
            "FontAwesome" => "Font Awesome",
            "Material" => "Material Design",
            "MaterialDesign" => "Material Design",
            "BootstrapIcons" => "Bootstrap Icons",
            "BoxIcons" => "Boxicons",
            "Octicons" => "Octicons",
            "Ionicons" => "Ionicons",
            "Lucide" => "Lucide",
            "FeatherIcons" => "Feather",
            "PhosphorIcons" => "Phosphor",
            "RemixIcon" => "Remix Icon",

            _ => name
        };
    }


    // ================================================================
    // TOGGLE FULL PACK
    // ================================================================

    private static void TogglePack(IconPackViewModel pack)
    {
        if (pack == null)
            return;


        if (pack.IsExpanded)
        {
            /*
             * Возвращаем первые 10.
             */

            pack.Icons.Clear();

            foreach (var icon in pack.AllIcons.Take(10))
                pack.Icons.Add(icon);

            pack.IsExpanded = false;
        }
        else
        {
            /*
             * Загружаем все иконки.
             */

            if (pack.AllIcons.Count == pack.TotalCount)
            {
                pack.Icons.Clear();

                foreach (var icon in pack.AllIcons)
                    pack.Icons.Add(icon);
            }
            else
            {
                LoadAllIcons(pack);
            }

            pack.IsExpanded = true;
        }


        pack.OnPropertyChanged(nameof(pack.ExpandButtonText));
    }


    // ================================================================
    // LOAD ALL ICONS
    // ================================================================

    private static void LoadAllIcons(IconPackViewModel pack)
    {
        if (pack.IconType == null ||
            pack.KindProperty == null)
            return;


        var values =
            Enum.GetValues(pack.KindProperty.PropertyType)
                .Cast<object>();


        pack.AllIcons.Clear();


        foreach (var kind in values)
        {
            var icon =
                CreateIcon(
                    pack.IconType,
                    pack.KindProperty,
                    kind);


            if (icon == null)
                continue;


            pack.AllIcons.Add(
                new IconViewModel
                {
                    KindName = kind.ToString()!,
                    Icon = icon,
                    PackType = pack.IconType
                });
        }


        pack.Icons.Clear();

        foreach (var icon in pack.AllIcons)
            pack.Icons.Add(icon);
    }


    // ================================================================
    // SEARCH
    // ================================================================

    private void Refresh()
    {
        Packs.Clear();


        string search = SearchText.Trim();


        /*
         * Пустой поиск:
         * показываем все наборы.
         */

        if (string.IsNullOrWhiteSpace(search))
        {
            foreach (var pack in _allPacks)
            {
                pack.ShowPreview();

                Packs.Add(pack);
            }

            UpdateStatus();

            return;
        }


        foreach (var pack in _allPacks)
        {
            /*
             * Если совпало имя набора,
             * показываем весь набор.
             */

            if (pack.Name.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase))
            {
                pack.ShowPreview();

                Packs.Add(pack);

                continue;
            }


            /*
             * Ищем совпадения среди названий иконок.
             */

            var matches =
                pack.AllIcons
                    .Where(icon =>
                        icon.KindName.Contains(
                            search,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();


            if (matches.Count == 0)
                continue;


            var filtered =
                new IconPackViewModel
                {
                    Name = pack.Name,
                    TypeName = pack.TypeName,
                    IconType = pack.IconType,
                    KindProperty = pack.KindProperty,
                    TotalCount = pack.TotalCount
                };


            foreach (var icon in matches)
            {
                filtered.AllIcons.Add(icon);
                filtered.Icons.Add(icon);
            }


            filtered.IsSearchResult = true;

            Packs.Add(filtered);
        }


        UpdateStatus();
    }


    // ================================================================
    // COPY XAML
    // ================================================================

    private void CopyIcon(IconViewModel icon)
    {
        if (icon == null)
            return;


        string packName =
            icon.PackType.Name;


        if (packName.StartsWith("PackIcon"))
            packName = packName["PackIcon".Length..];


        string xaml =
            $"<iconPacks:PackIcon{packName} " +
            $"Kind=\"{icon.KindName}\" />";


        Clipboard.SetText(xaml);


        StatusText =
            $"Скопировано: {xaml}";
    }


    // ================================================================
    // STATUS
    // ================================================================

    private void UpdateStatus()
    {
        int visibleIcons =
            Packs.Sum(x => x.Icons.Count);


        StatusText =
            $"Наборов: {Packs.Count}    |    " +
            $"Иконок отображается: {visibleIcons}";
    }


    // ================================================================
    // PROPERTY CHANGED
    // ================================================================

    public event PropertyChangedEventHandler?
        PropertyChanged;


    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}


/*
 * ====================================================================
 * ICON PACK VIEW MODEL
 * ====================================================================
 */

public class IconPackViewModel : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public Type? IconType { get; set; }

    public PropertyInfo? KindProperty { get; set; }

    public int TotalCount { get; set; }


    public ObservableCollection<IconViewModel> AllIcons { get; }
        = new();


    public ObservableCollection<IconViewModel> Icons { get; }
        = new();


    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;

        set
        {
            _isExpanded = value;

            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(ExpandButtonText));
        }
    }


    public bool IsSearchResult { get; set; }


    public string ExpandButtonText =>
        IsExpanded
            ? "Свернуть"
            : $"Показать все ({TotalCount})";


    public void ShowPreview()
    {
        IsExpanded = false;

        Icons.Clear();

        foreach (var icon in AllIcons.Take(10))
            Icons.Add(icon);
    }


    public event PropertyChangedEventHandler?
        PropertyChanged;


    public void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}


/*
 * ====================================================================
 * ICON VIEW MODEL
 * ====================================================================
 */

public class IconViewModel
{
    public string KindName { get; set; } = string.Empty;

    public object Icon { get; set; } = null!;

    public Type PackType { get; set; } = null!;
}


/*
 * ====================================================================
 * RELAY COMMAND
 * ====================================================================
 */

public class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;


    public RelayCommand(Action<T> execute)
    {
        _execute = execute;
    }


    public event EventHandler? CanExecuteChanged;


    public bool CanExecute(object? parameter)
    {
        return parameter is T;
    }


    public void Execute(object? parameter)
    {
        if (parameter is T value)
            _execute(value);
    }
}