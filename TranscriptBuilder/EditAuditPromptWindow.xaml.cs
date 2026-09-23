using System.Windows;
using TranscriptBuilder.Models;

namespace TranscriptBuilder;

// Modal editor for the saved audit-prompt text; Insert Audit Prompt uses whatever was last saved here.
public partial class EditAuditPromptWindow : Window
{
    // Null after a save means "use the built-in default" — see AuditPrompt.NormalizeCustom.
    public string? SavedCustomPrompt { get; private set; }

    public EditAuditPromptWindow(string currentEffectivePrompt)
    {
        InitializeComponent();
        PromptTextBox.Text = currentEffectivePrompt;
    }

    // The earliest point this window's HWND exists, so the title bar is themed before it's shown.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBarTheme.Apply(this);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e) => PromptTextBox.Text = AuditPrompt.Base;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SavedCustomPrompt = AuditPrompt.NormalizeCustom(PromptTextBox.Text);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
