namespace KareNot;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(e.ExceptionObject as Exception);
        if (!LibVlc.TryInit(out var error))
        {
            MessageBox.Show(error, "KareNot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (args.Length == 3 && args[0] is "--selftest" or "--profile-area")
        {
            var form = new MainForm(null);
            form.Shown += async (s, e) =>
            {
                if (args[0] == "--selftest") await form.RunSelfTest(args[1], args[2]);
                else await form.ProfileArea(args[1], args[2]);
            };
            Application.Run(form);
            return;
        }

        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
    }

    static void ReportCrash(Exception ex)
    {
        var path = Path.Combine(Path.GetTempPath(), "karenot_hata.txt");
        try { File.AppendAllText(path, DateTime.Now + "\n" + ex + "\n\n"); } catch { }
        MessageBox.Show("Beklenmeyen hata (ayrıntı: " + path + "):\n\n" + ex?.Message, "KareNot", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
