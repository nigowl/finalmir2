using System.Security.Cryptography;
using System.Text;

namespace Mir2.Companions;

public static class CompanionStorageRules
{
    public static string Password(string account,string password) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(password),Encoding.UTF8.GetBytes("Mir2.Companion.Storage/v1:"+account)))[..14];
}
