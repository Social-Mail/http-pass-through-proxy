using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace PassThrough;

class Encryption
{

    private static Dictionary<string, Encryption> cache = new Dictionary<string, Encryption>();

    private byte[] key;
    private byte[] iv;

    public Encryption(string key, string encryptionIV)
    {
        this.key = System.Text.Encoding.UTF8.GetBytes(key);
        this.iv =  System.Text.Encoding.UTF8.GetBytes(encryptionIV);
    }

    public static Encryption For(string secretKey)
    {
        string host = Environment.GetEnvironmentVariable("HOST")!;

        var cacheKey = $"{host}:{secretKey}";

        if(cache.TryGetValue(cacheKey, out var ek))
        {
            return ek;
        }

        var digest = System.Security.Cryptography.SHA512
            .HashData(System.Text.Encoding.UTF8.GetBytes(secretKey));
        var key = System.Convert.ToHexString(digest).Substring(0, 32).ToLower();


        digest = System.Security.Cryptography.SHA512
               .HashData(System.Text.Encoding.UTF8.GetBytes(secretKey));
        var encryptionIV = System.Convert.ToHexString(digest).Substring(0, 16).ToLower();
        ek = new Encryption(key, encryptionIV);
        cache[cacheKey] = ek;
        return ek;
    }

    public string Decrypt(string code)
    {
        // Set up the encryption objects
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // Decrypt the input ciphertext using the AES algorithm
            using (ICryptoTransform decryptor = aes.CreateDecryptor())
            {
                var cipherBytes = System.Text.Encoding.UTF8.GetBytes(code.Replace("*", "="));
                var decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                return System.Text.Encoding.UTF8.GetString(decryptedBytes);
            }
        }

    }

}