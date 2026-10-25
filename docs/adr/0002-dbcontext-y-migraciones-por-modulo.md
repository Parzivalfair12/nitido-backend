# 0002. Un DbContext y un historial de migraciones por módulo, en una sola base

| | |
| --- | --- |
| Estado | Aceptado |
| Fecha | 2026-10-18 (aceptado por el usuario el mismo día) |

## Contexto

F0-02a crea las primeras tablas (`T_FIRMAS`, `T_USUARIOS`, `T_EMPRESAS`, `T_USUARIOS_EMPRESAS`), los primeros módulos con persistencia (Firmas y Clientes) y `Nitido.Compartido.Infraestructura`. Antes de escribir la primera migración hay que fijar cómo se reparten la base de datos y las migraciones entre los 14 módulos, porque cambiarlo después obliga a rehacer migraciones ya aplicadas.

Lo que ya está decidido y pesa sobre esta decisión:

- **ADR 0001:** Clean Architecture dentro de cada módulo. Un módulo no ve la `Infraestructura` de otro; solo sus `Contratos`.
- **`convenciones-bd`:** tablas `T_` y llaves `UUID_` en un solo espacio de nombres; columnas de auditoría como propiedades sombra; seguridad por fila con `SESSION_CONTEXT` (F0-02b) que consulta `T_USUARIOS_EMPRESAS` desde el predicado de todas las tablas de cliente.
- **Regla 3 (multiempresa)** y **regla 1 (todo asiento cuadra):** las dos se apoyan en restricciones de SQL Server (llaves foráneas, seguridad por fila, triggers). Una base partida las debilita.
- **Equipo:** un desarrollador con agentes. La regla tiene que verificarse sola.
- **Sin despliegue en la fase 0:** cómo se aplican las migraciones en un ambiente compartido se decide con el proveedor de nube.

## Opciones

1. **Un solo `DbContext` para toda la aplicación,** en un proyecto común.
   - A favor: una sola cadena de migraciones, sin problemas de orden; transacciones entre tablas de módulos distintos gratis.
   - En contra: el proyecto común tendría que referenciar las entidades de todos los módulos, lo que rompe la frontera del ADR 0001; cualquier módulo podría consultar las tablas de otro; los conflictos de la instantánea del modelo crecen con cada historia.
2. **Un `DbContext` por módulo, en la misma base y el mismo esquema (`dbo`), cada uno con su propia tabla de historial de migraciones.** Las llaves foráneas entre módulos se crean por nombre de tabla, a mano, en la migración del módulo que referencia.
   - A favor: cada módulo es dueño de sus tablas y de sus migraciones; la frontera del ADR 0001 llega hasta la base; las restricciones de SQL Server (llaves foráneas, seguridad por fila) siguen funcionando entre módulos.
   - En contra: hay un orden entre las migraciones de módulos (el que referencia va después); la instantánea de un módulo no conoce las llaves foráneas hacia otro; no hay transacción compartida entre contextos.
3. **Igual que la 2, pero con un esquema SQL por módulo** (`firmas.T_USUARIOS`, `clientes.T_EMPRESAS`).
   - A favor: la propiedad de cada tabla se ve en la base; permisos por esquema.
   - En contra: el prefijo `T_` y los nombres de `convenciones-bd` ya evitan choques; el predicado de seguridad por fila y los futuros reportes (fase 4) tendrían que calificar cada tabla; agrega una dimensión más a las pruebas de esquema sin resolver un problema que hoy exista. Se puede adoptar después con `ALTER SCHEMA ... TRANSFER`, sin perder datos.
4. **Una base por módulo.**
   - A favor: aislamiento total.
   - En contra: pierde las llaves foráneas y la seguridad por fila entre módulos (regla 3), obliga a consistencia eventual desde el día uno y multiplica cadenas de conexión y respaldos. Desproporcionado para un monolito modular.

## Decisión

Se adopta la **opción 2**:

- **Un `DbContext` por módulo con tablas,** dentro de `Nitido.<M>.Infraestructura`, que hereda de `DbContextNitido` (en `Nitido.Compartido.Infraestructura`). La clase base aplica las convenciones de nombres, las columnas de auditoría, el generador de llaves GUID v7 y el comportamiento de borrado `Restrict` a todas las llaves foráneas.
- **Historial propio:** `__EFMigrationsHistory_<Modulo>` (`__EFMigrationsHistory_Firmas`, `__EFMigrationsHistory_Clientes`). Las migraciones viven en `Modulos/<M>/Infraestructura/Migraciones/`.
- **Cada tabla tiene un solo dueño.** Ningún `DbContext` mapea una tabla, ni una tabla de historial, de otro módulo. Lo vigilan dos barreras:
  - el compilador y las pruebas de arquitectura, porque un módulo no ve las clases de persistencia de otro;
  - la prueba de integración `Tablas_PertenecenAUnSoloDbContext`, que construye el modelo de cada contexto sin conectarse y falla si dos contextos nombran la misma tabla.
- **Llaves foráneas entre módulos:** se permiten. Se escriben a mano en la migración del módulo que referencia (`migrationBuilder.AddForeignKey(...)` por nombre de tabla y columna, sin entidad ni navegación) y se quitan en su `Down`. Siempre `ON DELETE NO ACTION`.
- **Orden de las migraciones:** el módulo referenciado va antes que el que lo referencia. El orden vigente es `Firmas` → `Clientes`. Se mantiene en una sola lista, en el ayudante de las pruebas de integración, que es quien lo ejecuta (`Migraciones_SeAplicanSobreBaseLimpia`), y se copia en `db/README.md` para el comando manual en local. Una dependencia circular entre módulos está prohibida: si aparece, la llave foránea se elimina y la relación se valida en la aplicación.
- **La API no aplica migraciones al arrancar.** En local se aplican con `dotnet ef database update --context <M>DbContext`, en el orden de la lista. En un ambiente compartido se decide con el ADR de despliegue (paquete de migraciones o script idempotente por contexto, en el mismo orden).
- **Sin transacción entre contextos** (`docs/arquitectura-backend.md`, 3.5). Una operación de escritura toca un solo `DbContext`. Por eso `T_USUARIOS_EMPRESAS` es de Clientes y no de Firmas: crear un cliente y dar acceso a quien lo crea es un solo `SaveChanges` (F0-04).

## Consecuencias

**Lo bueno:**
- Cada módulo agrega y revisa sus migraciones sin tocar las de otro; los conflictos de la instantánea del modelo quedan dentro de un módulo.
- Las restricciones de SQL Server siguen protegiendo los datos entre módulos: llaves foráneas y, desde F0-02b, la seguridad por fila.
- Separar un módulo en el futuro es mover sus carpetas y su historial.

**Lo que cuesta:**
- Un orden entre módulos que hay que mantener y probar sobre una base limpia en cada pull request.
- Las llaves foráneas entre módulos no están en la instantánea del modelo: quien cambia la tabla referenciada tiene que buscarlas en las migraciones de los otros módulos. Eliminar o renombrar una tabla con llaves foráneas entrantes sigue la regla de cambios destructivos en dos versiones de `convenciones-bd`.
- Varias tablas de historial de migraciones en la misma base.

**Pendiente:**
- Cómo se aplican las migraciones en un ambiente compartido y con qué usuario de base de datos (ADR de despliegue; F0-02b ya pide un usuario de aplicación distinto del de migraciones).
- Si un caso futuro exige escribir en dos contextos de forma atómica, se decide en un ADR (conexión y transacción compartidas), como dice 3.5.
- La opción 3 (esquema por módulo) puede revisarse si los permisos por esquema llegan a hacer falta.
