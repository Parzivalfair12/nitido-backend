namespace Nitido.Pruebas.Unidad.Arquitectura;

/// <summary>
/// Guardianes de arquitectura sobre la solución real (docs/arquitectura-backend.md, sección 5).
/// Cada prueba aplica una regla pura de <see cref="ReglasDeArquitectura"/> al modelo armado con
/// Nitido.slnx, los .csproj y la reflexión de cada ensamblado. Que cada regla detecta su
/// infracción lo demuestra <see cref="ReglasDeDependenciaPruebas"/> con modelos sintéticos.
/// También incluye la regla 7 de CLAUDE.md: nada de SDK de una nube en el dominio ni en los
/// casos de uso.
/// </summary>
public class ArquitecturaPruebas
{
    [Fact]
    public void Solucion_ProyectosSiguenConvencionDeNombresYCarpetas() =>
        Verificar(ReglasDeArquitectura.Convencion);

    [Fact]
    public void Compartido_SoloDependeDelFramework() =>
        Verificar(ReglasDeArquitectura.Compartido);

    [Fact]
    public void Dominio_SoloDependeDeCompartido() =>
        Verificar(ReglasDeArquitectura.Dominio);

    [Fact]
    public void Contratos_SoloDependenDeCompartido() =>
        Verificar(ReglasDeArquitectura.Contratos);

    [Fact]
    public void Aplicacion_NoDependeDeInfraestructuraNiWeb() =>
        Verificar(ReglasDeArquitectura.Aplicacion);

    [Fact]
    public void Api_NoReferenciaInfraestructura() =>
        Verificar(ReglasDeArquitectura.Api);

    [Fact]
    public void Infraestructura_NoReferenciaApi() =>
        Verificar(ReglasDeArquitectura.Infraestructura);

    [Fact]
    public void Modulos_SoloSeReferencianPorContratos() =>
        Verificar(ReglasDeArquitectura.Modulos);

    [Fact]
    public void InfraestructuraYApi_SoloLasReferenciaElHost() =>
        Verificar(ReglasDeArquitectura.SoloElHost);

    [Fact]
    public void Host_ComponeTodosLosModulos() =>
        Verificar(ReglasDeArquitectura.Host);

    [Fact]
    public void ArchivosComunesDeMsBuild_NoAgreganReferencias() =>
        Verificar(ReglasDeArquitectura.ArchivosComunes);

    [Fact]
    public void SdkDeNube_SoloEnProyectosDeProveedor() =>
        Verificar(ReglasDeArquitectura.SdkDeNube);

    [Fact]
    public void SdkDeNube_ListaDeProveedoresAutorizadosEstaVacia()
    {
        // Hoy ningún ensamblado Nitido.* puede referenciar un SDK de nube. Agregar un proveedor
        // exige el ADR de despliegue y un cambio explícito de esta prueba.
        Assert.Empty(ReglasDeArquitectura.ProveedoresDeNubeAutorizados);
    }

    [Fact]
    public void Modelo_SeCompletaConLaReflexionDeLosEnsamblados()
    {
        var modelo = ModeloReal.Obtener();

        var host = Assert.Single(modelo.Proyectos, p => p.Tipo == TipoProyecto.Host);
        var compartido = Assert.Single(modelo.Proyectos, p => p.Tipo == TipoProyecto.Compartido);

        // Sin la parte de reflexión, las reglas sobre ensamblados pasarían en vacío.
        Assert.NotNull(host.EnsambladosUsados);
        Assert.NotEmpty(host.EnsambladosUsados);
        Assert.NotNull(compartido.EnsambladosUsados);
        Assert.NotEmpty(compartido.EnsambladosUsados);
        Assert.Contains(compartido.EnsambladosUsados, n => n.StartsWith("System.", StringComparison.Ordinal));
    }

