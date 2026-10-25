using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Cada tabla tiene un solo dueño (ADR 0002). No se conecta a ninguna base: construye el modelo de cada
/// contexto del host con una cadena ficticia.
/// </summary>
public class TablasPorContextoPruebas
{
    private const string CadenaFicticia = "Server=tcp:127.0.0.1,1;Database=ficticia;User Id=sa;Password=x;Encrypt=False;Connect Timeout=1";

    private static readonly Dictionary<Type, string[]> TablasEsperadas = new()
    {
        [typeof(FirmasDbContext)] = ["T_FIRMAS", "T_FIRMAS_HISTORIAL", "T_USUARIOS", "T_USUARIOS_HISTORIAL", "__EFMigrationsHistory_Firmas"],
        [typeof(ClientesDbContext)] = ["T_EMPRESAS", "T_EMPRESAS_HISTORIAL", "T_USUARIOS_EMPRESAS", "T_USUARIOS_EMPRESAS_HISTORIAL", "__EFMigrationsHistory_Clientes"],
    };

    /// <summary>Tablas (con su historial temporal y su historial de migraciones) que nombra un contexto.</summary>
    private static IReadOnlyCollection<string> TablasDe(DbContext contexto)
    {
        var tablas = new List<string>();
        var modelo = contexto.GetService<IDesignTimeModel>().Model;
        foreach (var entidad in modelo.GetEntityTypes())
        {
            var tabla = entidad.GetTableName();
            if (tabla is not null)
            {
                tablas.Add(tabla);
            }

            var historial = entidad.GetHistoryTableName();
            if (historial is not null)
            {
                tablas.Add(historial);
            }
        }

        var relacional = contexto.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single();
        tablas.Add(relacional.MigrationsHistoryTableName ?? "__EFMigrationsHistory");
        return [.. tablas.Distinct()];
    }

    /// <summary>Tablas que nombra más de un contexto.</summary>
    internal static List<string> TablasRepetidas(IEnumerable<(string Contexto, IReadOnlyCollection<string> Tablas)> contextos) =>
    [
        .. contextos
            .SelectMany(c => c.Tablas.Select(t => (c.Contexto, Tabla: t)))
            .GroupBy(x => x.Tabla, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Contexto).Distinct().Count() > 1)
            .Select(g => $"{g.Key}: la nombran {string.Join(" y ", g.Select(x => x.Contexto).Distinct())}."),
    ];

    [Fact]
    public async Task Tablas_PertenecenAUnSoloDbContext()
    {
        await using var fabrica = new FabricaNitido(CadenaFicticia);
        var recogidas = new List<(string Contexto, IReadOnlyCollection<string> Tablas)>();

        foreach (var (modulo, tipo) in AyudanteDeMigraciones.ContextosEnOrden)
        {
            using var alcance = fabrica.Services.CreateScope();
            var contexto = (DbContext)alcance.ServiceProvider.GetRequiredService(tipo);
            var tablas = TablasDe(contexto);
            recogidas.Add((tipo.Name, tablas));

            // Que cada contexto mapee las tablas de su módulo, y solo esas (no pasa en vacío).
            Assert.Equal(TablasEsperadas[tipo].Order(), tablas.Order());
            Assert.Contains($"__EFMigrationsHistory_{modulo}", tablas);
        }

        var repetidas = TablasRepetidas(recogidas);
        Assert.True(repetidas.Count == 0, "Hay tablas con más de un dueño:\n- " + string.Join("\n- ", repetidas));
    }

    [Fact]
    public void Tablas_LaReglaDetectaDosContextosConLaMismaTabla()
    {
        var repetidas = TablasRepetidas(
        [
            ("FirmasDbContext", ["T_USUARIOS", "T_USUARIOS_HISTORIAL", "__EFMigrationsHistory_Firmas"]),
            ("ClientesDbContext", ["T_EMPRESAS", "T_USUARIOS", "__EFMigrationsHistory_Firmas"]),
        ]);

        Assert.Equal(2, repetidas.Count);
        Assert.Contains(repetidas, r => r.StartsWith("T_USUARIOS:", StringComparison.Ordinal));
        Assert.Contains(repetidas, r => r.StartsWith("__EFMigrationsHistory_Firmas:", StringComparison.Ordinal));
    }
}
