using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace OctoglowSender;

public sealed partial class HubPage
{
    private async Task ShowAddDevicePickerAsync()
    {
        var root = XamlRoot;
        if (_pickerOpen || root is null) return;
        _pickerOpen = true;

        var scanCancellation = new CancellationTokenSource();
        var scanToken = scanCancellation.Token;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var candidateList = new StackPanel { Spacing = 8 };
        var emptyState = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12),
        };
        var emptyHost = new Grid { Children = { emptyState } };
        emptyState.Children.Add(new ProgressRing
        {
            Width = 36, Height = 36, IsActive = true,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        emptyState.Children.Add(new TextBlock
        {
            Text = AppStrings.Get("hub.lookingNearby"),
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        emptyState.Children.Add(new TextBlock
        {
            Text = AppStrings.Get("hub.searchTip"),
            Opacity = 0.7,
            MaxWidth = 450,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        title.Children.Add(new FontIcon
        {
            Glyph = "\uE710", FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
        });
        title.Children.Add(new TextBlock
        {
            Text = AppStrings.Get("hub.addDeviceQuestion"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var manualButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 9,
                Children =
                {
                    new FontIcon { Glyph = "\uE710", FontSize = 16 },
                    new TextBlock { Text = AppStrings.Get("hub.manualWifi") },
                },
            },
            Padding = new Thickness(14, 9, 14, 9),
        };
        var cancelButton = new Button
        {
            Content = AppStrings.Get("hub.cancel"),
            MinWidth = 100,
            Padding = new Thickness(14, 9, 14, 9),
        };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { manualButton, cancelButton },
        };

        var contents = new StackPanel { Spacing = 18 };
        contents.Children.Add(title);
        contents.Children.Add(new TextBlock
        {
            Text = AppStrings.Get("hub.chooseClock"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.76,
        });
        var listScroller = new ScrollViewer
        {
            MaxHeight = 390,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = candidateList,
        };
        contents.Children.Add(listScroller);
        contents.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(ActualTheme == ElementTheme.Light
                ? ColorHelper.FromArgb(255, 224, 224, 224) : ColorHelper.FromArgb(255, 62, 62, 62)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(-24, 0, -24, 0),
        });
        contents.Children.Add(footer);

        var card = new Border
        {
            Background = new SolidColorBrush(ActualTheme == ElementTheme.Light
                ? ColorHelper.FromArgb(255, 250, 250, 250) : ColorHelper.FromArgb(255, 42, 42, 42)),
            BorderBrush = new SolidColorBrush(ActualTheme == ElementTheme.Light
                ? ColorHelper.FromArgb(255, 218, 218, 218) : ColorHelper.FromArgb(255, 70, 70, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24),
            Child = contents,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Shadow = new ThemeShadow(),
        };
        var overlay = new Grid
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(170, 0, 0, 0)),
            RequestedTheme = ActualTheme,
            Children = { card },
        };
        var popup = new Popup { XamlRoot = root, Child = overlay };
        var signingIn = false;
        var closed = false;

        void Fit()
        {
            overlay.Width = root.Size.Width;
            overlay.Height = root.Size.Height;
            card.Width = Math.Clamp(root.Size.Width - 48, 320, 670);
            card.MaxHeight = Math.Max(300, root.Size.Height - 48);
            candidateList.Width = card.Width - 48;
            listScroller.Height = Math.Clamp(root.Size.Height - 300, 150, 300);
            listScroller.MaxHeight = listScroller.Height;
            emptyHost.Height = listScroller.Height;
        }

        void RenderCandidates()
        {
            if (closed || signingIn) return;
            candidateList.Children.Clear();
            var nearby = App.DeviceHub.NearbyClocks;
            var other = App.DeviceHub.OtherClocks;
            if (nearby.Count > 0) AddGroup(AppStrings.Get("hub.nearbyDevices"), nearby);
            if (other.Count > 0) AddGroup(AppStrings.Get("hub.otherDevices"), other);
            if (nearby.Count == 0 && other.Count == 0)
                candidateList.Children.Add(emptyHost);

            void AddGroup(string heading, IReadOnlyList<DeviceDescriptor> devices)
            {
                candidateList.Children.Add(new TextBlock
                {
                    Text = heading,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 6, 0, 2),
                });
                foreach (var device in devices)
                {
                    var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                    labels.Children.Add(new TextBlock
                    {
                        Text = device.DisplayName,
                        FontWeight = FontWeights.SemiBold,
                    });
                    labels.Children.Add(new TextBlock
                    {
                        Text = device.Detail,
                        FontSize = 12,
                        Opacity = 0.68,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                    var row = new Grid { ColumnSpacing = 14, MinHeight = 64 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new Image
                    {
                        Source = PickerClockImage,
                        Width = 82,
                        Height = 54,
                        Stretch = Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                    Grid.SetColumn(labels, 1);
                    row.Children.Add(labels);
                    var arrow = new FontIcon
                    {
                        Glyph = "\uE76C",
                        FontSize = 13,
                        Opacity = 0.72,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                    };
                    Grid.SetColumn(arrow, 2);
                    row.Children.Add(arrow);
                    var button = new Button
                    {
                        Content = row,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Padding = new Thickness(12, 8, 16, 8),
                        IsEnabled = device.CanSignIn,
                        CornerRadius = new CornerRadius(8),
                    };
                    button.Click += async (_, _) => await SignInAsync(device);
                    candidateList.Children.Add(button);
                }
            }
        }

        void CandidatesChanged(object? sender, EventArgs args) =>
            DispatcherQueue.TryEnqueue(RenderCandidates);
        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Fit();
        void PageUnloaded(object sender, RoutedEventArgs args) => ClosePicker();
        void ClosePicker()
        {
            if (closed) return;
            closed = true;
            scanCancellation.Cancel();
            App.DeviceHub.CandidatesChanged -= CandidatesChanged;
            root.Changed -= RootChanged;
            Unloaded -= PageUnloaded;
            popup.IsOpen = false;
            scanCancellation.Dispose();
            _pickerOpen = false;
            completion.TrySetResult();
        }

        async Task SignInAsync(DeviceDescriptor device)
        {
            if (signingIn || closed) return;
            signingIn = true;
            try
            {
                if (await ShowClockPairingDialogAsync(device))
                {
                    ClosePicker();
                    if (App.MainWindow is MainWindow window)
                    {
                        var paired = App.DeviceHub.Devices.FirstOrDefault(item => item.Id == device.Id) ?? device;
                        window.NavigateToDevice(paired);
                    }
                }
            }
            finally
            {
                signingIn = false;
                if (!closed) RenderCandidates();
            }
        }

        async Task ScanUntilClosedAsync()
        {
            try
            {
                while (!scanToken.IsCancellationRequested)
                {
                    await App.DeviceHub.RefreshAsync(scanNearby: true);
                    await Task.Delay(TimeSpan.FromSeconds(5), scanToken);
                }
            }
            catch (OperationCanceledException) when (scanToken.IsCancellationRequested)
            {
            }
        }

        manualButton.Click += async (_, _) =>
        {
            if (signingIn || closed) return;
            signingIn = true;
            try
            {
                var manualDevice = await ShowManualDeviceDialogAsync();
                if (manualDevice is not null)
                {
                    signingIn = false;
                    await SignInAsync(manualDevice);
                }
            }
            finally
            {
                signingIn = false;
                if (!closed) RenderCandidates();
            }
        };
        cancelButton.Click += (_, _) => ClosePicker();
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape || signingIn) return;
            args.Handled = true;
            ClosePicker();
        };

        Fit();
        root.Changed += RootChanged;
        Unloaded += PageUnloaded;
        App.DeviceHub.CandidatesChanged += CandidatesChanged;
        RenderCandidates();
        popup.IsOpen = true;
        cancelButton.Focus(FocusState.Programmatic);
        _ = ScanUntilClosedAsync();
        await completion.Task;
    }

    private async Task<DeviceDescriptor?> ShowManualDeviceDialogAsync()
    {
        if (XamlRoot is null) return null;
        var address = new TextBox
        {
            Header = AppStrings.Get("connection.ip"),
            PlaceholderText = "192.168.1.100",
        };
        var port = new NumberBox
        {
            Header = AppStrings.Get("connection.port"),
            Value = 80,
            Minimum = 1,
            Maximum = 65535,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var error = new TextBlock
        {
            Text = AppStrings.Get("clock.invalidAddressMessage"),
            Foreground = new SolidColorBrush(Colors.OrangeRed),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var body = new StackPanel { Width = 320, Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = AppStrings.Get("hub.manualClockDescription"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
        });
        body.Children.Add(address);
        body.Children.Add(port);
        body.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = DialogHeading.Create("\uE710", AppStrings.Get("hub.addClockTitle")),
            Content = body,
            PrimaryButtonText = AppStrings.Get("hub.continue"),
            CloseButtonText = AppStrings.Get("hub.cancel"),
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ActualTheme,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 384.0;
        DeviceDescriptor? selected = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var enteredPort = double.IsNaN(port.Value) ? 0 : (int)port.Value;
            if (enteredPort is >= 1 and <= 65535 &&
                DeskClockDiscoveryService.TryNormalizeConfiguredAddress(address.Text, enteredPort, out var endpoint))
            {
                selected = DeviceDescriptor.DeskClock(endpoint.Address, endpoint.Port, false,
                    $"{endpoint.Address}:{endpoint.Port}");
                return;
            }
            args.Cancel = true;
            error.Visibility = Visibility.Visible;
        };
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary ? selected : null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
