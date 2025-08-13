using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            string exeFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c install-interception.exe /install",
                Verb = "runas",
                UseShellExecute = true,
                WorkingDirectory = exeFolder
            };

            Console.WriteLine("Setting folder to: " + exeFolder);
            Console.WriteLine("Running: install-interception.exe /install");
            Process proc = Process.Start(psi);
            proc.WaitForExit(); // Wait until the install command finishes

            Thread.Sleep(5000); // wait 5 seconds

            // Restart PC
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/r /t 0",
                Verb = "runas",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
