using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using NvidiaGsyncTweaks.Services;
using static Microsoft.UI.Reactor.Factories;

namespace NvidiaGsyncTweaks.UI;

internal static class 백업Page
{
    public static Element Build(Ctx c, Action<string> restore)
    {
        var folder = RegistryStore.BackupFolder;
        string[] files = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.reg").OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase).Take(30).ToArray()
            : [];

        var rows = files.Select(f =>
            Ui.SettingCard(
                Path.GetFileNameWithoutExtension(f),
                File.GetLastWriteTime(f).ToString("f"),
                HStack(8,
                    Button("Show", () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{f}\"") { UseShellExecute = true })),
                    Ui.ElevatedButton("복원 & restart driver", () => restore(f), enabled: c.Gpu?.KeyPath is not null && c.Gpu.Adapter is not null && !c.Busy))).WithKey(f)).ToArray();

        return VStack(16,
            Ui.PageHeader("백업",
                "A restore point (.reg file) of every value this app manages is written automatically before each Apply. " +
                "Restoring puts the values back exactly as they were - absent values are deleted again."),
            HStack(8,
                Button("백업 폴더 열기", () =>
                {
                    Directory.CreateDirectory(folder);
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
                })),
            files.Length == 0
                ? Ui.Panel(Ui.MutedBody("No backups yet. The first one is created when you apply a change."))
                : VStack(4, rows),
            Ui.Muted("Restoring writes the values and restarts the display driver; Windows asks for administrator permission (UAC) once."));
    }
}
