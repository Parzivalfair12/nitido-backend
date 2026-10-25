# F0-01b · Fase 0 · 1b · Base de Clean Architecture

| | |
| --- | --- |
| Estimación | 1 día |
| Depende de | F0-01 (terminada). El [ADR 0001](../adr/0001-clean-architecture.md) fue aceptado el 2026-10-18 |
| Orden de construcción | 2 de 11: después de F0-01 y antes de F0-02a |
| Repositorio | Backend (`nitido-backend`). El frontend no cambia. |
| Etiquetas | `fase-0`, `historia`, `infraestructura`, `backend`, `requiere-adr` |
| Estado | Terminada el 2026-10-18: dos revisiones sin hallazgos graves ni medios abiertos; 243 pruebas en verde (234 de unidad, 8 de integración, 1 contable). ADR 0001 aceptado. Sin issue en GitHub. |

Diseño completo: `docs/arquitectura-backend.md`. Esta historia construye la base. Los módulos reales (Firmas y Clientes) se crean con la plantilla en F0-02a.

## Repositorio

`backend`.

## Historia

Como desarrollador de nítido quiero que la regla de dependencias de Clean Architecture y las fronteras entre módulos se verifiquen solas en cada compilación y en cada `dotnet test`, para que ningún agente pueda poner EF Core en el dominio, saltarse la interfaz pública de otro módulo o meter un SDK de nube en los casos de uso sin que una prueba falle.

## Criterios de aceptación

### Decisión

- [x] El ADR `docs/adr/0001-clean-architecture.md` está en estado «Aceptado», y el índice de `docs/adr/README.md` lo refleja. Aceptado por el usuario el 2026-10-18.

### Núcleo compartido

- [ ] `Nitido.Compartido/Resultados/` contiene `TipoError` (`Validacion`, `NoEncontrado`, `Conflicto`, `NoAutorizado`), `Error`, `Resultado` y `Resultado<T>`, como describe `docs/arquitectura-backend.md`, sección 3.2.
- [ ] `Nitido.Compartido.csproj` sigue sin `ProjectReference`, `PackageReference` ni `FrameworkReference`.
- [ ] Pruebas de unidad de `Resultado` en `Nitido.Pruebas.Unidad`, todas en verde:
  - `Resultado_ExitoExponeElValor`;
  - `Resultado_ErrorExponeElError`;
  - `Resultado_LeerValorDeUnErrorLanzaExcepcion`;
  - `Resultado_LeerErrorDeUnExitoLanzaExcepcion`;
  - `Resultado_SeConvierteImplicitamenteDesdeValorYDesdeError`;
  - `Error_ValidacionConservaLosCampos`.

### Pruebas de arquitectura

Todas en `Nitido.Pruebas.Unidad` (se sugiere la carpeta `Arquitectura/`) y sin paquetes nuevos: solo reflexión y `System.Xml.Linq`. El detalle de cada una está en `docs/arquitectura-backend.md`, sección 5.3.

- [ ] Las reglas se escriben como funciones puras sobre un modelo de proyectos, que se arma leyendo `Nitido.slnx` y cada `.csproj`, y se completa con `Assembly.Load` de cada proyecto de la solución y `GetReferencedAssemblies()`.
- [ ] La raíz del repositorio se busca subiendo hasta `Nitido.slnx`. Las pruebas pasan sin importar el directorio de trabajo.
- [ ] Existen y pasan sobre la solución real:
  - `Solucion_ProyectosSiguenConvencionDeNombresYCarpetas`
  - `Compartido_SoloDependeDelFramework`
  - `Dominio_SoloDependeDeCompartido`
  - `Contratos_SoloDependenDeCompartido`
  - `Aplicacion_NoDependeDeInfraestructuraNiWeb`
  - `Api_NoReferenciaInfraestructura`
  - `Infraestructura_NoReferenciaApi`
  - `Modulos_SoloSeReferencianPorContratos`
  - `InfraestructuraYApi_SoloLasReferenciaElHost`
  - `Host_ComponeTodosLosModulos`
  - `ArchivosComunesDeMsBuild_NoAgreganReferencias`
  - `SdkDeNube_SoloEnProyectosDeProveedor`
- [ ] Hoy no hay módulos, así que las pruebas por capa no tienen proyectos que revisar. Para que no pasen en vacío sin que nadie lo note:
  - `Solucion_ProyectosSiguenConvencionDeNombresYCarpetas` falla si no encuentra `Nitido.Api` y `Nitido.Compartido`;
  - cada regla está cubierta por los casos sintéticos del criterio siguiente.
