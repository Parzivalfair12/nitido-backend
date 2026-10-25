# Fase 0 · Fundaciones: índice de historias

17 de octubre de 2026 · arquitecto

## Estructura de repositorios

Decisión del 18 de octubre de 2026: el proyecto pasó de un monorepo a **dos repositorios independientes**. Las especificaciones de esta carpeta se escribieron para el monorepo; sus rutas se corrigieron según el mapa de abajo y las historias que cruzan los dos repositorios llevan una fila «Repositorio» en su cabecera.

**Motivo:** cada parte tiene su propia pila (.NET y SQL Server frente a Node y Angular), su propio ciclo de pruebas y su propia integración continua, y el contrato entre ambas queda explícito. El contrato es OpenAPI: el backend publica `/openapi/v1.json` y el frontend genera su cliente. Un cambio que toca la API y una pantalla son dos pull requests coordinados, con el del backend primero.

| Ruta en el monorepo | Ahora |
| --- | --- |
| `backend/Nitido.Api`, `backend/Nitido.Compartido`, `backend/Modulos/...` | `Nitido.Api`, `Nitido.Compartido`, `Modulos/...` en la raíz de `nitido-backend` |
| `frontend/...` | Raíz de `nitido-frontend` (`src/...`, `public/...`) |
| `e2e/...` | `e2e/` en la raíz de `nitido-frontend` |
| `.nvmrc` | Raíz de `nitido-frontend` |
| `docs/mockup-nitido.dc.html`, `docs/logo-nitido.svg` | `docs/` de `nitido-frontend` |
| `tests/`, `db/`, `docs/specs`, `docs/adr`, `docs/plan-de-desarrollo.md`, `CLAUDE.md` | Sin cambio, en `nitido-backend` |
| `.claude/` | Carpeta raíz `nitido/`, común a los dos repositorios |

| Historia | Repositorio |
| --- | --- |
| F0-01 | Ambos (la solución de .NET en el backend; Angular, `e2e/` y `.nvmrc` en el frontend) |
| F0-01b, F0-02a, F0-02b, F0-03a, F0-03b | Backend |
| F0-04 | Backend (publica el contrato que consume F0-05c) |
| F0-05a, F0-05b, F0-05c | Frontend (F0-05b y F0-05c consumen la API del backend) |
| F0-06 | Ambos: dos workflows, uno por repositorio |

Agentes y skills comunes en `.claude/` de la carpeta raíz (decisión del 18 de octubre de 2026).

## Alcance de la fase 0

La fase 0 del plan (`docs/plan-de-desarrollo.md`, «Fases y entregables») tiene como criterio de salida: **ingresar, crear una firma y un cliente**, con el estilo Cristal. Aquí está partida en 11 historias de 1 o 2 días. F0-01b se agregó el 18 de octubre de 2026 con el ADR 0001.

- La fase 0 **no tiene reglas contables ni tributarias**. Ninguna historia necesita al agente `contador`, salvo la consulta sobre la validación del NIT (pregunta abierta 2).
- **No hay despliegue en la fase 0.** El proveedor de nube no está decidido. Los pasos 9 (staging) y 10 (producción) del ciclo de `nueva-funcionalidad` no aplican; cada historia termina en el paso 8, con la integración continua en verde.
- Las historias **no están creadas en GitHub**: el repositorio aún no existe y `gh` no está instalado. Mientras tanto, estos archivos reemplazan el paso 1 del ciclo (`gh issue view`).

## Decisiones tomadas

