using System.Reflection;
using System.Xml.Linq;

namespace Nitido.Pruebas.Unidad.Arquitectura;

/// <summary>
/// Pruebas de la lectura estática (.csproj y archivos comunes de MSBuild), de la clasificación de
/// proyectos de pruebas y de la carga de ensamblados. Complementan los casos de
/// <see cref="ReglasDeDependenciaPruebas"/>: aquí se ve qué lee el lector, allá qué marca cada regla.
/// </summary>
public class LecturaDeMsBuildPruebas
{
    private static LecturaDeCsproj Leer(string xml) => LectorDeMsBuild.LeerProyecto(XDocument.Parse(xml));

    [Fact]
    public void Lectura_SeparaPorPuntoYComaLosProjectPackageYFrameworkReference()
    {
        var lectura = Leer(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\A\Nitido.A.csproj; ..\B\Nitido.B.csproj;" />
                <PackageReference Include="Uno;Dos" />
                <FrameworkReference Include="Marco.Uno;Marco.Dos" />
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(["Nitido.A", "Nitido.B"], lectura.Referencias);
        Assert.Equal(["Uno", "Dos"], lectura.Paquetes);
        Assert.Equal(["Marco.Uno", "Marco.Dos"], lectura.Marcos);
        Assert.Empty(lectura.Problemas);
    }

    [Fact]
    public void Lectura_UnCsprojNormalNoTieneProblemas()
    {
        var lectura = Leer(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Compile Include="Carpeta\Archivo.cs" />
                <Compile Include="Sub\**\*.cs" />
                <Compile Remove="..\Otro\*.cs" />
                <Compile Include="$(MSBuildThisFileDirectory)Local.cs" />
                <Compile Include="$(MSBuildProjectDirectory)\Sub\**\*.cs" />
                <Compile Include="$(MSBuildProjectDirectory)/Otro.cs" />
                <PackageReference Include="xunit.v3" />
                <PackageReference Update="xunit.v3" />
                <Using Include="Xunit" />
                <ProjectReference Include="..\..\..\Nitido.Compartido\Nitido.Compartido.csproj" />
              </ItemGroup>
            </Project>
            """);

        Assert.Empty(lectura.Problemas);
        Assert.Equal(["Nitido.Compartido"], lectura.Referencias);
        Assert.Equal(["xunit.v3"], lectura.Paquetes);
        Assert.Empty(lectura.Marcos);
    }

    [Theory]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk.Web" />""")]
    [InlineData("""<Project Sdk="microsoft.net.sdk.web" />""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk.Web/10.0.0" />""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk;Microsoft.NET.Sdk.Web" />""")]
    [InlineData("""<Project Sdk="Otro.Sdk/1.0 ; Microsoft.NET.Sdk.Web/10.0.0" />""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><Sdk Name="Microsoft.NET.Sdk.Web" /></Project>""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><Sdk Name="Microsoft.NET.Sdk.Web" Version="10.0.0" /></Project>""")]
    [InlineData("""<Project><Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk.Web" /></Project>""")]
    [InlineData("""<Project sdk="Microsoft.NET.Sdk"><SDK name="Microsoft.NET.Sdk.Web" /></Project>""")]
    public void Lectura_ElSdkWebTraeAspNetCoreEnCualquierForma(string xml)
    {
        var lectura = Leer(xml);

        Assert.Equal(["Microsoft.AspNetCore.App"], lectura.Marcos);
    }

    [Fact]
    public void Lectura_ElSdkRazorSeTrataComoWebPorPrecaucionAunqueNoTraigaElMarcoPorSiSolo()
    {
        // En el SDK 10 solo Microsoft.NET.Sdk.Web agrega Microsoft.AspNetCore.App implícito. Razor se
        // trata igual por precaución: un proyecto Razor no debe pasar por una biblioteca sin ASP.NET Core.
        var lectura = Leer("""<Project Sdk="Microsoft.NET.Sdk.Razor" />""");

        Assert.Equal(["Microsoft.AspNetCore.App"], lectura.Marcos);
    }

    [Fact]
    public void Lectura_LosNombresDeElementoYDeAtributoNoDistinguenMayusculasComoMSBuild()
    {
        var lectura = Leer(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <projectreference Include="..\A\Nitido.A.csproj" />
                <PROJECTREFERENCE include="..\B\Nitido.B.csproj" />
                <packagereference Include="Uno" />
                <FrameworkReference INCLUDE="Marco.Uno" />
                <packagereference Include="Dos.*" />
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(["Nitido.A", "Nitido.B"], lectura.Referencias);
        Assert.Equal(["Uno"], lectura.Paquetes);
        Assert.Equal(["Marco.Uno"], lectura.Marcos);
        var problema = Assert.Single(lectura.Problemas);
        Assert.StartsWith("PackageReference Include=", problema.Elemento, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<import project="..\comun.props" />""", "Import")]
    [InlineData("""<REFERENCE Include="Algo" />""", "Reference")]
    [InlineData("""<compile include="..\Otro\Archivo.cs" />""", "Compile")]
    public void Lectura_ImportReferenceYCompileEnMinusculasTambienSonProblemas(string elemento, string tipo)
    {
        var lectura = Leer($"""<Project Sdk="Microsoft.NET.Sdk">{elemento}</Project>""");

        var problema = Assert.Single(lectura.Problemas);
        Assert.StartsWith(tipo, problema.Elemento, StringComparison.Ordinal);
    }

    [Fact]
    public void Lectura_ElSdkWebNoDuplicaUnFrameworkReferenceDeclarado()
    {
        var lectura = Leer(
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
            </Project>
            """);

        Assert.Equal(["Microsoft.AspNetCore.App"], lectura.Marcos);
    }

    [Theory]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk" />""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk;Microsoft.NET.Sdk.Worker" />""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><Sdk Name="Otro.Sdk" /></Project>""")]
    [InlineData("""<Project />""")]
    public void Lectura_UnSdkQueNoEsWebNoAgregaMarcos(string xml)
    {
        Assert.Empty(Leer(xml).Marcos);
    }

    [Theory]
    [InlineData("""<ProjectReference Include="..\*\*.csproj" />""", "ProjectReference")]
    [InlineData("""<ProjectReference Include="$(Raiz)\X.csproj" />""", "ProjectReference")]
    [InlineData("""<ProjectReference Include="@(Proyectos)" />""", "ProjectReference")]
    [InlineData("""<ProjectReference Include="..\X\X?.csproj" />""", "ProjectReference")]
    [InlineData("""<PackageReference Include="Serilog.*" />""", "PackageReference")]
    [InlineData("""<PackageReference Include="$(Paquete)" />""", "PackageReference")]
    [InlineData("""<FrameworkReference Include="$(Marco)" />""", "FrameworkReference")]
    [InlineData("""<FrameworkReference Include="Microsoft.*" />""", "FrameworkReference")]
    public void Lectura_ElIncludeConComodinOPropiedadEsUnProblemaYNoUnValor(string elemento, string tipo)
    {
        var lectura = Leer($"""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>{elemento}</ItemGroup></Project>""");

        var problema = Assert.Single(lectura.Problemas);
        Assert.StartsWith(tipo, problema.Elemento, StringComparison.Ordinal);
        Assert.Empty(lectura.Referencias);
        Assert.Empty(lectura.Paquetes);
        Assert.Empty(lectura.Marcos);
    }

    [Fact]
    public void Lectura_EnUnIncludeConVariosValoresSoloMarcaElDinamico()
    {
        var lectura = Leer(
            """<Project><ItemGroup><PackageReference Include="Uno;Dos.*;Tres" /></ItemGroup></Project>""");

        Assert.Equal(["Uno", "Tres"], lectura.Paquetes);
        var problema = Assert.Single(lectura.Problemas);
        Assert.Contains("Dos.*", problema.Elemento, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<Import Project="..\comun.props" />""", "Import")]
    [InlineData("""<Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />""", "Import")]
    [InlineData("""<Reference Include="Algo"><HintPath>x.dll</HintPath></Reference>""", "Reference")]
    [InlineData("""<Compile Include="..\Otro\Archivo.cs" />""", "Compile")]
    [InlineData("""<Compile Include="Local.cs;..\Otro\**\*.cs" />""", "Compile")]
    [InlineData("""<Compile Include="$(MSBuildThisFileDirectory)..\Otro\Archivo.cs" />""", "Compile")]
    [InlineData("""<Compile Include="/otro/Archivo.cs" />""", "Compile")]
    [InlineData("""<Compile Include="D:\otro\Archivo.cs" />""", "Compile")]
    [InlineData("""<Compile Include="$(SolutionDir)Modulos\Firmas\Dominio\X.cs" />""", "Compile")]
    [InlineData("""<Compile Include="@(Otros)" />""", "Compile")]
    [InlineData("""<Compile Include="%(Ruta)\X.cs" />""", "Compile")]
    [InlineData("""<Compile Include="Local.cs;$(OtraCarpeta)\X.cs" />""", "Compile")]
    [InlineData("""<Compile Include="$(MSBuildProjectDirectory)\..\Otro\X.cs" />""", "Compile")]
    [InlineData("""<Compile Include="$(MSBuildThisFileDirectory)../Otro/X.cs" />""", "Compile")]
    [InlineData("""<Compile Include="$(MSBuildProjectDirectory)\Sub\..\..\Otro\X.cs" />""", "Compile")]
    [InlineData("""<Compile Include="Sub\$(Otra)\X.cs" />""", "Compile")]
    public void Lectura_ImportReferenceYCompileFueraDeLaCarpetaSonProblemas(string elemento, string tipo)
    {
        var lectura = Leer($"""<Project Sdk="Microsoft.NET.Sdk">{elemento}</Project>""");

        var problema = Assert.Single(lectura.Problemas);
        Assert.StartsWith(tipo, problema.Elemento, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchivoComun_LaCadenaEstandarDeGetPathOfFileAboveSeAceptaEnSuPropioArchivo()
    {
        const string Cadena =
            "$([MSBuild]::GetPathOfFileAbove('{0}', '$(MSBuildThisFileDirectory)../'))";

        foreach (var archivo in new[] { "Directory.Build.props", "Directory.Build.targets" })
        {
            var xml = $"""<Project><Import Project="{string.Format(Cadena, archivo)}" Condition="Exists('x')" /></Project>""";

            var comun = LectorDeMsBuild.LeerArchivoComun($"Modulos/Clientes/{archivo}", XDocument.Parse(xml));

            Assert.Empty(comun.ReferenciasAgregadas);
        }
    }

    [Theory]
    [InlineData("Directory.Build.props", "Directory.Build.targets")]
    [InlineData("Directory.Build.targets", "Directory.Build.props")]
    [InlineData("Directory.Packages.props", "Directory.Packages.props")]
    public void ArchivoComun_LaCadenaEstandarDeOtroArchivoOEnPackagesPropsSeMarca(string archivo, string buscado)
    {
        var xml = $"""
            <Project><Import Project="$([MSBuild]::GetPathOfFileAbove('{buscado}', '$(MSBuildThisFileDirectory)../'))" /></Project>
            """;

        var comun = LectorDeMsBuild.LeerArchivoComun(archivo, XDocument.Parse(xml));

        Assert.Single(comun.ReferenciasAgregadas);
    }

    [Theory]
    [InlineData("""<Import Project="..\comun.props" />""")]
    [InlineData("""<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../../'))" />""")]
    [InlineData("""<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(Raiz)'))" />""")]
    [InlineData("""<Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />""")]
    public void ArchivoComun_CualquierOtroImportSeMarca(string importacion)
    {
        var comun = LectorDeMsBuild.LeerArchivoComun(
            "Directory.Build.props",
            XDocument.Parse($"<Project>{importacion}</Project>"));

        Assert.Single(comun.ReferenciasAgregadas);
    }

    [Fact]
    public void ArchivoComun_SigueMarcandoLasReferenciasQueYaMarcaba()
    {
        var comun = LectorDeMsBuild.LeerArchivoComun(
            "Directory.Packages.props",
            XDocument.Parse(
                """
                <Project><ItemGroup>
                  <PackageVersion Include="Serilog" Version="1.0" />
                  <GlobalPackageReference Include="Serilog" />
                  <PackageReference Include="Otro" />
                </ItemGroup></Project>
                """));

        Assert.Equal(["GlobalPackageReference Serilog", "PackageReference Otro"], comun.ReferenciasAgregadas);
    }

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    public void ArchivoComun_LaCadenaEstandarEnLaRaizDelRepositorioSeMarcaPorqueImportariaUnArchivoDeFuera(string archivo)
    {
        var xml = $"""
            <Project><Import Project="$([MSBuild]::GetPathOfFileAbove('{archivo}', '$(MSBuildThisFileDirectory)../'))" /></Project>
            """;

        var enLaRaiz = LectorDeMsBuild.LeerArchivoComun(archivo, XDocument.Parse(xml));
        var anidado = LectorDeMsBuild.LeerArchivoComun($"tests/{archivo}", XDocument.Parse(xml));

        Assert.Single(enLaRaiz.ReferenciasAgregadas);
        Assert.Empty(anidado.ReferenciasAgregadas);
    }

    [Fact]
    public void ArchivoComun_LosElementosEnMinusculasSeMarcanIgual()
    {
        var comun = LectorDeMsBuild.LeerArchivoComun(
            "Directory.Build.props",
            XDocument.Parse(
                """
                <Project>
                  <import Project="..\comun.props" />
                  <ItemGroup><packagereference include="Serilog" /><REFERENCE Include="Otro" /></ItemGroup>
                </Project>
                """));

        Assert.Equal(["Import ..\\comun.props", "PackageReference Serilog", "Reference Otro"], comun.ReferenciasAgregadas);
    }

    [Theory]
    [InlineData("Modulos/Clientes/Aplicacion/Nitido.Clientes.Aplicacion.csproj", @"..\Dominio\Nitido.Clientes.Dominio.csproj", "Modulos/Clientes/Dominio/Nitido.Clientes.Dominio.csproj")]
    [InlineData("Modulos/Clientes/Aplicacion/Nitido.Clientes.Aplicacion.csproj", @"..\..\Firmas\Contratos\Nitido.Firmas.Contratos.csproj", "Modulos/Firmas/Contratos/Nitido.Firmas.Contratos.csproj")]
    [InlineData("tests/unidad/Nitido.Pruebas.Unidad/Nitido.Pruebas.Unidad.csproj", @"..\..\..\Nitido.Api\Nitido.Api.csproj", "Nitido.Api/Nitido.Api.csproj")]
    [InlineData("Nitido.Api/Nitido.Api.csproj", @".\..\Nitido.Compartido\Nitido.Compartido.csproj", "Nitido.Compartido/Nitido.Compartido.csproj")]
    [InlineData("Modulos/Clientes/Aplicacion/Nitido.Clientes.Aplicacion.csproj", @"..\..\..\..\Otro\X.csproj", "../Otro/X.csproj")]
    public void Lectura_LasRutasDeLosProjectReferenceSeResuelvenContraLaCarpetaDelProyecto(
        string rutaDelProyecto,
        string include,
        string esperada)
    {
        var lectura = Leer($"""<Project><ItemGroup><ProjectReference Include="{include}" /></ItemGroup></Project>""");

        var rutas = LectorDeMsBuild.ResolverRutas(rutaDelProyecto, lectura.IncludesDeReferencias);

        var ruta = Assert.Single(rutas);
        Assert.Equal(esperada, ruta.Ruta);
        Assert.Equal(Path.GetFileNameWithoutExtension(esperada), ruta.Nombre);
    }

    [Fact]
    public void BuscarArchivosComunes_NoEntraALasCarpetasQueElGitignoreIgnora()
    {
        using var raiz = new CarpetaTemporal();
        foreach (var carpeta in new[]
        {
            ".", "Modulos/Clientes", "bin", "obj", "TestResults", "coverage", "almacen", "node_modules", "Modulos/Clientes/obj",
        })
        {
            raiz.Archivo($"{carpeta}/Directory.Build.props");
        }

        var encontrados = ModeloReal
            .BuscarArchivosComunes(new DirectoryInfo(raiz.Ruta), Listar)
            .Select(a => Path.GetRelativePath(raiz.Ruta, a.FullName).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["Directory.Build.props", "Modulos/Clientes/Directory.Build.props"], encontrados);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    public void BuscarArchivosComunes_SeSaltaLaCarpetaQueNoSePuedeLeerYSigue(Type excepcion)
    {
        using var raiz = new CarpetaTemporal();
        raiz.Archivo("Directory.Build.props");
        raiz.Archivo("Cerrada/Directory.Build.props");
        raiz.Archivo("Abierta/Directory.Build.props");

        var encontrados = ModeloReal
            .BuscarArchivosComunes(
                new DirectoryInfo(raiz.Ruta),
                carpeta => carpeta.Name == "Cerrada"
                    ? throw (Exception)Activator.CreateInstance(excepcion)!
                    : Listar(carpeta))
            .Select(a => Path.GetRelativePath(raiz.Ruta, a.FullName).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["Abierta/Directory.Build.props", "Directory.Build.props"], encontrados);
    }

    [Fact]
    public void BuscarArchivosComunes_UnErrorQueNoEsDeAccesoNoSeTraga()
    {
        using var raiz = new CarpetaTemporal();
        raiz.Archivo("Cerrada/Directory.Build.props");

        Assert.Throws<IOException>(() => ModeloReal.BuscarArchivosComunes(
            new DirectoryInfo(raiz.Ruta),
            carpeta => carpeta.Name == "Cerrada" ? throw new IOException("disco") : Listar(carpeta)));
    }

    private static (IReadOnlyList<FileInfo> Archivos, IReadOnlyList<DirectoryInfo> Carpetas) Listar(
        DirectoryInfo carpeta) =>
        (carpeta.GetFiles(), carpeta.GetDirectories());

    /// <summary>Una carpeta nueva dentro de la carpeta temporal del sistema, que se borra al terminar.</summary>
    private sealed class CarpetaTemporal : IDisposable
    {
        public CarpetaTemporal()
        {
            Ruta = Path.Combine(Path.GetTempPath(), "nitido-pruebas-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Ruta);
        }

        public string Ruta { get; }

        public void Archivo(string relativa)
        {
            var completa = Path.GetFullPath(Path.Combine(Ruta, relativa));
            Directory.CreateDirectory(Path.GetDirectoryName(completa)!);
            File.WriteAllText(completa, "<Project />");
        }

        public void Dispose() => Directory.Delete(Ruta, recursive: true);
    }

    [Theory]
    [InlineData("Nitido.Pruebas.Unidad", "tests/unidad/Nitido.Pruebas.Unidad/Nitido.Pruebas.Unidad.csproj", "Pruebas")]
    [InlineData("Nitido.Pruebas.Unidad", null, "Pruebas")]
    [InlineData("Nitido.Pruebas.Unidad", "Extra/Nitido.Pruebas.Unidad.csproj", "Desconocido")]
    [InlineData("Nitido.ContextoDePrueba", "tests/apoyo/Nitido.ContextoDePrueba/Nitido.ContextoDePrueba.csproj", "Desconocido")]
    [InlineData("Nitido.ContextoDePrueba", null, "Desconocido")]
    [InlineData("Nitido.Pagos.Dominio", "tests/unidad/Nitido.Pagos.Dominio/Nitido.Pagos.Dominio.csproj", "Modulo")]
    [InlineData("Nitido.Pagos.Dominio", null, "Modulo")]
    public void Clasificador_UnProyectoEsDePruebasSoloConRutaBajoTestsYNombreNitidoPruebas(
        string nombre,
        string? ruta,
        string esperado)
    {
        Assert.Equal(esperado, Clasificador.Clasificar(nombre, ruta).Tipo.ToString());
    }

    [Theory]
    [InlineData("Nitido.Pruebas.Unidad", null, true)]
    [InlineData("Nitido.ContextoDePrueba", "tests/apoyo/Nitido.ContextoDePrueba/Nitido.ContextoDePrueba.csproj", true)]
    [InlineData("Nitido.ContextoDePrueba", null, false)]
    [InlineData("Nitido.Clientes.Dominio", "Modulos/Clientes/Dominio/Nitido.Clientes.Dominio.csproj", false)]
    public void Clasificador_UnaReferenciaParecePruebasPorRutaOPorNombre(string nombre, string? ruta, bool esperado)
    {
        Assert.Equal(esperado, Clasificador.ParecePruebas(nombre, ruta));
    }

    [Fact]
    public void Carga_UnFileLoadExceptionGuardaTipoYMensajeEnVezDeTragarselo()
    {
        var (usados, error) = ModeloReal.CargarEnsamblado(
            "Nitido.X",
            _ => throw new FileLoadException("No se pudo cargar el archivo\r\nde Nitido.X."));

        Assert.Null(usados);
        Assert.Equal("FileLoadException: No se pudo cargar el archivo de Nitido.X.", error);
    }

    [Fact]
    public void Carga_UnBadImageFormatExceptionGuardaTipoYMensaje()
    {
        var (usados, error) = ModeloReal.CargarEnsamblado(
            "Nitido.X",
            _ => throw new BadImageFormatException("Formato incorrecto."));

        Assert.Null(usados);
        Assert.Equal("BadImageFormatException: Formato incorrecto.", error);
    }

    [Fact]
    public void Carga_UnFileNotFoundExceptionNoGuardaErrorPorqueEsQueElHostNoLoCompone()
    {
        var (usados, error) = ModeloReal.CargarEnsamblado(
            "Nitido.X",
            n => throw new FileNotFoundException("No está.", n));

        Assert.Null(usados);
        Assert.Null(error);
    }

    [Fact]
    public void Carga_OtraExcepcionNoSeTraga()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ModeloReal.CargarEnsamblado("Nitido.X", _ => throw new InvalidOperationException("falla")));
    }

    [Fact]
    public void Carga_UnEnsambladoQueCargaDevuelveLosQueUsaSinErrores()
    {
        var (usados, error) = ModeloReal.CargarEnsamblado(
            "Nitido.Compartido",
            n => Assembly.Load(new AssemblyName(n)));

        Assert.Null(error);
        Assert.NotNull(usados);
        Assert.Contains(usados, n => n.StartsWith("System.", StringComparison.Ordinal));
    }

    [Fact]
    public void Host_ElErrorDeCargaSeDistingueDeQueElHostNoLoCompone()
    {
        var sano = ModelosSinteticos.DosModulosBienFormados();

        var noCompuesto = ReglasDeArquitectura.HostComponeTodosLosModulos(
            sano.SinCargar("Nitido.Firmas.Dominio"));
        var conError = ReglasDeArquitectura.HostComponeTodosLosModulos(
            sano.ConErrorDeCarga("Nitido.Firmas.Dominio", "FileLoadException", "Mensaje de prueba."));

        var primera = Assert.Single(noCompuesto);
        Assert.Contains("el host no lo compone", primera, StringComparison.Ordinal);

        var segunda = Assert.Single(conError);
        Assert.Contains("FileLoadException: Mensaje de prueba.", segunda, StringComparison.Ordinal);
        Assert.DoesNotContain("el host no lo compone", segunda, StringComparison.Ordinal);
    }

    [Fact]
    public void ReglasDeDependencia_ElModeloSanoTambienSeAceptaConUnCsprojLeido()
    {
        // Un .csproj bien escrito leído con el lector real no agrega infracciones.
        var modelo = ModelosSinteticos.DosModulosBienFormados().ConCsproj(
            "Nitido.Clientes.Aplicacion",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="FluentValidation" />
                <ProjectReference Include="..\Dominio\Nitido.Clientes.Dominio.csproj;..\..\Firmas\Contratos\Nitido.Firmas.Contratos.csproj" />
              </ItemGroup>
            </Project>
            """);

        foreach (var (nombre, regla) in ReglasDeArquitectura.Todas)
        {
            Assert.True(
                regla(modelo).Count == 0,
                $"{nombre} marcó un .csproj bien formado: {string.Join(" | ", regla(modelo))}");
        }
    }
}
