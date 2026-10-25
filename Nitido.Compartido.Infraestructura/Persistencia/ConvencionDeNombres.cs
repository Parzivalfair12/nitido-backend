using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Nitido.Compartido.Infraestructura.Persistencia;

/// <summary>
/// Convención de EF que traduce el modelo en C# (PascalCase) a los nombres de la base de datos de nítido
/// (skill <c>convenciones-bd</c>). Solo pone los nombres que nadie configuró a mano: un
/// <c>HasColumnName</c>, <c>HasName</c>, <c>HasConstraintName</c> o <c>HasDatabaseName</c> explícito gana.
/// <list type="bullet">
/// <item>Columna: la propiedad en mayúsculas con guion bajo (<c>RazonSocial</c> es <c>RAZON_SOCIAL</c>).</item>
/// <item>Llave primaria de una sola columna: <c>UUID_&lt;TABLA sin T_&gt;</c>, sin importar el nombre de la propiedad.</item>
/// <item>Restricciones: <c>PK_&lt;TABLA&gt;</c>, <c>FK_&lt;TABLA&gt;_&lt;TABLA_PRINCIPAL&gt;</c> (o <c>FK_&lt;TABLA&gt;_&lt;COLUMNA&gt;</c>
/// si la tabla referencia dos veces la misma), <c>IX_</c> y <c>UX_</c> (únicos) con tabla y columnas.</item>
/// </list>
/// El nombre de la tabla no se deduce: cada configuración lo declara, porque el plural en español no se puede calcular.
/// </summary>
internal sealed class ConvencionDeNombres : IModelFinalizingConvention
{
    private const string PrefijoDeTabla = "T_";
    private const string PrefijoDeLlave = "UUID_";

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        var entidades = modelBuilder.Metadata.GetEntityTypes()
            .Where(e => e.GetTableName() is not null)
            .ToList();

        // Primero las columnas: los nombres de índices y llaves foráneas se arman con ellas.
        foreach (var entidad in entidades)
        {
            NombrarColumnas(entidad);
        }

        foreach (var entidad in entidades)
        {
            NombrarRestricciones(entidad);
        }
    }

    /// <summary>Convierte un nombre en PascalCase a mayúsculas con guion bajo (<c>UuidFirmas</c> a <c>UUID_FIRMAS</c>).</summary>
    internal static string AMayusculasConGuionBajo(string nombre)
    {
        var resultado = new StringBuilder(nombre.Length + 8);
        for (var i = 0; i < nombre.Length; i++)
        {
            var actual = nombre[i];
            if (i > 0 && char.IsUpper(actual))
            {
                var anterior = nombre[i - 1];
                var siguienteEsMinuscula = i + 1 < nombre.Length && char.IsLower(nombre[i + 1]);
                if (char.IsLower(anterior) || char.IsDigit(anterior) || (char.IsUpper(anterior) && siguienteEsMinuscula))
                {
                    resultado.Append('_');
                }
            }

            resultado.Append(char.ToUpperInvariant(actual));
        }

        return resultado.ToString();
    }

    private static string SinPrefijoDeTabla(string tabla) =>
        tabla.StartsWith(PrefijoDeTabla, StringComparison.Ordinal) ? tabla[PrefijoDeTabla.Length..] : tabla;

    private static void NombrarColumnas(IConventionEntityType entidad)
    {
        var tabla = entidad.GetTableName()!;
        var llave = entidad.IsOwned() || entidad.BaseType is not null ? null : entidad.FindPrimaryKey();
        var llaveSencilla = llave is { Properties.Count: 1 } ? llave.Properties[0] : null;

        foreach (var propiedad in entidad.GetDeclaredProperties())
        {
            var nombre = propiedad == llaveSencilla
                ? PrefijoDeLlave + SinPrefijoDeTabla(tabla)
                : AMayusculasConGuionBajo(propiedad.Name);
            propiedad.Builder.HasColumnName(nombre);
        }
    }

    private static void NombrarRestricciones(IConventionEntityType entidad)
    {
        if (entidad.IsOwned())
        {
            return;
        }

        var tabla = entidad.GetTableName()!;
        var objeto = StoreObjectIdentifier.Table(tabla, entidad.GetSchema());

        foreach (var llave in entidad.GetDeclaredKeys().Where(l => l == entidad.FindPrimaryKey()))
        {
            llave.Builder.HasName($"PK_{tabla}");
        }

        var llavesForaneas = entidad.GetDeclaredForeignKeys().ToList();
        foreach (var llaveForanea in llavesForaneas)
        {
            var tablaPrincipal = llaveForanea.PrincipalEntityType.GetTableName();
            if (tablaPrincipal is null)
            {
                continue;
            }

            var repetida = llavesForaneas.Count(f => f.PrincipalEntityType.GetTableName() == tablaPrincipal) > 1;
            var sufijo = repetida
                ? llaveForanea.Properties[0].GetColumnName(objeto) ?? llaveForanea.Properties[0].Name
                : tablaPrincipal;
            llaveForanea.Builder.HasConstraintName($"FK_{tabla}_{sufijo}");
        }

        foreach (var indice in entidad.GetDeclaredIndexes())
        {
            var columnas = indice.Properties.Select(p => p.GetColumnName(objeto) ?? p.Name);
            var prefijo = indice.IsUnique ? "UX" : "IX";
            indice.Builder.HasDatabaseName($"{prefijo}_{tabla}_{string.Join('_', columnas)}");
        }
    }
}
