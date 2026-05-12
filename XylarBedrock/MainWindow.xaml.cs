using System;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms.Design;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows.Data;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Management.Core;
using Windows.Management.Deployment;
using Windows.System;
using XylarBedrock.Classes;
using System.Windows.Media.Animation;
using XylarBedrock.Pages;
using XylarBedrock.Pages.Welcome;
using XylarBedrock.ViewModels;
using XylarBedrock.Pages.Settings;
using XylarBedrock.Pages.Play;
using XylarBedrock.Pages.News;
using XylarBedrock.Pages.Preview;
using XylarBedrock.Handlers;
using XylarBedrock.UI.Pages.Common;
using XylarBedrock.UI.Components;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace XylarBedrock
{
    public partial class MainWindow : Window
    {
        private const int WmGetMinMaxInfoMessage = 0x0024;
        private static readonly TimeSpan OpenAnimationDuration = TimeSpan.FromMilliseconds(220);
        private static readonly TimeSpan ActionAnimationDuration = TimeSpan.FromMilliseconds(170);
        private bool isAnimatedClosePending;
        private bool isWindowAnimationRunning;
        private WindowState previousWindowState;

        public MainWindow()
        {
            this.DataContext = MainViewModel.Default;
            InitializeComponent();
            SourceInitialized += Window_SourceInitialized;
            ContentRendered += MainWindow_ContentRendered;
            previousWindowState = WindowState;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Keyboard.ClearFocus();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            Keyboard.ClearFocus();

            if (e.ClickCount == 2)
            {
                _ = ToggleWindowStateAnimatedAsync();
                return;
            }

            try
            {
                DragMove();
            }
            catch
            {
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            await PlayWindowActionAnimationAsync(0.74, 0.985);
            WindowState = WindowState.Minimized;
            ResetWindowFrameVisuals();
        }

        private async void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            await ToggleWindowStateAnimatedAsync();
        }

        private async void Window_StateChanged(object sender, EventArgs e)
        {
            UpdateWindowFrameChrome();
            if (previousWindowState == WindowState.Minimized && WindowState != WindowState.Minimized)
            {
                await PlayWindowRevealAnimationAsync();
            }

            previousWindowState = WindowState;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateWindowFrameChrome();
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
            {
                hwndSource.AddHook(WindowProc);
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (isAnimatedClosePending)
            {
                Properties.LauncherSettings.Default.LastSessionClosedCleanly = true;
                Properties.LauncherSettings.Default.Save();
                return;
            }

            MainViewModel.Default.AttemptClose(sender, e);
            if (!e.Cancel)
            {
                e.Cancel = true;
                _ = BeginCloseAnimationAsync();
            }
        }
        private async void Window_Initialized(object sender, EventArgs e)
        {
            Panel.SetZIndex(MainFrame, 0);
            Panel.SetZIndex(OverlayFrame, 1);
            Panel.SetZIndex(ErrorFrame, 2);
            Panel.SetZIndex(UpdateButton, 3);

            if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            {
                UpdateWindowFrameChrome();
                bool startupReady = await RuntimeHandler.RecoverAndRunStartupAsync(async () =>
                {
                    await Program.OnApplicationLoaded();
                });

                if (!startupReady) return;

                Properties.LauncherSettings.Default.LastSessionClosedCleanly = false;
                Properties.LauncherSettings.Default.Save();
                MainPage.NavigateToGamePage();
                Program.StartDeferredStartupWork();
                StartupArgsHandler.RunStartupArgs();

                bool isFirstLaunch = Properties.LauncherSettings.Default.GetIsFirstLaunch(MainDataModel.Default.Config.profiles.Count());
                if (isFirstLaunch) 
                {
                    Properties.LauncherSettings.Default.IsFirstLaunch = false;
                    Properties.LauncherSettings.Default.Save();
                    MainViewModel.Default.SetOverlayFrame(new WelcomePage(), true);
                }
            }
        }

        private async void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            ContentRendered -= MainWindow_ContentRendered;
            await PlayWindowRevealAnimationAsync();
        }

        private void UpdateWindowFrameChrome()
        {
            if (WindowFrameBorder == null) return;

            bool isMaximized = WindowState == WindowState.Maximized;
            WindowFrameBorder.Margin = new Thickness(0);
            WindowFrameBorder.CornerRadius = isMaximized ? new CornerRadius(0) : new CornerRadius(14);
            WindowFrameBorder.BorderThickness = new Thickness(0);
        }

        private async Task ToggleWindowStateAnimatedAsync()
        {
            if (isWindowAnimationRunning) return;

            await PlayWindowActionAnimationAsync(0.9, 0.992);
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            UpdateWindowFrameChrome();
            await PlayWindowRevealAnimationAsync();
        }

        private async Task BeginCloseAnimationAsync()
        {
            if (isWindowAnimationRunning) return;

            await PlayWindowActionAnimationAsync(0.0, 0.985);
            isAnimatedClosePending = true;
            Close();
        }

        private async Task PlayWindowRevealAnimationAsync()
        {
            if (WindowFrameBorder == null || WindowFrameScaleTransform == null) return;
            if (isWindowAnimationRunning) return;

            isWindowAnimationRunning = true;
            WindowFrameBorder.Opacity = 0.0;
            WindowFrameScaleTransform.ScaleX = 0.988;
            WindowFrameScaleTransform.ScaleY = 0.988;
            await AnimateWindowFrameAsync(1.0, 1.0, OpenAnimationDuration);
            isWindowAnimationRunning = false;
        }

        private async Task PlayWindowActionAnimationAsync(double targetOpacity, double targetScale)
        {
            if (WindowFrameBorder == null || WindowFrameScaleTransform == null) return;
            if (isWindowAnimationRunning) return;

            isWindowAnimationRunning = true;
            await AnimateWindowFrameAsync(targetOpacity, targetScale, ActionAnimationDuration);
            isWindowAnimationRunning = false;
        }

        private Task AnimateWindowFrameAsync(double targetOpacity, double targetScale, TimeSpan duration)
        {
            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            CubicEase easing = new CubicEase() { EasingMode = EasingMode.EaseOut };

            DoubleAnimation opacityAnimation = new DoubleAnimation(targetOpacity, duration)
            {
                EasingFunction = easing
            };

            DoubleAnimation scaleXAnimation = new DoubleAnimation(targetScale, duration)
            {
                EasingFunction = easing
            };

            DoubleAnimation scaleYAnimation = new DoubleAnimation(targetScale, duration)
            {
                EasingFunction = easing
            };

            opacityAnimation.Completed += (_, __) => completion.TrySetResult(true);

            WindowFrameBorder.BeginAnimation(OpacityProperty, opacityAnimation);
            WindowFrameScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnimation);
            WindowFrameScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnimation);

            return completion.Task;
        }

        private void ResetWindowFrameVisuals()
        {
            if (WindowFrameBorder == null || WindowFrameScaleTransform == null) return;

            WindowFrameBorder.Opacity = 1;
            WindowFrameScaleTransform.ScaleX = 1;
            WindowFrameScaleTransform.ScaleY = 1;
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmGetMinMaxInfoMessage)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                GetMonitorInfo(monitor, monitorInfo);
                RECT workArea = monitorInfo.rcWork;
                RECT monitorArea = monitorInfo.rcMonitor;

                minMaxInfo.ptMaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
                minMaxInfo.ptMaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
                minMaxInfo.ptMaxSize.X = Math.Abs(workArea.Right - workArea.Left);
                minMaxInfo.ptMaxSize.Y = Math.Abs(workArea.Bottom - workArea.Top);
            }

            Marshal.StructureToPtr(minMaxInfo, lParam, true);
        }

        private const int MonitorDefaultToNearest = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor = new RECT();
            public RECT rcWork = new RECT();
            public int dwFlags;
        }

    }
}

