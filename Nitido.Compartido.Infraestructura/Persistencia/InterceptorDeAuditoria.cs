using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Compartido.Infraestructura.Persistencia;

/// <summary>
/// Llena las columnas de auditoría en cada <c>SaveChanges</c>: quién y cuándo creó o modificó cada fila.
/// Antes de eso, a las filas nuevas cuya llave GUID llega vacía (<see cref="Guid.Empty"/>) les pone una
/// llave versión 7 con la hora del <see cref="TimeProvider"/> (ver <c>AsignarLlaveSiEstaVacia</c>). La hora sale de <see cref="TimeProvider"/> y el usuario de <see cref="IContextoSesion"/>, que se pide
/// al guardar (no al construir) para que el host arranque aunque todavía no haya una implementación.
/// Si no hay contexto de sesión, o el usuario es <see cref="Guid.Empty"/> (petición sin sesión), falla: nunca se
/// guarda sin saber quién actúa.
/// </summary>
internal sealed class InterceptorDeAuditoria(TimeProvider tiempo) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Auditar(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Auditar(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static Guid ObtenerUsuario(DbContext contexto)
    {
        try
        {
            return contexto.GetService<IContextoSesion>().UuidUsuarios;
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "No se puede guardar sin saber qué usuario actúa: no hay un IContextoSesion registrado.",
                ex);
        }
    }

    private void Auditar(DbContext? contexto)
    {
        if (contexto is null)
        {
            return;
        }

        var entradas = contexto.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified
                && e.Metadata.FindProperty(ColumnasDeAuditoria.FechaCreacion) is not null)
            .ToList();
        if (entradas.Count == 0)
        {
            return;
        }

        var usuario = ObtenerUsuario(contexto);
        if (usuario == Guid.Empty)
        {
            throw new InvalidOperationException(
                "No se puede guardar sin saber qué usuario actúa: el IContextoSesion no tiene usuario.");
        }

        var momento = tiempo.GetUtcNow();
        var ahora = momento.UtcDateTime;

        foreach (var entrada in entradas)
        {
            if (entrada.State == EntityState.Added)
            {
                AsignarLlaveSiEstaVacia(entrada, momento);
                entrada.Property(ColumnasDeAuditoria.FechaCreacion).CurrentValue = ahora;
                entrada.Property(ColumnasDeAuditoria.UuidUsuariosCreacion).CurrentValue = usuario;
            }
            else
            {
                entrada.Property(ColumnasDeAuditoria.FechaModificacion).CurrentValue = ahora;
                entrada.Property(ColumnasDeAuditoria.UuidUsuariosModificacion).CurrentValue = usuario;
                MarcarComoSinCambios(entrada, ColumnasDeAuditoria.FechaCreacion);
                MarcarComoSinCambios(entrada, ColumnasDeAuditoria.UuidUsuariosCreacion);
            }
        }
    }

    /// <summary>
    /// Pone una llave GUID versión 7 (<c>Guid.CreateVersion7(momento)</c>, con la hora de <see cref="TimeProvider"/>)
    /// a una entrada nueva cuya llave es <see cref="Guid.Empty"/>. Solo toca llaves primarias de una columna
    /// de tipo <see cref="Guid"/> y solo en filas nuevas: una llave que ya trae valor (la de las fábricas del
    /// dominio) no se reemplaza. Las llaves del modelo son <c>ValueGeneratedNever</c>, de modo que EF nunca
    /// supone que una entidad con llave ya existe en la base; por eso la asignación se hace aquí y no con un
    /// generador de valores de EF.
    /// </summary>
    private static void AsignarLlaveSiEstaVacia(EntityEntry entrada, DateTimeOffset momento)
    {
        var llave = entrada.Metadata.FindPrimaryKey();
        if (llave is not { Properties.Count: 1 } || llave.Properties[0].ClrType != typeof(Guid))
        {
            return;
        }

        var propiedad = entrada.Property(llave.Properties[0].Name);
        if (propiedad.CurrentValue is Guid actual && actual == Guid.Empty)
        {
            propiedad.CurrentValue = Guid.CreateVersion7(momento);
        }
    }

    private static void MarcarComoSinCambios(EntityEntry entrada, string propiedad) =>
        entrada.Property(propiedad).IsModified = false;
}
