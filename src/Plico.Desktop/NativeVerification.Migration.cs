using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyMigrationAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string evidenceDirectory)
    {
        string enex = Path.Combine(model.LibraryPath, "Native migration.enex");
        File.WriteAllText(enex, """
            <en-export><note><title>Native migration</title><tag>Migration fixture</tag>
            <created>20261024T120000Z</created>
            <content><![CDATA[<en-note><p>INCOMINGMIGRATION470</p></en-note>]]></content></note></en-export>
            """);
        dialogs.MigrationSource = enex;
        await model.MigrateEvernoteCommand.ExecuteAsync(null);
        Require(dialogs.MigrationReport is { Added: 1, Failed: 0 }, $"The desktop migration did not file the note. {model.Status}");
        var dialog = new MigrationReportDialog(dialogs.MigrationReport!);
        var opened = dialog.ShowDialog(window);
        try
        {
            await CaptureAsync(dialog, evidenceDirectory, "migration-report.png");
        }
        finally
        {
            dialog.Close();
            await opened;
        }
    }
}
