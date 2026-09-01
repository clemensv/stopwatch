using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StopwatchOverlay;

public sealed class CustomAppBackground
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FileName { get; set; } = "";
}

public sealed record AppBackgroundChoice(
    string Id,
    string DisplayName,
    string? ResourceUri,
    string? FilePath,
    bool IsCustom)
{
    // Custom files can be removed or damaged outside the app. Keep those entries
    // visible in the selector so the user can explicitly remove stale metadata.
    public bool IsAvailable { get; set; } = true;
    public string DisplayLabel => IsAvailable
        ? DisplayName
        : $"{DisplayName} (unavailable)";
    public bool IsThemeDefault => Id == AppBackgroundCatalog.ThemeDefault;
    public override string ToString() => DisplayLabel;
}

public static class AppBackgroundCatalog
{
    public const string ThemeDefault = "theme-default";
    public const string FestiveChalk = "preset:festive-chalk";
    public const string WoodlandMushrooms = "preset:woodland-mushrooms";
    public const string AutumnPatchwork = "preset:autumn-patchwork";
    public const string GreenCreatures = "preset:green-creatures";
    public const string AquaTattoo = "preset:aqua-tattoo";
    public const string SapphireGarden = "preset:sapphire-garden";
    public const string TurquoisePomegranate = "preset:turquoise-pomegranate";
    public const string MidnightPaisley = "preset:midnight-paisley";
    public const string AzureMosaic = "preset:azure-mosaic";

    public const double DefaultPatternStrength = 30;
    public const double MinimumPatternStrength = 10;
    public const double MaximumPatternStrength = 45;

