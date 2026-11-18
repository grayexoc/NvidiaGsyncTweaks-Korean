using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using NvidiaGsyncTweaks.Services;
using static Microsoft.UI.Reactor.Factories;

namespace NvidiaGsyncTweaks.UI;

internal static class BackupsPage
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
                    Button("보기", () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{f}\"") { UseShellExecute = true })),
                    Ui.ElevatedButton("복원 후 드라이버 다시 시작", () => restore(f), enabled: c.Gpu?.KeyPath is not null && c.Gpu.Adapter is not null && !c.Busy))).WithKey(f)).ToArray();

        return VStack(16,
            Ui.PageHeader("백업",
                "이 앱이 관리하는 모든 값의 복원 지점(.reg 파일)이 적용할 때마다 자동으로 생성됩니다. " +
                "복원하면 값이 저장 당시 상태로 정확히 되돌아가며, 당시 존재하지 않았던 값은 다시 삭제됩니다."),
            HStack(8,
                Button("백업 폴더 열기", () =>
                {
                    Directory.CreateDirectory(folder);
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
                })),
            files.Length == 0
                ? Ui.Panel(Ui.MutedBody("백업이 없습니다. 변경 사항을 처음 적용하면 백업이 자동 생성됩니다."))
                : VStack(4, rows),
            Ui.Muted("복원하면 값을 기록하고 디스플레이 드라이버를 다시 시작합니다. 이때 Windows에서 관리자 권한(UAC)을 한 번 요청합니다."));
    }
}
