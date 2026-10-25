# 0001. Clean Architecture dentro de cada módulo del monolito

| | |
| --- | --- |
| Estado | Aceptado |
| Fecha | 2026-10-18 (aceptado por el usuario el 2026-10-18) |

## Contexto

El usuario pidió aplicar Clean Architecture en el backend. Hoy solo existen el host `Nitido.Api` (`/salud`, Serilog, ProblemDetails y OpenAPI) y `Nitido.Compartido`, que está vacío. Todavía no hay módulos ni EF Core. F0-01 está terminada, con 23 pruebas en verde.

Lo que ya está decidido y pesa sobre esta decisión:

- **Monolito modular** con 14 módulos previstos (plan, «Arquitectura»). Cada módulo tiene las capas Dominio, Aplicacion, Infraestructura y Api, y solo habla con otro por su interfaz pública o por eventos internos.
- **Un `DbContext` por módulo**, con su propio historial de migraciones (F0-02a).
- **Multiempresa en dos barreras:** filtro global de EF y seguridad por fila con `SESSION_CONTEXT` (F0-02b, regla 3).
- **Regla 7:** nada de SDK de una nube en el dominio ni en los casos de uso. Los puertos son propios (`IAlmacenArchivos`, `ISecretos`).
- **El equipo** es un desarrollador con 8 agentes. Las fronteras tienen que hacerse cumplir solas, sin depender de que un agente se acuerde de ellas.
- **F0-02a** ya pide «los proyectos Dominio, Aplicacion, Infraestructura y Api» por módulo y la prueba `Dominio_NoReferenciaEntityFramework`. **F0-04** pide `Clientes_NoReferenciaInfraestructuraDeFirmas`.

«Clean Architecture» puede entenderse de varias formas en un monolito modular. Hay que escoger una antes de crear Firmas y Clientes en F0-02a, porque después cambiarla cuesta una migración de código en cada módulo.

## Opciones

1. **Capas globales.** Cuatro proyectos para todo el sistema (`Nitido.Dominio`, `Nitido.Aplicacion`, `Nitido.Infraestructura`, `Nitido.Api`), con los módulos como carpetas dentro de cada capa.
   - A favor: solo 4 proyectos.
   - En contra:
     - Rompe la frontera entre módulos: todo el dominio comparte un ensamblado, así que `Clientes` puede usar cualquier clase de `Firmas` y nada lo impide.
     - Un solo `DbContext` o varios en el mismo proyecto vuelve confuso «un `DbContext` por módulo».
     - Contradice el plan, `CLAUDE.md` y F0-02a.
     - Separar un módulo más adelante obliga a desarmar las cuatro capas.
2. **Clean Architecture dentro de cada módulo, con un proyecto `.csproj` por capa.** Cada módulo tiene `Nitido.<Modulo>.Dominio`, `.Aplicacion`, `.Infraestructura` y `.Api`. Cuando otro módulo lo necesita, agrega `.Contratos`. Un núcleo compartido mínimo, `Nitido.Compartido`, solo depende de la librería base de .NET.
   - A favor:
     - El compilador hace cumplir la regla de dependencias: un proyecto no puede usar lo que no referencia.
     - Las fronteras entre módulos y entre capas se verifican leyendo los `.csproj`, sin librerías nuevas.
     - Es lo que ya dicen el plan, `CLAUDE.md` y F0-02a.
   - En contra:
     - Entre 4 y 5 proyectos por módulo: unos 60 al final de la fase 4.
     - Más archivos `.csproj` y un poco más de tiempo de compilación.
3. **Un proyecto por módulo, con las capas como carpetas y espacios de nombres.** Son 14 proyectos, y las reglas se verifican con pruebas de arquitectura sobre los espacios de nombres.
   - A favor: pocos proyectos y menos ceremonia.
   - En contra:
     - Si el proyecto del módulo referencia EF Core, el compilador deja que el dominio lo use.
     - Detectarlo exige inspeccionar los tipos y el cuerpo de los métodos, y para eso hace falta una librería (NetArchTest no publica versión desde 2021; ArchUnitNET es más pesada) o leer IL a mano.
     - La regla 7 y «el dominio no depende de EF» quedarían protegidas por una prueba débil.
4. **Variante de la opción 2 con 3 proyectos por módulo**, uniendo Dominio y Aplicacion en un `Nucleo`.
   - A favor: un proyecto menos por módulo.
   - En contra:
     - La frontera «el dominio no conoce FluentValidation, los puertos ni los registros» queda solo como convención.
     - Contradice el texto de F0-02a.
     - Ahorra poco: un `.csproj` de tres líneas por módulo.

## Decisión

