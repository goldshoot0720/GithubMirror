using System.Text;
using GithubMirror.Models;
using GithubMirror.Services;
using Xunit;

namespace GithubMirror.Tests;

public sealed class EncryptedBackupTests
{
    private const string Password = "1234";
    private static BackupData Sample() => new()
    {
        HideTutorial = true,
        NameTemplate = "backup-{name}",
        Accounts = new() { new AccountCredential { Token = "private-token-do-not-upload", GitHttpsPassword = "git-secret" } }
    };

    [Fact]
    public void Backup_is_portable_and_contains_no_plaintext_secrets()
    {
        var encrypted = EncryptedBackup.Encrypt(Sample(), Password);
        Assert.DoesNotContain("private-token", Encoding.UTF8.GetString(encrypted));
        var restored = EncryptedBackup.Decrypt(encrypted, Password);
        Assert.Equal("private-token-do-not-upload", restored.Accounts[0].Token);
        Assert.Equal("git-secret", restored.Accounts[0].GitHttpsPassword);
        Assert.True(restored.HideTutorial);
        Assert.Equal("backup-{name}", restored.NameTemplate);
        Assert.NotEqual(encrypted, EncryptedBackup.Encrypt(Sample(), Password));
    }

    [Fact]
    public void Wrong_password_and_tampering_are_rejected()
    {
        var bytes = EncryptedBackup.Encrypt(Sample(), Password);
        Assert.Throws<InvalidDataException>(() => EncryptedBackup.Decrypt(bytes, "5678"));
        foreach (var index in new[] { 0, 8, 24, 36, bytes.Length - 1 })
        {
            var changed = bytes.ToArray();
            changed[index] ^= 1;
            Assert.Throws<InvalidDataException>(() => EncryptedBackup.Decrypt(changed, Password));
        }
    }

    [Fact]
    public void Invalid_files_and_non_four_digit_pins_are_rejected()
    {
        foreach (var pin in new[] { "", "123", "12345", "12ab", "abcd", "12 4", "１２３４" })
        {
            Assert.Throws<ArgumentException>(() => EncryptedBackup.Encrypt(Sample(), pin));
            Assert.Throws<ArgumentException>(() => EncryptedBackup.Decrypt(new byte[4], pin));
        }
        Assert.Throws<InvalidDataException>(() => EncryptedBackup.Decrypt(new byte[4], Password));
        Assert.Throws<InvalidDataException>(() => EncryptedBackup.Decrypt(new byte[EncryptedBackup.MaximumFileSize + 1], Password));
        var invalid = Sample();
        invalid.Accounts.Add(invalid.Accounts[0]);
        Assert.Throws<InvalidDataException>(() => EncryptedBackup.Encrypt(invalid, Password));
    }
}
