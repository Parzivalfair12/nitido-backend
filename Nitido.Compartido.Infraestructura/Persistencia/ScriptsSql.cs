using System.Reflection;

namespace Nitido.Compartido.Infraestructura.Persistencia;

/// <summary>
/// Lee los scripts SQL incrustados como recurso en el ensamblado de Infraestructura de un módulo (por ejemplo,
/// los de <c>db/seguridad-por-fila/</c>). Las migraciones los aplican con <c>migrationBuilder.Sql</c>.
/// </summary>
public static class ScriptsSql
{
    /// <summary>Devuelve el texto del recurso incrustado con ese nombre lógico.</summary>
    /// <param name="ensamblado">Ensamblado que incrusta el script.</param>
    /// <param name="nombreLogico">Nombre lógico del recurso, por ejemplo <c>SeguridadPorFila.0001-firmas-politica.sql</c>.</param>
    /// <exception cref="InvalidOperationException">El recurso no existe en el ensamblado.</exception>
    public static string Leer(Assembly ensamblado, string nombreLogico)
    {
        ArgumentNullException.ThrowIfNull(ensamblado);
        ArgumentException.ThrowIfNullOrWhiteSpace(nombreLogico);

        using var flujo = ensamblado.GetManifestResourceStream(nombreLogico)
            ?? throw new InvalidOperationException(
                $"No existe el recurso incrustado «{nombreLogico}» en {ensamblado.GetName().Name}. Revisa el EmbeddedResource del .csproj.");
        using var lector = new StreamReader(flujo);
        return lector.ReadToEnd();
    }
}
