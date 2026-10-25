using System.Reflection;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Los scripts de seguridad por fila viven en <c>db/seguridad-por-fila/NNNN-&lt;modulo&gt;-&lt;tema&gt;.sql</c>, cada
/// <c>Infraestructura</c> los incluye como recurso incrustado y su migración los aplica con
/// <see cref="ScriptsSql.Leer"/> (ADR 0003, «Scripts»). No necesitan base de datos.
/// </summary>
public class ScriptsSqlPruebas
{
    private static string RaizDelRepositorio()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "Nitido.slnx")))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio (Nitido.slnx) subiendo desde la salida de las pruebas.");
    }

    public static TheoryData<string, string, string, string> Scripts => new()
    {
        { "Firmas", "SeguridadPorFila.0001-firmas-politica.sql", "0001-firmas-politica.sql", "CREATE SECURITY POLICY seguridad.PS_FIRMAS" },
        { "Clientes", "SeguridadPorFila.0002-clientes-politica.sql", "0002-clientes-politica.sql", "CREATE SECURITY POLICY seguridad.PS_CLIENTES" },
    };

    private static Assembly EnsambladoDe(string modulo) =>
        modulo == "Firmas" ? typeof(FirmasDbContext).Assembly : typeof(ClientesDbContext).Assembly;

    [Theory]
    [MemberData(nameof(Scripts))]
    public void ScriptsSql_ElRecursoIncrustadoEsElArchivoVersionadoEnDb(string modulo, string recurso, string archivo, string debeContener)
    {
        var incrustado = ScriptsSql.Leer(EnsambladoDe(modulo), recurso);

        var enDisco = File.ReadAllText(Path.Combine(RaizDelRepositorio(), "db", "seguridad-por-fila", archivo));
        Assert.Contains(debeContener, incrustado, StringComparison.Ordinal);
        Assert.Equal(enDisco.ReplaceLineEndings("\n"), incrustado.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ScriptsSql_ElDeClientesNoRepiteLasFuncionesDeFirmas()
    {
        // Una política y sus funciones por módulo: las funciones no se comparten entre módulos (ADR 0003).
        var firmas = ScriptsSql.Leer(typeof(FirmasDbContext).Assembly, "SeguridadPorFila.0001-firmas-politica.sql");
        var clientes = ScriptsSql.Leer(typeof(ClientesDbContext).Assembly, "SeguridadPorFila.0002-clientes-politica.sql");

        Assert.Contains("seguridad.FN_FIRMAS_ES_DE_LA_FIRMA", firmas, StringComparison.Ordinal);
        Assert.DoesNotContain("seguridad.FN_FIRMAS_", clientes, StringComparison.Ordinal);
        Assert.DoesNotContain("seguridad.FN_CLIENTES_", firmas, StringComparison.Ordinal);
        Assert.Contains("CREATE SCHEMA seguridad", firmas, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE SCHEMA", clientes, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptsSql_UnRecursoQueNoExisteFallaConUnMensajeClaro()
    {
        var ex = Assert.ThrowsAny<Exception>(() => ScriptsSql.Leer(typeof(FirmasDbContext).Assembly, "SeguridadPorFila.9999-no-existe.sql"));

        Assert.Contains("SeguridadPorFila.9999-no-existe.sql", ex.Message, StringComparison.Ordinal);
    }
}
