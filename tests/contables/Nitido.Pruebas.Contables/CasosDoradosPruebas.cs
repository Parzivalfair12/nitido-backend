namespace Nitido.Pruebas.Contables;

/// <summary>
/// Marcador de la fase 0: todavía no hay casos contables, pero la carpeta donde vivirán
/// (tests/contables/casos/) debe existir en el repositorio.
/// </summary>
public class CasosDoradosPruebas
{
    [Fact]
    public void CasosDorados_CarpetaExiste()
    {
        var raiz = BuscarRaizDelRepositorio();
        var carpeta = Path.Combine(raiz, "tests", "contables", "casos");

        Assert.True(Directory.Exists(carpeta), $"No existe la carpeta de casos dorados: {carpeta}");
    }

    private static string BuscarRaizDelRepositorio()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null)
        {
            if (File.Exists(Path.Combine(directorio.FullName, "Nitido.slnx")))
            {
                return directorio.FullName;
            }

            directorio = directorio.Parent;
        }

        throw new InvalidOperationException(
            $"No se encontró Nitido.slnx subiendo desde {AppContext.BaseDirectory}.");
    }
}
