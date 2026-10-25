using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Compartido.Infraestructura.Persistencia;

/// <summary>
/// Clase base de los <c>DbContext</c> de todos los módulos. Aplica las convenciones de nítido
/// (skill <c>convenciones-bd</c>):
/// <list type="bullet">
/// <item><c>decimal</c> como <c>DECIMAL(19,2)</c> y <c>DateTime</c> como <c>DATETIME2(3)</c>.</item>
/// <item>Nombres de la base con <see cref="ConvencionDeNombres"/>.</item>
/// <item>Las configuraciones <see cref="IEntityTypeConfiguration{TEntity}"/> del ensamblado del contexto derivado.</item>
/// <item>Las cuatro columnas de auditoría como propiedades sombra (ver <see cref="ColumnasDeAuditoria"/>); las llena
/// <see cref="InterceptorDeAuditoria"/>.</item>
/// <item>Llaves GUID de una columna como <c>ValueGeneratedNever</c>; las que llegan vacías (<see cref="Guid.Empty"/>)
/// reciben un GUID versión 7 en <see cref="InterceptorDeAuditoria"/> al guardar.</item>
/// <item>Todas las llaves foráneas con <see cref="DeleteBehavior.Restrict"/>: nada se borra en cascada.</item>
/// <item>El filtro global de multiempresa (<see cref="FiltroMultiempresa"/>) a toda entidad que implemente
/// <see cref="IDeFirma"/> o <see cref="IDeEmpresa"/> (primera barrera, ADR 0003).</item>
/// </list>
/// </summary>
/// <remarks>
/// El filtro lee la sesión solo a través de propiedades de la instancia del contexto: EF guarda el modelo en caché
/// por tipo de contexto y únicamente reemplaza por la instancia actual lo que cuelga del propio contexto. Capturar
/// en el filtro el <see cref="IContextoSesion"/> o un valor leído congelaría la sesión de la primera petición. Por
/// la misma razón no se usa <c>AddDbContextPool</c>.
/// </remarks>
public abstract class DbContextNitido(DbContextOptions opciones) : DbContext(opciones)
{
    /// <summary>Nombre del filtro global de multiempresa (filtros con nombre de EF Core 10).</summary>
    public const string FiltroMultiempresa = "Multiempresa";

    private const int PrecisionDeMomentos = 3;

    private static readonly MethodInfo PropiedadDeEf =
        typeof(EF).GetMethod(nameof(EF.Property))!.MakeGenericMethod(typeof(Guid));

    private IContextoSesion? _sesion;

    /// <summary>Usuario de la sesión de esta instancia (<see cref="Guid.Empty"/> si no hay sesión).</summary>
    protected Guid UsuarioDeLaSesion => Sesion.UuidUsuarios;

    /// <summary>Firma de la sesión de esta instancia (<see cref="Guid.Empty"/> si no hay sesión).</summary>
    protected Guid FirmaDeLaSesion => Sesion.UuidFirmas;

    /// <summary>Cliente activo de la sesión de esta instancia (<see cref="Guid.Empty"/> si no hay).</summary>
    protected Guid EmpresaDeLaSesion => Sesion.UuidEmpresas ?? Guid.Empty;

    private IContextoSesion Sesion => _sesion ??= ResolverSesion();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<decimal>().HavePrecision(19, 2);
        configurationBuilder.Properties<DateTime>().HavePrecision(PrecisionDeMomentos);
        configurationBuilder.Conventions.Add(_ => new ConvencionDeNombres());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        foreach (var entidad in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (!entidad.IsOwned() && entidad.BaseType is null && !entidad.IsKeyless)
            {
                AgregarColumnasDeAuditoria(entidad);
                NoGenerarLaLlaveGuid(entidad);
            }

            foreach (var llaveForanea in entidad.GetDeclaredForeignKeys())
            {
                llaveForanea.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }

        AplicarFiltroMultiempresa(modelBuilder);
    }

    private IContextoSesion ResolverSesion()
    {
        try
        {
            return this.GetService<IContextoSesion>();
        }
        catch (InvalidOperationException)
        {
            // Sin IContextoSesion registrado: sin sesión, y el filtro no devuelve nada.
            return new SesionVacia();
        }
    }

