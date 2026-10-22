using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using TypingMimic.App.Services;
using TypingMimic.App.ViewModels;
using TypingMimic.Core;

namespace TypingMimic.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    private GlobalHotkeyService? hotkeys;
    public MainWindow()
    {
        InitializeComponent();
        var foreground = new ForegroundWindowService();
        model = new(new TypingEngine(new KeyboardInputService(), foreground), foreground, new PdfTextExtractor(),
            count => MessageBox.Show(this, $"This session will type {count:N0} characters. Continue?", "Long typing session",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
        DataContext = model;
        SourceInitialized += (_, _) =>
        {
            try
            {
                hotkeys = new(new WindowInteropHelper(this).Handle, model.TogglePause, model.Stop);
                ApplyShortcuts(this, new RoutedEventArgs());
            }
            catch (Exception ex) { model.ReportError($"Typing is disabled: {ex.Message}"); }
        };
        Closed += (_, _) => { model.Dispose(); hotkeys?.Dispose(); };
    }
    private void ApplyShortcuts(object sender, RoutedEventArgs e)
    {
        if (!model.CanEdit || hotkeys is null) return;
        try
        {
            var pause = ShortcutGesture.Parse(PauseShortcutInput.Text);
            var stop = ShortcutGesture.Parse(StopShortcutInput.Text);
            hotkeys.Configure(pause, stop);
            model.SetHotkeysReady(true, pause.ToString(), stop.ToString());
            PauseShortcutInput.Text = pause.ToString(); StopShortcutInput.Text = stop.ToString();
            model.ReportError("Shortcuts applied. Paste text, open a PDF, or select Random input to begin.");
        }
        catch (Exception ex)
        {
            model.SetHotkeysReady(hotkeys.IsReady);
            model.ReportError(ex.Message);
        }
    }
    private async void OpenPdf(object sender, RoutedEventArgs e)
    {
        if (!model.CanEdit) return;
        var dialog = new OpenFileDialog { Filter = "PDF documents (*.pdf)|*.pdf", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await model.ImportPdfAsync(dialog.FileName);
    }
    private void PasteText(object sender, RoutedEventArgs e)
    {
        if (!model.CanEdit) return;
        try
        {
            if (Clipboard.ContainsText()) { Preview.SelectedText = Clipboard.GetText(); Preview.Focus(); }
            else model.ReportError("The clipboard does not contain text.");
        }
        catch (Exception ex) { model.ReportError($"Could not read clipboard: {ex.Message}"); }
    }
    private void ClearText(object sender, RoutedEventArgs e) { if (model.CanEdit) model.Text = ""; }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = model.CanEdit ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        if (!model.CanEdit) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths)
            await model.ImportPdfAsync(paths[0]);
        else model.ReportError("Drop one PDF at a time.");
    }
}
