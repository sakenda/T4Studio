using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using MaterialDesignThemes.Wpf;
using Mono.TextTemplating;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;

namespace T4Studio.Client;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;
    private string? _currentFilePath = null;
    private bool _isDirty = false;
    private string _appName = "T4 Studio";

    private readonly TemplateGenerator _generator = new TemplateGenerator();

    public MainWindow()
    {
        InitializeComponent();
        LoadHighlighting();
        ApplyEditorTheme(true);
        UpdateTitle();

        this.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.S && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                SaveFile_Click(null!, null!);
                e.Handled = true;
            }
        };
    }

    public async Task TransformAsync(CancellationToken token)
    {
        // UI-Check vorab
        if (EditorInput == null || string.IsNullOrWhiteSpace(EditorInput.Text)) return;

        string inputCode = EditorInput.Text;
        StatusText.Text = "Transformiere...";

        try
        {
            // Die Transformation läuft im Hintergrund
            // WICHTIG: Wir nutzen die Instanz _generator
            var result = await _generator.ProcessTemplateAsync(
                "dummy.tt",
                inputCode,
                "dummy.txt",
                token
            );

            // Prüfen, ob wir abgebrochen wurden, bevor wir die UI anfassen
            if (token.IsCancellationRequested) return;

            // UI Update auf dem Hauptthread
            Dispatcher.Invoke(() => {
                EditorOutput.Text = result.Item2 ?? "";

                // Fehlerliste sicher befüllen
                if (_generator.Errors != null)
                {
                    var errors = _generator.Errors.Cast<System.CodeDom.Compiler.CompilerError>().ToList();
                    ErrorList.ItemsSource = errors;
                    // ErrorList immer anzeigen, damit man sieht wenn Fehler verschwinden
                    ErrorList.Visibility = Visibility.Visible;
                }
            });
        }
        catch (OperationCanceledException) { /* Gewollter Abbruch */ }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => {
                EditorOutput.Text = "Fehler: " + ex.Message;
                StatusText.Text = "Fehler aufgetreten";
            });
        }
        finally
        {
            if (!token.IsCancellationRequested)
                StatusText.Text = "Fertig";
        }
    }

    private void LoadHighlighting()
    {
        try
        {
            // Sicherstellen, dass C# Highlighting für den Import im XSHD bereitsteht
            var csharpDefinition = HighlightingManager.Instance.GetDefinition("C#");
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("T4Studio.Client.T4Highlighting.xshd"))
            {
                if (stream != null)
                {
                    using (var reader = new XmlTextReader(stream))
                    {
                        // Hier geben wir den HighlightingManager mit, damit er "C#/" auflösen kann
                        EditorInput.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    }
                }
            }

            EditorInput.Options.ShowSpaces = false;
            EditorInput.Options.ShowTabs = false;

            EditorInput.Options.ShowColumnRuler = true;
            EditorInput.FontFamily = new FontFamily("Consolas");
            EditorInput.FontSize = 14;
            EditorInput.LineNumbersForeground = Brushes.DimGray;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Highlighting konnte nicht geladen werden: " + ex.Message);
        }
    }

    private void LoadDebugTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            string templateKey = menuItem.Tag?.ToString();
            string code = "";

            switch (templateKey)
            {
                case "Loop":
                    code = "<#@ template language=\"C#\" #>\n" +
                           "Liste:\n<# for(int i=1; i<=5; i++) { #>\n" +
                           "Punkt <#= i #>\n<# } #>";
                    break;

                case "Poco":
                    code = """
                        <#@ template language="C#" #>
                        <#@ import namespace="System.Collections.Generic" #>
                        <# 
                            var properties = new List<string> { "Id", "Username", "Email", "CreatedAt" };
                        #>
                        public class UserProfile 
                        {
                        <# foreach (var prop in properties) { #>
                            public <#= prop == "Id" ? "int" : "string" #> <#= prop #> { get; private set; }
                        <# } #>

                        	private UserProfile() { }

                        	/// <summary>
                        	/// Creates a new User
                        	/// </summary>
                        	public static UserProfile Create(int id, string username, string email, string createdAt)
                        	{
                        		return new UserProfile()
                        		{
                        			Id = id,
                        			Username = username,
                        			Email = email,
                        			CreatedAt = createdAt
                        		};
                        	}
                        }
                        """;
                    break;

                case "Error":
                    code = "<#@ template language=\"C#\" #>\n" +
                           "<# // Fehler: Fehlendes Semikolon und falsche Variable\n" +
                           "int x = 5\n" +
                           "string s = y; #>";
                    break;
            }

            if (!string.IsNullOrEmpty(code))
            {
                EditorInput.Text = code;
                // Da wir den Text programmatisch ändern, wird TextChanged gefeuert 
                // und die Vorschau aktualisiert sich automatisch nach 500ms.
            }
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    
    private void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "T4 Templates (*.tt)|*.tt|Text Dateien (*.txt)|*.txt|Alle Dateien (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            _currentFilePath = openFileDialog.FileName;
            EditorInput.Text = System.IO.File.ReadAllText(_currentFilePath);
            _isDirty = false;
            UpdateTitle();
        }
    }
    
    private void InsertSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (EditorInput?.Document == null) return;

        if (sender is Button btn && btn.Tag != null)
        {
            int offset = EditorInput.CaretOffset;
            string snippet = "";
            int caretOffsetAdjustment = 0; // Wie weit soll der Cursor von der Startposition springen?

            switch (btn.Tag.ToString())
            {
                case "Dir":
                    snippet = "<#@ template language=\"C#\" #>\r\n";
                    caretOffsetAdjustment = snippet.Length;
                    break;

                case "Loop":
                    // Umschließt markierten Code mit einer For-Schleife
                    SurroundWithBlock("<# for (int i = 0; i < 10; i++) { #>", "<# } #>");
                    break;

                case "If":
                    // Umschließt markierten Code mit einem If-Block
                    SurroundWithBlock("<# if (true) { #>", "<# } #>");
                    break;

                case "Comment":
                    // Macht aus markiertem Text einen T4-Kommentar
                    SurroundWithBlock("<#+ /* ", " */ #>");
                    break;

                case "Expression":
                    // Macht aus einem Wort eine Ausgabe: <#= wort #>
                    SurroundWithBlock("<#= ", " #>");
                    break;

                case "Prop":
                    string prop = "public string MyProperty { get; set; }";
                    EditorInput.Document.Insert(offset, prop);
                    // Markiert 'MyProperty', damit du es sofort überschreiben kannst
                    // 14 ist der Start von 'MyProperty', 10 ist die Länge
                    EditorInput.Select(offset + 14, 10);
                    break;

                case "Date":
                    snippet = "<#= DateTime.Now.ToString() #>";
                    caretOffsetAdjustment = snippet.Length;
                    break;
            }

            if (!string.IsNullOrEmpty(snippet))
            {
                // 2. Text an dieser Stelle einfügen
                EditorInput.Document.Insert(offset, snippet);

                // 3. Fokus zurück auf den Editor (wichtig!)
                EditorInput.Focus();

                // 4. Cursor intelligent platzieren
                EditorInput.CaretOffset = offset + caretOffsetAdjustment;

                // Optional: Wenn ein Teil markiert werden soll (wie der Name "MyProperty")
                // EditorInput.Select(offset + 14, 10); 
            }
        }
    }

    private void SurroundWithBlock(string header, string footer)
    {
        if (EditorInput?.Document == null)
            return;

        var selection = EditorInput.SelectionStart;
        var length = EditorInput.SelectionLength;

        if (length > 0)
        {
            // 1. Den markierten Text holen
            string selectedText = EditorInput.SelectedText;

            // 2. Jede Zeile im markierten Text einrücken
            string indentedText = string.Join("\r\n",
                selectedText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                .Select(line => "    " + line)); // Fügt 4 Leerzeichen hinzu

            // 3. Alles zusammensetzen
            string newText = $"{header}\r\n{indentedText}\r\n{footer}";

            EditorInput.Document.Replace(selection, length, newText);
        }
        else
        {
            // Falls nichts markiert ist, einfach Standard-Snippet
            EditorInput.Document.Insert(EditorInput.CaretOffset, $"{header}\r\n    \r\n{footer}");
            EditorInput.CaretOffset = selection + header.Length + 6; // Cursor in die eingerückte Zeile
        }

        EditorInput.Focus();
    }

    private void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFilePath))
        {
            SaveFileAs_Click(sender, e);
        }
        else
        {
            System.IO.File.WriteAllText(_currentFilePath, EditorInput.Text);
            _isDirty = false;
            UpdateTitle();
        }
    }

    private void SaveFileAs_Click(object sender, RoutedEventArgs e)
    {
        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "T4 Templates (*.tt)|*.tt",
            DefaultExt = "tt"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            _currentFilePath = saveFileDialog.FileName;
            System.IO.File.WriteAllText(_currentFilePath, EditorInput.Text);
            _isDirty = false;
            UpdateTitle();
        }
    }

    private void ThemeDark_Click(object sender, RoutedEventArgs e)
    {
        ModifyTheme(BaseTheme.Dark);
        ApplyEditorTheme(true);
    }

    private void ThemeLight_Click(object sender, RoutedEventArgs e)
    {
        ModifyTheme(BaseTheme.Light);
        ApplyEditorTheme(false);
    }

    private static void ModifyTheme(BaseTheme mode)
    {
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(mode);
        paletteHelper.SetTheme(theme);
    }

    private void ApplyEditorTheme(bool isDark)
    {
        if (isDark)
        {
            EditorInput.Background = System.Windows.Media.Brushes.Transparent;
            EditorInput.Foreground = System.Windows.Media.Brushes.White;
            EditorOutput.Foreground = System.Windows.Media.Brushes.LightGray;
            // Zeilennummern-Farbe
            EditorInput.LineNumbersForeground = System.Windows.Media.Brushes.DarkGray;
        }
        else
        {
            EditorInput.Foreground = System.Windows.Media.Brushes.Black;
            // ... usw für Light Mode
        }

        EditorInput.LineNumbersForeground = System.Windows.Media.Brushes.DimGray;
        EditorOutput.LineNumbersForeground = System.Windows.Media.Brushes.DimGray;
    }

    private bool IsTemplateValid(string code)
    {
        if (string.IsNullOrEmpty(code)) return true;
        int opens = Regex.Matches(code, "<#").Count;
        int closes = Regex.Matches(code, "#>").Count;
        return opens == closes;
    }

    private async void EditorInput_TextChanged(object sender, EventArgs e)
    {
        if (EditorInput?.Document == null) return;

        // Alten Task abbrechen
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            // Wartezeit (Debounce), damit nicht bei jedem Buchstaben gefeuert wird
            await Task.Delay(500, token);

            string currentText = EditorInput.Text;

            // FIX: TemplateValidierung nur wenn Text da ist
            if (!string.IsNullOrEmpty(currentText) && IsTemplateValid(currentText))
            {
                await TransformAsync(token);
            }

            if (!_isDirty)
            {
                _isDirty = true;
                UpdateTitle();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fehler: {ex.Message}");
        }
    }

    private void ErrorList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ErrorList.SelectedItem is System.CodeDom.Compiler.CompilerError error)
        {
            // T4 Zeilen sind oft 1-basiert
            int line = error.Line;
            if (line > 0 && line <= EditorInput.LineCount)
            {
                // Springe zur Zeile
                var lineOffset = EditorInput.Document.GetLineByNumber(line);
                EditorInput.ScrollToLine(line);
                EditorInput.Select(lineOffset.Offset, lineOffset.Length);
                EditorInput.Focus();
            }
        }
    }

    private void UpdateTitle()
    {
        string fileName = string.IsNullOrEmpty(_currentFilePath) ? "Unbenannt.tt" : System.IO.Path.GetFileName(_currentFilePath);
        string dirtyMarker = _isDirty ? "*" : "";
        this.Title = $"{_appName} - [{fileName}{dirtyMarker}]";
    }

}