Se adopta la **opción 2**, con estas condiciones para que no se vuelva sobreingeniería:

- **Proyectos por módulo:**
  - Son cuatro fijos: `Dominio`, `Aplicacion`, `Infraestructura` y `Api`.
  - `Contratos` se crea solo cuando otro módulo lo necesita de verdad.
  - Un módulo sin reglas propias (por ejemplo Reportes) puede no tener `Dominio`, si su especificación lo justifica.
- **Regla de dependencias:**
  - `Dominio` → `Nitido.Compartido`.
  - `Aplicacion` → `Dominio` y los `Contratos` de otros módulos.
  - `Infraestructura` y `Api` → `Aplicacion`.
  - El host `Nitido.Api` es la raíz de composición y el único que referencia `Infraestructura` y `Api` de los módulos.
- **Núcleo compartido en tres piezas,** porque el dominio no puede ver EF ni ASP.NET y los 14 módulos no deben duplicar lo común:
  - `Nitido.Compartido`: solo la librería base de .NET; no tiene reglas de negocio.
  - `Nitido.Compartido.Infraestructura`: EF Core común. Se crea en F0-02a.
  - `Nitido.Compartido.Api`: lo común de los endpoints. Se crea con el primer filtro de endpoint, en F0-02b.
- **Sin librerías nuevas:**
  - Casos de uso propios en lugar de MediatR, que tiene licencia comercial (o RPL-1.5) desde la versión 13.
  - Un tipo `Resultado` propio para los errores de negocio, sin ErrorOr ni FluentResults.
  - Mapeos escritos a mano, sin AutoMapper, que también pasó a licencia comercial.
  - Pruebas de arquitectura con reflexión y lectura de `.csproj`, sin NetArchTest ni ArchUnitNET.
- **CQRS ligero:**
  - Los comandos pasan por repositorios por agregado y una unidad de trabajo del módulo.
  - Las consultas pasan por una interfaz de consultas de Aplicacion, implementada en Infraestructura con proyecciones directas de EF.
- **Cumplimiento automático:** un grupo de pruebas de arquitectura en `Nitido.Pruebas.Unidad` verifica todo lo anterior en cada `dotnet test`.

El detalle está en `docs/arquitectura-backend.md`. La base se construye en la historia `F0-01b` (`docs/specs/f0-01b-clean-architecture.md`), antes de F0-02a.

**Por qué esta opción y no la 3:** con un desarrollador y agentes, lo que más protege el proyecto es que el error no compile. Cuesta entre 40 y 60 archivos `.csproj` de pocas líneas, hechos desde una plantilla. A cambio, la regla 7 y la frontera entre módulos quedan verificadas por el compilador. Una prueba que alguien tiene que mantener al día protege menos.

## Consecuencias

**Lo bueno:**
- `Dominio` y `Aplicacion` se prueban sin base de datos ni HTTP. El plan pide 90 % de cobertura en el dominio.
- Un módulo no puede tocar el `DbContext`, las tablas ni las clases internas de otro.
- Cambiar de proveedor de nube toca solo proyectos de infraestructura.
- Separar un módulo más adelante es mover sus cinco carpetas.

**Lo que cuesta:**
- Entre 4 y 5 proyectos por módulo.
- Tres representaciones de un mismo dato en algunos casos: entidad, DTO del caso de uso y respuesta HTTP, con mapeo a mano.
- La compilación completa de la solución crecerá; se mide en CI (F0-06).

**Lo que cambia en las especificaciones existentes:** se lista en `docs/arquitectura-backend.md`, sección 6.
- Dos pruebas con nombre propio quedan cubiertas por pruebas genéricas: `Dominio_NoReferenciaEntityFramework` (F0-02a) y `Clientes_NoReferenciaInfraestructuraDeFirmas` (F0-04). Los criterios de F0-02a y F0-04 se cambiaron el 2026-10-18 para pedir `Dominio_SoloDependeDeCompartido` y `Modulos_SoloSeReferencianPorContratos`.
- Una sola interfaz `IContextoSesion` reemplaza a `IUsuarioActual` (F0-02a) más `IContextoSesion` (F0-02b).
- `Compartido_NoDependeDeSdkDeNube` (F0-01) se renombra y se amplía, sin aflojar la regla.

**Pendiente, con su propio ADR cuando llegue la necesidad:**
- Eventos internos entre módulos y su bandeja de salida (fase 2, «documento leído»).
- Ubicación de las implementaciones por proveedor de `IAlmacenArchivos` e `ISecretos` (al decidir el despliegue).
- Lectura de varios módulos para reportes pesados (fase 4).
- El ADR de autenticación (F0-03a) decide la clase de usuario de Identity dentro de `Firmas.Infraestructura`.
