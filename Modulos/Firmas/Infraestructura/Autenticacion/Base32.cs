using System.Text;

namespace Nitido.Firmas.Infraestructura.Autenticacion;

/// <summary>Base32 de la RFC 4648 (alfabeto A–Z y 2–7), sin relleno: el formato de las claves TOTP.</summary>
internal static class Base32
{
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Codificar(ReadOnlySpan<byte> datos)
    {
        var resultado = new StringBuilder((datos.Length * 8 + 4) / 5);
        var acumulado = 0;
        var bits = 0;
        foreach (var octeto in datos)
        {
            acumulado = (acumulado << 8) | octeto;
            bits += 8;
            while (bits >= 5)
            {
                resultado.Append(Alfabeto[(acumulado >> (bits - 5)) & 31]);
                bits -= 5;
                acumulado &= (1 << bits) - 1;
            }
        }

        if (bits > 0)
        {
            resultado.Append(Alfabeto[(acumulado << (5 - bits)) & 31]);
        }

        return resultado.ToString();
    }

    public static byte[] Decodificar(string texto)
    {
        var resultado = new List<byte>(texto.Length * 5 / 8);
        var acumulado = 0;
        var bits = 0;
        foreach (var caracter in texto.Trim().TrimEnd('=').ToUpperInvariant())
        {
            var valor = Alfabeto.IndexOf(caracter, StringComparison.Ordinal);
            if (valor < 0)
            {
                throw new FormatException($"El carácter «{caracter}» no es base32.");
            }

            acumulado = (acumulado << 5) | valor;
            bits += 5;
            if (bits >= 8)
            {
                resultado.Add((byte)((acumulado >> (bits - 8)) & 0xFF));
                bits -= 8;
                acumulado &= (1 << bits) - 1;
            }
        }

        return [.. resultado];
    }
}
