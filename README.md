# FolderLockApp

A simple Windows 10 WinForms app that locks a folder behind a password by zipping and encrypting it into a `.locked` file. Use the same password to unlock and restore the folder.

## Build

```bash
dotnet build FolderLockApp/FolderLockApp.csproj
```

## Usage

1. Run the app.
2. **Lock folder**: choose a folder and set a password, then click **Lock folder**.
3. **Unlock folder**: choose the `.locked` file and enter the password, then click **Unlock folder**.

> Note: Locking deletes the original folder after it has been encrypted. Unlocking removes the `.locked` file after restoring the folder.
