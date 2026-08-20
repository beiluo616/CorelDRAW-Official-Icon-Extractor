using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace CDRIconExtractor.App;

public partial class CodeTemplateWindow : Window
{
    private readonly string _code;
    private readonly string _defaultExtension;
    private readonly string _defaultFileName;

    public CodeTemplateWindow(string title, string code, string defaultExtension, string defaultFileName)
    {
        InitializeComponent();
        Title = title;
        HeaderText.Text = title;
        _code = code ?? string.Empty;
        _defaultExtension = NormalizeExtension(defaultExtension);
        _defaultFileName = string.IsNullOrWhiteSpace(defaultFileName) ? "CorelDRAWIconTemplate" + _defaultExtension : defaultFileName;
        CodeTextBox.Text = _code;
        SaveButton.Content = $"另存为 {_defaultExtension}";
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_code);
        CodeTextBox.Focus();
        CodeTextBox.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var filterLabel = _defaultExtension.Equals(".bas", StringComparison.OrdinalIgnoreCase) ? "VBA 模块" : "C/C++ 源码";
        var dialog = new SaveFileDialog
        {
            Title = "保存代码模板",
            FileName = _defaultFileName,
            DefaultExt = _defaultExtension,
            AddExtension = true,
            Filter = $"{filterLabel} (*{_defaultExtension})|*{_defaultExtension}|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        File.WriteAllText(dialog.FileName, _code, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string NormalizeExtension(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ".txt";
        return value.StartsWith('.') ? value : "." + value;
    }
}
