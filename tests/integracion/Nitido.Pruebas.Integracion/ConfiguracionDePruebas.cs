// Las pruebas de registro redirigen la consola, que es un recurso de todo el proceso.
// Se ejecutan en serie para que no se mezclen con otras.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
