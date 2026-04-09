using MaterialDesignThemes.Wpf;
using Mono.TextTemplating;
using System.CodeDom.Compiler;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using T4Studio.Client.Abstractions;

namespace T4Studio.Client.Editor;

internal sealed class EditorViewModel : ViewModelBase
{
    private readonly TemplateGenerator _generator = new();
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Current Input Edit Value
    /// </summary>
    public string T4Content
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                IsDirty = true;
                TriggerTransform();
            }
        }
    } = "";

    /// <summary>
    /// Current Output Value
    /// </summary>
    public string GeneratedOutput { get => field; set => SetProperty(ref field, value); } = "";

    /// <summary>
    /// T4-Generating Status
    /// </summary>
    public string StatusText { get => field; set => SetProperty(ref field, value); } = "Ready";

    /// <summary>
    /// Filepath from the current opened file
    /// </summary>
    public string? CurrentFilePath { get => field; set { SetProperty(ref field, value); OnPropertyChanged(nameof(WindowTitle)); } }

    /// <summary>
    /// Edited Flag
    /// </summary>
    public bool IsDirty { get => field; set { SetProperty(ref field, value); OnPropertyChanged(nameof(WindowTitle)); } }

    /// <summary>
    /// Current errors from the T4-Genration
    /// </summary>
    public ObservableCollection<CompilerError> Errors { get => field; set => SetProperty(ref field, value); } = [];

    /// <summary>
    /// Window Title Text
    /// </summary>
    public string WindowTitle => $"T4 Studio - [{(string.IsNullOrEmpty(CurrentFilePath) ? "New.tt" : Path.GetFileName(CurrentFilePath))}{(IsDirty ? "*" : "")}]";

    private enum SnippetKey
    {
        // Infrastructure / Directives
        T4_Directive_Template,
        T4_Directive_Output,
        T4_Directive_Import,
        T4_Directive_Assembly,
        T4_Directive_Host,

        // Control / Logic
        T4_Control_ForLoop,
        T4_Control_ForEach,
        T4_Control_IfCondition,
        T4_Control_Expression,
        T4_Control_FeatureBlock,
        T4_Control_Comment,

        // C# Objects
        CSharp_Object_Property,
        CSharp_Object_PropNotify,
        CSharp_Object_Method,
        CSharp_Object_Constructor,
        CSharp_Object_DateTime
    }

    /// <summary>
    /// Category: Template (Infrastructure)
    /// </summary>
    public ObservableCollection<SnippetInfo> TemplateSnippets { get; } = [
        new(nameof(SnippetKey.T4_Directive_Template), "Template", PackIconKind.FileCode),
        new(nameof(SnippetKey.T4_Directive_Output),   "Output",   PackIconKind.FileExport),
        new(nameof(SnippetKey.T4_Directive_Import),   "Import",   PackIconKind.Import),
        new(nameof(SnippetKey.T4_Directive_Assembly), "Assembly", PackIconKind.Library),
        new(nameof(SnippetKey.T4_Directive_Host),     "Host",     PackIconKind.ServerNetwork)
    ];

    /// <summary>
    /// Category: Control (Loops & Logic)
    /// </summary>
    public ObservableCollection<SnippetInfo> ControlSnippets { get; } = [
        new(nameof(SnippetKey.T4_Control_ForLoop),     "For Loop",    PackIconKind.Repeat),
        new(nameof(SnippetKey.T4_Control_ForEach),     "ForEach",     PackIconKind.Loop),
        new(nameof(SnippetKey.T4_Control_IfCondition), "If Condition",PackIconKind.CodeBraces),
        new(nameof(SnippetKey.T4_Control_Expression),  "Expression",  PackIconKind.CodeEqual),
        new(nameof(SnippetKey.T4_Control_FeatureBlock),"Class Block", PackIconKind.FunctionVariant),
        new(nameof(SnippetKey.T4_Control_Comment),     "Comment",     PackIconKind.CommentText)
    ];

    /// <summary>
    /// Category: Objects (C# Code-Generation)
    /// </summary>
    public ObservableCollection<SnippetInfo> ObjectSnippets { get; } = [
        new(nameof(SnippetKey.CSharp_Object_Property),    "Property",    PackIconKind.AlphaPBox),
        new(nameof(SnippetKey.CSharp_Object_PropNotify),  "Notify Prop", PackIconKind.BellRing),
        new(nameof(SnippetKey.CSharp_Object_Method),      "Method",      PackIconKind.Bracket),
        new(nameof(SnippetKey.CSharp_Object_Constructor), "Constructor", PackIconKind.Hammer),
        new(nameof(SnippetKey.CSharp_Object_DateTime),    "Date Time",   PackIconKind.CalendarClock)
    ];

    public ICommand LoadTemplateCommand { get; }
    public ICommand LoadCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand ExitCommand { get; }

    public EditorViewModel()
    {
        LoadTemplateCommand = new RelayCommand(p => ExecuteLoadTemplate(p?.ToString()));
        LoadCommand = new RelayCommand(_ => ExecuteLoad());
        SaveCommand = new RelayCommand(_ => ExecuteSave());
        SaveAsCommand = new RelayCommand(_ => ExecuteSaveAs());
        ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
    }

    public SnippetResult GetSnippet(string tag, string selectedText)
    {
        if (!Enum.TryParse(tag, out SnippetKey key))
            return new SnippetResult(string.Empty, 0);

        return key switch
        {
            // --- Infrastructure ---
            SnippetKey.T4_Directive_Template => new SnippetResult("<#@ template language=\"C#\" #>\r\n", 32),
            SnippetKey.T4_Directive_Import => new SnippetResult("<#@ import namespace=\"Namespace\" #>", 22, 9),
            SnippetKey.T4_Directive_Assembly => new SnippetResult("<#@ assembly name=\"System.Core\" #>", 19, 11),
            SnippetKey.T4_Directive_Output => new SnippetResult("<#@ output extension=\".cs\" #>", 22),
            SnippetKey.T4_Directive_Host => new SnippetResult("<#= this.Host.ResolvePath(\"file.txt\") #>", 26, 9),

            // --- Control Structures ---
            SnippetKey.T4_Control_ForLoop => SurroundWith(selectedText, "<# for (int i = 0; i < 10; i++) { #>", "<# } #>", 34),
            SnippetKey.T4_Control_ForEach => SurroundWith(selectedText, "<# foreach (var item in collection) { #>", "<# } #>", 24),
            SnippetKey.T4_Control_IfCondition => SurroundWith(selectedText, "<# if (true) { #>", "<# } #>", 15),
            SnippetKey.T4_Control_Expression => SurroundWith(selectedText, "<#= ", " #>", 4),
            SnippetKey.T4_Control_FeatureBlock => SurroundWith(selectedText, "<#+ ", " #>", 4),
            SnippetKey.T4_Control_Comment => SurroundWith(selectedText, "<#+ /* ", " */ #>", 7),

            // --- C# Objects ---
            SnippetKey.CSharp_Object_Property => new SnippetResult("public string MyProperty { get; set; }", 14, 10),
            SnippetKey.CSharp_Object_PropNotify => new SnippetResult("private string _field;\r\npublic string MyProperty\r\n{\r\n    get => _field;\r\n    set => SetProperty(ref _field, value);\r\n}", 38, 10),
            SnippetKey.CSharp_Object_Method => new SnippetResult("public void MyMethod()\r\n{\r\n    \r\n}", 12, 8),
            SnippetKey.CSharp_Object_Constructor => new SnippetResult("public MyClass()\r\n{\r\n    \r\n}", 7, 7),
            SnippetKey.CSharp_Object_DateTime => new SnippetResult($"<#= DateTime.Now.ToString(\"yyyy-MM-dd HH:mm:ss\") #>", 27),

            _ => new SnippetResult(string.Empty, 0)
        };
    }

    public async Task TransformAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(T4Content)) return;
        StatusText = "Transform...";

        try
        {
            var result = await _generator.ProcessTemplateAsync("dummy.tt", T4Content, "dummy.txt", token);
            if (token.IsCancellationRequested) return;

            GeneratedOutput = result.Item2 ?? "";

            Errors.Clear();
            if (_generator.Errors != null)
            {
                foreach (CompilerError err in _generator.Errors) Errors.Add(err);
            }
            StatusText = "Done";
        }
        catch (Exception ex)
        {
            GeneratedOutput = "Error: " + ex.Message;
            StatusText = "Error occured";
        }
    }

    private void ExecuteLoadTemplate(string? key)
    {
        T4Content = key switch
        {
            "Loop" => """
                <#@ template language="C#" #>            
                Liste:
                <#
                for(int i=1; i<=5; i++) {
                #>
                Punkt <#= i #>
                <#
                }
                #>
                """,
            "Poco" => """
                <#@ template language="C#" #>
                <#@ import namespace="System.Collections.Generic" #>
                <# 
                    var properties = new Dictionary<string, string>
                    {
                        { "Id", "int" },
                        { "Username", "string" },
                        { "Email", "string" },
                        { "CreatedAt", "DateTime" }
                    };
                #>
                namespace Test;

                public class UserProfile 
                {
                <#
                foreach (var kvp in properties)
                {
                    var property = $"public {kvp.Value} {kvp.Key} {{ get; set; }}";
                #>
                    <#= property #>
                <#
                }
                #>

                    private UserProfile()
                    {
                    }

                    /// <summary>
                    /// Creates a new User
                    /// </summary>
                    public static UserProfile Create(int id, string username, string email, DateTime createdAt)
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
                """,
            "Error" => """
                <#@ template language="C#" #>
                <# // Fehler: Fehlendes Semikolon und falsche Variable
                    int x = 5
                    string s = y;
                #>
                """,
            _ => T4Content
        };

        IsDirty = false;
    }

    private void ExecuteLoad()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "T4 Templates (*.tt)|*.tt"
        };

        if (dialog.ShowDialog() == true)
        {
            CurrentFilePath = dialog.FileName;
            T4Content = File.ReadAllText(CurrentFilePath);
            IsDirty = false;
        }
    }

    private void ExecuteSave()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            ExecuteSaveAs();
        }
        else
        {
            File.WriteAllText(CurrentFilePath, T4Content);
            IsDirty = false;
        }
    }

    private void ExecuteSaveAs()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "T4 Templates (*.tt)|*.tt" 
        };

        if (dialog.ShowDialog() == true)
        {
            CurrentFilePath = dialog.FileName;
            File.WriteAllText(CurrentFilePath, T4Content);
            IsDirty = false;
        }
    }

    private SnippetResult SurroundWith(string selectedText, string header, string footer, int caretIfEmpty)
    {
        if (!string.IsNullOrEmpty(selectedText))
        {
            string indented = string.Join("\r\n",
                selectedText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => "    " + line));

            string newText = $"{header}\r\n{indented}\r\n{footer}";
            return new SnippetResult(newText, newText.Length);
        }

        string emptyTemplate = $"{header}\r\n    \r\n{footer}";
        int safeOffset = Math.Min(caretIfEmpty + 6, emptyTemplate.Length);
        return new SnippetResult(emptyTemplate, safeOffset);
    }

    private async void TriggerTransform()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            await Task.Delay(500, token);
            if (IsTemplateValid(T4Content)) await TransformAsync(token);
        }
        catch (OperationCanceledException) { }
    }

    private bool IsTemplateValid(string code)
    {
        if (string.IsNullOrEmpty(code)) return true;
        return Regex.Matches(code, "<#").Count == Regex.Matches(code, "#>").Count;
    }

}
