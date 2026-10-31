using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Services;

/// <summary>Windows Credential Manager stores the refresh token for the current Windows user on this device.</summary>
internal sealed class WindowsAccountCredentialStore(string target = "Semojum/Desktop/Session") : IAccountCredentialStore
{
    private const uint Generic = 1, PersistLocalMachine = 2;
    private const int NotFound = 1168;
    private static readonly Mutex StoreGate = new(false, @"Local\SemojumAccountCredentialStore");
    private static void Locked(Action action)
    {
        try { try { StoreGate.WaitOne(); } catch (AbandonedMutexException) { } action(); }
        finally { StoreGate.ReleaseMutex(); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);

    public SessionCredential? Read()
    {
        if (!CredRead(target, Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error, "저장된 로그인 정보를 읽지 못했습니다.");
        }
        try
        {
            var record = Marshal.PtrToStructure<Credential>(pointer);
            if (record.CredentialBlobSize is 0 or > 2560) throw new InvalidDataException("저장된 로그인 정보 형식이 올바르지 않습니다.");
            var bytes = new byte[record.CredentialBlobSize];
            try
            {
                Marshal.Copy(record.CredentialBlob, bytes, 0, bytes.Length);
                var credential = JsonSerializer.Deserialize<SessionCredential>(bytes);
                if (credential is null || string.IsNullOrWhiteSpace(credential.LoginId) || string.IsNullOrWhiteSpace(credential.RefreshToken))
                    throw new InvalidDataException("저장된 로그인 정보 형식이 올바르지 않습니다.");
                return credential;
            }
            catch (JsonException) { throw new InvalidDataException("저장된 로그인 정보 형식이 올바르지 않습니다."); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { CredFree(pointer); }
    }
    public void Write(SessionCredential credential) => Locked(() => WriteCore(credential));
    private void WriteCore(SessionCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credential);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            if (bytes.Length is 0 or > 2560) throw new InvalidDataException("로그인 정보 크기가 올바르지 않습니다.");
            var record = new Credential { Type = Generic, TargetName = target, CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = handle.AddrOfPinnedObject(), Persist = PersistLocalMachine, UserName = credential.LoginId };
            if (!CredWrite(ref record, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "로그인 정보를 안전하게 저장하지 못했습니다.");
        }
        finally { handle.Free(); CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Delete() => Locked(DeleteCore);
    public void DeleteIfMatches(SessionCredential credential) => Locked(() => { if (Read() == credential) DeleteCore(); });
    private void DeleteCore()
    {
        if (!CredDelete(target, Generic, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NotFound) throw new Win32Exception(error, "저장된 로그인 정보를 삭제하지 못했습니다.");
        }
    }
}
