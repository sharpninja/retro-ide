using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

internal sealed class GitRemoteDialog : Window
{
    internal GitRemoteDialog(GitHistoryViewModel view)
    {
        Width = 660;
        Height = 480;
        MinWidth = 455;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        view.Localization.Observe(this, () => Title = view.Localization.Text(WorkbenchText.GitRemoteTitle));

        var list = new ListBox
        {
            Height = 155,
            ItemTemplate = new FuncDataTemplate<GitRemoteOperations.Remote>((remote, _) => remote is null ? null : RemoteRow(remote)),
        };
        AutomationProperties.SetAutomationId(list, "git.remotes.list");
        AutomationProperties.SetName(list, view.Localization.Text(WorkbenchText.GitRemoteTitle));
        list.Bind(ItemsControl.ItemsSourceProperty, BindingFor("RemoteSettings.Remotes"));
        list.Bind(ListBox.SelectedItemProperty, BindingFor(nameof(view.SelectedRemote), BindingMode.TwoWay));

        var fields = new StackPanel { Spacing = 5 };
        AddField(WorkbenchText.GitRemoteName, nameof(view.RemoteName), "git.remotes.name");
        AddField(WorkbenchText.GitRemoteUrl, nameof(view.RemoteUrl), "git.remotes.url");
        var push = new CheckBox();
        view.Localization.Observe(push, () => push.Content = view.Localization.Text(WorkbenchText.GitRemotePush));
        push.Bind(CheckBox.IsCheckedProperty, BindingFor(nameof(view.EditPushUrl), BindingMode.TwoWay));
        fields.Children.Add(push);
        var actions = new WrapPanel { Children =
        {
            Button(WorkbenchCommands.RefreshGitRemotes, view.RefreshRemotesCommand),
            Button(WorkbenchCommands.AddGitRemote, view.AddRemoteCommand),
            Button(WorkbenchCommands.EditGitRemote, view.EditRemoteCommand),
            Button(WorkbenchCommands.RenameGitRemote, view.RenameRemoteCommand),
            Button(WorkbenchCommands.RemoveGitRemote, view.RemoveRemoteCommand),
            Button(WorkbenchCommands.FetchGitRemote, view.FetchRemoteCommand),
        } };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        result.Bind(TextBlock.TextProperty, BindingFor("LastResult.Summary"));
        var close = new Button { IsCancel = true };
        AutomationProperties.SetAutomationId(close, "git.remotes.close");
        view.Localization.Observe(close, () => close.Content = view.Localization.Text(WorkbenchText.GitRemoteClose));
        close.Click += (_, _) => Close();
        var content = new StackPanel { Margin = new Thickness(14), Spacing = 8,
            Children = { list, fields, actions, Divergence(view), result, close } };
        Content = view.WrapRemoteSurface(new ScrollViewer { Content = content });

        Binding BindingFor(string path, BindingMode mode = BindingMode.OneWay) => new(path) { Source = view, Mode = mode };

        void AddField(string slug, string property, string id)
        {
            var caption = new TextBlock();
            var input = new TextBox();
            AutomationProperties.SetAutomationId(input, id);
            view.Localization.Observe(caption, () =>
            {
                caption.Text = view.Localization.Text(slug);
                AutomationProperties.SetName(input, caption.Text);
            });
            input.Bind(TextBox.TextProperty, BindingFor(property, BindingMode.TwoWay));
            fields.Children.Add(caption);
            fields.Children.Add(input);
        }

        Button Button(CommandDescriptor descriptor, ICommand command)
        {
            KeyBindings.Add(new KeyBinding { Command = command, Gesture = KeyGesture.Parse(descriptor.EffectiveGesture) });
            return CommandPresentation.CreateButton(descriptor, command, view.Localization);
        }
    }

    private static Control RemoteRow(GitRemoteOperations.Remote remote) => new StackPanel
    {
        Children = { new TextBlock { Text = remote.Name, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = string.Join(" · ", remote.FetchUrls.Concat(remote.PushUrls)), TextWrapping = TextWrapping.Wrap } },
    };

    private static Control Divergence(GitHistoryViewModel view)
    {
        var text = new TextBlock();
        AutomationProperties.SetAutomationId(text, "git.remotes.divergence");
        void Update()
        {
            text.Text = view.LastFetchStatus is { Ahead: { } a, Behind: { } b }
                ? WorkbenchText.GitRemoteDivergence(view.Localization, a, b)
                : null;
        }
        System.ComponentModel.PropertyChangedEventHandler changed = (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(view.LastFetchStatus), StringComparison.Ordinal))
                Update();
        };
        text.AttachedToVisualTree += (_, _) => view.PropertyChanged += changed;
        text.DetachedFromVisualTree += (_, _) => view.PropertyChanged -= changed;
        view.Localization.Observe(text, Update);
        return text;
    }
}
