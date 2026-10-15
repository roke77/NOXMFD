using System;
using System.IO;

namespace NOXMFD.Tests;

public sealed class KillLogTests
{
    private static readonly DateTime T = new DateTime(2026, 10, 18, 20, 5, 9);

    [Fact]
    public void Row_FormatsColumnsAndLeavesMissingFieldsBlank()
    {
        string row = KillLog.Row(T, null, null, "T-90", "Primeva", AkfKillKind.Vehicle, null);
        Assert.Equal("2026-10-18 20:05:09,,,T-90,Primeva,vehicle,", row);
    }

    [Fact]
    public void Cell_QuotesDelimitersAndNeutralizesFormulas()
    {
        Assert.Equal("\"Viper, 1-1\"", KillLog.Cell("Viper, 1-1"));
        Assert.Equal("\"say \"\"hi\"\"\"", KillLog.Cell("say \"hi\""));
        Assert.Equal("'=HYPERLINK(1)", KillLog.Cell("=HYPERLINK(1)"));
        Assert.Equal("'-1", KillLog.Cell("-1"));
        Assert.Equal("F-16", KillLog.Cell("F-16"));
    }

    [Fact]
    public void FileName_StripsCharactersWindowsRejects()
    {
        string name = KillLog.FileName("Op: Red/Sky", T);
        Assert.Equal("kills_2026-10-18_20-05-09_Op__Red_Sky.csv", name);
        Assert.Equal("kills_2026-10-18_20-05-09.csv", KillLog.FileName("", T));
    }

    [Fact]
    public void Write_CreatesFileOnFirstKillWithHeaderOnlyWhenEnabled()
    {
        string dir = Path.Combine(Path.GetTempPath(), "noxmfd-killlog-" + Guid.NewGuid().ToString("N"));
        string prevDir = KillLog.Dir;
        Func<bool> prevEnabled = KillLog.Enabled;
        try
        {
            KillLog.Dir = dir;
            bool enabled = false;
            KillLog.Enabled = () => enabled;
            var log = new KillLog();

            log.Write(T, "M", "A", "Boscali", "B", "Primeva", AkfKillKind.Aircraft, "AIM-9X");
            Assert.False(Directory.Exists(dir));

            enabled = true;
            log.Write(T, "M", "A", "Boscali", "B", "Primeva", AkfKillKind.Aircraft, "AIM-9X");
            log.Write(T, "M", null, null, "C", "Primeva", AkfKillKind.Building, null);
            log.Dispose();

            string[] lines = File.ReadAllLines(Path.Combine(dir, KillLog.FileName("M", T)));
            Assert.Equal(new[]
            {
                KillLog.Header,
                "2026-10-18 20:05:09,A,Boscali,B,Primeva,aircraft,AIM-9X",
                "2026-10-18 20:05:09,,,C,Primeva,building,",
            }, lines);
        }
        finally
        {
            KillLog.Dir = prevDir;
            KillLog.Enabled = prevEnabled;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
