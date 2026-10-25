using Microsoft.Extensions.DependencyInjection;
using Nitido.Firmas.Dominio.Firmas;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

public class IdentificadoresPruebas(ContenedorSqlServer contenedor)
{
    [Fact]
    public async Task Identificadores_SonGuidVersion7()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var ahora = new DateTimeOffset(2031, 3, 4, 5, 6, 7, 89, TimeSpan.Zero);
        fabrica.Reloj.Ahora = ahora;

        // Firma, cliente y acceso: la llave la pone la fábrica de dominio con Guid.CreateVersion7(ahora).
        var firma = Firma.Crear($"Firma {Guid.NewGuid():N}", fabrica.Reloj.GetUtcNow());
        GuidVersion7Ayuda.AssertEsVersion7Con(firma.Uuid, ahora, "Firma.Crear");

        // Usuario: llega con la llave vacía y la pone el generador de EF con el TimeProvider.
        var usuario = DatosDePrueba.NuevoUsuario(firma.Uuid);
        Assert.Equal(Guid.Empty, usuario.Uuid);
        fabrica.Sesion.Actuar(Guid.CreateVersion7(), firma.Uuid); // F0-02b: se guarda con la sesión de la firma nueva
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
            contexto.Add(firma);
            contexto.Add(usuario);
            await contexto.SaveChangesAsync(token);
        }

        GuidVersion7Ayuda.AssertEsVersion7Con(usuario.Uuid, ahora, "Usuario (generador de llaves)");

        var cliente = DatosDePrueba.NuevoCliente(fabrica, firma.Uuid, usuario.Uuid);
        var acceso = cliente.Accesos.Single();
        GuidVersion7Ayuda.AssertEsVersion7Con(cliente.Uuid, ahora, "Cliente.Crear");
        GuidVersion7Ayuda.AssertEsVersion7Con(acceso.Uuid, ahora, "AccesoCliente (por Cliente.Crear)");
        await DatosDePrueba.GuardarAsync(fabrica, cliente, token);

        // Lo que quedó en las cuatro tablas es lo mismo: el generador no pisa la llave de la fábrica,
        // y lo guardado es versión 7 con la marca de tiempo del reloj.
        var guardadas = new (string Tabla, string Llave, Guid Esperada)[]
        {
            ("T_FIRMAS", "UUID_FIRMAS", firma.Uuid),
            ("T_USUARIOS", "UUID_USUARIOS", usuario.Uuid),
            ("T_EMPRESAS", "UUID_EMPRESAS", cliente.Uuid),
            ("T_USUARIOS_EMPRESAS", "UUID_USUARIOS_EMPRESAS", acceso.Uuid),
        };
        var sesion = new SesionSql(usuario.Uuid, firma.Uuid);
        foreach (var (tabla, llave, esperada) in guardadas)
        {
            var leida = await ConsultasSql.EscalarAsync(
                cadena,
                sesion,
                $"SELECT {llave} FROM {tabla} WHERE {llave} = @llave",
                token,
                ("@llave", esperada));
            Assert.NotNull(leida);
            GuidVersion7Ayuda.AssertEsVersion7Con((Guid)leida, ahora, $"{tabla}.{llave}");
        }
    }

    [Fact]
    public async Task Identificadores_ElGeneradorUsaElRelojAlMomentoDeGuardar()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        fabrica.Reloj.Ahora = new DateTimeOffset(2031, 3, 4, 5, 6, 7, 89, TimeSpan.Zero);
        var firma = Firma.Crear($"Firma {Guid.NewGuid():N}", fabrica.Reloj.GetUtcNow());
        var primero = DatosDePrueba.NuevoUsuario(firma.Uuid);
        var segundo = DatosDePrueba.NuevoUsuario(firma.Uuid);
        fabrica.Sesion.Actuar(Guid.CreateVersion7(), firma.Uuid);

        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        contexto.Add(firma);
        contexto.Add(primero);
        await contexto.SaveChangesAsync(token);

        var despues = new DateTimeOffset(2031, 3, 4, 5, 6, 8, 9, TimeSpan.Zero);
        fabrica.Reloj.Ahora = despues;
        contexto.Add(segundo);
        await contexto.SaveChangesAsync(token);

        GuidVersion7Ayuda.AssertEsVersion7Con(primero.Uuid, new DateTimeOffset(2031, 3, 4, 5, 6, 7, 89, TimeSpan.Zero), "primer usuario");
        GuidVersion7Ayuda.AssertEsVersion7Con(segundo.Uuid, despues, "segundo usuario");
        Assert.NotEqual(primero.Uuid, segundo.Uuid);
    }

    [Fact]
    public async Task Identificadores_LaLlaveQueLlegaDeLaFabricaNoSeReemplaza()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var momentoDeLaFabrica = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        fabrica.Reloj.Ahora = new DateTimeOffset(2031, 3, 4, 5, 6, 7, 89, TimeSpan.Zero);
        var firma = Firma.Crear($"Firma {Guid.NewGuid():N}", momentoDeLaFabrica);
        var llaveAntes = firma.Uuid;
        fabrica.Sesion.Actuar(Guid.CreateVersion7(), firma.Uuid);

        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        contexto.Add(firma);
        await contexto.SaveChangesAsync(token);

        Assert.Equal(llaveAntes, firma.Uuid);
        GuidVersion7Ayuda.AssertEsVersion7Con(firma.Uuid, momentoDeLaFabrica, "Firma guardada");
    }
}
