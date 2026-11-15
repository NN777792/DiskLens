namespace DiskCleanerAssistant.Services;

public interface IDialogService
{
    string? ChooseFolder(string? initialDirectory);
    bool Confirm(string title, string message);
    void ShowInfo(string title, string message);
    void ShowError(string title, string message);
}