    [Fact]
    public void Modelo_IncluyeNitidoCompartidoApi_ParaQueLaReglaDeApiLoRevise()
    {
        // F0-02b: Api_NoReferenciaInfraestructura revisa también a Nitido.Compartido.Api (paquetes de EF Core y
        // ensamblado). Si el proyecto no estuviera en el modelo, o no se hubiera cargado su ensamblado, esa parte
        // de la regla pasaría en vacío.
        var modelo = ModeloReal.Obtener();

        var compartidoApi = Assert.Single(modelo.Proyectos, p => p.Tipo == TipoProyecto.CompartidoApi);
        Assert.Equal("Nitido.Compartido.Api", compartidoApi.Nombre);
        Assert.Equal("Nitido.Compartido.Api/Nitido.Compartido.Api.csproj", compartidoApi.Ruta);
        Assert.NotNull(compartidoApi.EnsambladosUsados);
        Assert.Contains("Nitido.Compartido", compartidoApi.EnsambladosUsados);
        Assert.Contains("Nitido.Compartido", compartidoApi.Referencias);
        Assert.DoesNotContain(compartidoApi.Paquetes, p => p.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(compartidoApi.Usados, u => u.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain("Nitido.Compartido.Infraestructura", compartidoApi.Usados);
    }

    [Fact]
    public void Modelo_ElHostYLosApiDeLosModulosReferencianNitidoCompartidoApi()
    {
        // Diseño 1: el host y los dos Api de módulo llevan ProjectReference a Nitido.Compartido.Api (plantilla 4.3);
        // la Infraestructura de un módulo no.
        var modelo = ModeloReal.Obtener();

        foreach (var nombre in new[] { "Nitido.Api", "Nitido.Firmas.Api", "Nitido.Clientes.Api" })
        {
            var proyecto = Assert.Single(modelo.Proyectos, p => p.Nombre == nombre);
            Assert.Contains("Nitido.Compartido.Api", proyecto.Referencias);
        }

        foreach (var infraestructura in modelo.Proyectos.Where(p => p.Capa == CapaDeModulo.Infraestructura))
        {
            Assert.DoesNotContain("Nitido.Compartido.Api", infraestructura.Referencias);
            Assert.DoesNotContain("Nitido.Compartido.Api", infraestructura.Usados);
        }
    }

    [Fact]
    public void Modelo_LeeLasReferenciasDeLosCsproj()
    {
        var modelo = ModeloReal.Obtener();

        var host = Assert.Single(modelo.Proyectos, p => p.Tipo == TipoProyecto.Host);
        Assert.Contains("Nitido.Compartido", host.Referencias);
        Assert.Contains("Serilog.AspNetCore", host.Paquetes);

        var unidad = Assert.Single(modelo.Proyectos, p => p.Nombre == "Nitido.Pruebas.Unidad");
        Assert.Contains("Nitido.Api", unidad.Referencias);
        Assert.Equal(
            "tests/unidad/Nitido.Pruebas.Unidad/Nitido.Pruebas.Unidad.csproj",
            unidad.Ruta);
    }

    [Fact]
    public void Modelo_EncuentraLosArchivosComunesDeMsBuild()
    {
        var modelo = ModeloReal.Obtener();

        // Si no los encontrara, ArchivosComunesDeMsBuild_NoAgreganReferencias pasaría en vacío.
        Assert.Contains(modelo.ArchivosComunes, a => a.Ruta == "Directory.Build.props");
    }

    [Theory]
    [InlineData("Azure.Storage.Blobs", true)]
    [InlineData("Azure.Core", true)]
    [InlineData("AWSSDK.S3", true)]
    [InlineData("AWSSDK.Core", true)]
    [InlineData("Amazon.Runtime", true)]
    [InlineData("Google.Cloud.Storage.V1", true)]
    [InlineData("Serilog", false)]
    [InlineData("Serilog.AspNetCore", false)]
    [InlineData("Microsoft.AspNetCore.OpenApi", false)]
    [InlineData("System.Runtime", false)]
    [InlineData("Nitido.Compartido", false)]
    [InlineData("Google.Protobuf", false)]
    public void DeteccionDeSdkDeNube_MarcaSoloLosPrefijosProhibidos(string nombre, bool seMarca)
    {
        var marcados = BuscarSdkDeNube([nombre]);

        Assert.Equal(seMarca, marcados.Count == 1);
    }

    [Fact]
    public void DeteccionDeSdkDeNube_DevuelveSoloLosMarcadosDeUnaLista()
    {
        string[] referencias =
        [
            "System.Runtime",
            "Azure.Storage.Blobs",
            "Serilog",
            "AWSSDK.S3",
            "Google.Cloud.Storage.V1",
            "Amazon.Runtime",
        ];

        var marcados = BuscarSdkDeNube(referencias);

        Assert.Equal(
            ["Azure.Storage.Blobs", "AWSSDK.S3", "Google.Cloud.Storage.V1", "Amazon.Runtime"],
            marcados);
    }

    private static List<string> BuscarSdkDeNube(IEnumerable<string> nombres) =>
        ReglasDeArquitectura.BuscarSdkDeNube(nombres);

    private static void Verificar(string nombreDeLaRegla)
    {
        var infracciones = ReglasDeArquitectura.Todas[nombreDeLaRegla](ModeloReal.Obtener());

        Assert.True(
            infracciones.Count == 0,
            $"{nombreDeLaRegla} no se cumple:{Environment.NewLine}"
            + string.Join(Environment.NewLine, infracciones.Select(i => $"  - {i}")));
    }
}
