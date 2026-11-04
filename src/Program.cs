
namespace AzimuthFH3;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--self-test") return SelfTest.Run(args[1], args[2]);
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Azimuth FH3 Save Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        string? initial = args.FirstOrDefault();
        Application.Run(new EditorForm(initial)); return 0;
    }
}