- [ ] `ReglasDeDependencia_DetectanCadaInfraccion`: es una teoría con al menos un caso sintético que infringe cada una de las reglas anteriores, y comprueba que la regla lo marca, con un mensaje que nombra el proyecto y la referencia. Incluye como mínimo estos casos:
  - un `Dominio` con `PackageReference` a EF Core;
  - una `Aplicacion` con `FrameworkReference` a ASP.NET Core;
  - un módulo que referencia la `Infraestructura` de otro;
  - un `Api` que referencia su `Infraestructura`;
  - un proyecto que no es el host y referencia un `Api`;
  - un host que no compone un módulo;
  - un `Directory.Build.props` con `PackageReference`;
  - un ensamblado de `Aplicacion` que usa `Serilog`, detectado por la parte de reflexión.
- [ ] `ReglasDeDependencia_AceptanUnModuloBienFormado`: un modelo sintético de dos módulos según la plantilla de la sección 4.3, donde uno usa el `Contratos` del otro, no produce infracciones.

### Regla 7

- [ ] `Compartido_NoDependeDeSdkDeNube` se renombra a `SdkDeNube_SoloEnProyectosDeProveedor`. Mantiene la revisión por reflexión y agrega la de los `PackageReference` de cada `.csproj`.
- [ ] Su lista de proyectos de proveedor autorizados está vacía. Hoy ningún ensamblado `Nitido.*` puede referenciar `Azure.`, `Amazon`, `AWSSDK` ni `Google.Cloud`: la regla no se afloja.
- [ ] `DeteccionDeSdkDeNube_MarcaSoloLosPrefijosProhibidos` y `DeteccionDeSdkDeNube_DevuelveSoloLosMarcadosDeUnaLista` quedan sin cambios.

### Nada se rompe

- [ ] `dotnet build` termina con «0 Advertencias» y «0 Errores».
- [ ] `dotnet format --verify-no-changes` sale con código 0.
- [ ] `dotnet test` pasa con las 23 pruebas anteriores (22 sin cambios y 1 renombrada) más las nuevas. El conteo por proyecto queda anotado en el PR.
- [ ] `Directory.Packages.props` no tiene paquetes nuevos, y `Nitido.Api` y `Program.cs` no cambian. Las 8 pruebas de integración siguen en verde sin tocarse.
- [ ] `dotnet list package --vulnerable --include-transitive` sigue limpio.

## Pantalla

No aplica.

## Reglas contables y tributarias

No aplica. Es una historia de estructura, sin reglas contables.

## Diseño técnico

Resumen. El detalle está en `docs/arquitectura-backend.md`.

### 1. Módulos que cambian

- **Ningún módulo de negocio.** Solo cambia `Nitido.Compartido`, que gana `Resultados/`, y `tests/unidad`, que gana las pruebas de arquitectura y las de `Resultado`.
- **No se crean** `Nitido.Compartido.Infraestructura` (llega en F0-02a) ni `Nitido.Compartido.Api` (llega en F0-02b): no se crean proyectos vacíos. Las reglas ya los contemplan por nombre.
- **No se crea un módulo de ejemplo.** Las reglas se prueban con modelos sintéticos, y un módulo de ejemplo sería código descartable.

### 2. Tablas

Ninguna.

### 3. Endpoints

Ninguno.

### 4. Eventos internos

Ninguno.

### 5. Trabajos en segundo plano

Ninguno.

### 6. Pantallas

Ninguna. El frontend no cambia, y el contrato OpenAPI tampoco.

### 7. Riesgos

