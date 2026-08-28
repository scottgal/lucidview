using System.Diagnostics;

namespace MarkdownViewer.Services;

/// <summary>
/// Sends a generated PDF to the OS default printer. Cross-platform: ShellExecute "print"
/// verb on Windows, <c>lp</c> (CUPS) on macOS and Linux. There's no native printer-picker
/// dialog — this prints to whatever the user has set as the default. A dedicated printer
/// dialog would need a heavy platform-interop layer that's out of scope here.
/// </summary>
public static class PrintService
{
    public static async Task PrintAsync(string pdfPath, CancellationToken ct = default)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("PDF to print does not exist", pdfPath);

        if (OperatingSystem.IsWindows())
        {
            // ShellExecute with the "print" verb hands the file to the registered handler
            // (Acrobat / Edge / Sumatra / etc.) which silently prints to the default printer.
            var psi = new ProcessStartInfo(pdfPath)
            {
                Verb = "print",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            // ShellExecute spawns the handler process; we don't wait for it.
            await Task.CompletedTask;
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            // CUPS lp ships with macOS and most Linux desktop distros.
            var psi = new ProcessStartInfo("lp", $"\"{pdfPath}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start lp — is CUPS installed?");
            await p.WaitForExitAsync(ct);

            if (p.ExitCode != 0)
            {
                var stderr = await p.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException(
                    $"lp exited with code {p.ExitCode}: {stderr.Trim()}");
            }
        }
        else
        {
            throw new PlatformNotSupportedException(
                "Printing is supported on Windows, macOS, and Linux only.");
        }
    }
}
