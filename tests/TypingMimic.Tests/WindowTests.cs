using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using TypingMimic.App;
using TypingMimic.App.ViewModels;
using Xunit;

namespace TypingMimic.Tests;

public sealed class WindowTests
{
    [Fact]
    public async Task WindowLayoutAndReadOnlyBindingsLoadWithoutOpeningDesktopWindow()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1120, 820));
                content.Arrange(new Rect(0, 0, 1120, 820));
                content.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
                var bar = Find<ProgressBar>(content);
                Assert.NotNull(bar);
                Assert.Equal(BindingMode.OneWay, bar.GetBindingExpression(ProgressBar.ValueProperty)!.ParentBinding.Mode);
                var combo = Find<ComboBox>(content);
                Assert.NotNull(combo);
                Assert.Equal("Enter", combo.SelectedItem);
                Assert.Equal(4, combo.Items.Count);
                var model = Assert.IsType<MainViewModel>(window.DataContext);
                model.SetHotkeysReady();
                model.SetHotkeysReady(true, "Ctrl+Shift+P", "Ctrl+Shift+S");
                Assert.Contains("Ctrl+Shift+P", model.ShortcutSummary);
                Assert.Contains("Ctrl+Shift+S", model.ShortcutSummary);
                model.SetHotkeysReady(false);
                Assert.Contains("unavailable", model.ShortcutSummary);
                model.SetHotkeysReady();
                Assert.False(model.StartCommand.CanExecute(null));
                model.RandomInput = true;
                Assert.True(model.StartCommand.CanExecute(null));
                Assert.Empty(model.Text);
                model.RandomLength = "0";
                Assert.False(model.StartCommand.CanExecute(null));
                model.RandomLength = "1000";
                Assert.True(model.StartCommand.CanExecute(null));
                model.RandomInput = false;
                Assert.False(model.StartCommand.CanExecute(null));
                model.Text = "sample";
                model.SetHotkeysReady(); // No native registration: the window is never shown.
                Assert.True(model.StartCommand.CanExecute(null));
                model.NewLineShortcut = "Shift+Enter";
                Assert.True(model.StartCommand.CanExecute(null));
                model.NewLineShortcut = "Ctrl+Enter";
                Assert.True(model.StartCommand.CanExecute(null));
                model.NewLineShortcut = "invalid";
                Assert.False(model.StartCommand.CanExecute(null));
                Assert.NotEmpty(model.Validation);
                model.NewLineShortcut = "Enter";
                Assert.True(model.StartCommand.CanExecute(null));
                model.Wpm = "invalid";
                Assert.False(model.StartCommand.CanExecute(null));
                Assert.NotEmpty(model.Validation);
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally
            {
                (window?.DataContext as IDisposable)?.Dispose();
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (Find<T>(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
}
