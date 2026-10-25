using Microsoft.Extensions.Configuration;

namespace Nitido.Firmas.Infraestructura.Autenticacion;

/// <summary>Valores de seguridad de la autenticación, leídos de la sección <c>Seguridad</c> de la configuración.</summary>
internal sealed class OpcionesDeSeguridadDeFirmas
{
    public const int IntentosMaximosPorDefecto = 5;
    public const int MinutosDeBloqueoPorDefecto = 15;
    public const int LargoMinimoDeClavePorDefecto = 12;

    /// <summary><c>Seguridad:Bloqueo:IntentosMaximos</c>.</summary>
    public int IntentosMaximos { get; set; } = IntentosMaximosPorDefecto;

    /// <summary><c>Seguridad:Bloqueo:Minutos</c>.</summary>
    public int MinutosDeBloqueo { get; set; } = MinutosDeBloqueoPorDefecto;

    /// <summary>Lee un entero de la configuración; si falta o no es un número válido mayor que cero, usa el valor por defecto.</summary>
    public static int LeerEntero(IConfiguration configuracion, string clave, int valorPorDefecto) =>
        int.TryParse(configuracion[clave], out var valor) && valor > 0 ? valor : valorPorDefecto;
}