| Tema | Decisión | Historia |
| --- | --- | --- |
| Arquitectura del backend | Clean Architecture **dentro de cada módulo**, con un `.csproj` por capa (`Dominio`, `Aplicacion`, `Infraestructura`, `Api` y `Contratos` solo si otro módulo lo usa). Núcleo compartido mínimo y regla de dependencias verificada por pruebas de arquitectura. Sin MediatR ni AutoMapper; errores de negocio con `Resultado`. [ADR 0001](../adr/0001-clean-architecture.md), aceptado el 2026-10-18; detalle en `docs/arquitectura-backend.md`. | 1b; impacta 2a, 2b, 3a, 3b, 4 |
| Roles | Un solo rol, **Administrador**. Los roles de contador titular, auxiliar y solo lectura con acceso por cliente (plan, «Entornos y seguridad») quedan para una historia posterior. | 2a, 4 |
| Alta de firma | `POST /api/firmas/registro`, activo solo si `Registro:Habilitado=true`: `true` en Development y `false` por defecto. No hay pantalla de registro; el ingreso muestra «Solicita acceso» como texto, igual que el mockup. | 4, 5b |
| Cliente en la base | El cliente de la firma es `T_EMPRESAS` / `UUID_EMPRESAS`, como pide `convenciones-bd`. En la API y en las pantallas se llama «cliente». | 2a |
| Documento del cliente | Solo NIT (código DIAN 31). nítido calcula el dígito de verificación. La etiqueta del mockup, «NIT o cédula», se conserva. | 4, 5c |
| Base de datos | Un `DbContext` por módulo, cada uno con su propia tabla de historial de migraciones: una migración por módulo afectado en cada historia. [ADR 0002](../adr/0002-dbcontext-y-migraciones-por-modulo.md), aceptado el 2026-10-18. | 2a |
| Auditoría con tablas temporales | Decidido el 2026-10-18: `T_FIRMAS`, `T_USUARIOS`, `T_EMPRESAS` y `T_USUARIOS_EMPRESAS` son tablas temporales, con historial `T_<NOMBRE>_HISTORIAL`. Los periodos `PERIODO_INICIO` y `PERIODO_FIN` (`DATETIME2(3)`, UTC) quedan **ocultos** (`HIDDEN`), como los crea EF Core 10 cuando son propiedades sombra: `SELECT *` no los devuelve y se piden por nombre. Aceptado por el usuario el 2026-10-18; pasarán a visibles cuando EF 11 permita mapearlos. | 2a; impacta 2b (predicados en las tablas de historial) |
| Multiempresa | Dos barreras: filtro global de EF Core y seguridad por fila en SQL Server. El cliente activo viaja en la ruta, y `SESSION_CONTEXT` lo fija un interceptor de conexión. Requiere ADR. | 2b |
| Autenticación | ASP.NET Core Identity, con cookie `HttpOnly` en el mismo origen y antifalsificación. Doble factor TOTP obligatorio; códigos de recuperación guardados con hash; secreto TOTP cifrado con Data Protection. Requiere ADR. | 3a, 3b |
| Valores de seguridad | Bloqueo tras 5 intentos, por 15 minutos. Contraseña de mínimo 12 caracteres. Sesión vence tras 30 minutos de inactividad. Máximo 10 peticiones por minuto y por IP a `/api/autenticacion/*`. Todo va en configuración. No hay reinicio del doble factor por un administrador. | 3a, 3b |
| Pantalla de ingreso | Sin «¿La olvidaste?» ni «Mantener la sesión iniciada». Sí van los textos de términos de uso y de política de datos, y «Solicita acceso» como texto. | 5b |
| Pantalla de Clientes | Columnas Cliente, Pendientes, «Contabilizado en <mes>» y Estado. Pendientes y Contabilizado muestran «—». Botón «Agregar cliente» con formulario en línea. | 5c |
| Integración continua | Va justo después de 2a. Corre compilación y pruebas en cada pull request. Playwright en CI, CodeQL y Dependabot quedan para después de la fase 0. | 6 |
| Herramientas | Pruebas del frontend con lo que traiga la plantilla de Angular 22, como pide el plan (se espera Vitest). SQL Server 2025 en local y en Testcontainers. Node 24. | 1, 2a |
| Duración | Unas 4 semanas (19 días de trabajo), no las 2 del plan. El plan subestimó el doble factor y la seguridad por fila. | todas |

### ADR pendientes

Se escriben en el paso 3 de la historia que los necesita, en `docs/adr/`. El número 0001 ya lo tomó «Clean Architecture dentro de cada módulo» (F0-01b); los de esta lista toman 0002 en adelante, en el orden en que se escriban.

