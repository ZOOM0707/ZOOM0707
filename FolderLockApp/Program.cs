using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace FolderLockApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    private readonly TextBox _folderTextBox = new();
    private readonly TextBox _passwordTextBox = new();
    private readonly TextBox _lockedFileTextBox = new();

    public MainForm()
    {
        Text = "Folder Lock";
        Width = 640;
        Height = 320;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var folderLabel = new Label { Text = "Folder to lock:", AutoSize = true, Top = 20, Left = 20 };
        _folderTextBox.SetBounds(20, 45, 460, 25);
        var browseFolderButton = new Button { Text = "Browse", Top = 43, Left = 500, Width = 100 };
        browseFolderButton.Click += (_, _) => BrowseFolder();

        var passwordLabel = new Label { Text = "Password:", AutoSize = true, Top = 85, Left = 20 };
        _passwordTextBox.SetBounds(20, 110, 460, 25);
        _passwordTextBox.PasswordChar = '●';

        var lockButton = new Button { Text = "Lock folder", Top = 145, Left = 20, Width = 140 };
        lockButton.Click += (_, _) => LockFolder();

        var lockedFileLabel = new Label { Text = "Locked file (.locked):", AutoSize = true, Top = 190, Left = 20 };
        _lockedFileTextBox.SetBounds(20, 215, 460, 25);
        var browseLockedButton = new Button { Text = "Browse", Top = 213, Left = 500, Width = 100 };
        browseLockedButton.Click += (_, _) => BrowseLockedFile();

        var unlockButton = new Button { Text = "Unlock folder", Top = 250, Left = 20, Width = 140 };
        unlockButton.Click += (_, _) => UnlockFolder();

        Controls.AddRange(
        [
            folderLabel,
            _folderTextBox,
            browseFolderButton,
            passwordLabel,
            _passwordTextBox,
            lockButton,
            lockedFileLabel,
            _lockedFileTextBox,
            browseLockedButton,
            unlockButton
        ]);
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void BrowseLockedFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Locked Files (*.locked)|*.locked|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _lockedFileTextBox.Text = dialog.FileName;
        }
    }

    private void LockFolder()
    {
        var folderPath = _folderTextBox.Text.Trim();
        var password = _passwordTextBox.Text;

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            MessageBox.Show(this, "Select a valid folder.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show(this, "Enter a password.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var lockedFilePath = folderPath.TrimEnd(Path.DirectorySeparatorChar) + ".locked";
        if (File.Exists(lockedFilePath))
        {
            MessageBox.Show(this, "A locked file already exists for this folder.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
            ZipFile.CreateFromDirectory(folderPath, tempZipPath, CompressionLevel.Optimal, true);

            FolderLockCrypto.EncryptFile(tempZipPath, lockedFilePath, password);

            File.Delete(tempZipPath);
            Directory.Delete(folderPath, true);

            MessageBox.Show(this, "Folder locked.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to lock folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UnlockFolder()
    {
        var lockedFilePath = _lockedFileTextBox.Text.Trim();
        var password = _passwordTextBox.Text;

        if (string.IsNullOrWhiteSpace(lockedFilePath) || !File.Exists(lockedFilePath))
        {
            MessageBox.Show(this, "Select a valid locked file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show(this, "Enter the password.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var folderPath = lockedFilePath[..^".locked".Length];
        if (Directory.Exists(folderPath))
        {
            MessageBox.Show(this, "Target folder already exists. Move or delete it first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
            FolderLockCrypto.DecryptFile(lockedFilePath, tempZipPath, password);

            ZipFile.ExtractToDirectory(tempZipPath, folderPath);
            File.Delete(tempZipPath);
            File.Delete(lockedFilePath);

            MessageBox.Show(this, "Folder unlocked.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (CryptographicException)
        {
            MessageBox.Show(this, "Incorrect password or corrupted file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to unlock folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal static class FolderLockCrypto
{
    private static readonly byte[] Magic = Encoding.UTF8.GetBytes("FLOCK1");
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32;
    private const int HmacKeySize = 32;
    private const int Iterations = 150_000;

    public static void EncryptFile(string inputPath, string outputPath, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var keyMaterial = DeriveKeyMaterial(password, salt);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = keyMaterial[..KeySize];
        aes.IV = iv;

        using var inputStream = File.OpenRead(inputPath);
        using var outputStream = File.Create(outputPath);
        outputStream.Write(Magic, 0, Magic.Length);
        outputStream.Write(salt, 0, salt.Length);
        outputStream.Write(iv, 0, iv.Length);

        using var encryptor = aes.CreateEncryptor();
        using var cryptoStream = new CryptoStream(outputStream, encryptor, CryptoStreamMode.Write);
        inputStream.CopyTo(cryptoStream);
        cryptoStream.FlushFinalBlock();

        var hmacKey = keyMaterial[KeySize..(KeySize + HmacKeySize)];
        outputStream.Flush();
        outputStream.Position = 0;
        using var hmac = new HMACSHA256(hmacKey);
        var tag = hmac.ComputeHash(outputStream);
        outputStream.Position = outputStream.Length;
        outputStream.Write(tag, 0, tag.Length);
    }

    public static void DecryptFile(string inputPath, string outputPath, string password)
    {
        using var inputStream = File.OpenRead(inputPath);
        var header = new byte[Magic.Length];
        ReadExact(inputStream, header);
        if (!header.SequenceEqual(Magic))
        {
            throw new CryptographicException("Invalid file format.");
        }

        var salt = new byte[SaltSize];
        ReadExact(inputStream, salt);
        var iv = new byte[IvSize];
        ReadExact(inputStream, iv);

        var keyMaterial = DeriveKeyMaterial(password, salt);
        var hmacKey = keyMaterial[KeySize..(KeySize + HmacKeySize)];

        var tagLength = 32;
        if (inputStream.Length < Magic.Length + SaltSize + IvSize + tagLength)
        {
            throw new CryptographicException("Invalid file size.");
        }

        var cipherLength = inputStream.Length - Magic.Length - SaltSize - IvSize - tagLength;
        var cipherStart = inputStream.Position;

        inputStream.Position = 0;
        using var hmac = new HMACSHA256(hmacKey);
        var computedTag = hmac.ComputeHash(inputStream, 0, (int)(inputStream.Length - tagLength));
        inputStream.Position = inputStream.Length - tagLength;
        var storedTag = new byte[tagLength];
        ReadExact(inputStream, storedTag);
        if (!CryptographicOperations.FixedTimeEquals(computedTag, storedTag))
        {
            throw new CryptographicException("Invalid password or corrupted file.");
        }

        inputStream.Position = cipherStart;
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = keyMaterial[..KeySize];
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        using var cryptoStream = new CryptoStream(inputStream, decryptor, CryptoStreamMode.Read);
        using var outputStream = File.Create(outputPath);
        cryptoStream.CopyTo(outputStream);
    }

    private static byte[] DeriveKeyMaterial(string password, byte[] salt)
    {
        using var deriveBytes = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        return deriveBytes.GetBytes(KeySize + HmacKeySize);
    }

    private static void ReadExact(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of file.");
            }
            offset += read;
        }
    }
}
