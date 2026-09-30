using System.IO;
using Microsoft.Win32;
using TomeStack.AppService;

namespace TomeStack.DesktopShell;

/// <summary>Native dialogs and the PDF viewer for the in-process service. Commands run off the UI thread, so these marshal back to it.</summary>
public sealed class ShellHostServices(MainWindow owner) : IHostServices
{
    public bool CanOpenFiles => true;

    public string? ChooseSaveLocation(string suggestedFileName, string filterDescription, string extension) =>
        owner.Dispatcher.Invoke(() =>
        {
            var dialog = new SaveFileDialog
            {
                FileName = suggestedFileName,
                Filter = $"{filterDescription} (*{extension})|*{extension}",
                DefaultExt = extension,
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            return dialog.ShowDialog(owner) == true ? Path.GetFullPath(dialog.FileName) : null;
        });

    public string? ChooseOpenFile(string filterDescription, string extension) =>
        owner.Dispatcher.Invoke(() =>
        {
            // M6 slice 3: several extensions may be given as ".json;.csv".
            var patterns = string.Join(";", extension.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => "*" + e));
            var dialog = new OpenFileDialog
            {
                Filter = $"{filterDescription} ({patterns})|{patterns}",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            return dialog.ShowDialog(owner) == true ? Path.GetFullPath(dialog.FileName) : null;
        });

    public bool OpenPdf(string path, int page, string title) => owner.Dispatcher.Invoke(() => owner.OpenPdfViewer(path, page, title));
}
