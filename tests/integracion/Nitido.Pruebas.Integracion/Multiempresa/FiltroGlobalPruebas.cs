using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nitido.Clientes.Dominio.Clientes;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Compartido.Multiempresa;
using Nitido.Firmas.Dominio.Firmas;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Primera barrera de la multiempresa (ADR 0003): el filtro global de EF, con nombre <c>Multiempresa</c>, que
/// <see cref="DbContextNitido"/> aplica por convención a toda entidad <see cref="IDeFirma"/> o
/// <see cref="IDeEmpresa"/> y que <c>FirmasDbContext</c> declara a mano para <c>Firma</c>. Estas pruebas miden la
/// primera barrera <b>sola</b> (en una base propia con las dos políticas de SQL Server apagadas, y con control
/// de que están apagadas) o su cobertura sin conectarse a ninguna base.
/// </summary>
public class FiltroGlobalPruebas(ContenedorSqlServer contenedor)
{
    private const string CadenaFicticia = "Server=tcp:127.0.0.1,1;Database=ficticia;User Id=sa;Password=x;Encrypt=False;Connect Timeout=1";

    public static TheoryData<string> ContextosConFiltro => new(new[] { "Firmas", "Clientes", "ModeloDePrueba" });

    /// <summary>Un contexto por nombre, armado sin abrir ninguna conexión (el modelo no la necesita).</summary>
    private static (DbContext Contexto, IDisposable? Dueno) Armar(string nombre, FabricaNitido fabrica)
    {
        if (nombre == "ModeloDePrueba")
        {
            var opciones = new DbContextOptionsBuilder<ContextoDeModeloDePrueba>().UseSqlServer(CadenaFicticia).Options;
            return (new ContextoDeModeloDePrueba(opciones), null);
        }

        var tipo = AyudanteDeMigraciones.ContextosEnOrden.Single(c => c.Modulo == nombre).Contexto;
        var alcance = fabrica.Services.CreateScope();
        return ((DbContext)alcance.ServiceProvider.GetRequiredService(tipo), alcance);
    }

    [Theory]
    [MemberData(nameof(ContextosConFiltro))]
    public async Task FiltroGlobal_CubreTodaEntidadDeEmpresaYFirma(string nombreDelContexto)
    {
        await using var fabrica = new FabricaNitido(CadenaFicticia);
        var (contexto, dueno) = Armar(nombreDelContexto, fabrica);
        using (dueno)
        await using (contexto)
        {
            // Que la regla no pase en vacío: cada contexto tiene al menos una entidad que exige filtro.
            var exigidas = VerificadorDeFiltros.EntidadesQueExigenFiltro(contexto);
            Assert.True(exigidas >= 1, $"{contexto.GetType().Name} no tiene ninguna entidad con IDeFirma, IDeEmpresa ni columna UUID_FIRMAS/UUID_EMPRESAS.");

            var infracciones = VerificadorDeFiltros.Infracciones(contexto);

            Assert.True(
                infracciones.Count == 0,
                "Hay entidades sin el filtro global de multiempresa:\n- " + string.Join("\n- ", infracciones));
        }
    }

    [Fact]
    public async Task FiltroGlobal_LosContextosDelHostCubrenLasEntidadesEsperadas()
    {
        // Sin esto, FiltroGlobal_CubreTodaEntidadDeEmpresaYFirma pasaría en vacío si un contexto perdiera sus
        // entidades. Firmas: Firma (llave = firma) y Usuario (IDeFirma). Clientes: Cliente y AccesoCliente (IDeFirma).
        await using var fabrica = new FabricaNitido(CadenaFicticia);
        var esperadas = new Dictionary<string, string[]>
        {
            ["Firmas"] = [nameof(Firma), "Usuario"],
            ["Clientes"] = [nameof(Cliente), nameof(AccesoCliente)],
        };

        foreach (var (modulo, entidades) in esperadas)
        {
            var (contexto, dueno) = Armar(modulo, fabrica);
            using (dueno)
            await using (contexto)
            {
                var conFiltro = contexto.Model.GetEntityTypes()
                    .Where(e => !e.IsOwned() && e.GetDeclaredQueryFilters().Any(f => f.Key == DbContextNitido.FiltroMultiempresa))
                    .Select(e => e.ClrType.Name)
                    .Order()
                    .ToList();
                Assert.Equal(entidades.Order(), conFiltro);
            }
        }
    }

