using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nitido.Firmas.Infraestructura.Autenticacion;

/// <summary>
/// Contraseña de un solo uso basada en el tiempo (TOTP, RFC 6238): 6 dígitos, paso de 30 segundos, HMAC-SHA1
/// (el algoritmo que esperan Google Authenticator, Microsoft Authenticator y similares). Sin librerías externas.
/// </summary>
internal static class Totp
{
    public const int Digitos = 6;
    public const int PeriodoEnSegundos = 30;

    /// <summary>Pasos de tolerancia a cada lado del actual: 1 paso = 30 segundos por reloj desfasado.</summary>
    public const int VentanaDeTolerancia = 1;

    private const int LargoDelSecreto = 20;

    public static byte[] GenerarSecreto() => RandomNumberGenerator.GetBytes(LargoDelSecreto);

    public static long PasoDe(DateTimeOffset instante) => instante.ToUnixTimeSeconds() / PeriodoEnSegundos;

    /// <summary>Calcula el código de un paso de tiempo (HOTP, RFC 4226, con el paso como contador).</summary>
    public static string Codigo(ReadOnlySpan<byte> secreto, long paso)
    {
        Span<byte> contador = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(contador, paso);

        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // RFC 6238: las aplicaciones autenticadoras usan HMAC-SHA1; no protege nada por sí solo, el secreto va cifrado.
        HMACSHA1.HashData(secreto, contador, hash);
#pragma warning restore CA5350

        var desplazamiento = hash[^1] & 0x0F;
        var binario = ((hash[desplazamiento] & 0x7F) << 24)
            | (hash[desplazamiento + 1] << 16)
            | (hash[desplazamiento + 2] << 8)
            | hash[desplazamiento + 3];
        return (binario % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Verifica un código. Acepta el paso actual y uno a cada lado, y rechaza cualquier paso que no sea posterior a
    /// <paramref name="ultimoPasoUsado"/>, para que un código ya usado no sirva otra vez aunque siga en su ventana.
    /// </summary>
    /// <returns>El paso que coincidió, o <c>null</c> si el código no es válido.</returns>
    public static long? Verificar(ReadOnlySpan<byte> secreto, string codigo, DateTimeOffset ahora, long? ultimoPasoUsado)
    {
        if (codigo.Length != Digitos || !codigo.All(char.IsAsciiDigit))
        {
            return null;
        }

        var recibido = Encoding.ASCII.GetBytes(codigo);
        var actual = PasoDe(ahora);
        long? coincidencia = null;

        // Se recorren todos los pasos de la ventana, sin cortar al primer acierto, para no filtrar cuál coincidió.
        for (var paso = actual - VentanaDeTolerancia; paso <= actual + VentanaDeTolerancia; paso++)
        {
            var esperado = Encoding.ASCII.GetBytes(Codigo(secreto, paso));
            if (CryptographicOperations.FixedTimeEquals(recibido, esperado) && (ultimoPasoUsado is null || paso > ultimoPasoUsado))
            {
                coincidencia = paso;
            }
        }

        return coincidencia;
    }

    /// <summary>URI <c>otpauth://totp/...</c> que leen las aplicaciones autenticadoras (formato de Google Authenticator).</summary>
    public static string UriOtpauth(string emisor, string cuenta, string secretoBase32) =>
        $"otpauth://totp/{Uri.EscapeDataString(emisor)}:{Uri.EscapeDataString(cuenta)}"
        + $"?secret={secretoBase32}&issuer={Uri.EscapeDataString(emisor)}&algorithm=SHA1&digits={Digitos}&period={PeriodoEnSegundos}";
}
