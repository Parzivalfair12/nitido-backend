using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Revisa el modelo de un <see cref="DbContext"/> contra la regla de la primera barrera (ADR 0003): toda
/// entidad que implemente <see cref="IDeFirma"/> o <see cref="IDeEmpresa"/>, <b>o</b> que tenga en su tabla una
/// columna <c>UUID_FIRMAS</c> o <c>UUID_EMPRESAS</c> (aunque sea una propiedad sombra o la llave primaria, como
/// en <c>Firma</c>), debe tener el filtro de consulta con nombre <see cref="DbContextNitido.FiltroMultiempresa"/>.
/// Devuelve la lista de infracciones (vacía si todo cumple). Construir el modelo no abre ninguna conexión.
/// </summary>
internal static class VerificadorDeFiltros
{
    private const string ColumnaDeFirma = "UUID_FIRMAS";
    private const string ColumnaDeEmpresa = "UUID_EMPRESAS";

    public static List<string> Infracciones(DbContext contexto)
    {
        var infracciones = new List<string>();
        var modelo = contexto.GetService<IDesignTimeModel>().Model;
        var entidades = modelo.GetEntityTypes()
            .Where(e => !e.IsOwned() && e.BaseType is null && e.GetTableName() is not null)
            .ToList();

        foreach (var entidad in entidades)
        {
            var clr = entidad.ClrType;
            var tabla = StoreObjectIdentifier.Table(entidad.GetTableName()!, entidad.GetSchema());
            var columnas = entidad.GetProperties().Select(p => p.GetColumnName(tabla)).ToHashSet();

            var esDeFirma = typeof(IDeFirma).IsAssignableFrom(clr);
            var esDeEmpresa = typeof(IDeEmpresa).IsAssignableFrom(clr);
            var tieneColumnaDeFirma = columnas.Contains(ColumnaDeFirma);
            var tieneColumnaDeEmpresa = columnas.Contains(ColumnaDeEmpresa);
            if (!esDeFirma && !esDeEmpresa && !tieneColumnaDeFirma && !tieneColumnaDeEmpresa)
            {
                continue;
            }

            var filtro = entidad.GetDeclaredQueryFilters().FirstOrDefault(f => f.Key == DbContextNitido.FiltroMultiempresa);
            if (filtro is null)
            {
                infracciones.Add(
                    $"{contexto.GetType().Name}: {clr.Name} (tabla {entidad.GetTableName()}) "
                    + $"{Razon(esDeFirma, esDeEmpresa, tieneColumnaDeFirma, tieneColumnaDeEmpresa)} "
                    + $"y no tiene el filtro con nombre «{DbContextNitido.FiltroMultiempresa}».");
                continue;
            }

            // El filtro tiene que ser el de la clase de entidad: la columna que filtra sale de la interfaz y,
            // si no hay interfaz (Firma), de la columna que tiene la tabla.
            var esperadas = new List<string>();
            if (esDeFirma)
            {
                esperadas.Add(ColumnaDeFirma);
            }

            if (esDeEmpresa)
            {
                esperadas.Add(ColumnaDeEmpresa);
            }

            if (!esDeFirma && !esDeEmpresa)
            {
                if (tieneColumnaDeFirma)
                {
                    esperadas.Add(ColumnaDeFirma);
                }

                if (tieneColumnaDeEmpresa)
                {
                    esperadas.Add(ColumnaDeEmpresa);
                }
            }

            var sql = ConsultaDe(contexto, clr);
            var posicionDelWhere = sql.IndexOf("WHERE", StringComparison.Ordinal);
            if (posicionDelWhere < 0)
            {
                infracciones.Add($"{contexto.GetType().Name}: la consulta de {clr.Name} no tiene WHERE: el filtro no se aplica.");
                continue;
            }

            var condicion = sql[posicionDelWhere..];
            foreach (var columna in esperadas.Where(c => !condicion.Contains($"[{c}]", StringComparison.Ordinal)))
            {
                infracciones.Add($"{contexto.GetType().Name}: el WHERE de {clr.Name} no filtra por {columna}: {condicion}");
            }

            // Una entidad que solo es de la firma no debe quedar filtrada por el cliente activo, ni al revés
            // (para la entidad con las dos interfaces se esperan las dos columnas, ya revisadas arriba).
            if (esDeFirma && !esDeEmpresa && !tieneColumnaDeEmpresa && condicion.Contains($"[{ColumnaDeEmpresa}]", StringComparison.Ordinal))
            {
                infracciones.Add($"{contexto.GetType().Name}: el WHERE de {clr.Name} filtra por {ColumnaDeEmpresa} y la entidad no es de un cliente.");
            }

            if (esDeEmpresa && !esDeFirma && !tieneColumnaDeFirma && condicion.Contains($"[{ColumnaDeFirma}]", StringComparison.Ordinal))
            {
                infracciones.Add($"{contexto.GetType().Name}: el WHERE de {clr.Name} filtra por {ColumnaDeFirma} y la entidad no es de una firma.");
            }
        }

        return infracciones;
    }

    /// <summary>Cuántas entidades del modelo exige revisar la regla (para que una prueba no pase en vacío).</summary>
    public static int EntidadesQueExigenFiltro(DbContext contexto)
    {
        var modelo = contexto.GetService<IDesignTimeModel>().Model;
        return modelo.GetEntityTypes()
            .Where(e => !e.IsOwned() && e.BaseType is null && e.GetTableName() is not null)
            .Count(e =>
            {
                var tabla = StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema());
                var columnas = e.GetProperties().Select(p => p.GetColumnName(tabla)).ToHashSet();
                return typeof(IDeFirma).IsAssignableFrom(e.ClrType)
                    || typeof(IDeEmpresa).IsAssignableFrom(e.ClrType)
                    || columnas.Contains(ColumnaDeFirma)
                    || columnas.Contains(ColumnaDeEmpresa);
            });
    }

    /// <summary>El SQL de <c>Set&lt;T&gt;()</c> con los filtros del modelo, sin abrir ninguna conexión.</summary>
    public static string ConsultaDe(DbContext contexto, Type entidad)
    {
        var set = typeof(DbContext)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(entidad)
            .Invoke(contexto, null)!;
        return ((IQueryable)set).ToQueryString();
    }

    private static string Razon(bool esDeFirma, bool esDeEmpresa, bool columnaFirma, bool columnaEmpresa)
    {
        var partes = new List<string>();
        if (esDeFirma)
        {
            partes.Add("implementa IDeFirma");
        }

        if (esDeEmpresa)
        {
            partes.Add("implementa IDeEmpresa");
        }

        if (columnaFirma)
        {
            partes.Add("tiene la columna UUID_FIRMAS");
        }

        if (columnaEmpresa)
        {
            partes.Add("tiene la columna UUID_EMPRESAS");
        }

        return string.Join(" y ", partes);
    }
}
