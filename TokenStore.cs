using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

public static class TokenStore
{
    private const string Target = "MySteamDownloader/SteamAuth";

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    private class StoredTokens
    {
        public string Username { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(
        string target,
        uint type,
        uint flags);

    public static void Save(
        string username,
        string accessToken,
        string refreshToken)
    {
        var stored = new StoredTokens
        {
            Username = username,
            AccessToken = accessToken,
            RefreshToken = refreshToken
        };

        string json = JsonSerializer.Serialize(stored);
        byte[] data = System.Text.Encoding.UTF8.GetBytes(json);

        IntPtr blob = Marshal.AllocCoTaskMem(data.Length);

        try
        {
            Marshal.Copy(data, 0, blob, data.Length);

            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = Target,
                UserName = username,
                CredentialBlobSize = (uint)data.Length,
                CredentialBlob = blob,
                Persist = CRED_PERSIST_LOCAL_MACHINE
            };

            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public static bool TryLoad(
        out string? username,
        out string? accessToken,
        out string? refreshToken)
    {
        username = null;
        accessToken = null;
        refreshToken = null;

        if (!CredRead(
                Target,
                CRED_TYPE_GENERIC,
                0,
                out IntPtr credentialPtr))
        {
            return false;
        }

        try
        {
            var credential =
                Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);

            username = credential.UserName;

            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize == 0)
            {
                return false;
            }

            byte[] data = new byte[credential.CredentialBlobSize];

            Marshal.Copy(
                credential.CredentialBlob,
                data,
                0,
                (int)credential.CredentialBlobSize);

            string json = System.Text.Encoding.UTF8.GetString(data);

            var stored = JsonSerializer.Deserialize<StoredTokens>(json);

            if (stored == null)
                return false;

            username = stored.Username;
            accessToken = stored.AccessToken;
            refreshToken = stored.RefreshToken;

            return !string.IsNullOrEmpty(username) &&
                   !string.IsNullOrEmpty(accessToken);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public static void Delete()
    {
        CredDelete(Target, CRED_TYPE_GENERIC, 0);
    }
}