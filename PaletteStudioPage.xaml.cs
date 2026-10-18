using System.Globalization;
using System.Text.Json;
using EzPocket.Controls;
using EzPocket.Models;
using EzPocket.Services;

namespace EzPocket;

public partial class PaletteStudioPage : ContentPage
{
    private readonly PocketSelectionService selection;
    private readonly AssetService assets;
    private readonly AssetImportSelectionService imports;
    private readonly IFolderPickerService folders;
    private readonly IColorPickerService colorPicker;
    private readonly PaletteStudioLibrary library = new();
    private readonly List<PaletteTile> tiles = [];
    private readonly Dictionary<string, StudioPalette> favorites = [];
    private readonly Entry[] colorEntries = new Entry[4];
    private readonly EzButton[] swatches = new EzButton[4];
    private readonly Border[] hexBorders = new Border[4];
    private bool pickingColor;
    private StudioPalette current = new("Paper", ".gbp", [0xFFFFFF, 0xAAAAAA, 0x555555, 0x151515]);
    private StudioPalette original = null!;
    private uint[] comparison = [0xFFFFFF, 0xAAAAAA, 0x555555, 0x151515];
    private PaletteScreenshot? screenshot = PalettePreviewService.CalibrationPattern();
    private byte[]? referencePreview;
    private bool updating;
    private bool active;
    private bool initialized;
    private bool packLoading;
    private int group;

