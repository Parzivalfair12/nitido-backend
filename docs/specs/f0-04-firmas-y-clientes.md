# F0-04 · Fase 0 · 4 · Firmas y clientes: registrar una firma, crear un cliente y listar clientes

| | |
| --- | --- |
| Estimación | 2 días |
| Depende de | F0-03b |
| Orden de construcción | 7 de 10 |
| Repositorio | Backend (`nitido-backend`). Publica el contrato OpenAPI que consume la pantalla F0-05c en `nitido-frontend`; su pull request va primero. |
| Etiquetas | `fase-0`, `historia`, `backend`, `multiempresa`, `modulo:firmas`, `modulo:clientes` |
| Estado | Terminada el 2026-10-25 (implementada, con pruebas; sin pull request: todavía no hay repositorios en GitHub). Los casos de uso usan FluentValidation y el `Resultado` propio. La prueba de que el plan de la lista usa un índice que empieza por `UUID_FIRMAS` no se escribió: se verificó el tiempo (2.000 clientes en menos de 500 ms) y el índice único `UX_T_EMPRESAS_UUID_FIRMAS_TIPO_DOCUMENTO_NUMERO_DOCUMENTO` empieza por `UUID_FIRMAS`. |

> **Ver ADR 0001** y `docs/arquitectura-backend.md` (secciones 3 y 6.2). Los casos de uso van en Aplicacion y los errores de negocio salen con `Resultado`. El registro de firma necesita una transacción explícita. Decisión del usuario del 2026-10-18, ya aplicada: el criterio `Clientes_NoReferenciaInfraestructuraDeFirmas` pasó a `Modulos_SoloSeReferencianPorContratos`.

## Historia

Como dueño de una firma de contadores quiero registrar mi firma, crear mis clientes y consultarlos para empezar a organizar en nítido las empresas que llevo.

## Criterios de aceptación

### Registro de la firma

- [ ] `POST /api/firmas/registro` con `{nombreFirma, administrador: {nombre, correo, clave}}` responde `201 {uuidFirma}`. En una sola transacción crea la fila de `T_FIRMAS` y el usuario de `T_USUARIOS` con rol `ADMINISTRADOR`.
- [ ] El registro solo funciona con `Registro:Habilitado=true`. El valor es `true` en `appsettings.Development.json` y `false` en `appsettings.json`; con `false` el endpoint responde `404`. Lo comprueba la prueba `Registro_DeshabilitadoResponde404`.
- [ ] Un correo que ya existe responde `409` con «Ya existe una cuenta con ese correo.»
- [ ] Una clave de menos de 12 caracteres responde `400` en formato ProblemDetails, con «La contraseña debe tener al menos 12 caracteres.» en el campo `administrador.clave`.
- [ ] El administrador recién registrado ingresa y recibe `{"paso":"inscribir-segundo-factor"}` (F0-03b).

### Crear un cliente

- [ ] `POST /api/clientes` con `{razonSocial, numeroDocumento}`, los dos campos del formulario del mockup:
  - responde `201` con el encabezado `Location: /api/clientes/{uuid}` y el cuerpo `{uuid, tipoDocumento: "31", numeroDocumento, digitoVerificacion, razonSocial, activo}`;
  - quien lo crea queda con acceso en `T_USUARIOS_EMPRESAS`;
  - las columnas de auditoría quedan con su usuario.
- [ ] `numeroDocumento` acepta `800197268`, `800.197.268` y `800197268-4`. nítido guarda solo los dígitos y **calcula** el dígito de verificación.
- [ ] Si llega un dígito de verificación que no corresponde, responde `400` con «El dígito de verificación no corresponde a ese NIT.»
- [ ] La prueba de unidad `DigitoVerificacion_CalculaComoLaDian` comprueba con NIT públicos que 800197268 da 4 (DIAN) y 890903938 da 8 (Bancolombia). **No se usan los NIT del mockup como datos de prueba:** sus dígitos de verificación son de relleno y no cuadran con el cálculo.
- [ ] En la fase 0 solo se acepta NIT de persona jurídica: 9 dígitos que empiezan por 8 o 9. La regla está pendiente de confirmar con el `contador` (pregunta abierta 2 del índice).
  - Cualquier otro número responde `400` con «Por ahora solo puedes agregar clientes con NIT. Escribe los 9 dígitos, sin el dígito de verificación.»
- [ ] Las demás validaciones (FluentValidation) responden `400` en formato ProblemDetails:
  - razón social vacía: «Escribe la razón social o el nombre del cliente.»;
  - razón social de más de 200 caracteres: «La razón social puede tener hasta 200 caracteres.»;
  - documento vacío: «Escribe el NIT o la cédula del cliente.»
