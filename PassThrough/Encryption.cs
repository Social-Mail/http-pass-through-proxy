using System;

namespace PassThrough;

class Encryption
{

    object CreateKey(string secretKey)
    {
        var digest = System.Security.Cryptography.SHA512
            .HashData(System.Text.Encoding.UTF8.GetBytes(secretKey));
        var key = System.Convert.ToHexString(digest).Substring(0, 32);

        string host = Environment.GetEnvironmentVariable("HOST")!;

        System.Security.Cryptography.SHA512
            .HashData(System.Text.Encoding.UTF8.GetBytes(secretKey))

        var encryptionIV = 
            
    }

}