    private const int MaximumCustomBackgrounds = 64;
    private const int MaximumDisplayNameLength = 80;
    private const long MaximumImportBytes = 25L * 1024 * 1024;
    private const int MaximumImageDimension = 8_192;
    private const long MaximumPixelCount = 40_000_000;

    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".bmp"];

    private static readonly AppBackgroundChoice[] BuiltInChoices =
    [
        new(ThemeDefault, "Theme default", null, null, false),
        new(FestiveChalk, "Festive Chalk",
            "pack://application:,,,/StopwatchOverlay;component/Assets/Backgrounds/festive-chalk.jpg", null, false),
        new(WoodlandMushrooms, "Woodland Mushrooms",
            "pack://application:,,,/StopwatchOverlay;component/Assets/Backgrounds/woodland-mushrooms.jpg", null, false),
        new(AutumnPatchwork, "Autumn Patchwork",
            "pack://application:,,,/StopwatchOverlay;component/Assets/Backgrounds/autumn-patchwork.jpg", null, false),
        new(GreenCreatures, "Green Creatures",
            "pack://application:,,,/StopwatchOverlay;component/Assets/Backgrounds/green-creatures.jpg", null, false),
        new(AquaTattoo, "Aqua Tattoo",
            "pack://application:,,,/StopwatchOverlay;component/Assets/Backgrounds/aqua-tattoo.jpg", null, false),
        new(SapphireGarden, "Sapphire Garden",
            "pack://application:,,,/StopwatchOverlay;component/Assets/Backgrounds/sapphire-garden.png", null, false),
        new(TurquoisePomegranate, "Turquoise Pomegranate",
            "pack://application:,,,/StopwatchOverlay;component/Assets/Backgrounds/turquoise-pomegranate.png", null, false),
        new(MidnightPaisley, "Midnight Paisley",
            "pack://application:,,,/StopwatchOverlay;component/Assets/Backgrounds/midnight-paisley.png", null, false),
        new(AzureMosaic, "Azure Mosaic",
            "pack://application:,,,/StopwatchOverlay;component/Assets/Backgrounds/azure-mosaic.png", null, false)
    ];

    public static IReadOnlyList<string> BuiltInIds { get; } =
        BuiltInChoices.Select(choice => choice.Id).ToArray();

    public static string ManagedDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StopwatchOverlay",
        "Backgrounds");

    public static void NormalizeSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.PanelBackgroundStrength = double.IsFinite(settings.PanelBackgroundStrength)
            ? Math.Clamp(
                settings.PanelBackgroundStrength,
                MinimumPatternStrength,
                MaximumPatternStrength)
            : DefaultPatternStrength;

        var normalized = new List<CustomAppBackground>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CustomAppBackground? item in settings.CustomBackgrounds ?? [])
        {
            string normalizedId = Guid.TryParse(item?.Id, out Guid parsed)
                ? parsed.ToString("N")
                : "";
            if (normalized.Count >= MaximumCustomBackgrounds
                || item == null
                || normalizedId.Length == 0
                || !TryNormalizeManagedFileName(item.FileName, out string fileName)
                || !Path.GetFileNameWithoutExtension(fileName).Equals(
                    $"custom-{normalizedId}",
                    StringComparison.OrdinalIgnoreCase)
                || !seenIds.Add(normalizedId)
                || !seenFiles.Add(fileName))
            {
                continue;
            }

            normalized.Add(new CustomAppBackground
            {
                Id = normalizedId,
                DisplayName = NormalizeDisplayName(item.DisplayName),
                FileName = fileName
            });
        }

        settings.CustomBackgrounds = normalized;
        settings.PanelBackgroundId = NormalizeSelection(
            settings.PanelBackgroundId,
            normalized);
    }

    public static string NormalizeSelection(
        string? requestedId,
        IEnumerable<CustomAppBackground>? customBackgrounds)
    {
        string candidate = requestedId?.Trim() ?? "";
        AppBackgroundChoice? builtIn = BuiltInChoices.FirstOrDefault(choice =>
            candidate.Equals(choice.Id, StringComparison.OrdinalIgnoreCase)
            || candidate.Equals(choice.DisplayName, StringComparison.OrdinalIgnoreCase));
        if (builtIn != null)
            return builtIn.Id;

        if (candidate.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
        {
            string requestedCustomId = candidate["custom:".Length..];
            CustomAppBackground? custom = customBackgrounds?.FirstOrDefault(item =>
                item != null
                && requestedCustomId.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            if (custom != null)
                return CustomSelectionId(custom.Id);
        }

        return ThemeDefault;
    }

    public static IReadOnlyList<AppBackgroundChoice> GetAvailableChoices(
        AppSettings settings,
        string? managedDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        NormalizeSettings(settings);
        string root = managedDirectory ?? ManagedDirectory;
        var choices = new List<AppBackgroundChoice>(BuiltInChoices);
        foreach (CustomAppBackground custom in settings.CustomBackgrounds)
        {
            bool resolved = TryResolveManagedPath(root, custom.FileName, out string fullPath);
            var choice = new AppBackgroundChoice(
                CustomSelectionId(custom.Id),
                custom.DisplayName,
                null,
                resolved ? fullPath : null,
                true)
            {
                IsAvailable = resolved && CanReadManagedImage(fullPath)
            };
            choices.Add(choice);
        }

        return choices;
    }

    public static AppBackgroundChoice ResolveChoice(
        AppSettings settings,
        string? managedDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        NormalizeSettings(settings);

        AppBackgroundChoice? builtIn = BuiltInChoices.FirstOrDefault(choice =>
            choice.Id.Equals(settings.PanelBackgroundId, StringComparison.OrdinalIgnoreCase));
        if (builtIn != null)
            return builtIn;

        if (settings.PanelBackgroundId.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
        {
            string requestedId = settings.PanelBackgroundId["custom:".Length..];
            CustomAppBackground? custom = settings.CustomBackgrounds.FirstOrDefault(item =>
                item.Id.Equals(requestedId, StringComparison.OrdinalIgnoreCase));
            string root = managedDirectory ?? ManagedDirectory;
            if (custom != null
                && TryResolveManagedPath(root, custom.FileName, out string fullPath)
                && CanReadManagedImage(fullPath))
            {
                return new AppBackgroundChoice(
                    CustomSelectionId(custom.Id),
                    custom.DisplayName,
                    null,
                    fullPath,
                    true);
            }
        }

        settings.PanelBackgroundId = ThemeDefault;
        return BuiltInChoices[0];
    }

    public static bool TryImport(
        string sourcePath,
        IEnumerable<CustomAppBackground>? existing,
        out CustomAppBackground? imported,
        out string? error,
        string? managedDirectory = null)
    {
        imported = null;
        error = null;
        string? temporaryPath = null;
        string? destinationPath = null;

        try
        {
            if ((existing ?? []).Count(item => item != null) >= MaximumCustomBackgrounds)
            {
                error = $"The background library can contain up to {MaximumCustomBackgrounds} custom images.";
                return false;
            }

            string sourceFullPath = Path.GetFullPath(sourcePath);
            var source = new FileInfo(sourceFullPath);
            if (!source.Exists)
            {
                error = "That image could not be found.";
                return false;
            }

            string extension = source.Extension.ToLowerInvariant();
            if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                error = "Choose a JPG, JPEG, PNG, or BMP image.";
                return false;
            }

            if (source.Length <= 0 || source.Length > MaximumImportBytes)
            {
                error = "Choose an image smaller than 25 MB.";
                return false;
            }

            if (!TryValidateImage(sourceFullPath, extension, out error))
                return false;

            string root = Path.GetFullPath(managedDirectory ?? ManagedDirectory);
            Directory.CreateDirectory(root);

            string id = Guid.NewGuid().ToString("N");
            string fileName = $"custom-{id}{extension}";
            if (!TryResolveManagedPath(root, fileName, out destinationPath))
            {
                error = "The custom background location is not available.";
                return false;
            }

            temporaryPath = destinationPath + ".tmp";
            using (var input = new FileStream(
                       sourceFullPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            using (var output = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       81920,
                       FileOptions.WriteThrough))
            {
                if (input.Length <= 0 || input.Length > MaximumImportBytes)
                    throw new InvalidDataException("The image size changed while it was being imported.");
                CopyWithLimit(input, output, MaximumImportBytes);
                output.Flush(flushToDisk: true);
            }

            // The source may live in a synchronized/network folder and can change
            // between the first validation and the copy. Validate the exact bytes
            // that are about to become the managed background.
            if (!TryValidateImage(temporaryPath, extension, out error))
                return false;

            File.Move(temporaryPath, destinationPath);
            temporaryPath = null;

            imported = new CustomAppBackground
            {
                Id = id,
                DisplayName = MakeUniqueDisplayName(
                    NormalizeDisplayName(Path.GetFileNameWithoutExtension(source.Name)),
                    existing),
                FileName = fileName
            };
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or InvalidOperationException or FormatException
            or OutOfMemoryException or OverflowException)
        {
            error = "This image could not be added. Try a different JPG, PNG, or BMP file.";
            return false;
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); }
                catch { }
            }

            if (imported == null && destinationPath != null)
            {
                try { File.Delete(destinationPath); }
                catch { }
            }
        }
    }

    public static bool DeleteManagedCopy(
        CustomAppBackground custom,
        string? managedDirectory = null)
    {
        string root = managedDirectory ?? ManagedDirectory;
        if (!TryResolveManagedPath(root, custom.FileName, out string path))
            return false;

        try
        {
            if (!File.Exists(path))
                return true;

            File.Delete(path);
            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public static string CustomSelectionId(string id) => $"custom:{id}";

    private static BitmapDecoder CreateDecoder(
        Stream stream,
        string extension,
        BitmapCreateOptions createOptions,
        BitmapCacheOption cacheOption) => extension switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapDecoder(stream, createOptions, cacheOption),
            ".png" => new PngBitmapDecoder(stream, createOptions, cacheOption),
            ".bmp" => new BmpBitmapDecoder(stream, createOptions, cacheOption),
            _ => throw new NotSupportedException("Unsupported image type.")
        };

    private static bool TryValidateImage(
        string path,
        string extension,
        out string? error)
    {
        error = null;
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        BitmapDecoder metadataDecoder = CreateDecoder(
            stream,
            extension,
            BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.None);
        BitmapFrame frame = metadataDecoder.Frames[0];
        if (frame.PixelWidth <= 0
            || frame.PixelHeight <= 0
            || frame.PixelWidth > MaximumImageDimension
            || frame.PixelHeight > MaximumImageDimension
            || (long)frame.PixelWidth * frame.PixelHeight > MaximumPixelCount)
        {
            error = "Choose an image no larger than 8,192 pixels per side or 40 megapixels.";
            return false;
        }

        // Rewind and make the matching codec cache the complete frame so truncated
        // files fail here instead of on the next application start.
        stream.Position = 0;
        BitmapDecoder validationDecoder = CreateDecoder(
            stream,
            extension,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        _ = validationDecoder.Frames[0].PixelWidth;
        return true;
    }

    private static bool CanReadManagedImage(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists
                || file.Length <= 0
                || file.Length > MaximumImportBytes)
            {
                return false;
            }

            string extension = file.Extension.ToLowerInvariant();
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            BitmapDecoder decoder = CreateDecoder(
                stream,
                extension,
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            BitmapFrame frame = decoder.Frames[0];
            return frame.PixelWidth > 0
                   && frame.PixelHeight > 0
                   && frame.PixelWidth <= MaximumImageDimension
                   && frame.PixelHeight <= MaximumImageDimension
                   && (long)frame.PixelWidth * frame.PixelHeight <= MaximumPixelCount;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or InvalidOperationException or FormatException
            or OutOfMemoryException or OverflowException)
        {
            return false;
        }
    }

    private static void CopyWithLimit(Stream input, Stream output, long maximumBytes)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maximumBytes)
                throw new InvalidDataException("The image exceeded the import size limit.");
            output.Write(buffer, 0, read);
        }
    }

    private static string MakeUniqueDisplayName(
        string requested,
        IEnumerable<CustomAppBackground>? existing)
    {
        var used = new HashSet<string>(
            BuiltInChoices.Select(choice => choice.DisplayName)
                .Concat((existing ?? [])
                    .Where(item => item != null)
                    .Select(item => item.DisplayName)),
            StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(requested))
            return requested;

        for (int suffix = 2; suffix < 10_000; suffix++)
        {
            string suffixText = $" ({suffix})";
            int prefixLength = Math.Max(
                1,
                MaximumDisplayNameLength - suffixText.Length);
            string candidate = requested[..Math.Min(requested.Length, prefixLength)] + suffixText;
            if (!used.Contains(candidate))
                return candidate;
        }

        return "Custom background";
    }

    private static string NormalizeDisplayName(string? name)
    {
        string cleaned = new((name ?? "")
            .Where(character => !char.IsControl(character))
            .ToArray());
        cleaned = cleaned.Trim();
        if (cleaned.Length == 0)
            cleaned = "Custom background";
        return cleaned[..Math.Min(cleaned.Length, MaximumDisplayNameLength)];
    }

    private static bool TryNormalizeManagedFileName(
        string? requested,
        out string fileName)
    {
        fileName = Path.GetFileName(requested ?? "");
        string extension = Path.GetExtension(fileName);
        return fileName.Length > 0
               && fileName.Equals(requested, StringComparison.Ordinal)
               && SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryResolveManagedPath(
        string root,
        string? requestedFileName,
        out string fullPath)
    {
        fullPath = "";
        if (!TryNormalizeManagedFileName(requestedFileName, out string fileName))
            return false;

        string fullRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, fileName));
        if (!string.Equals(
                Path.GetDirectoryName(candidate),
                fullRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }
}