1. DbContext y migraciones por módulo (2a). Escrito: [ADR 0002](../adr/0002-dbcontext-y-migraciones-por-modulo.md), estado «Aceptado» (2026-10-18).
2. Multiempresa en dos barreras: contexto por petición, filtro de EF y seguridad por fila (2b).
3. Autenticación: Identity, cookie de sesión y doble factor obligatorio (3a; cubre también 3b).

## Orden de construcción

Se conservan los identificadores originales para no perder trazabilidad. Los encabezados «Orden de construcción N de 10» de cada especificación no se renumeraron al agregar F0-01b: manda este índice.

1. `F0-01` Esqueleto del repositorio (terminada)
2. `F0-01b` Base de Clean Architecture
3. `F0-02a` Conexión y tablas (terminada el 2026-10-18; revisión con 1 hallazgo medio (M1) corregido)
4. `F0-06` Integración continua (etapa 1 terminada el 2026-10-18: workflows escritos y verificados en local, revisados sin hallazgos graves ni medios; la etapa 2 —PR de prueba en rojo y en verde y protección de `main`— espera el repositorio en GitHub; pendientes del usuario: A3 y A4)
5. `F0-02b` Multiempresa y seguridad por fila
6. `F0-03a` Ingreso con contraseña
7. `F0-03b` Doble factor
8. `F0-04` Firmas y clientes
9. `F0-05a` Cristal: tokens y componentes. Puede ir en paralelo desde el día 3, apenas termine `F0-01`.
10. `F0-05b` Pantalla de ingreso
11. `F0-05c` Pantalla de Clientes

## Resumen

| Orden | Id | Archivo | Título | Días | Depende de |
| --- | --- | --- | --- | --- | --- |
| 1 | F0-01 | `f0-01-esqueleto-del-repositorio.md` | Esqueleto del repositorio | 2 | ninguna (prerrequisitos: SDK de .NET 10 y git local) |
| 2 | F0-01b | `f0-01b-clean-architecture.md` | Base de Clean Architecture: `Resultado` y pruebas de arquitectura | 1 | F0-01; ADR 0001 aceptado |
| 3 | F0-02a | `f0-02a-conexion-y-tablas.md` | Conexión a SQL Server local y tablas de firmas, usuarios y clientes | 2 | F0-01b |
| 4 | F0-06 | `f0-06-integracion-continua.md` | Integración continua en GitHub Actions | 1 | F0-02a; repositorio en GitHub |
| 5 | F0-02b | `f0-02b-multiempresa-y-seguridad-por-fila.md` | Multiempresa: filtro global y seguridad por fila | 2 | F0-02a |
| 6 | F0-03a | `f0-03a-ingreso-con-contrasena.md` | Ingreso con correo y contraseña | 2 | F0-02b |
| 7 | F0-03b | `f0-03b-doble-factor.md` | Doble factor con aplicación autenticadora | 2 | F0-03a |
| 8 | F0-04 | `f0-04-firmas-y-clientes.md` | Registrar firma, crear y listar clientes | 2 | F0-03b |
| 9 | F0-05a | `f0-05a-cristal-tokens-y-componentes.md` | Cristal: tokens, componentes, menú y área de trabajo | 2 | F0-01 (en paralelo) |
| 10 | F0-05b | `f0-05b-pantalla-de-ingreso.md` | Pantalla de ingreso con doble factor | 2 | F0-05a, F0-03b |
| 11 | F0-05c | `f0-05c-pantalla-de-clientes.md` | Pantalla de Clientes | 2 | F0-05a, F0-04 |

**Total:** 20 días de trabajo. La ruta crítica es F0-01 → F0-01b → F0-02a → F0-06 → F0-02b → F0-03a → F0-03b → F0-04 → F0-05c, unos 16 días. F0-05a y F0-05b se acomodan en paralelo.

## Impacto del ADR 0001 en las historias