- [ ] Un NIT repetido en la misma firma responde `409` con «Este cliente ya está en tu firma.» La prueba `Clientes_MismoNitEnDosFirmasEsPermitido` comprueba que dos firmas sí pueden tener el mismo NIT.
- [ ] La firma del cliente se toma siempre de la sesión. La prueba `Clientes_IgnoraFirmaEnviadaEnElCuerpo` comprueba que un `uuidFirmas` ajeno enviado en el cuerpo no tiene efecto.

### Consultar clientes

- [ ] `GET /api/clientes` responde `200` con la lista de clientes a los que el usuario tiene acceso: `[{uuid, tipoDocumento, numeroDocumento, digitoVerificacion, razonSocial, activo}]`, ordenada por razón social.
  - En la fase 0 no hay paginación ni búsqueda en el servidor: la pantalla filtra en el navegador, igual que el mockup.
- [ ] Con 2.000 clientes sembrados en una firma, `GET /api/clientes` responde en menos de 500 ms contra el contenedor local. El plan de la consulta usa un índice que empieza por `UUID_FIRMAS`.
- [ ] `GET /api/clientes/{uuidCliente}` responde `200` para un cliente propio. Para uno de otra firma, o sin acceso, responde `404` (no `403`, para no revelar que existe). Lo comprueba la prueba `Clientes_UsuarioDeOtraFirmaRecibe404`.
- [ ] La prueba `Clientes_ListaSoloLosDeMiFirma` pasa, con dos firmas y clientes sembrados en cada una.

### Contrato y fronteras

- [ ] En Development, los cuatro endpoints aparecen en `/openapi/v1.json` con todos sus códigos de respuesta.
- [ ] La prueba `Modulos_SoloSeReferencianPorContratos` (F0-01b) pasa con Firmas y Clientes ya en la solución: Clientes no referencia ningún proyecto de Firmas que no sea `Nitido.Firmas.Contratos`, y ese proyecto solo existe si Clientes de verdad lo necesita. (Reemplaza a `Clientes_NoReferenciaInfraestructuraDeFirmas`; Ver ADR 0001, 2026-10-18.)

## Pantalla

No aplica. La pantalla de Clientes llega en F0-05c. No hay pantalla de registro de firma en la fase 0: el ingreso muestra «Solicita acceso» solo como texto, y el registro se usa desde `Nitido.Api.http` o desde las pruebas.

## Reglas contables y tributarias

No aplica. La fase 0 no tiene reglas contables.

- El dígito de verificación es una regla de cálculo de la DIAN, no un valor tributario. El `contador` debe confirmar su fuente.
- Los datos fiscales del cliente (régimen, responsabilidades del RUT, CIIU, calendario tributario) quedan para una historia con el `contador` en la fase 3 (perfil tributario).

## Diseño técnico

Se completa en el paso 3. Ya están decididos estos puntos, heredados de F0-02b ([ADR 0003](../adr/0003-multiempresa-en-dos-barreras.md)):

- **Registro de firma:** con `CrearAlcanceConContexto(this IServiceScopeFactory, Guid uuidUsuarios, Guid uuidFirmas, Guid? uuidEmpresas)` (en `Nitido.Compartido.Infraestructura`, se construye aquí, en su primer uso), con `UuidFirmas` igual a la firma nueva y `UuidUsuarios` igual al usuario «sistema» (`00000000-0000-0000-0000-000000000001`). Firma y administrador se guardan en una transacción. Sin puertas en el predicado.
- **Correo repetido:** el índice único global de correo se ve aunque la política oculte los usuarios de otras firmas; el repositorio traduce esa violación a `409`.
- **Lista de clientes:** `IConsultasClientes` filtra por `Accesos.Any(a => a.UuidUsuarios == sesion.UuidUsuarios)`, para que las dos barreras digan lo mismo (el filtro de EF de `Cliente` es solo por firma).
- **Rutas de detalle:** con `MapearRutasDelCliente` de `Nitido.Compartido.Api`; nunca se arman a mano.
- **Primer cliente:** `Cliente.Crear` y su acceso se guardan en un solo `SaveChanges`; los AFTER INSERT de `T_EMPRESAS` (solo firma) y de `T_USUARIOS_EMPRESAS` (firma y usuario) lo permiten.

## Fuera de alcance

- Clientes con cédula u otro documento. El mockup dice «NIT o cédula»; en la fase 0 solo se acepta NIT.
- Editar, desactivar o eliminar clientes.
- Invitar usuarios y asignarles clientes.
- Roles de contador titular, auxiliar y solo lectura con acceso por cliente (plan, «Entornos y seguridad»): historia posterior.
- Paginación y búsqueda en el servidor. Se agregan cuando una firma pase del volumen probado.
- Pantalla de registro de firma.
- Importar clientes desde un archivo.
