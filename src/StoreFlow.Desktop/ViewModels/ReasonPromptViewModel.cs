using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StoreFlow.Desktop.Localization;

namespace StoreFlow.Desktop.ViewModels;

/// <summary>
/// Asks the owner for a reason. A reversal and a stock count both need one, and the reason is what
/// makes the entry traceable later (PRD PUR-06, INV-05).
/// </summary>
public sealed partial class ReasonPromptViewModel : ObservableObject
{
    private readonly IStrings _strings;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>Creates the prompt.</summary>
    /// <param name="strings">The application's strings.</param>
    public ReasonPromptViewModel(IStrings strings)
    {
        _strings = strings;
    }

    /// <summary>The title of the prompt.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The label on the reason field.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The label on the accept command.</summary>
    public string AcceptText => _strings[StringKeys.CommonOk];

    /// <summary>The label on the cancel command.</summary>
    public string CancelText => _strings[StringKeys.CommonCancel];

    /// <summary>Raised when the prompt wants to close itself.</summary>
    public event EventHandler<string?>? CloseRequested;

    /// <summary>Accepts the reason and closes the prompt.</summary>
    [RelayCommand]
    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = _strings[StringKeys.ErrorInventoryRequired];
            return;
        }

        CloseRequested?.Invoke(this, Reason.Trim());
    }

    /// <summary>Closes the prompt without a reason.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, null);
}