- **Pruebas que pasan en vacío** mientras no haya módulos. Lo mitigan los casos sintéticos de cada regla y la verificación de que el modelo encontró el host y Compartido.
- **Falsos positivos de la reflexión:** `GetReferencedAssemblies()` solo lista lo que el código usa. Por eso se combina con la lectura de los `.csproj`, que ve lo declarado.
- **Rutas en Windows y Linux:** los `.csproj` usan `\` en `Include`. El modelo normaliza los separadores antes de comparar, porque CI corre en Linux.
- **Seguridad y multiempresa:** no aplican. No hay datos ni endpoints nuevos.

### 8. Plan de implementación

| Paso | Agente | Qué | En paralelo con |
| --- | --- | --- | --- |
| 0 | usuario | Acepta el ADR 0001 y decide los puntos abiertos de abajo. **Hecho el 2026-10-18.** | ninguno |
| 1 | `tester` | Pruebas de `Resultado` (no compilan hasta el paso 2). Modelo de solución, reglas, casos sintéticos y pruebas sobre la solución real. Renombra y amplía la prueba de la regla 7. | paso 2 |
| 2 | `dev-backend` | `Nitido.Compartido/Resultados/` (`TipoError`, `Error`, `Resultado`, `Resultado<T>`). Nada más en producción. | paso 1 |
| 3 | `tester` | Verificación: cada criterio con su comando y el conteo de pruebas. | ninguno |
| 4 | `revisor` | Reglas que no pasen en vacío, lista de proveedores vacía, sin paquetes nuevos y nombres en español. | ninguno |

`dba` y `devops` no participan. Un día de trabajo.

### Puntos abiertos (decididos por el usuario el 2026-10-18)

1. Aceptar el ADR 0001: Clean Architecture por módulo, con un `.csproj` por capa. **Recomendación:** sí. **Decidido el 2026-10-18: sí.**
2. Nombres técnicos propios en español (`IRepositorioClientes`, `CrearCliente`), y no en inglés como dice el plan. **Recomendación:** español, como el código actual. **Decidido el 2026-10-18: sí.**
3. Un solo `IContextoSesion` en lugar de `IUsuarioActual` más `IContextoSesion`. **Recomendación:** uno solo (afecta los criterios de F0-02a). **Decidido el 2026-10-18: sí.** Aplicado en F0-02a.
4. Reemplazar los criterios con nombre propio de F0-02a (`Dominio_NoReferenciaEntityFramework`) y F0-04 (`Clientes_NoReferenciaInfraestructuraDeFirmas`) por las pruebas genéricas de esta historia. **Recomendación:** sí. **Decidido el 2026-10-18: sí.** Aplicado en F0-02a y F0-04.

El usuario aprobó además, el mismo día, tres puntos de `docs/arquitectura-backend.md` (sección 6): la clase de `T_USUARIOS` vive en `Firmas.Infraestructura` desde F0-02a (F0-02a), el `401` de F0-03a usa ProblemDetails con el texto en `title` (F0-03a) y no se crea un módulo de ejemplo en esta historia (ver «Módulos que cambian»).

## Fuera de alcance

- Crear Firmas, Clientes u otro módulo (F0-02a, con la plantilla de la sección 4.3).
- `Nitido.Compartido.Infraestructura` (F0-02a) y `Nitido.Compartido.Api` (F0-02b).
- `IDeEmpresa`, `IDeFirma` e `IContextoSesion` (F0-02a y F0-02b).
- La conversión de `Resultado` a ProblemDetails, que vive en `Nitido.Compartido.Api` (F0-02b).
- Cambios en `Program.cs`, `Nitido.slnx` o el host.
- Eventos internos y bandeja de salida (ADR en la fase 2).
- Integración continua (F0-06).
- Editar `CLAUDE.md`, las skills o los agentes: los cambios de la sección 9 de `docs/arquitectura-backend.md` ya se aplicaron el 2026-10-18, fuera de esta historia.

## Pendientes de la revisión (2026-10-18), para el arquitecto

No bloquean esta historia. Se deciden antes de F0-06 o del ADR de despliegue:

- **Regla 7, prefijos:** la lista no cubre `Microsoft.Azure.*`, `Microsoft.Extensions.Azure`, `Google.Apis.*` ni `WindowsAzure.*`. Agregarlos endurece la regla; cambia la sección 5.2.
- **Regla 7 y referencias evaluadas por MSBuild:** leer `obj/project.assets.json` de cada proyecto (con `System.Text.Json`, sin paquetes) para ver el cierre transitivo y las referencias ya evaluadas. Cierra los SDK de nube que llegan por paquetes de terceros (p. ej. sinks de Serilog) y lo que la lectura literal de los `.csproj` no ve.
- **Falsos positivos que vendrán:** analizadores o SourceLink con `PrivateAssets="all"` (como `GlobalPackageReference` o en Dominio, Contratos y Compartido) y pruebas de integración con emuladores de nube (Azurite) bajo la regla 7. Decidir y documentar las excepciones.
- **Aplicacion e `IServiceCollection`:** hoy nada impide que Aplicacion use `Microsoft.Extensions.DependencyInjection.Abstractions`; la sección 5.2 habla de una sola lista con `Microsoft.Extensions.` y el código tiene varias.
- **`Api_NoReferenciaInfraestructura` como lista blanca:** marca una referencia directa a `Nitido.Compartido` o al `Dominio`/`Contratos` del propio módulo. Decidir si se permite.
- **Reglas adicionales del tester (resuelto):** Documentado el 2026-10-18 en la sección 5 (5.2 y 5.5 de `docs/arquitectura-backend.md`). Pedido original: documentarlas en la sección 5.3 (producción no referencia pruebas, Compartido.Infraestructura/Api solo dependen de Compartido, un módulo exige `Infraestructura` y `Api`, revisión de `Directory.Packages.props` y `.targets`, Sdk Web cuenta como `FrameworkReference`).
- **Entorno local:** Control inteligente de aplicaciones de Windows bloqueó al azar DLL recién compiladas en copias temporales (`0x800711C7`). Si pasa en la carpeta real con los módulos de F0-02a, revisar esa configuración de Windows.
