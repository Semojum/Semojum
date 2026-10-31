using Semojum.Core.Integration;

namespace Semojum.Feature.Account;

public interface IAccountCredentialStore
{
    SessionCredential? Read();
    void Write(SessionCredential credential);
    void Delete();
    void DeleteIfMatches(SessionCredential credential);
}
