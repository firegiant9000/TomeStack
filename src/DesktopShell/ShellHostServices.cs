using System.IO;
using System.Windows;
using Microsoft.Win32;
using TomeStack.AppService;

namespace TomeStack.DesktopShell;

/// <summary>Native dialogs for the in-process service. Commands run off the UI thread, so dialogs marshal back to it.</summary>
public sealed class ShellHostServices(Window owner) : IHostServices
{
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
}
