using System.Text.RegularExpressions;
using Nitido.Compartido.Resultados;

namespace Nitido.Clientes.Dominio.Clientes;

/// <summary>
/// NIT de una persona jurídica: 9 dígitos que empiezan por 8 o 9, más el dígito de verificación que nítido
/// calcula (módulo 11 con los factores de la DIAN). En la fase 0 es el único documento aceptado.
/// </summary>
public sealed partial record Nit
{
    /// <summary>Código de la DIAN del tipo de documento NIT.</summary>
    public const string TipoDocumento = "31";

    public const string CampoDelDocumento = "numeroDocumento";

    public const string MensajeSoloNit =
        "Por ahora solo puedes agregar clientes con NIT. Escribe los 9 dígitos, sin el dígito de verificación.";

    public const string MensajeDigitoIncorrecto = "El dígito de verificación no corresponde a ese NIT.";

    // Factores de la DIAN, de derecha a izquierda (el último dígito del número lleva el primer factor).
    private static readonly int[] Factores = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    private Nit(string numero, string digitoVerificacion)
    {
        Numero = numero;
        DigitoVerificacion = digitoVerificacion;
    }

    /// <summary>Los 9 dígitos, sin puntos ni dígito de verificación.</summary>
    public string Numero { get; }

    /// <summary>Un dígito, calculado por nítido.</summary>
    public string DigitoVerificacion { get; }

    /// <summary>
    /// Interpreta lo que escribió la persona: <c>800197268</c>, <c>800.197.268</c> o <c>800197268-4</c>. Guarda solo
    /// los dígitos y calcula el dígito de verificación; si llegó uno y no corresponde, es un error de validación.
    /// </summary>
    public static Resultado<Nit> Crear(string? texto)
    {
        var coincidencia = FormatoDelNit().Match((texto ?? string.Empty).Trim());
        if (!coincidencia.Success)
        {
            return ErrorDelDocumento(MensajeSoloNit);
        }

        var numero = coincidencia.Groups["numero"].Value.Replace(".", string.Empty, StringComparison.Ordinal);
        if (numero[0] is not ('8' or '9'))
        {
            return ErrorDelDocumento(MensajeSoloNit);
        }

        var digito = Calcular(numero);
        if (coincidencia.Groups["digito"].Success && coincidencia.Groups["digito"].Value != digito)
        {
            return ErrorDelDocumento(MensajeDigitoIncorrecto);
        }

        return new Nit(numero, digito);
    }

    /// <summary>Calcula el dígito de verificación de un número de hasta 15 dígitos (algoritmo de la DIAN).</summary>
    public static string Calcular(string numero)
    {
        ArgumentException.ThrowIfNullOrEmpty(numero);
        if (numero.Length > Factores.Length || !numero.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("El NIT debe tener solo dígitos, hasta 15.", nameof(numero));
        }

        var suma = 0;
        for (var i = 0; i < numero.Length; i++)
        {
            suma += (numero[numero.Length - 1 - i] - '0') * Factores[i];
        }

        var residuo = suma % 11;
        return (residuo > 1 ? 11 - residuo : residuo).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Error ErrorDelDocumento(string mensaje) =>
        Error.Validacion(new Dictionary<string, string[]> { [CampoDelDocumento] = [mensaje] });

    [GeneratedRegex(@"^(?<numero>\d{9}|\d{3}\.\d{3}\.\d{3})(-(?<digito>\d))?$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoDelNit();
}