    [Fact]
    public void FiltroGlobal_LaPruebaDetectaCadaEntidadSinFiltro()
    {
        // Control negativo de la regla: un contexto armado a propósito sin la convención. Debe marcar las cuatro
        // formas de incumplir (interfaz de firma, interfaz de empresa, columna en sombra sin interfaz, y llave
        // primaria que es la firma, como Firma) y no marcar la que cumple ni la neutra.
        var opciones = new DbContextOptionsBuilder<ContextoDeModeloIncumplido>().UseSqlServer(CadenaFicticia).Options;
        using var contexto = new ContextoDeModeloIncumplido(opciones);

        var infracciones = VerificadorDeFiltros.Infracciones(contexto);

        Assert.Equal(4, infracciones.Count);
        Assert.Contains(infracciones, i => i.Contains(nameof(IncumpleConInterfazDeFirma), StringComparison.Ordinal));
        Assert.Contains(infracciones, i => i.Contains(nameof(IncumpleConInterfazDeEmpresa), StringComparison.Ordinal));
        Assert.Contains(infracciones, i => i.Contains(nameof(IncumpleConColumnaEnSombra), StringComparison.Ordinal));
        Assert.Contains(infracciones, i => i.Contains(nameof(IncumpleConLlaveQueEsLaFirma), StringComparison.Ordinal));
        Assert.DoesNotContain(infracciones, i => i.Contains(nameof(CumpleConFiltroNombrado), StringComparison.Ordinal));
        Assert.DoesNotContain(infracciones, i => i.Contains(nameof(SinColumnasDeMultiempresa), StringComparison.Ordinal));
    }

