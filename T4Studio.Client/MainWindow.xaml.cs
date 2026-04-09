using MaterialDesignThemes.Wpf;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using T4Studio.Client.Editor;

namespace T4Studio.Client;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _isDarkTheme = true;
    
    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme();
        LoadSavedTemplates();

        if (DataContext is EditorViewModel vm)
        {
            RegisterViewModelEvents(vm);
        }

        DataContextChanged += (s, e) =>
        {
            if (e.NewValue is EditorViewModel newVm)
                RegisterViewModelEvents(newVm);
        };

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.S && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                (DataContext as EditorViewModel)?.SaveAsCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.O && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                (DataContext as EditorViewModel)?.LoadCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.X && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                (DataContext as EditorViewModel)?.ExitCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private void RegisterViewModelEvents(EditorViewModel vm)
    {
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(EditorViewModel.T4Content))
            {
                if (EditorInput.Text != vm.T4Content)
                    EditorInput.Text = vm.T4Content ?? "";
            }

            if (args.PropertyName == nameof(EditorViewModel.GeneratedOutput))
            {
                if (EditorOutput.Text != vm.GeneratedOutput)
                    EditorOutput.Text = vm.GeneratedOutput ?? "";
            }

            if (args.PropertyName == nameof(EditorViewModel.StatusText))
            {
                StatusText.Text = vm.StatusText;
            }
        };
    }

    private void EditorInput_TextChanged(object sender, EventArgs e)
    {
        if (DataContext is EditorViewModel vm)
        {
            if (vm.T4Content != EditorInput.Text)
            {
                vm.T4Content = EditorInput.Text;
            }
        }
    }

    private void SwitchEditorTheme(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var editors = new[] { EditorInput, EditorOutput };
        foreach (var editor in editors)
        {
            editor.Foreground = Brushes.Black;
            editor.Options.HighlightCurrentLine = true;
            editor.Options.IndentationSize = 4;
            editor.Options.ShowColumnRuler = false;
            editor.Options.ShowSpaces = false;
            editor.Options.ShowTabs = false;
            editor.LineNumbersForeground = Brushes.DarkGray;
            editor.FontFamily = new FontFamily("Consolas");
            editor.FontSize = 14;
        }

        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(_isDarkTheme ? BaseTheme.Dark : BaseTheme.Light);
        paletteHelper.SetTheme(theme);
    }

    private void InsertSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is EditorViewModel vm && sender is Button { Tag: string tag })
        {
            int start = EditorInput.SelectionStart;
            string selectedText = EditorInput.SelectedText;

            var result = vm.GetSnippet(tag, selectedText);

            if (!string.IsNullOrEmpty(result.Text))
            {
                EditorInput.Document.Replace(start, EditorInput.SelectionLength, result.Text);
                int targetCaretPos = start + result.CaretOffsetAdjustment;
                if (targetCaretPos >= 0 && targetCaretPos <= EditorInput.Document.TextLength)
                {
                    if (result.SelectionLength > 0)
                    {
                        EditorInput.Select(targetCaretPos, result.SelectionLength);
                    }
                    else
                    {
                        EditorInput.CaretOffset = targetCaretPos;
                    }
                }
                else
                {
                    EditorInput.CaretOffset = start + result.Text.Length;
                }

                EditorInput.Focus();
            }
        }
    }

    private void ErrorList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ErrorList.SelectedItem is System.CodeDom.Compiler.CompilerError error)
        {
            int line = error.Line;
            if (line > 0 && line <= EditorInput.LineCount)
            {
                var lineOffset = EditorInput.Document.GetLineByNumber(line);
                EditorInput.ScrollToLine(line);
                EditorInput.Select(lineOffset.Offset, lineOffset.Length);
                EditorInput.Focus();
            }
        }
    }

    private async void AddAsTemplate_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as EditorViewModel;

        if (RootDialogHost.DialogContent is null)
        {
            StatusText.Text = "Missing Component: RootDialogHost.DialogContent not found!";
            return;
        }

        TemplateNameInput.Text = string.Empty;
        var result = await DialogHost.Show(RootDialogHost.DialogContent, "MainDialogHost");

        if (result is string templateName && !string.IsNullOrWhiteSpace(templateName))
        {
            try
            {
                string folderPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "T4Studio", "Templates");

                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                string filePath = Path.Combine(folderPath, $"{templateName}.t4");

                if (vm != null)
                {
                    File.WriteAllText(filePath, vm.T4Content);
                    MessageBox.Show($"Template '{templateName}' saved!", "Success");
                }

                LoadSavedTemplates();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving template: {ex.Message}", "Error");
            }
        }
    }

    private void LoadSavedTemplates()
    {
        try
        {
            string folderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "T4Studio", "Templates");

            if (!Directory.Exists(folderPath)) return;

            DynamicTemplatesMenuItem.Items.Clear();

            var files = Directory.GetFiles(folderPath, "*.t4");

            if (!files.Any())
            {
                var emptyItem = new MenuItem { Header = "Keine Templates gefunden", IsEnabled = false };
                DynamicTemplatesMenuItem.Items.Add(emptyItem);
                return;
            }

            foreach (var file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);

                var item = new MenuItem
                {
                    Header = fileName,
                    Tag = file
                };

                item.Icon = new PackIcon
                {
                    Kind = PackIconKind.FileDocumentOutline, 
                    Width = 16, 
                    Height = 16
                };

                item.Click += (s, e) => {
                    string content = File.ReadAllText(file);
                    var vm = DataContext as EditorViewModel;
                    if (vm != null)
                    {
                        vm.T4Content = content;
                        EditorInput.Text = content;
                        StatusText.Text = $"Loaded template: {fileName}";
                    }
                };

                DynamicTemplatesMenuItem.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error loading templates: " + ex.Message;
        }
    }

}