    public PaletteStudioPage()
    {
        InitializeComponent();
        var services = IPlatformApplication.Current?.Services;
        selection = services?.GetService<PocketSelectionService>() ?? new();
        assets = services?.GetService<AssetService>() ?? new();
        imports = services?.GetService<AssetImportSelectionService>() ?? new();
        folders = services?.GetService<IFolderPickerService>() ?? new UnsupportedFolderPickerService();
        colorPicker = services?.GetService<IColorPickerService>() ?? new MauiColorPickerService();
        PaletteList.ItemTemplate = new DataTemplate(CreateTile);
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            swatches[i] = new EzButton { Text = "EDIT COLOR", MinimumHeight = 54, FontSize = 11, ButtonPadding = new Thickness(8, 6) };
            swatches[i].Clicked += async (_, _) => await ChooseColorAsync(index);
            SemanticProperties.SetDescription(swatches[i], $"Choose color for shade {i + 1}");
            ToolTipProperties.SetText(swatches[i], "Open color picker");
            colorEntries[i] = new Entry { MaxLength = 7, FontSize = 14, TextColor = Color.FromArgb("#151515"), BackgroundColor = Color.FromArgb("#F4F4F0"), Placeholder = "#RRGGBB" };
            SemanticProperties.SetDescription(colorEntries[i], $"Shade {i + 1} hex color, editable");
            colorEntries[i].TextChanged += (_, _) => OnColorChanged(index);
            colorEntries[i].Focused += (_, _) => UpdateHexBorder(index);
            colorEntries[i].Unfocused += (_, _) => UpdateHexBorder(index);
            hexBorders[i] = new Border { Stroke = Color.FromArgb("#90908B"), StrokeThickness = 1, BackgroundColor = Color.FromArgb("#F4F4F0"), Padding = new Thickness(8, 0), Content = colorEntries[i] };
            var column = new VerticalStackLayout { Spacing = 4 };
            column.Children.Add(new Label { Text = i == 0 ? "LIGHT" : i == 3 ? "DARK" : $"SHADE {i + 1}", FontSize = 10, TextColor = Color.FromArgb("#60605C") });
            column.Children.Add(new Border { Stroke = Color.FromArgb("#C9C9C2"), StrokeThickness = 1, Content = swatches[i] });
            column.Children.Add(new Label { Text = "HEX", FontSize = 10, TextColor = Color.FromArgb("#60605C"), Margin = new Thickness(0, 4, 0, 0) });
            column.Children.Add(hexBorders[i]);
            ColorEditors.Add(column, i);
        }
        original = current.Copy();
        LoadEditor();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        active = true;
        if (!initialized)
        {
            initialized = true;
            Add(current, "STARTER");
            Add(new("Phosphor", ".gbp", [0xE3F2C0, 0x9AB66F, 0x496848, 0x172C2B]), "STARTER");
            Add(new("Amber", ".gbp", [0xFFF0CC, 0xD9B16F, 0x97623C, 0x302422]), "STARTER");
            try
            {
                foreach (var palette in library.LoadFavorites()) { favorites[PaletteStudioLibrary.Key(palette)] = palette; Add(palette, "FAVORITE"); }
            }
            catch (Exception ex) when (IsFileError(ex) || ex is JsonException)
            { Status.Text = "Saved favorites could not be read. Your palette files have not changed."; }
            if (selection.SelectedPocket is PocketDrive pocket)
            {
                int skipped = 0;
                foreach (var asset in assets.Scan(pocket))
                {
                    try { Add(PaletteStudioLibrary.ReadFile(Path.Combine(pocket.RootPath, asset.RelativePath)), "INSTALLED"); }
                    catch (Exception ex) when (IsFileError(ex)) { skipped++; }
                }
                if (skipped > 0) Status.Text = $"Skipped {skipped} unreadable or unsupported installed palettes.";
            }
            RefreshList();
        }
        string? success = imports.ConsumeSuccess();
        if (success is not null) Status.Text = success;
        UpdateActions();
    }

    protected override void OnDisappearing() { active = false; base.OnDisappearing(); }

    private View CreateTile()
    {
        var name = new Label { FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#151515") };
        name.SetBinding(Label.TextProperty, nameof(PaletteTile.Name));
        var source = new Label { FontSize = 10, TextColor = Color.FromArgb("#60605C") };
        source.SetBinding(Label.TextProperty, nameof(PaletteTile.Detail));
        var strip = new Grid { ColumnDefinitions = new ColumnDefinitionCollection(new( GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)), HeightRequest = 24 };
        for (int i = 0; i < 4; i++)
        {
            var box = new BoxView();
            box.SetBinding(BoxView.ColorProperty, $"Color{i}");
            strip.Add(box, i);
        }
        return new VerticalStackLayout { Padding = new Thickness(6, 10), Spacing = 5, Children = { name, source, strip } };
    }

    private void Add(StudioPalette palette, string source)
    {
        string key = PaletteStudioLibrary.Key(palette);
        if (tiles.Any(tile => tile.Key == key)) return;
        tiles.Add(new(palette.Copy(), source, key));
    }

    private void RefreshList()
    {
        if (!initialized) return;
        string query = Search.Text?.Trim() ?? "";
        var visible = tiles.Where(tile => tile.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            && (!FavoritesOnly.IsToggled || favorites.ContainsKey(tile.Key))).ToArray();
        PaletteList.ItemsSource = visible;
        LibrarySummary.Text = $"{visible.Length} shown · {tiles.Count} in this session";
        UpdateActions();
    }

    private void OnPaletteSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PaletteTile tile) return;
        current = tile.Palette.Copy(); original = current.Copy(); group = 0;
        LoadEditor();
        Status.Text = "Editing a local copy. Favorite or export to keep changes; installation requires review.";
    }

    private void LoadEditor()
    {
        updating = true;
        PaletteName.Text = current.Name;
        GroupPicker.ItemsSource = current.Extension == ".pal" ? new[] { "Background", "Sprites 0", "Sprites 1", "Window" } : new[] { "All colors" };
        GroupPicker.SelectedIndex = group;
        FormatSummary.Text = current.Extension == ".pal" ? "APGB .pal · Separate groups and LCD-off color are preserved." : "GBP .gbp · Four colors for compatible GB cores.";
        for (int i = 0; i < 4; i++)
        {
            string hex = $"#{current.Colors[group * 4 + i]:X6}";
            colorEntries[i].Text = hex;
            colorEntries[i].TextColor = Color.FromArgb("#151515");
            UpdateSwatch(i, current.Colors[group * 4 + i]);
            UpdateHexBorder(i);
        }
        updating = false;
        RenderPreview();
        UpdateActions();
    }

    private bool ValidColors => colorEntries.All(entry => TryColor(entry.Text, out _));
    private static bool TryColor(string? text, out uint color) => uint.TryParse((text ?? "").Trim().TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color)
        && (text ?? "").Trim().TrimStart('#').Length == 6;

    private void OnColorChanged(int index)
    {
        if (updating) return;
        bool valid = TryColor(colorEntries[index].Text, out uint color);
        colorEntries[index].TextColor = Color.FromArgb(valid ? "#151515" : "#A52A2A");
        if (valid)
        {
            current.Colors[group * 4 + index] = color;
            UpdateSwatch(index, color);
            RenderPreview();
        }
        UpdateActions();
        UpdateHexBorder(index);
    }

    private void UpdateSwatch(int index, uint color)
    {
        swatches[index].ButtonBackgroundColor = Color.FromArgb($"#{color:X6}");
        double luminance = ((color >> 16) * .2126 + ((color >> 8) & 255) * .7152 + (color & 255) * .0722) / 255;
        swatches[index].ButtonTextColor = luminance > .55 ? Colors.Black : Colors.White;
    }

    private void UpdateHexBorder(int index)
    {
        if (hexBorders[index] is null) return;
        bool valid = TryColor(colorEntries[index].Text, out _);
        hexBorders[index].Stroke = Color.FromArgb(!valid ? "#A52A2A" : colorEntries[index].IsFocused ? "#151515" : "#90908B");
        hexBorders[index].StrokeThickness = colorEntries[index].IsFocused ? 2 : 1;
    }

    private async Task ChooseColorAsync(int index)
    {
        if (pickingColor) return;
        pickingColor = true;
        var palette = current;
        int colorIndex = group * 4 + index;
        uint before = palette.Colors[colorIndex];
        string previousText = colorEntries[index].Text;
        void Show(uint color)
        {
            if (!ReferenceEquals(palette, current)) return;
            colorEntries[index].Text = $"#{color:X6}";
        }
        try
        {
            uint? chosen = await colorPicker.PickAsync(before, Show);
            Show(chosen ?? before);
            if (chosen is null && ReferenceEquals(palette, current)) colorEntries[index].Text = previousText;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            Show(before);
            if (ReferenceEquals(palette, current)) colorEntries[index].Text = previousText;
            Status.Text = "Could not open the color picker. You can still edit the HEX field below each swatch.";
        }
        finally { pickingColor = false; }
    }

    private void OnNameChanged(object? sender, TextChangedEventArgs e) { if (!updating) { current = current with { Name = e.NewTextValue ?? "" }; UpdateActions(); } }
    private void OnGroupChanged(object? sender, EventArgs e) { if (updating || GroupPicker.SelectedIndex < 0) return; group = GroupPicker.SelectedIndex; LoadEditor(); }
    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => RefreshList();
    private void OnFavoritesChanged(object? sender, ToggledEventArgs e) => RefreshList();
    private void OnReverseClicked(object? sender, EventArgs e) { Array.Reverse(current.Colors, group * 4, 4); LoadEditor(); }
    private void OnResetClicked(object? sender, EventArgs e) { current = original.Copy(); LoadEditor(); }
    private void OnCompareClicked(object? sender, EventArgs e) { comparison = current.Group(group); referencePreview = null; CompareLabel.Text = $"REFERENCE · {current.Name}"; RenderPreview(); }

    private void UpdateActions()
    {
        if (ReviewButton is null || colorEntries.Any(entry => entry is null)) return;
        bool valid = ValidColors && !string.IsNullOrWhiteSpace(current.Name);
        ReviewButton.IsEnabled = valid && selection.SelectedPocket is not null;
        ExportButton.IsEnabled = valid;
        FavoriteButton.IsEnabled = valid;
        PackButton.IsEnabled = !packLoading && selection.SelectedPocket is not null;
        FavoriteButton.Text = favorites.ContainsKey(PaletteStudioLibrary.Key(current)) ? "★ FAVORITED" : "☆ FAVORITE";
        TargetNote.Text = selection.SelectedPocket is PocketDrive pocket ? $"Install target: {pocket.Name}. Review before any files change." : "Connect a target from the dashboard to browse the community pack or review an install. Editing and export work without one.";
    }

    private async void OnOpenClicked(object? sender, EventArgs e)
    {
        try
        {
            var files = await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = "Open .gbp or .pal palettes" });
            if (!active) return;
            int added = 0, skipped = 0;
            StudioPalette? last = null;
            foreach (var file in files.OfType<FileResult>())
            {
                try
                {
                    using var stream = await file.OpenReadAsync();
                    byte[] bytes = new byte[57];
                    int length = await stream.ReadAtLeastAsync(bytes, 57, false);
                    var palette = PaletteCodec.Read(Path.GetFileNameWithoutExtension(file.FileName), Path.GetExtension(file.FileName), bytes[..length]);
                    Add(palette, "OPENED FILE"); last = palette; added++;
                }
                catch (Exception ex) when (IsFileError(ex)) { skipped++; }
            }
            if (!active) return;
            if (last is not null) { current = last.Copy(); original = current.Copy(); group = 0; LoadEditor(); }
            RefreshList();
            Status.Text = $"Opened {added} palettes. Skipped {skipped} unsupported or unreadable files.";
        }
        catch (Exception ex) when (IsFileError(ex)) { Status.Text = "Could not open the selected palettes."; }
    }

    private async void OnPackClicked(object? sender, EventArgs e)
    {
        if (packLoading || selection.SelectedPocket is not PocketDrive pocket) return;
        packLoading = true; PackButton.IsBusy = true; UpdateActions();
        AssetImportPreview? catalog = null;
        try
        {
            Status.Text = "Downloading community palettes for browsing. Nothing is installed.";
            catalog = await Task.Run(() => assets.PreparePalettePackAsync(pocket));
            if (!active) return;
            int skipped = 0;
            foreach (var candidate in catalog.Changes)
            {
                try { Add(PaletteStudioLibrary.ReadFile(candidate.SourcePath), "COMMUNITY PACK"); }
                catch (Exception ex) when (IsFileError(ex)) { skipped++; }
            }
            RefreshList();
            Status.Text = $"Community pack ready. {skipped} unsupported files skipped. Select a palette to preview it.";
        }
        catch (Exception ex) when (IsFileError(ex) || ex is HttpRequestException) { if (active) Status.Text = "Could not load the community pack. Check your connection and retry."; }
        finally { if (catalog is not null) assets.Cleanup(catalog); packLoading = false; PackButton.IsBusy = false; UpdateActions(); }
    }

    private async void OnScreenshotClicked(object? sender, EventArgs e)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Open your Game Boy screenshot", FileTypes = FilePickerFileType.Images });
            if (file is null || !active) return;
            using var stream = await file.OpenReadAsync();
            var loaded = await Task.Run(() => PalettePreviewService.Load(stream));
            if (!active) return;
            screenshot = loaded;
            referencePreview = null;
            PreviewNote.Text = $"{file.FileName} · Four-shade approximation · processed locally";
            RenderPreview();
        }
        catch (Exception ex) when (IsFileError(ex) || ex is ArgumentException) { Status.Text = ex is InvalidDataException ? ex.Message : "Could not open this screenshot. Choose a PNG or JPEG image."; }
    }

    private void RenderPreview()
    {
        if (screenshot is null) return;
        byte[] left = referencePreview ??= PalettePreviewService.Render(screenshot, comparison);
        byte[] right = PalettePreviewService.Render(screenshot, current.Group(group));
        ReferenceImage.Source = ImageSource.FromStream(() => new MemoryStream(left));
        CurrentImage.Source = ImageSource.FromStream(() => new MemoryStream(right));
    }

    private void OnFavoriteClicked(object? sender, EventArgs e)
    {
        string key = PaletteStudioLibrary.Key(current);
        var next = new Dictionary<string, StudioPalette>(favorites);
        bool removed = next.Remove(key);
        if (!removed) next[key] = current.Copy();
        try
        {
            library.SaveFavorites(next.Values);
            favorites.Clear(); foreach (var pair in next) favorites.Add(pair.Key, pair.Value);
            Add(current, "FAVORITE"); RefreshList();
            Status.Text = removed ? "Removed from favorites. Source files are unchanged." : "Saved this palette to local favorites.";
        }
        catch (Exception ex) when (IsFileError(ex)) { Status.Text = "Could not save favorites. Try exporting the palette instead."; }
    }

    private async void OnReviewClicked(object? sender, EventArgs e)
    {
        if (!ValidColors || selection.SelectedPocket is not PocketDrive pocket) return;
        string? staging = null;
        try
        {
            string fileName = PaletteStudioLibrary.FileName(current.Name, current.Extension);
            staging = Path.Combine(FileSystem.CacheDirectory, "EzPocket", "studio", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            string path = Path.Combine(staging, fileName);
            File.WriteAllBytes(path, PaletteCodec.Write(current));
            var preview = assets.PreparePaletteImport(pocket, [path]) with { StagingPath = staging, Kind = "Palette Studio" };
            if (!preview.CanApply) { Status.Text = preview.Blockers.Count > 0 ? string.Join(" ", preview.Blockers) : "This palette is already installed with the same name and colors."; assets.Cleanup(preview); return; }
            imports.SetPreview(preview);
            await AppNavigation.GoToAsync("AssetReviewPage");
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            if (staging is not null) assets.Cleanup(new(pocket, [], [], staging));
            Status.Text = ex is InvalidDataException ? ex.Message : "Could not prepare this palette for review.";
        }
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        if (!ValidColors) return;
        try
        {
            var palette = current.Copy();
            string name = PaletteStudioLibrary.FileName(palette.Name, palette.Extension);
            string? folder = await folders.PickFolderAsync();
            if (folder is null || !active) return;
            PaletteStudioLibrary.Export(palette, folder, selection.SelectedPocket?.RootPath);
            Status.Text = $"Exported {name}. Keep it with your palette collection.";
        }
        catch (Exception ex) when (IsFileError(ex)) { Status.Text = ex is InvalidDataException ? ex.Message : "Export did not complete. Choose another name or writable folder; existing files are never overwritten."; }
    }

    private async void OnWebClicked(object? sender, EventArgs e)
    {
        try { await Launcher.Default.OpenAsync("https://www.nortakales.com/page/gbpEditor"); }
        catch (Exception ex) when (ex is FeatureNotSupportedException or InvalidOperationException) { Status.Text = "The browser could not open. The editor is at nortakales.com/page/gbpEditor."; }
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await AppNavigation.GoToAsync("..");

    private void OnWorkspaceSizeChanged(object? sender, EventArgs e)
    {
        bool narrow = Workspace.Width < 850;
        if (Workspace.ColumnDefinitions.Count == (narrow ? 1 : 2)) return;
        Workspace.ColumnDefinitions = narrow ? new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star)) : new ColumnDefinitionCollection(new ColumnDefinition(new GridLength(300)), new ColumnDefinition(GridLength.Star));
        Workspace.RowDefinitions = narrow ? new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)) : new RowDefinitionCollection(new RowDefinition(GridLength.Auto));
        Grid.SetColumn(Editor, narrow ? 0 : 1); Grid.SetRow(Editor, narrow ? 1 : 0);
    }

    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException or NotSupportedException;

    private sealed record PaletteTile(StudioPalette Palette, string Source, string Key)
    {
        public string Name => Palette.Name;
        public string Detail => $"{Source} · {Palette.Extension.ToUpperInvariant()}";
        public Color Color0 => Color.FromArgb($"#{Palette.Colors[0]:X6}");
        public Color Color1 => Color.FromArgb($"#{Palette.Colors[1]:X6}");
        public Color Color2 => Color.FromArgb($"#{Palette.Colors[2]:X6}");
        public Color Color3 => Color.FromArgb($"#{Palette.Colors[3]:X6}");
    }
}
