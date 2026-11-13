using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
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
    /// <summary>Width of the mounted-ToolKit list on the left, in device-independent pixels.</summary>
    private const double SidebarWidth = 240;
    private const double SidebarMargin = 16;
    private const double CardMargin = 24;

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
        // The reference window is the card plus the padding around it, on the theme's subtle
        // background (Shots/Program.cs:248-249, :283-286). That window has no sidebar, so its width is
        // 390 + 24 + 24 = 438; this one does, and copying 438 verbatim clipped the card instead of
        // matching anything. The width is therefore derived from the three parts that are actually
        // on screen, so that adding a fourth later fails to compile rather than fails to look right.
        Background = null;
        SetDynamic(this, TemplatedControl.BackgroundProperty, "KitXSubtleBg");
        Width = SidebarWidth + (SidebarMargin * 2) + SurfaceMetrics.CardWidth + (CardMargin * 2);
        Height = SurfaceMetrics.CardHeight + (CardMargin * 2) + 48;

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

    /// <summary>
    /// Sets <paramref name="property"/> from a theme resource, so a host that redefines the key wins.
    /// </summary>
    /// <remarks>
    /// <b>A binding, not a lookup.</b> Asking the application for a brush by name returns nothing:
    /// the theme's colours live in dictionaries resolved per control against that control's own
    /// variant, which is the same reason Shots' <c>Themed</c> binds rather than reads. A one-shot
    /// lookup here leaves the frame transparent in both themes and the failure looks like "the
    /// border was never drawn", which is exactly the defect this method was added to fix.
    /// </remarks>
    private static void SetDynamic(AvaloniaObject target, AvaloniaProperty property, string key)
        => target.Bind(property, new DynamicResourceExtension(key));

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
        // The frame is the same three resources Shots uses to draw the reference images
        // (SurfaceCard/../Shots/Program.cs:243-249): background, border brush, corner radius, all
        // read through DynamicResource so a host can replace them the way KitX.axaml:14-17 documents.
        //
        // The previous version set BorderThickness=1 and CornerRadius=8 with no BorderBrush, which
        // draws a transparent line: the 1px edge the value asked for was never on screen. Hard-coding
        // 8 instead of KitXRadiusCard had the same shape — a value that looked like it came from the
        // theme and did not.
        var frame = new Border
        {
            Child = _card,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };

        SetDynamic(frame, Border.BackgroundProperty, "KitXCardBg");
        SetDynamic(frame, Border.BorderBrushProperty, "KitXCardBorder");
        SetDynamic(frame, Border.CornerRadiusProperty, "KitXRadiusCard");

        var right = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(CardMargin),
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

        // Header above the list, list filling what is left. The previous version added the header
        // first and docked only the list, so the header took the default Dock.Left and the two were
        // laid out side by side: the caption was clipped to "已挂载的 ToolK" and the list got 152 of
        // the sidebar's 240 pixels. Measured, not guessed — the header measured 88 wide.
        var header = new TextBlock
        {
            Text = "已挂载的 ToolKit",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var left = new DockPanel { Width = SidebarWidth, Margin = new Thickness(SidebarMargin) };
        DockPanel.SetDock(header, Dock.Top);
        left.Children.Add(header);
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

            // Before connecting, so `welcome` already carries them. A value written afterwards
            // arrives as a delta and the first frame is still blank — the flicker is not the bug,
            // but the blank first frame is the thing a person sees.
            _session.SeedSampleValues(id);

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
