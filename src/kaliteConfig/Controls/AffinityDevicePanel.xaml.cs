using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using kaliteConfig.Models;

namespace kaliteConfig.Controls
{
    public sealed class AffinityPolicyOption
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public sealed partial class AffinityDevicePanel : UserControl
    {
        public static readonly DependencyProperty DeviceProperty = DependencyProperty.Register(
            nameof(Device), typeof(AffinityDeviceItem), typeof(AffinityDevicePanel),
            new PropertyMetadata(null, (d, e) => ((AffinityDevicePanel)d).Bindings.Update()));

        public AffinityDeviceItem? Device
        {
            get => (AffinityDeviceItem?)GetValue(DeviceProperty);
            set => SetValue(DeviceProperty, value);
        }

        public List<string> PriorityOptions { get; } = new() { "Undefined", "Low", "Normal", "High" };

        public List<AffinityPolicyOption> PolicyOptions { get; } = new()
        {
            new AffinityPolicyOption { Label = "IrqPolicyMachineDefault", Value = "IrqPolicyMachineDefault" },
            new AffinityPolicyOption { Label = "IrqPolicyAllCloseProcessors", Value = "IrqPolicyAllCloseProcessors" },
            new AffinityPolicyOption { Label = "IrqPolicyOneCloseProcessor", Value = "IrqPolicyOneCloseProcessor" },
            new AffinityPolicyOption { Label = "IrqPolicyAllProcessorsInMachine", Value = "IrqPolicyAllProcessorsInMachine" },
            new AffinityPolicyOption { Label = "IrqPolicySpecifiedProcessors", Value = "IrqPolicySpecifiedProcessors" },
            new AffinityPolicyOption { Label = "IrqPolicySpreadMessagesAcrossAllProcessors", Value = "IrqPolicySpreadMessagesAcrossAllProcessors" }
        };

        public event EventHandler? ApplyRequested;
        public event EventHandler? CancelRequested;

        public AffinityDevicePanel()
        {
            InitializeComponent();
            RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
            {
                if (Visibility == Visibility.Visible) AnimateIn();
            });
        }

        private void AnimateIn()
        {
            Slide.Y = -14;
            Body.Opacity = 0;

            var duration = new Duration(TimeSpan.FromMilliseconds(220));
            var fade = new DoubleAnimation { To = 1, Duration = duration };
            Storyboard.SetTarget(fade, Body);
            Storyboard.SetTargetProperty(fade, "Opacity");

            var slide = new DoubleAnimation { To = 0, Duration = duration, EnableDependentAnimation = true };
            Storyboard.SetTarget(slide, Slide);
            Storyboard.SetTargetProperty(slide, "Y");

            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Children.Add(slide);
            storyboard.Begin();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (Device is null) return;
            foreach (var group in Device.CoreGroups)
                foreach (var thread in group.Threads)
                    thread.IsChecked = true;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (Device is null) return;
            foreach (var group in Device.CoreGroups)
                foreach (var thread in group.Threads)
                    thread.IsChecked = false;
        }

        private void Apply_Click(object sender, RoutedEventArgs e) => ApplyRequested?.Invoke(this, EventArgs.Empty);

        private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

        public static Visibility BoolToVis(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    }
}
