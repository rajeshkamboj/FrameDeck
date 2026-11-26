using System;
using System.IO;
using System.Net;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("FrameDeck")]
[assembly: System.Reflection.AssemblyProduct("FrameDeck")]
[assembly: System.Reflection.AssemblyDescription("Local video player for Windows")]
[assembly: System.Reflection.AssemblyVersion("1.5.0.0")]

namespace FrameDeck
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameDeck", "library.json");
            string file = args.Length == 0 ? null : Path.GetFullPath(args[0]);
            using (var window = new DeckWindow(file, state))
                Application.Run(window);
        }
    }
}