public static class AppBackgroundManager
{
    private const int MaximumCachedImages = 12;
    private static readonly Dictionary<string, BitmapSource> ImageCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> ImageCacheOrder = new();

    private static string? _baseTheme;
    private static Brush? _baseBackground;
    private static BitmapSource? _currentPattern;
    private static double _currentStrength = AppBackgroundCatalog.DefaultPatternStrength;

    public static bool HasPattern => _currentPattern != null;

    internal static void InvalidateThemeBase()
    {
        _baseTheme = null;
        _baseBackground = null;
        _currentPattern = null;
    }

    public static bool Apply(AppSettings settings, out string? warning)
    {
        ArgumentNullException.ThrowIfNull(settings);
        warning = null;
        var application = Application.Current;
        if (application == null)
            return false;

        AppBackgroundCatalog.NormalizeSettings(settings);
        string theme = AppThemeCatalog.Normalize(settings.ThemeMode);
        if (_baseBackground == null || _baseTheme != theme)
        {
            _baseBackground = application.Resources["AppBackgroundBrush"] is Brush current
                ? current.Clone()
                : new SolidColorBrush(Colors.Transparent);
            _baseTheme = theme;
        }

        string requestedId = settings.PanelBackgroundId;
        AppBackgroundChoice choice = AppBackgroundCatalog.ResolveChoice(settings);
        if (choice.IsThemeDefault
            && !requestedId.Equals(
                AppBackgroundCatalog.ThemeDefault,
                StringComparison.OrdinalIgnoreCase))
        {
            warning = "The saved custom background is missing; Theme default is being used.";
        }
        _currentStrength = settings.PanelBackgroundStrength;
        _currentPattern = null;

        DrawingBrush next;
        if (choice.IsThemeDefault)
        {
            next = EnsureDrawingBrush(_baseBackground.Clone());
        }
        else
        {
            try
            {
                _currentPattern = LoadImage(choice);
                next = CreateTiledBrush(
                    _baseBackground.Clone(),
                    _currentPattern,
                    _currentStrength,
                    opacity: 1);
            }
            catch
            {
                warning = $"{choice.DisplayName} could not be loaded; Theme default is being used.";
                settings.PanelBackgroundId = AppBackgroundCatalog.ThemeDefault;
                next = EnsureDrawingBrush(_baseBackground.Clone());
            }
        }

        ApplyApplicationBrush(application, next);
        return warning == null;
    }

