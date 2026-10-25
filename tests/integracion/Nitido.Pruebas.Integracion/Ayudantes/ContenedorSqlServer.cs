using Microsoft.Data.SqlClient;
using Nitido.Pruebas.Integracion;
using Testcontainers.MsSql;

// Un solo contenedor de SQL Server para toda la corrida (assembly fixture de xUnit v3).
[assembly: AssemblyFixture(typeof(ContenedorSqlServer))]

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// SQL Server 2025 en un contenedor, compartido por todas las pruebas de integración. Se inicia la
/// primera vez que alguna prueba lo pide (la prueba de salud sin base de datos no lo necesita) y se
/// destruye al terminar la corrida (Testcontainers lo recoge además con Ryuk si el proceso muere).
/// </summary>
public sealed class ContenedorSqlServer : IAsyncLifetime
{
    /// <summary>Etiqueta fija: <c>2025-latest</c> cambia sin aviso.</summary>
    public const string Imagen = "mcr.microsoft.com/mssql/server:2025-CU9-GDR1-ubuntu-24.04";

    private const string BaseCompartidaMigrada = "nitido_pruebas";
    private const string BaseCompartidaSinTablas = "nitido_sin_tablas";

    /// <summary>Tiempo máximo para crear una base compartida (más el arranque del contenedor, que tiene el suyo).</summary>
    private static readonly TimeSpan TopeDeLaBaseCompartida = TimeSpan.FromMinutes(5);

    // La clave de sa se genera al azar en cada instancia (cada corrida), nunca la constante por defecto de Testcontainers.
    private readonly string _clave;
    private readonly MsSqlContainer _contenedor;
    private readonly Dictionary<string, Task<string>> _basesCompartidas = [];
    private Task? _inicio;

    public ContenedorSqlServer()
    {
        _clave = GeneradorDeClaveSql.Generar();
        _contenedor = new MsSqlBuilder(Imagen).WithPassword(_clave).Build();
    }

    /// <summary>
    /// Clave de <c>sa</c> de esta instancia. Solo para que las pruebas la comparen sin arrancar el contenedor;
    /// nunca debe ir a un mensaje de falla ni a un registro.
    /// </summary>
    internal bool UsaLaMismaClaveQue(ContenedorSqlServer otro) => string.Equals(_clave, otro._clave, StringComparison.Ordinal);

    /// <summary>Tiempo que tardó en quedar listo el contenedor (descarga de la imagen aparte).</summary>
    public TimeSpan TiempoDeArranque { get; private set; }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_inicio is not null)
        {
            try
            {
                await _inicio;
            }
            catch (Exception)
            {
                // Si el arranque falló, igual se intenta liberar lo que haya quedado.
            }
        }

        await _contenedor.DisposeAsync();
    }

    /// <summary>Arranca el contenedor la primera vez; las siguientes esperan el mismo arranque.</summary>
    public Task IniciarAsync()
    {
        return _inicio ??= ArrancarAsync();

        async Task ArrancarAsync()
        {
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            // Margen amplio por la primera descarga de la imagen (unos 1,5 GB).
            using var limite = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            await _contenedor.StartAsync(limite.Token);
            TiempoDeArranque = reloj.Elapsed;
        }
    }

    /// <summary>Cadena de conexión a una base nueva y vacía, con nombre único, en el mismo contenedor.</summary>
    public async Task<string> CrearBaseVaciaAsync(CancellationToken token)
    {
        var nombre = $"nitido_{Guid.NewGuid():N}";
        return await CrearBaseAsync(nombre, token);
    }

    /// <summary>
    /// Base compartida sin ninguna tabla. La usan las pruebas que solo necesitan que la API arranque y
    /// que SQL Server responda (salud, registro, errores, OpenAPI), sin depender de las migraciones.
    /// </summary>
    public Task<string> CadenaDeBaseSinTablasAsync(CancellationToken token) =>
        Compartida(BaseCompartidaSinTablas, tope => CrearBaseAsync(BaseCompartidaSinTablas, tope), token);

    /// <summary>
    /// Base compartida con las migraciones de todos los módulos aplicadas, una sola vez por corrida.
    /// Las pruebas que la usan no deben suponer que las tablas están vacías: cada una crea llaves nuevas.
    /// Si las migraciones fallan, todas las pruebas que dependen de ella fallan con ese mismo error.
    /// </summary>
    public Task<string> CadenaDeBaseMigradaAsync(CancellationToken token) =>
        Compartida(BaseCompartidaMigrada, async tope =>
        {
            var cadena = await CrearBaseAsync(BaseCompartidaMigrada, tope);
            await using var fabrica = new FabricaNitido(cadena);
            await AyudanteDeMigraciones.AplicarAsync(fabrica.Services, tope);
            return cadena;
        }, token);

    /// <summary>
    /// La base compartida la crea una sola vez la primera prueba que la pide, pero la sirve a todas. Por eso su
    /// creación NO usa el token de esa primera prueba (si se cancelara, fallarían todas las demás): corre con su
    /// propio tope de tiempo. El token de quien pide solo decide hasta cuándo espera él.
    /// </summary>
    private Task<string> Compartida(string nombre, Func<CancellationToken, Task<string>> crear, CancellationToken esperaDeQuienPide)
    {
        Task<string> tarea;
        lock (_basesCompartidas)
        {
            if (!_basesCompartidas.TryGetValue(nombre, out var existente))
            {
                existente = CrearConTopeAsync(crear);
                _basesCompartidas[nombre] = existente;
            }

            tarea = existente;
        }

        return tarea.WaitAsync(esperaDeQuienPide);
    }

    private async Task<string> CrearConTopeAsync(Func<CancellationToken, Task<string>> crear)
    {
        // El arranque del contenedor tiene su propio tope (15 minutos por la descarga de la imagen); el de la
        // base cuenta desde que el contenedor ya responde.
        await IniciarAsync();
        using var tope = new CancellationTokenSource(TopeDeLaBaseCompartida);
        return await crear(tope.Token);
    }

    private async Task<string> CrearBaseAsync(string nombre, CancellationToken token)
    {
        await IniciarAsync();

        var maestra = new SqlConnectionStringBuilder(_contenedor.GetConnectionString())
        {
            InitialCatalog = "master",
        };
        await using (var conexion = new SqlConnection(maestra.ConnectionString))
        {
            await conexion.OpenAsync(token);
            await using var comando = conexion.CreateCommand();
            comando.CommandText = $"CREATE DATABASE [{nombre}]";
            await comando.ExecuteNonQueryAsync(token);
        }

        return new SqlConnectionStringBuilder(maestra.ConnectionString) { InitialCatalog = nombre }.ConnectionString;
    }
}
