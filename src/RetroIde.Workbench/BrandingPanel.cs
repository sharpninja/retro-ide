using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIde.Application;

namespace RetroIde.Workbench;

internal sealed class BrandingPanel : Border
{
    internal const string ProductName = "Retro IDE";
    internal static Uri Repository { get; } = new("https://github.com/sharpninja/retro-ide");
    private static readonly Lazy<Bitmap> Logo = new(LoadLogo);
    private static readonly IBrush Canvas = Brush.Parse("#0B1424");
    private static readonly IBrush Card = Brush.Parse("#142239");
    private static readonly IBrush Outline = Brush.Parse("#314763");
    private static readonly IBrush Heading = Brush.Parse("#F0F6FF");
    private static readonly IBrush Body = Brush.Parse("#B2C4DC");
    private static readonly IBrush Link = Brush.Parse("#63EBD9");

    internal BrandingPanel(WorkbenchLocalization localization, ILogger logger, bool startup = false, Func<Uri, Task<bool>>? launch = null)
    {
        Background = Canvas;
        var surface = startup ? "branding.splash" : "branding.start";
        AutomationProperties.SetAutomationId(this, surface);
        var mark = new Image { Source = Logo.Value, Width = 148, Height = 148, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = ProductName, FontSize = 44, FontWeight = FontWeight.Bold, Foreground = Heading };
        var description = new TextBlock { FontSize = 15, Foreground = Body, TextWrapping = TextWrapping.Wrap };
        var status = new TextBlock { Foreground = Body, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var repository = new HyperlinkButton
        {
            Content = Repository.AbsoluteUri, Foreground = Link, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 4),
            FlowDirection = FlowDirection.LeftToRight,
        };
        AutomationProperties.SetAutomationId(repository, $"{surface}.repository");
        localization.Observe(description, () => description.Text = localization.Text(WorkbenchText.BrandingDescription));
        localization.Observe(status, () => status.Text = localization.Text(WorkbenchText.BrandingRepositoryUnavailable));
        localization.Observe(repository, () => ToolTip.SetTip(repository, localization.Text(WorkbenchText.BrandingOpenRepository)));
        repository.Command = new AsyncRelayCommand(async () =>
        {
            status.IsVisible = false;
            try
            {
                var opened = launch is not null
                    ? await launch(Repository).ConfigureAwait(true)
                    : TopLevel.GetTopLevel(repository) is { } top &&
                      await top.Launcher.LaunchUriAsync(Repository).ConfigureAwait(true);
                repository.IsVisited = opened;
                status.IsVisible = !opened;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (RuntimeFailures.CanRecover(error))
            {
                RuntimeFailures.Report(logger, error, "open Retro IDE repository", LogLevel.Warning);
                status.IsVisible = true;
            }
        });
        var details = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        details.Children.AddRange([title, description, repository, status]);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 28 };
        Grid.SetColumn(details, 1);
        content.Children.AddRange([mark, details]);
        Child = new Border
        {
            Background = Card, BorderBrush = Outline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
            Padding = new Thickness(30), Margin = new Thickness(20), MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Child = content,
        };
    }

    internal static WindowIcon CreateIcon() => new(Logo.Value);

    private static Bitmap LoadLogo()
    {
        using var stream = typeof(BrandingPanel).Assembly.GetManifestResourceStream("RetroIde.Branding.Icon") ??
            throw new InvalidDataException("The bundled Retro IDE logo resource is missing.");
        return new Bitmap(stream);
    }
}
