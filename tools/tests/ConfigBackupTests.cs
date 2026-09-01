using System;
using System.IO;
using NOXMFD;

namespace NOXMFD.Tests
{
    public class ConfigBackupTests
    {
        [Fact]
        public void CopiesCurrentContentToDotBak()
        {
            string path = Path.GetTempFileName();
            string bak = path + ".bak";
            try
            {
                File.WriteAllText(path, "v1");
                ConfigBackup.BackupIfExists(path);
                Assert.True(File.Exists(bak));
                Assert.Equal("v1", File.ReadAllText(bak));
            }
            finally { File.Delete(path); File.Delete(bak); }
        }

        [Fact]
        public void OverwritesThePreviousBackupOnEachCall()
        {
            string path = Path.GetTempFileName();
            string bak = path + ".bak";
            try
            {
                File.WriteAllText(path, "v1");
                ConfigBackup.BackupIfExists(path);
                File.WriteAllText(path, "v2");
                ConfigBackup.BackupIfExists(path);
                Assert.Equal("v2", File.ReadAllText(bak));
            }
            finally { File.Delete(path); File.Delete(bak); }
        }

        [Fact]
        public void NoopsWhenTheSourceFileDoesNotExist()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            ConfigBackup.BackupIfExists(path);
            Assert.False(File.Exists(path + ".bak"));
        }
    }
}
