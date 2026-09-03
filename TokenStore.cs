using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public static class TokenStore
{
    // Старая запись из версии с одним аккаунтом.
    // Нужна только для автоматического переноса старой авторизации.
    private const string LegacyTarget = "MySteamDownloader/SteamAuth";

    // Здесь хранится список аккаунтов и выбранный аккаунт.
    private const string IndexTarget =
        "MySteamDownloader/SteamAuth/Accounts";

    // Отдельная запись Credential Manager для каждого аккаунта.
    private const string AccountTargetPrefix =
        "MySteamDownloader/SteamAuth/Account/";

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    public sealed class AccountInfo
    {
        public string Username { get; init; } = "";
        public bool IsActive { get; init; }
    }

    private sealed class StoredTokens
    {
        public string Username { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }

    private sealed class AccountIndex
    {
        public List<string> Usernames { get; set; } = new();
        public string? ActiveUsername { get; set; }
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

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern bool CredWrite(
        ref CREDENTIAL userCredential,
        uint flags);

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credentialPtr);

    [DllImport(
        "advapi32.dll",
        SetLastError = true)]
    private static extern void CredFree(
        IntPtr credential);

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern bool CredDelete(
        string target,
        uint type,
        uint flags);

    // ============================================================
    // SAVE
    // ============================================================

    // Старые вызовы TokenStore.Save(...) можно не менять.
    // Теперь метод сохраняет отдельный аккаунт и делает его выбранным.
    public static void Save(
        string username,
        string accessToken,
        string refreshToken)
    {
        EnsureMigrated();

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException(
                "Имя Steam-аккаунта не указано.",
                nameof(username));
        }

        var stored =
            new StoredTokens
            {
                Username = username,
                AccessToken = accessToken,
                RefreshToken = refreshToken
            };

        WriteJsonCredential(
            GetAccountTarget(username),
            username,
            stored);

        AccountIndex index =
            LoadIndex();

        string? existing =
            index.Usernames.FirstOrDefault(
                x => x.Equals(
                    username,
                    StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            index.Usernames.Add(username);
        }
        else if (!string.Equals(
                     existing,
                     username,
                     StringComparison.Ordinal))
        {
            int position =
                index.Usernames.IndexOf(existing);

            index.Usernames[position] =
                username;
        }

        // Последний вошедший аккаунт становится выбранным.
        index.ActiveUsername =
            username;

        SaveIndex(index);
    }

    // ============================================================
    // LOAD ACTIVE ACCOUNT
    // ============================================================

    // Старый TryLoad(...) сохраняется.
    // Теперь он загружает именно выбранный аккаунт.
    public static bool TryLoad(
        out string? username,
        out string? accessToken,
        out string? refreshToken)
    {
        EnsureMigrated();

        username = null;
        accessToken = null;
        refreshToken = null;

        AccountIndex index =
            LoadIndex();

        if (string.IsNullOrWhiteSpace(
                index.ActiveUsername))
        {
            return false;
        }

        return TryLoad(
            index.ActiveUsername,
            out username,
            out accessToken,
            out refreshToken);
    }

    // ============================================================
    // LOAD SPECIFIC ACCOUNT
    // ============================================================

    public static bool TryLoad(
        string usernameToLoad,
        out string? username,
        out string? accessToken,
        out string? refreshToken)
    {
        EnsureMigrated();

        username = null;
        accessToken = null;
        refreshToken = null;

        if (string.IsNullOrWhiteSpace(
                usernameToLoad))
        {
            return false;
        }

        if (!TryReadJsonCredential(
                GetAccountTarget(usernameToLoad),
                out StoredTokens? stored) ||
            stored == null)
        {
            return false;
        }

        username =
            stored.Username;

        accessToken =
            stored.AccessToken;

        refreshToken =
            stored.RefreshToken;

        return
            !string.IsNullOrWhiteSpace(username) &&
            !string.IsNullOrWhiteSpace(refreshToken);
    }

    // ============================================================
    // ACCOUNT LIST
    // ============================================================

    public static IReadOnlyList<AccountInfo> GetAccounts()
    {
        EnsureMigrated();

        AccountIndex index =
            LoadIndex();

        var validAccounts =
            new List<string>();

        foreach (string username
                 in index.Usernames)
        {
            if (TryReadJsonCredential(
                    GetAccountTarget(username),
                    out StoredTokens? stored) &&
                stored != null &&
                !string.IsNullOrWhiteSpace(
                    stored.Username))
            {
                validAccounts.Add(
                    stored.Username);
            }
        }

        bool changed =
            validAccounts.Count !=
            index.Usernames.Count;

        if (!changed)
        {
            for (int i = 0;
                 i < validAccounts.Count;
                 i++)
            {
                if (!validAccounts[i].Equals(
                        index.Usernames[i],
                        StringComparison.OrdinalIgnoreCase))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(
                index.ActiveUsername) &&
            !validAccounts.Any(
                x => x.Equals(
                    index.ActiveUsername,
                    StringComparison.OrdinalIgnoreCase)))
        {
            index.ActiveUsername =
                validAccounts.FirstOrDefault();

            changed = true;
        }

        if (changed)
        {
            index.Usernames =
                validAccounts;

            SaveIndex(index);
        }

        return validAccounts
            .Select(
                username =>
                    new AccountInfo
                    {
                        Username = username,
                        IsActive =
                            !string.IsNullOrWhiteSpace(
                                index.ActiveUsername) &&
                            username.Equals(
                                index.ActiveUsername,
                                StringComparison.OrdinalIgnoreCase)
                    })
            .ToList();
    }

    // ============================================================
    // ACTIVE ACCOUNT
    // ============================================================

    public static string? GetActiveUsername()
    {
        EnsureMigrated();

        AccountIndex index =
            LoadIndex();

        return string.IsNullOrWhiteSpace(
            index.ActiveUsername)
            ? null
            : index.ActiveUsername;
    }

    public static bool SetActive(
        string username)
    {
        EnsureMigrated();

        if (!TryLoad(
                username,
                out string? storedUsername,
                out _,
                out _))
        {
            return false;
        }

        AccountIndex index =
            LoadIndex();

        index.ActiveUsername =
            storedUsername;

        SaveIndex(index);

        return true;
    }

    // ============================================================
    // DELETE
    // ============================================================

    // Для совместимости со старым Program.cs:
    // Delete() удаляет только выбранный аккаунт.
    public static void Delete()
    {
        string? activeUsername =
            GetActiveUsername();

        if (!string.IsNullOrWhiteSpace(
                activeUsername))
        {
            Delete(activeUsername);
        }
    }

    public static bool Delete(
        string username)
    {
        EnsureMigrated();

        if (string.IsNullOrWhiteSpace(
                username))
        {
            return false;
        }

        AccountIndex index =
            LoadIndex();

        string? storedName =
            index.Usernames.FirstOrDefault(
                x => x.Equals(
                    username,
                    StringComparison.OrdinalIgnoreCase));

        if (storedName == null)
            return false;

        CredDelete(
            GetAccountTarget(storedName),
            CRED_TYPE_GENERIC,
            0);

        index.Usernames.RemoveAll(
            x => x.Equals(
                storedName,
                StringComparison.OrdinalIgnoreCase));

        // Если удалили активный аккаунт —
        // выбираем первый оставшийся.
        if (!string.IsNullOrWhiteSpace(
                index.ActiveUsername) &&
            index.ActiveUsername.Equals(
                storedName,
                StringComparison.OrdinalIgnoreCase))
        {
            index.ActiveUsername =
                index.Usernames.FirstOrDefault();
        }

        SaveIndex(index);

        return true;
    }

    public static void DeleteAll()
    {
        EnsureMigrated();

        AccountIndex index =
            LoadIndex();

        foreach (string username
                 in index.Usernames)
        {
            CredDelete(
                GetAccountTarget(username),
                CRED_TYPE_GENERIC,
                0);
        }

        CredDelete(
            IndexTarget,
            CRED_TYPE_GENERIC,
            0);
    }

    // ============================================================
    // INDEX
    // ============================================================

    private static AccountIndex LoadIndex()
    {
        if (TryReadJsonCredential(
                IndexTarget,
                out AccountIndex? index) &&
            index != null)
        {
            index.Usernames ??=
                new List<string>();

            return index;
        }

        return new AccountIndex();
    }

    private static void SaveIndex(
        AccountIndex index)
    {
        if (index.Usernames.Count == 0 &&
            string.IsNullOrWhiteSpace(
                index.ActiveUsername))
        {
            CredDelete(
                IndexTarget,
                CRED_TYPE_GENERIC,
                0);

            return;
        }

        WriteJsonCredential(
            IndexTarget,
            "Steam accounts",
            index);
    }

    // ============================================================
    // MIGRATION FROM OLD VERSION
    // ============================================================

    private static void EnsureMigrated()
    {
        // Если новый индекс уже существует,
        // значит новая система уже используется.
        if (CredRead(
                IndexTarget,
                CRED_TYPE_GENERIC,
                0,
                out IntPtr indexCredential))
        {
            CredFree(
                indexCredential);

            return;
        }

        // Проверяем старую одиночную запись.
        if (!TryReadJsonCredential(
                LegacyTarget,
                out StoredTokens? oldTokens) ||
            oldTokens == null ||
            string.IsNullOrWhiteSpace(
                oldTokens.Username))
        {
            return;
        }

        // Переносим токены в новую отдельную запись.
        WriteJsonCredential(
            GetAccountTarget(
                oldTokens.Username),
            oldTokens.Username,
            oldTokens);

        var index =
            new AccountIndex
            {
                Usernames =
                    new List<string>
                    {
                        oldTokens.Username
                    },

                ActiveUsername =
                    oldTokens.Username
            };

        SaveIndex(index);

        // Старую запись удаляем только
        // после успешного переноса.
        CredDelete(
            LegacyTarget,
            CRED_TYPE_GENERIC,
            0);
    }

    // ============================================================
    // CREDENTIAL TARGET
    // ============================================================

    private static string GetAccountTarget(
        string username)
    {
        // Используем SHA256 от логина, чтобы TargetName
        // всегда был безопасным и одинаковым.
        byte[] hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    username
                        .Trim()
                        .ToLowerInvariant()));

        return
            AccountTargetPrefix +
            Convert.ToHexString(hash);
    }

    // ============================================================
    // WRITE CREDENTIAL
    // ============================================================

    private static void WriteJsonCredential<T>(
        string target,
        string username,
        T value)
    {
        string json =
            JsonSerializer.Serialize(value);

        byte[] data =
            Encoding.UTF8.GetBytes(json);

        IntPtr blob =
            Marshal.AllocCoTaskMem(
                data.Length);

        try
        {
            Marshal.Copy(
                data,
                0,
                blob,
                data.Length);

            var credential =
                new CREDENTIAL
                {
                    Type =
                        CRED_TYPE_GENERIC,

                    TargetName =
                        target,

                    UserName =
                        username,

                    CredentialBlobSize =
                        (uint)data.Length,

                    CredentialBlob =
                        blob,

                    Persist =
                        CRED_PERSIST_LOCAL_MACHINE
                };

            if (!CredWrite(
                    ref credential,
                    0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(
                blob);
        }
    }

    // ============================================================
    // READ CREDENTIAL
    // ============================================================

    private static bool TryReadJsonCredential<T>(
        string target,
        out T? value)
    {
        value = default;

        if (!CredRead(
                target,
                CRED_TYPE_GENERIC,
                0,
                out IntPtr credentialPtr))
        {
            return false;
        }

        try
        {
            var credential =
                Marshal.PtrToStructure<CREDENTIAL>(
                    credentialPtr);

            if (credential.CredentialBlob ==
                    IntPtr.Zero ||
                credential.CredentialBlobSize ==
                    0)
            {
                return false;
            }

            byte[] data =
                new byte[
                    credential.CredentialBlobSize];

            Marshal.Copy(
                credential.CredentialBlob,
                data,
                0,
                (int)credential.CredentialBlobSize);

            string json =
                Encoding.UTF8.GetString(
                    data);

            value =
                JsonSerializer.Deserialize<T>(
                    json);

            return value != null;
        }
        catch
        {
            return false;
        }
        finally
        {
            CredFree(
                credentialPtr);
        }
    }
}
