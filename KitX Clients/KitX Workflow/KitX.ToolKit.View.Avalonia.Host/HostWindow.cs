using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.View;
using KitX.ToolKit.View.Avalonia;

namespace KitX.ToolKit.View.Avalonia.Host;

/// <summary>
/// The window: a list on the left, the card on the right.
/// </summary>
/// <remarks>
/// <para><b>Thin on purpose.</b> Every decision worth asserting lives in <see cref="HostSession"/>;
/// this class arranges controls and forwards clicks. The one thing it owns that the session does not
/// is thread affinity — a message pushed from the server arrives on whatever thread the store
/// raised it on, and touching a control from there throws. <see cref="Dispatcher.UIThread"/> is the
/// only correct answer and it belongs where the controls are.</para>
/// </remarks>
internal sealed class HostWindow : Window
{
    private readonly HostSession _session;
    private readonly ListBox _list = new();
    private readonly SurfaceCard _card = new();
    private readonly TextBlock _status = new();

    public HostWindow(HostSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        Title = "KitX · Surface 客户端";
        Width = 720;
        Height = 940;
        Background = new SolidColorBrush(Color.Parse("#F3F3F3"));

        Content = Compose();

        // The renderer ships English defaults for the confirmation dialog, on purpose: a host that
        // overrides nothing still gets a readable dialog rather than a blank one (KitX.axaml:93-97).
        // This host's own text is Chinese, so leaving them alone made the one dialog that appears
        // before an irreversible action the only English on the screen. Overriding them here is the
        // mechanism the theme documents; nothing in the renderer changes.
        Resources["KitXDangerTitle"] = "确定要清空吗？";
        Resources["KitXDangerBody"] = "这一操作无法撤销。";
        Resources["KitXDangerConfirm"] = "清空";
        Resources["KitXDangerCancel"] = "保留";

        _session.MessageApplied += (_, message) => OnMessage(message);

        // The card raises; this window is what carries it to the server. Without this line the
        // window is one-way — it could show every delta and still be unable to send one, and
        // `SomethingHappened` had no production subscriber at all until it was added here. The
        // gesture arrives on the UI thread, which is exactly where an `await` is safe: it returns
        // to the dispatcher rather than blocking it, so the click does not freeze the card.
        _card.SomethingHappened += async (_, raised) => await RaiseAsync(raised);

        Opened += async (_, _) => await StartAsync();
    }

    /// <summary>Sends one gesture to the server, or reports why it could not be turned into a command.</summary>
    /// <remarks>
    /// <b>An <c>async void</c> handler that throws takes the process down.</b> This is reached from a
    /// framework event, so nothing above it can catch; the try/catch here is the only backstop, and
    /// the status line is where a failure has to become visible, because a click that silently does
    /// nothing is indistinguishable from a card that refuses to respond.
    /// </remarks>
    private async Task RaiseAsync(SurfaceEvent raised)
    {
        var command = SurfaceEventCommand.ToCommand(raised);

        if (command is null)
        {
            // Not an event at all. The renderer only raises `event:` kinds today, so this means a
            // renderer bug — reporting it beats throwing, which would look like a channel failure.
            _status.Text = $"无法把「{raised.Kind}」变成一条命令。";
            return;
        }

        try
        {
            var error = await _session.SendAsync(command);

            _status.Text = error ?? $"已发送 {command.EventName}";
        }
        catch (Exception ex)
        {
            _status.Text = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private Control Compose()
    {
        var frame = new Border
        {
            Child = _card,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var right = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24),
            Children = { frame, _status },
        };

        _list.SelectionChanged += async (_, _) =>
        {
            if (_list.SelectedItem is ToolkitSummary row)
                await ConnectAsync(row.Id);
        };

        // Without a template the list renders `ToolkitSummary { Id = ..., ... }`: the record's
        // compiler-generated ToString. That is not "no styling" — it is the type's debug
        // representation shown to a person. The name is what the config calls the ToolKit; the id is
        // the directory name, and it belongs in a tooltip rather than in the row.
        _list.ItemTemplate = new FuncDataTemplate<ToolkitSummary>((row, _) => new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(row.Name) ? row.Id : row.Name,
        });

        var left = new DockPanel { Width = 240, Margin = new Thickness(16) };

        DockPanel.SetDock(_list, Dock.Top);
        left.Children.Add(new TextBlock
        {
            Text = "已挂载的 ToolKit",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        });
        left.Children.Add(_list);

        var split = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);

        split.Children.Add(left);
        split.Children.Add(right);

        return split;
    }

    /// <summary>Mount-first, then list. See <see cref="HostSession"/> for why that order is fixed.</summary>
    private async Task StartAsync()
    {
        try
        {
            var (id, created) = await _session.EnsureSampleAsync();
            _session.SeedSampleSurface(id);

            var (rows, error) = await _session.ListAsync();

            _list.ItemsSource = rows;

            if (error is not null)
            {
                _status.Text = error;
                return;
            }

            if (rows.Count == 0)
            {
                // Nothing to draw is a state, not a failure. A blank window reads as a crash.
                _status.Text = "没有已挂载的 ToolKit。";
                return;
            }

            _status.Text = created
                ? $"已创建并挂载示例 ToolKit：{id}"
                : $"已挂载：{id}";

            _list.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            // A start-up failure that only reaches stderr is a window that never appears and no
            // explanation. The point of this host is to make the chain visible, including when it
            // breaks.
            _status.Text = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task ConnectAsync(string toolkitId)
    {
        var error = await _session.ConnectAsync(toolkitId);

        if (error is not null)
            _status.Text = error;
    }

    private void OnMessage(ViewMessage message)
    {
        // The session folds the projection on the server's thread; everything to do with a control
        // has to happen here, on the UI thread, and that includes the very first paint.
        //
        // The `surface` case is the one that needs more than a forward: a surface message replaces
        // the whole layout (Surface.md:818) and carries no values, so a card that just rebuilt its
        // cells has nothing to show. The projection still holds the values from the earlier
        // welcome, so they are re-sent after the rebuild.
        if (Dispatcher.UIThread.CheckAccess())
        {
            Forward(message);
            return;
        }

        Dispatcher.UIThread.Post(() => Forward(message));
    }

    /// <summary>Applies one message to the card. UI thread only.</summary>
    private void Forward(ViewMessage message)
    {
        try
        {
            _card.Apply(message);

            if (message is SurfaceMessage)
            {
                foreach (var (key, value) in _session.View.Values)
                    _card.SetValue(key, value);
            }
        }
        catch (Exception ex)
        {
            // A renderer that refuses a message is exactly the disagreement this host exists to
            // surface. Swallowing it would make a blank card look like a slow server.
            _status.Text = $"渲染失败：{ex.GetType().Name}: {ex.Message}";
        }
    }
}
