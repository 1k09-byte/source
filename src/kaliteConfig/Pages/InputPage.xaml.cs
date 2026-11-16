using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using System;
using System.Collections.Generic;
using kaliteConfig.ViewModels;

namespace kaliteConfig.Pages
{
    public sealed partial class InputPage : Page
    {
        public InputViewModel ViewModel { get; }

        private readonly List<double> _history = new();
        private Polyline? _sparkLine;

        public InputPage()
        {
            this.InitializeComponent();
            this.ViewModel = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<InputViewModel>(App.Services) ?? new InputViewModel(
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Input.IMouseDiscoveryService>(App.Services),
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Input.IWindowsMouseSettingsService>(App.Services),
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Input.IMouseTestService>(App.Services),
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Input.IMouseLog>(App.Services),
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Input.IMousePrefStore>(App.Services),
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Input.IMouseProtocolRegistry>(App.Services),
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Input.IMouseWriteTransaction>(App.Services),
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Input.IPollRateAdvisor>(App.Services),
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Input.IVendorSoftwareLauncher>(App.Services));
            this.DataContext = ViewModel;
            ViewModel.Motion += OnMotion;
            this.Loaded += (_, _) =>
            {
                ViewModel.Open();
                BuildSpark();
                if (App.MainWindow is { } window) window.Activated += HostWindow_Activated;
            };
            this.Unloaded += (_, _) =>
            {
                if (App.MainWindow is { } window) window.Activated -= HostWindow_Activated;
                ViewModel.Motion -= OnMotion;
                try { ViewModel.Close(); } catch { }
            };
        }

        private void HostWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) return;
            try { ViewModel.RefreshDevices(); } catch { }
        }

        private void SectionBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            string tag = sender.SelectedItem?.Text ?? "Mouse";
            MouseSection.Visibility = tag == "Mouse" ? Visibility.Visible : Visibility.Collapsed;
            ButtonsSection.Visibility = tag == "Buttons" ? Visibility.Visible : Visibility.Collapsed;
            WindowsSection.Visibility = tag == "Windows" ? Visibility.Visible : Visibility.Collapsed;
            TestsSection.Visibility = tag == "Tests" ? Visibility.Visible : Visibility.Collapsed;
            IgnoredSection.Visibility = tag == "Ignored" ? Visibility.Visible : Visibility.Collapsed;
            LogSection.Visibility = tag == "Log" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Rescan_Click(object sender, RoutedEventArgs e) => ViewModel.Rescan();

        private void VendorSoftware_Click(object sender, RoutedEventArgs e) => ViewModel.OpenVendorSoftware();

        private void UseRecommendation_Click(object sender, RoutedEventArgs e) => ViewModel.UseRecommendation();

        private void ShowIgnored_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is MouseDeviceRow row) ViewModel.ShowIgnoredAnyway(row);
        }

        private void NicknameSave_Click(object sender, RoutedEventArgs e) => ViewModel.SaveNickname();

        private async void DeviceApply_Click(object sender, RoutedEventArgs e) => await ViewModel.ApplyDeviceAsync();

        private async void DeviceRevert_Click(object sender, RoutedEventArgs e) => await ViewModel.RevertDeviceAsync();

        private async void WindowsApply_Click(object sender, RoutedEventArgs e) => await ViewModel.ApplyWindowsAsync();

        private async void WindowsReset_Click(object sender, RoutedEventArgs e) => await ViewModel.ResetWindowsSectionAsync();

        private async void WindowsRevert_Click(object sender, RoutedEventArgs e) => await ViewModel.RevertWindowsAsync();

        private void TestStart_Click(object sender, RoutedEventArgs e) => ViewModel.StartTestManually();

        private void TestStop_Click(object sender, RoutedEventArgs e) => ViewModel.StopTest();

        private void DpiAnalyze_Click(object sender, RoutedEventArgs e) => ViewModel.AnalyzeDpi();

        private void ClickTestArea_PointerPressed(object sender, PointerRoutedEventArgs e) => ViewModel.RegisterClick();

        private void OnMotion(InputMotionSample sample)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(() => OnMotion(sample));
                return;
            }
            if (sample.Hertz > 0)
            {
                _history.Add(sample.Hertz);
                while (_history.Count > 63) _history.RemoveAt(0);
                DrawSpark();
            }
            else if (_history.Count > 0)
            {
                _history.Clear();
                DrawSpark();
            }
        }

        private void Spark_SizeChanged(object sender, SizeChangedEventArgs e) => BuildSpark();

        private void BuildSpark()
        {
            if (SparkCanvas == null) return;
            SparkCanvas.Children.Clear();
            _sparkLine = null;
            double width = SparkCanvas.ActualWidth;
            double height = SparkCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            Brush axisBrush = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
            for (int i = 0; i <= 2; i++)
            {
                double y = height * i / 2.0;
                var line = new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = axisBrush, StrokeThickness = 1, Opacity = 0.22 };
                SparkCanvas.Children.Add(line);
            }
            _sparkLine = new Polyline
            {
                Stroke = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                StrokeThickness = 2,
            };
            SparkCanvas.Children.Add(_sparkLine);
            DrawSpark();
        }

        private void DrawSpark()
        {
            if (_sparkLine == null) return;
            double width = SparkCanvas.ActualWidth;
            double height = SparkCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            _sparkLine.Points.Clear();
            if (_history.Count < 2) return;
            double max = 1;
            foreach (double value in _history)
                if (value > max) max = value;
            max *= 1.15;
            double step = width / 62.0;
            int offset = 62 - _history.Count;
            for (int i = 0; i < _history.Count; i++)
            {
                double y = height - Math.Clamp(_history[i] / max, 0, 1) * (height - 8) - 4;
                _sparkLine.Points.Add(new Windows.Foundation.Point((offset + i) * step, y));
            }
        }
    }
}
