using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Aplica las migraciones de los módulos en el único orden válido: el módulo referenciado va antes
/// que el que lo referencia (<c>Firmas</c> → <c>Clientes</c>, ADR 0002). Esta es la lista que se copia
/// en <c>db/README.md</c>. Usa los <c>DbContext</c> del host real, de modo que la tabla de historial y
/// las opciones son las de producción. <c>Migrate()</c> falla si el modelo tiene cambios sin migración.
/// </summary>
internal static class AyudanteDeMigraciones
{
    /// <summary>Contextos con tablas, en el orden en que se migran.</summary>
    public static IReadOnlyList<(string Modulo, Type Contexto)> ContextosEnOrden { get; } =
    [
        ("Firmas", typeof(FirmasDbContext)),
        ("Clientes", typeof(ClientesDbContext)),
    ];

    public static async Task AplicarAsync(IServiceProvider servicios, CancellationToken token)
    {
        foreach (var (_, tipo) in ContextosEnOrden)
        {
            using var alcance = servicios.CreateScope();
            var contexto = (DbContext)alcance.ServiceProvider.GetRequiredService(tipo);
            await contexto.Database.MigrateAsync(token);
        }
    }
}