    public static Brush CreatePreviewBrush(
        AppBackgroundChoice choice,
        double strength)
    {
        if (choice.IsThemeDefault || !choice.IsAvailable)
            return _baseBackground?.Clone() ?? new SolidColorBrush(Colors.Transparent);

        try
        {
            BitmapSource image = LoadImage(choice);
            DrawingBrush brush = CreateTiledBrush(
                _baseBackground?.Clone() ?? new SolidColorBrush(Colors.Transparent),
                image,
                strength,
                opacity: 1);
            double previewScale = Math.Min(
                54d / brush.Viewbox.Width,
                38d / brush.Viewbox.Height);
            brush.Viewport = new Rect(
                0,
                0,
                Math.Max(4, brush.Viewbox.Width * previewScale),
                Math.Max(4, brush.Viewbox.Height * previewScale));
            brush.Freeze();
            return brush;
        }
        catch
        {
            return _baseBackground?.Clone() ?? new SolidColorBrush(Colors.Transparent);
        }
    }

    public static Brush CreateOverlaySurfaceBrush(Color baseColor, double opacity)
    {
        double clampedOpacity = Math.Clamp(opacity, 0, 1);
        if (_currentPattern == null)
        {
            return new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(clampedOpacity * 255),
                baseColor.R,
                baseColor.G,
                baseColor.B));
        }

        DrawingBrush brush = CreateTiledBrush(
            new SolidColorBrush(Color.FromRgb(baseColor.R, baseColor.G, baseColor.B)),
            _currentPattern,
            _currentStrength,
            clampedOpacity);
        brush.Freeze();
        return brush;
    }

    public static void ClearImageCache()
    {
        ImageCache.Clear();
        ImageCacheOrder.Clear();
    }

    private static BitmapSource LoadImage(AppBackgroundChoice choice)
    {
        string key = choice.ResourceUri ?? choice.FilePath
            ?? throw new InvalidOperationException("The background has no image source.");
        if (ImageCache.TryGetValue(key, out BitmapSource? cached))
        {
            ImageCacheOrder.Remove(key);
            ImageCacheOrder.AddLast(key);
            return cached;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        if (choice.FilePath != null)
        {
            using var stream = new FileStream(
                choice.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            BitmapDecoder metadata = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            BitmapFrame frame = metadata.Frames[0];
            if (frame.PixelWidth >= frame.PixelHeight)
                image.DecodePixelWidth = 768;
            else
                image.DecodePixelHeight = 768;
        }
        else
        {
            // Bound decoded preset size while preserving the source aspect ratio.
            image.DecodePixelHeight = 768;
        }
        image.UriSource = new Uri(key, UriKind.RelativeOrAbsolute);
        image.EndInit();
        image.Freeze();

        while (ImageCacheOrder.Count >= MaximumCachedImages)
        {
            string oldest = ImageCacheOrder.First!.Value;
            ImageCacheOrder.RemoveFirst();
            ImageCache.Remove(oldest);
        }
        ImageCache[key] = image;
        ImageCacheOrder.AddLast(key);
        return image;
    }

    private static DrawingBrush CreateTiledBrush(
        Brush baseBrush,
        BitmapSource image,
        double strength,
        double opacity)
    {
        (double width, double height) = CalculateTileSize(image.PixelWidth, image.PixelHeight);
        var bounds = new Rect(0, 0, width, height);
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(
            baseBrush,
            null,
            new RectangleGeometry(bounds)));

        var imageLayer = new DrawingGroup
        {
            Opacity = Math.Clamp(strength, 0, 100) / 100d
        };
        imageLayer.Children.Add(new ImageDrawing(image, bounds));
        drawing.Children.Add(imageLayer);

        return new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = bounds,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = bounds,
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            Opacity = opacity
        };
    }

    private static (double Width, double Height) CalculateTileSize(int pixelWidth, int pixelHeight)
    {
        double width = Math.Max(1, pixelWidth);
        double height = Math.Max(1, pixelHeight);
        double scale = Math.Min(360d / width, 520d / height);
        return (Math.Max(96, width * scale), Math.Max(96, height * scale));
    }

    private static DrawingBrush EnsureDrawingBrush(Brush brush)
    {
        if (brush is DrawingBrush drawingBrush)
            return drawingBrush;

        var bounds = new Rect(0, 0, 1, 1);
        return new DrawingBrush(new GeometryDrawing(
            brush,
            null,
            new RectangleGeometry(bounds)))
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = bounds,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = bounds
        };
    }

    private static void ApplyApplicationBrush(Application application, DrawingBrush next)
    {
        if (application.Resources["AppBackgroundBrush"] is DrawingBrush current
            && !current.IsFrozen)
        {
            current.Drawing = next.Drawing?.Clone();
            current.TileMode = next.TileMode;
            current.ViewportUnits = next.ViewportUnits;
            current.Viewport = next.Viewport;
            current.ViewboxUnits = next.ViewboxUnits;
            current.Viewbox = next.Viewbox;
            current.Stretch = next.Stretch;
            current.AlignmentX = next.AlignmentX;
            current.AlignmentY = next.AlignmentY;
            current.Opacity = next.Opacity;
        }
        else
        {
            application.Resources["AppBackgroundBrush"] = next;
        }
    }
}
