using System.Security.Cryptography;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Genera la clave del usuario <c>sa</c> del SQL Server de pruebas: distinta en cada corrida, con aleatoriedad
/// criptográfica y cumpliendo la política de complejidad de SQL Server. Nunca contiene <c>;</c>, <c>=</c>,
/// comillas ni espacios, que romperían una cadena de conexión, ni <c>-</c>: Testcontainers pasa la clave a
/// <c>sqlcmd</c> como <c>-P &lt;clave&gt;</c> y una clave que empezara con <c>-</c> se leería como otra opción.
/// La clave no se imprime en ningún registro.
/// </summary>
public static class GeneradorDeClaveSql
{
    public const int Longitud = 32;

    public const string Mayusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    public const string Minusculas = "abcdefghijkmnopqrstuvwxyz";
    public const string Digitos = "23456789";
    public const string Simbolos = "_.!*+";

    private static readonly string[] Grupos = [Mayusculas, Minusculas, Digitos, Simbolos];
    private static readonly string Todos = string.Concat(Grupos);

    public static string Generar()
    {
        var caracteres = new char[Longitud];

        // Al menos un carácter de cada grupo; el resto sale del alfabeto completo.
        for (var i = 0; i < caracteres.Length; i++)
        {
            var alfabeto = i < Grupos.Length ? Grupos[i] : Todos;
            caracteres[i] = alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)];
        }

        // Fisher-Yates con aleatoriedad criptográfica, para que los grupos no queden siempre al inicio.
        for (var i = caracteres.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (caracteres[i], caracteres[j]) = (caracteres[j], caracteres[i]);
        }

        return new string(caracteres);
    }
}