    [Fact]
    public void FiltroGlobal_LaEntidadDeEmpresaFiltraPorElClienteYLaDeAmbasPorLasDosColumnas()
    {
        var opciones = new DbContextOptionsBuilder<ContextoDeModeloDePrueba>().UseSqlServer(CadenaFicticia).Options;
        using var contexto = new ContextoDeModeloDePrueba(opciones);

        var soloEmpresa = VerificadorDeFiltros.ConsultaDe(contexto, typeof(PruebaDeEmpresa));
        var ambas = VerificadorDeFiltros.ConsultaDe(contexto, typeof(PruebaDeFirmaYEmpresa));

        var condicionDeEmpresa = soloEmpresa[soloEmpresa.IndexOf("WHERE", StringComparison.Ordinal)..];
        Assert.Contains("[UUID_EMPRESAS]", condicionDeEmpresa, StringComparison.Ordinal);
        Assert.DoesNotContain("[UUID_FIRMAS]", condicionDeEmpresa, StringComparison.Ordinal);

        var condicionDeAmbas = ambas[ambas.IndexOf("WHERE", StringComparison.Ordinal)..];
        Assert.Contains("[UUID_EMPRESAS]", condicionDeAmbas, StringComparison.Ordinal);
        Assert.Contains("[UUID_FIRMAS]", condicionDeAmbas, StringComparison.Ordinal);
        Assert.Contains(" AND ", condicionDeAmbas, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FiltroGlobal_RamaDeEmpresa_FiltraPorElClienteActivoDeLaSesion()
    {
        // Ninguna entidad real implementa todavía IDeEmpresa: se prueba el comportamiento de esa rama con las
        // dos entidades del contexto de pruebas, en una base propia donde EF crea las tablas de prueba.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CrearBaseVaciaAsync(token);
        var firmaA = Guid.CreateVersion7();
        var firmaB = Guid.CreateVersion7();
        var empresa1 = Guid.CreateVersion7();
        var empresa2 = Guid.CreateVersion7();
        var sesion = new ContextoSesionDePrueba();
        var proveedor = new ServiceCollection().AddSingleton<IContextoSesion>(sesion).BuildServiceProvider();
        var opciones = new DbContextOptionsBuilder<ContextoDeModeloDePrueba>()
            .UseSqlServer(cadena)
            .UseApplicationServiceProvider(proveedor)
            .Options;

        await using (var escritura = new ContextoDeModeloDePrueba(opciones))
        {
            await escritura.Database.EnsureCreatedAsync(token);
            escritura.PruebasDeEmpresa.AddRange(
                new PruebaDeEmpresa { Uuid = Guid.CreateVersion7(), UuidEmpresas = empresa1, Nombre = "de la empresa 1" },
                new PruebaDeEmpresa { Uuid = Guid.CreateVersion7(), UuidEmpresas = empresa2, Nombre = "de la empresa 2" });
            escritura.PruebasDeFirmaYEmpresa.AddRange(
                new PruebaDeFirmaYEmpresa { Uuid = Guid.CreateVersion7(), UuidFirmas = firmaA, UuidEmpresas = empresa1, Nombre = "A y empresa 1" },
                new PruebaDeFirmaYEmpresa { Uuid = Guid.CreateVersion7(), UuidFirmas = firmaB, UuidEmpresas = empresa1, Nombre = "B y empresa 1" },
                new PruebaDeFirmaYEmpresa { Uuid = Guid.CreateVersion7(), UuidFirmas = firmaA, UuidEmpresas = empresa2, Nombre = "A y empresa 2" });
            await escritura.SaveChangesAsync(token);
        }

        // Con el cliente 1 y la firma A.
        sesion.Actuar(Guid.CreateVersion7(), firmaA, empresa1);
        await using (var lectura = new ContextoDeModeloDePrueba(opciones))
        {
            Assert.Equal(["de la empresa 1"], await lectura.PruebasDeEmpresa.Select(p => p.Nombre).ToListAsync(token));
            Assert.Equal(["A y empresa 1"], await lectura.PruebasDeFirmaYEmpresa.Select(p => p.Nombre).ToListAsync(token));
        }

        // Con el cliente 2 (otro contexto: la sesión se lee de la instancia, no del modelo en caché).
        sesion.Actuar(Guid.CreateVersion7(), firmaA, empresa2);
        await using (var lectura = new ContextoDeModeloDePrueba(opciones))
        {
            Assert.Equal(["de la empresa 2"], await lectura.PruebasDeEmpresa.Select(p => p.Nombre).ToListAsync(token));
            Assert.Equal(["A y empresa 2"], await lectura.PruebasDeFirmaYEmpresa.Select(p => p.Nombre).ToListAsync(token));
        }

        // Con la firma B y el cliente 1: la entidad con las dos interfaces exige las dos condiciones.
        sesion.Actuar(Guid.CreateVersion7(), firmaB, empresa1);
        await using (var lectura = new ContextoDeModeloDePrueba(opciones))
        {
            Assert.Equal(["B y empresa 1"], await lectura.PruebasDeFirmaYEmpresa.Select(p => p.Nombre).ToListAsync(token));
        }

        // Sin cliente activo (UuidEmpresas null) no se ve nada de un cliente: falla cerrado.
        sesion.Actuar(Guid.CreateVersion7(), firmaA, null);
        await using (var lectura = new ContextoDeModeloDePrueba(opciones))
        {
            Assert.Empty(await lectura.PruebasDeEmpresa.ToListAsync(token));
            Assert.Empty(await lectura.PruebasDeFirmaYEmpresa.ToListAsync(token));
        }

        // Control: sin el filtro (con su nombre) están las cinco filas, así que las listas vacías de arriba no
        // son porque la tabla esté vacía.
        await using (var lectura = new ContextoDeModeloDePrueba(opciones))
        {
            Assert.Equal(2, await lectura.PruebasDeEmpresa.IgnoreQueryFilters([DbContextNitido.FiltroMultiempresa]).CountAsync(token));
            Assert.Equal(3, await lectura.PruebasDeFirmaYEmpresa.IgnoreQueryFilters([DbContextNitido.FiltroMultiempresa]).CountAsync(token));
        }
    }

    [Fact]
    public async Task FiltroGlobal_CadaDbContextUsaLaSesionDeSuAlcance()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var siembra = new FabricaNitido(cadena);
        var s = await SiembraDeDosFirmas.CrearAsync(siembra, cadena, token);

        // Un IContextoSesion distinto en cada alcance (no uno singleton): así se ve si el filtro captura la sesión
        // de la primera instancia del DbContext, que es la que construye el modelo y se guarda en caché.
        await using var host = new FabricaNitido(cadena, usarSesionDePrueba: false);
        await using var porAlcance = host.WithWebHostBuilder(constructor => constructor.ConfigureTestServices(servicios =>
        {
            servicios.RemoveAll<IContextoSesion>();
            servicios.AddScoped<IContextoSesion>(_ => new ContextoSesionDePrueba());
        }));

        using var alcance1 = porAlcance.Services.CreateScope();
        using var alcance2 = porAlcance.Services.CreateScope();
        var sesion1 = (ContextoSesionDePrueba)alcance1.ServiceProvider.GetRequiredService<IContextoSesion>();
        var sesion2 = (ContextoSesionDePrueba)alcance2.ServiceProvider.GetRequiredService<IContextoSesion>();
        Assert.NotSame(sesion1, sesion2);
        sesion1.Actuar(s.A1, s.FirmaA);
        sesion2.Actuar(s.B1, s.FirmaB);
        var contexto1 = alcance1.ServiceProvider.GetRequiredService<ClientesDbContext>();
        var contexto2 = alcance2.ServiceProvider.GetRequiredService<ClientesDbContext>();

        // El alcance 1 (firma A) consulta primero: ahí se construye el modelo, si no estaba ya en caché.
        Assert.Equal([s.CA1], await contexto1.Clientes.Select(c => c.Uuid).ToListAsync(token));

        // El alcance 2 (firma B) ve solo B, no los datos de A que quedaron en el modelo en caché.
        Assert.Equal([s.CB1], await contexto2.Clientes.Select(c => c.Uuid).ToListAsync(token));

        // Y el alcance 1 vuelve a ver solo A.
        Assert.Equal([s.CA1], await contexto1.Clientes.Select(c => c.Uuid).ToListAsync(token));

        // Un alcance nuevo, que usa el mismo modelo en caché con una sesión más, también ve lo suyo.
        using var alcance3 = porAlcance.Services.CreateScope();
        ((ContextoSesionDePrueba)alcance3.ServiceProvider.GetRequiredService<IContextoSesion>()).Actuar(s.A2, s.FirmaA);
        var contexto3 = alcance3.ServiceProvider.GetRequiredService<ClientesDbContext>();
        Assert.Equal([s.CA2], await contexto3.Clientes.Select(c => c.Uuid).ToListAsync(token));
    }

    /// <summary>
    /// Mide la primera barrera sola: en una base propia las dos políticas de SQL Server se apagan (control: la
    /// consulta sin sesión y sin filtro ve las filas de las dos firmas), así que lo que deja afuera a la otra firma
    /// es solo el filtro de EF.
    /// </summary>
    private async Task<(FabricaNitido Fabrica, string Cadena, SiembraDeDosFirmas Siembra)> BaseSinPoliticasAsync(CancellationToken token)
    {
        var cadena = await contenedor.CrearBaseVaciaAsync(token);
        var fabrica = new FabricaNitido(cadena);
        await AyudanteDeMigraciones.AplicarAsync(fabrica.Services, token);
        await ConsultasSql.EjecutarAsync(
            cadena,
            "ALTER SECURITY POLICY seguridad.PS_CLIENTES WITH (STATE = OFF); ALTER SECURITY POLICY seguridad.PS_FIRMAS WITH (STATE = OFF);",
            token);
        var encendidas = await ConsultasSql.EscalarAsync(cadena, "SELECT COUNT(*) FROM sys.security_policies WHERE is_enabled = 1", token);
        Assert.Equal(0, Convert.ToInt32(encendidas));

        var siembra = await SiembraDeDosFirmas.CrearAsync(fabrica, cadena, token);

        // Control: sin sesión y sin filtro de EF, SQL ve las filas de las dos firmas. Si no las viera, la barrera
        // de SQL Server seguiría activa y estas pruebas no medirían el filtro de EF.
        var clientes = Convert.ToInt32(await ConsultasSql.EscalarAsync(cadena, "SELECT COUNT(*) FROM T_EMPRESAS", token));
        Assert.Equal(3, clientes);
        return (fabrica, cadena, siembra);
    }

    [Fact]
    public async Task Multiempresa_UsuarioDeOtraFirmaNoVeClientes()
    {
        var token = TestContext.Current.CancellationToken;
        var (fabrica, _, s) = await BaseSinPoliticasAsync(token);
        await using var anfitrion = fabrica;

        // Como A1: solo la firma A, y todo lo de A (el filtro de Cliente es por firma; el acceso lo exigen la
        // consulta explícita y la segunda barrera).
        fabrica.Sesion.Actuar(s.A1, s.FirmaA);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var clientes = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            var firmas = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();

            Assert.Equal(new[] { s.CA1, s.CA2 }.Order(), (await clientes.Clientes.Select(c => c.Uuid).ToListAsync(token)).Order());
            Assert.Equal(new[] { s.AccesoA1, s.AccesoA2 }.Order(), (await clientes.Accesos.Select(a => a.Uuid).ToListAsync(token)).Order());
            Assert.Equal(new[] { s.A1, s.A2 }.Order(), (await firmas.Set<Usuario>().Select(u => u.Uuid).ToListAsync(token)).Order());
            Assert.Equal([s.FirmaA], await firmas.Firmas.Select(f => f.Uuid).ToListAsync(token));
        }

        // Como B1: lo de B, nada de A.
        fabrica.Sesion.Actuar(s.B1, s.FirmaB);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var clientes = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            var firmas = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();

            Assert.Equal([s.CB1], await clientes.Clientes.Select(c => c.Uuid).ToListAsync(token));
            Assert.Equal([s.AccesoB1], await clientes.Accesos.Select(a => a.Uuid).ToListAsync(token));
            Assert.Equal([s.B1], await firmas.Set<Usuario>().Select(u => u.Uuid).ToListAsync(token));
            Assert.Equal([s.FirmaB], await firmas.Firmas.Select(f => f.Uuid).ToListAsync(token));
        }
    }

    [Fact]
    public async Task FiltroGlobal_SinSesionNoDevuelveNada()
    {
        var token = TestContext.Current.CancellationToken;
        var (fabrica, _, s) = await BaseSinPoliticasAsync(token);
        await using var anfitrion = fabrica;

        // Control: con sesión sí hay filas, de modo que el vacío de abajo es del filtro y no de la base.
        fabrica.Sesion.Actuar(s.A1, s.FirmaA);
        using (var alcance = fabrica.Services.CreateScope())
        {
            Assert.NotEmpty(await alcance.ServiceProvider.GetRequiredService<ClientesDbContext>().Clientes.ToListAsync(token));
        }

        // Sin sesión (las tres llaves en Guid.Empty): ninguna fila, ni de usuarios, ni de firmas, ni de accesos.
        fabrica.Sesion.Actuar(SesionSql.SinSesion);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var clientes = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            var firmas = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();

            Assert.Empty(await clientes.Clientes.ToListAsync(token));
            Assert.Empty(await clientes.Accesos.ToListAsync(token));
            Assert.Empty(await firmas.Set<Usuario>().ToListAsync(token));
            Assert.Empty(await firmas.Firmas.ToListAsync(token));
        }
    }

    [Fact]
    public async Task FiltroGlobal_ElFiltroSeLlamaMultiempresaYSePuedeIgnorarSoloPorNombre()
    {
        var token = TestContext.Current.CancellationToken;
        var (fabrica, _, s) = await BaseSinPoliticasAsync(token);
        await using var anfitrion = fabrica;
        fabrica.Sesion.Actuar(s.A1, s.FirmaA);

        using var alcance = fabrica.Services.CreateScope();
        var clientes = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();

        // Ignorar el filtro por su nombre «Multiempresa» deja ver las tres filas de la base (las dos políticas
        // están apagadas): confirma que ese es el nombre del filtro.
        var todos = await clientes.Clientes.IgnoreQueryFilters([DbContextNitido.FiltroMultiempresa]).Select(c => c.Uuid).ToListAsync(token);
        Assert.Equal(new[] { s.CA1, s.CA2, s.CB1 }.Order(), todos.Order());
        Assert.Equal("Multiempresa", DbContextNitido.FiltroMultiempresa);

        // Ignorar un filtro con otro nombre no quita el de multiempresa.
        var conOtroNombre = await clientes.Clientes.IgnoreQueryFilters(["BorradoLogico"]).Select(c => c.Uuid).ToListAsync(token);
        Assert.Equal(new[] { s.CA1, s.CA2 }.Order(), conOtroNombre.Order());
    }

    [Fact]
    public async Task FiltroGlobal_NoUsaPoolDeDbContext()
    {
        // Un DbContext de un pool conservaría la sesión de otra petición (ADR 0003): se prohíbe AddDbContextPool.
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(TestContext.Current.CancellationToken);
        List<ServiceDescriptor> registros = [];
        await using var fabrica = new FabricaNitido(cadena);
        await using var host = fabrica.WithWebHostBuilder(constructor =>
            constructor.ConfigureTestServices(servicios => registros = [.. servicios]));
        _ = host.Services; // construye el host para que corra ConfigureTestServices

        Assert.NotEmpty(registros);
        var delPool = registros
            .Where(r => r.ServiceType.IsGenericType
                && (r.ServiceType.GetGenericTypeDefinition().Name.Contains("DbContextPool", StringComparison.Ordinal)
                    || r.ServiceType.GetGenericTypeDefinition().Name.Contains("DbContextLease", StringComparison.Ordinal)))
            .Select(r => r.ServiceType.ToString())
            .ToList();
        Assert.True(delPool.Count == 0, "Se registró un pool de DbContext: " + string.Join(", ", delPool));
        Assert.Contains(registros, r => r.ServiceType == typeof(FirmasDbContext));
        Assert.Contains(registros, r => r.ServiceType == typeof(ClientesDbContext));
        Assert.All(
            registros.Where(r => r.ServiceType == typeof(FirmasDbContext) || r.ServiceType == typeof(ClientesDbContext)),
            r => Assert.Equal(ServiceLifetime.Scoped, r.Lifetime));
    }
}
