// Namespace: ajuste para o mesmo padrão do seu projeto.
// Ex.: PortalHelpdeskTI.Services (igual ao IEmailService)
namespace PortalHelpdeskTI.Services
{
    using System;
    using System.Security.Cryptography;
    using Microsoft.AspNetCore.Cryptography.KeyDerivation;

    /// <summary>
    /// Helper de senha com PBKDF2. Compatível com senhas legadas em texto puro:
    /// - Se o hash armazenado NÃO contém ":", considera que é senha pura e compara texto-a-texto.
    /// - Para novas senhas, use GerarHash (gera "salt:hash" em Base64).
    /// </summary>
    public static class Senhas
    {
        public static string GerarHash(string senha)
        {
            if (senha == null) throw new ArgumentNullException(nameof(senha));

            // 16 bytes de salt
            byte[] salt = RandomNumberGenerator.GetBytes(16);

            // PBKDF2-HMACSHA256, 100k iterações, 32 bytes
            byte[] key = KeyDerivation.Pbkdf2(
                password: senha,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 100_000,
                numBytesRequested: 32);

            return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(key)}";
        }

        public static bool Verificar(string senhaDigitada, string hashArmazenado)
        {
            if (hashArmazenado == null) return false;

            // Compatibilidade com legado (sem salt)
            if (!hashArmazenado.Contains(":"))
                return string.Equals(senhaDigitada ?? string.Empty, hashArmazenado, StringComparison.Ordinal);

            var partes = hashArmazenado.Split(':');
            if (partes.Length != 2) return false;

            byte[] salt = Convert.FromBase64String(partes[0]);
            byte[] esperado = Convert.FromBase64String(partes[1]);

            byte[] teste = KeyDerivation.Pbkdf2(
                password: senhaDigitada ?? string.Empty,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 100_000,
                numBytesRequested: 32);

            // Comparação em tempo constante
            return CryptographicOperations.FixedTimeEquals(esperado, teste);
        }
    }
}