    private void AplicarFiltroMultiempresa(ModelBuilder modelBuilder)
    {
        foreach (var entidad in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (entidad.IsOwned() || entidad.BaseType is not null)
            {
                continue;
            }

            var filtro = ConstruirFiltroMultiempresa(entidad.ClrType);
            if (filtro is not null)
            {
                modelBuilder.Entity(entidad.ClrType).HasQueryFilter(FiltroMultiempresa, filtro);
            }
        }
    }

    private LambdaExpression? ConstruirFiltroMultiempresa(Type tipo)
    {
        var deFirma = typeof(IDeFirma).IsAssignableFrom(tipo);
        var deEmpresa = typeof(IDeEmpresa).IsAssignableFrom(tipo);
        if (!deFirma && !deEmpresa)
        {
            return null;
        }

        var entidad = Expression.Parameter(tipo, "e");
        Expression? condicion = null;
        if (deFirma)
        {
            condicion = IgualALaSesion(entidad, "UuidFirmas", nameof(FirmaDeLaSesion));
        }

        if (deEmpresa)
        {
            var porEmpresa = IgualALaSesion(entidad, "UuidEmpresas", nameof(EmpresaDeLaSesion));
            condicion = condicion is null ? porEmpresa : Expression.AndAlso(condicion, porEmpresa);
        }

        return Expression.Lambda(condicion!, entidad);
    }

    /// <summary>
    /// Arma <c>EF.Property&lt;Guid&gt;(e, propiedad) == this.&lt;miembro&gt;</c>. El miembro es una propiedad de esta
    /// instancia: EF la reconoce y la evalúa en cada consulta sobre el contexto actual (patrón de multiinquilino).
    /// </summary>
    private BinaryExpression IgualALaSesion(ParameterExpression entidad, string propiedad, string miembroDelContexto)
    {
        var delContexto = typeof(DbContextNitido).GetProperty(miembroDelContexto, BindingFlags.NonPublic | BindingFlags.Instance)!;
        return Expression.Equal(
            Expression.Call(PropiedadDeEf, entidad, Expression.Constant(propiedad)),
            Expression.Property(Expression.Constant(this), delContexto));
    }

    private sealed class SesionVacia : IContextoSesion
    {
        public Guid UuidUsuarios => Guid.Empty;

        public Guid UuidFirmas => Guid.Empty;

        public Guid? UuidEmpresas => null;
    }

    private static void AgregarColumnasDeAuditoria(IMutableEntityType entidad)
    {
        AgregarPropiedadSombra(entidad, ColumnasDeAuditoria.FechaCreacion, typeof(DateTime));
        AgregarPropiedadSombra(entidad, ColumnasDeAuditoria.UuidUsuariosCreacion, typeof(Guid));
        AgregarPropiedadSombra(entidad, ColumnasDeAuditoria.FechaModificacion, typeof(DateTime?));
        AgregarPropiedadSombra(entidad, ColumnasDeAuditoria.UuidUsuariosModificacion, typeof(Guid?));
    }

    private static void AgregarPropiedadSombra(IMutableEntityType entidad, string nombre, Type tipo)
    {
        if (entidad.FindProperty(nombre) is not null)
        {
            return;
        }

        var propiedad = entidad.AddProperty(nombre, tipo);
        if (tipo == typeof(DateTime) || tipo == typeof(DateTime?))
        {
            propiedad.SetPrecision(PrecisionDeMomentos);
        }
    }

    /// <summary>
    /// Las llaves GUID de una sola columna quedan como <see cref="ValueGenerated.Never"/>: EF no genera nada. Si
    /// se marcaran <c>OnAdd</c>, una llave que llega con valor (la que pone la fábrica del dominio) haría que EF
    /// supusiera que la entidad ya existe, y un hijo nuevo agregado a un agregado cargado quedaría <c>Modified</c>
    /// y fallaría con <c>DbUpdateConcurrencyException</c>. El GUID versión 7 de las llaves vacías lo pone
    /// <see cref="InterceptorDeAuditoria"/> al guardar.
    /// </summary>
    private static void NoGenerarLaLlaveGuid(IMutableEntityType entidad)
    {
        var llave = entidad.FindPrimaryKey();
        if (llave is not { Properties.Count: 1 } || llave.Properties[0].ClrType != typeof(Guid))
        {
            return;
        }

        llave.Properties[0].ValueGenerated = ValueGenerated.Never;
    }
}