El detalle está en `docs/arquitectura-backend.md`, sección 6. Cada especificación afectada lleva al inicio una nota «Ver ADR 0001». El usuario aprobó el ADR y los siete puntos recomendados el 2026-10-18 («Sí, implementa lo que indica el arquitecto»); los conflictos de la tabla quedaron **decididos y aplicados** a las especificaciones ese mismo día.

| Historia | Ajuste de diseño (paso 3) | Conflicto con un criterio |
| --- | --- | --- |
| F0-01 | `Compartido_NoDependeDeSdkDeNube` se renombra a `SdkDeNube_SoloEnProyectosDeProveedor` en F0-01b, sin aflojar la regla. | Ninguno (historia terminada; el criterio queda como registro). |
| F0-02a | Firmas y Clientes con la plantilla de módulo. Se crea `Nitido.Compartido.Infraestructura`. Dueño de cada tabla: Firmas tiene `T_FIRMAS` y `T_USUARIOS`; Clientes tiene `T_EMPRESAS` y `T_USUARIOS_EMPRESAS`. Migraciones en orden: Firmas antes que Clientes. La clase de `T_USUARIOS` vive en `Firmas.Infraestructura`. Prueba nueva propuesta: `Tablas_PertenecenAUnSoloDbContext`. | Resuelto (2026-10-18): 1) `Dominio_NoReferenciaEntityFramework` → `Dominio_SoloDependeDeCompartido`. 2) `IUsuarioActual` → una sola `IContextoSesion`, con solo el usuario en F0-02a. |
| F0-02b | Las interfaces van en `Nitido.Compartido/Multiempresa/`; el filtro y `SESSION_CONTEXT`, en `Nitido.Compartido.Infraestructura`. Se crea `Nitido.Compartido.Api` con el filtro del cliente activo. | Ninguno. |
| F0-03a | Identity en `Firmas.Infraestructura`. Los casos de uso, detrás de un puerto en `Firmas.Aplicacion`. Cookie, antifalsificación y limitador en el host. | Resuelto (2026-10-18): el `401` usa ProblemDetails con el texto en `title`, en vez del cuerpo antiguo con un campo `mensaje`. F0-03b y F0-05b/5c se alinearon. |
| F0-03b | Almacén de códigos y marca TOTP en `Firmas.Infraestructura`; Data Protection en el host. | Ninguno. |
| F0-04 | Casos de uso en Aplicacion, consultas con proyección, `Nit` en `Clientes.Dominio`, errores con `Resultado`, transacción explícita en el registro. `Firmas.Contratos` solo si hace falta. | Resuelto (2026-10-18): `Clientes_NoReferenciaInfraestructuraDeFirmas` → `Modulos_SoloSeReferencianPorContratos`. |
| F0-06 | Sin cambios en los criterios. Las pruebas de arquitectura corren desde la raíz del checkout; hay que vigilar la meta de 15 minutos. | Ninguno. |
| F0-05a, F0-05b, F0-05c | Sin impacto. | Ninguno. |

## Preguntas que siguen abiertas

1. **Git local para F0-01.** El usuario pidió no subir nada a git todavía. ¿Se permite `git init` local, sin remoto, en cada uno de los dos repositorios? Sin un repositorio local no se puede verificar el `.gitignore` ni trabajar con ramas y PR. F0-06 además necesita los dos repositorios en GitHub: queda bloqueada hasta que existan.
2. **Validación del NIT del cliente (antes de F0-04).** El mockup trata como NIT los números de 9 dígitos que empiezan por 8 o 9, y calcula el dígito de verificación con los pesos 3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71 en módulo 11. El `contador` debe confirmar la fuente y la regla. No es un valor tributario, pero sí una regla de la DIAN.
   - Los NIT de ejemplo del mockup tienen dígitos de verificación de relleno: 900.481.226-3, por ejemplo, da 5 con ese cálculo. No sirven como datos de prueba. F0-04 usa NIT públicos: DIAN 800.197.268-4 y Bancolombia 890.903.938-8.
