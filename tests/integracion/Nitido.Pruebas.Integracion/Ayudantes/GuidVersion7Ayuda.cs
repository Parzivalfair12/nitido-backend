namespace Nitido.Pruebas.Integracion;

/// <summary>Lectura de un GUID versión 7 (RFC 9562): versión, variante y marca de tiempo de 48 bits.</summary>
internal static class GuidVersion7Ayuda
{
    /// <summary>Los primeros 48 bits del GUID: milisegundos desde 1970-01-01 UTC.</summary>
    public static long MarcaDeTiempoEnMilisegundos(this Guid guid)
    {
        var bytes = guid.ToByteArray(bigEndian: true);
        long marca = 0;
        for (var i = 0; i < 6; i++)
        {
            marca = (marca << 8) | bytes[i];
        }

        return marca;
    }

    public static void AssertEsVersion7Con(Guid guid, DateTimeOffset ahora, string que)
    {
        Assert.True(guid != Guid.Empty, $"{que}: la llave quedó vacía.");
        Assert.True(guid.Version == 7, $"{que}: la llave {guid} no es GUID versión 7 (versión {guid.Version}).");
        Assert.True(
            (guid.ToByteArray(bigEndian: true)[8] & 0xC0) == 0x80,
            $"{que}: la llave {guid} no tiene la variante RFC.");
        Assert.True(
            guid.MarcaDeTiempoEnMilisegundos() == ahora.ToUnixTimeMilliseconds(),
            $"{que}: la marca de tiempo de {guid} es {guid.MarcaDeTiempoEnMilisegundos()} y el reloj marca {ahora.ToUnixTimeMilliseconds()}.");
    }
}
