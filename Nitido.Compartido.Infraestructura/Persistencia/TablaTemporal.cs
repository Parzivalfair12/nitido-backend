using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nitido.Compartido.Infraestructura.Persistencia;

/// <summary>
/// Mapea una entidad a una tabla temporal de SQL Server con los nombres de nítido: periodos
/// <c>PERIODO_INICIO</c> y <c>PERIODO_FIN</c> e historial <c>T_&lt;NOMBRE&gt;_HISTORIAL</c> (skill
/// <c>convenciones-bd</c>).
/// </summary>
public static class TablaTemporal
{
    /// <summary>Nombre de la columna de inicio del periodo de validez de la fila.</summary>
    public const string PeriodoInicio = "PERIODO_INICIO";

    /// <summary>Nombre de la columna de fin del periodo de validez de la fila.</summary>
    public const string PeriodoFin = "PERIODO_FIN";

    /// <summary>Mapea la entidad a <paramref name="tabla"/> como tabla temporal, con historial <c>&lt;tabla&gt;_HISTORIAL</c>.</summary>
    public static EntityTypeBuilder<TEntidad> MapearATablaTemporal<TEntidad>(
        this EntityTypeBuilder<TEntidad> constructor,
        string tabla)
        where TEntidad : class
    {
        ((EntityTypeBuilder)constructor).MapearATablaTemporal(tabla);
        return constructor;
    }

    /// <summary>Mapea la entidad a <paramref name="tabla"/> como tabla temporal, con historial <c>&lt;tabla&gt;_HISTORIAL</c>.</summary>
    public static EntityTypeBuilder MapearATablaTemporal(this EntityTypeBuilder constructor, string tabla)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabla);

        constructor.ToTable(
            tabla,
            tablaConfigurada => tablaConfigurada.IsTemporal(temporal =>
            {
                temporal.HasPeriodStart(PeriodoInicio);
                temporal.HasPeriodEnd(PeriodoFin);
                temporal.UseHistoryTable($"{tabla}_HISTORIAL");
            }));
        return constructor;
    }
}