3. ~~**Tablas temporales en `T_USUARIOS` y `T_USUARIOS_EMPRESAS` (antes de F0-02a).**~~ **Decidida el 2026-10-18: sí.** El plan pide auditoría con tablas temporales y los cambios de usuarios y accesos son eventos de seguridad (OWASP ASVS nivel 2). El mismo día el usuario confirmó extenderlas a `T_FIRMAS` y `T_EMPRESAS`, aceptó los `Azure.*` transitivos de `Microsoft.Data.SqlClient` y aprobó los ajustes a la skill `convenciones-bd`, ya aplicados.
4. **«Salir» en celular (antes de F0-05a).** Por debajo de 760 px, el mockup oculta el bloque de usuario y, con él, «Salir». Recomendación: agregar «Salir» al final de la franja horizontal y anotar la diferencia. La alternativa es respetar el mockup y depender de que la sesión vence a los 30 minutos.
5. **Textos legales del ingreso.** Se copian los resúmenes de términos y de política de datos del mockup. Basta con confirmar que sirven para la fase 0; la revisión legal está en la fase 5 del plan.

## Ajustes sugeridos a archivos que el arquitecto no edita

Los decide el usuario o el coordinador.

Ya aplicados con la separación en dos repositorios (18 de octubre de 2026), salvo los dos últimos puntos de la lista:

- `CLAUDE.md`, en Comandos:
  - Playwright vive en `e2e/` con su propio `package.json`, así que el comando queda `cd e2e && npx playwright test`, desde la raíz del repositorio del frontend.
  - La referencia del mockup apunta a `docs/mockup-nitido.dc.html`, en el repositorio del frontend.
- `docs/plan-de-desarrollo.md`: decía `docs/mockup/`, pero el archivo está en `docs/mockup-nitido.dc.html` del repositorio del frontend.
- Skill `diseno-cristal`:
  - La plantilla actual de Angular sirve los archivos estáticos desde `public/`, no desde `src/assets/` (repositorio del frontend). El logo queda en `public/marca/`.
  - El mockup usa 0,55 de opacidad en la tarjeta de ingreso y 0,42 en el menú; la skill dice 0,5 para todo. Se sigue el mockup.
- Skill `convenciones-bd`: cambiar «una migración por historia» por «una migración por módulo afectado en cada historia». Aplicado el 2026-10-18, junto con el resto de la sección 9 de `docs/arquitectura-backend.md`.
- Skill `convenciones-bd`, aplicado el 2026-10-18 (aprobado por el usuario; propuesto en el diseño técnico de F0-02a): tablas temporales también en tablas de seguridad y maestras de la firma, nombres de periodos (`PERIODO_INICIO`, `PERIODO_FIN`, ocultos con `HIDDEN`; el texto propuesto decía «no ocultos» y se corrigió el mismo día por decisión del usuario), nombres de restricciones (`PK_`, `FK_`, `CK_`), exclusión de tablas de historial en las pruebas de esquema y llaves foráneas sin cascada. Texto exacto al final de `f0-02a-conexion-y-tablas.md`.

## Etiquetas

Todas las historias llevan `fase-0` e `historia`.

| Id | Etiquetas adicionales |
| --- | --- |
| F0-01 | `infraestructura`, `backend`, `frontend` |
| F0-01b | `infraestructura`, `backend`, `requiere-adr` |
| F0-02a | `base-de-datos`, `backend`, `modulo:firmas`, `modulo:clientes`, `requiere-adr` |
| F0-02b | `base-de-datos`, `multiempresa`, `seguridad`, `backend`, `requiere-adr` |
| F0-03a | `seguridad`, `backend`, `modulo:firmas`, `requiere-adr` |
| F0-03b | `seguridad`, `backend`, `modulo:firmas` |
| F0-04 | `backend`, `multiempresa`, `modulo:firmas`, `modulo:clientes` |
| F0-05a | `frontend`, `diseno` |
| F0-05b | `frontend`, `diseno`, `seguridad` |
| F0-05c | `frontend`, `diseno`, `modulo:clientes` |
| F0-06 | `ci`, `infraestructura` |
