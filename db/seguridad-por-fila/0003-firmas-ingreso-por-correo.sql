-- F0-03a · Ingreso por correo (ADR 0003, «Ingreso»).
-- El ingreso busca al usuario por su correo antes de saber su firma. Con la firma de la sesión en Guid.Empty y la llave
-- de solo lectura CORREO_INGRESO (que solo fija el alcance de ingreso del servidor, ver ContextoDeAlcance), el
-- predicado FILTER de T_USUARIOS deja ver únicamente la fila cuyo CORREO_NORMALIZADO es ese correo. Con la firma de la
-- sesión conocida sigue valiendo lo de siempre: solo las filas de esa firma. Los predicados BLOCK no cambian: la fila
-- encontrada así se puede leer, pero no escribir (los contadores se escriben en un alcance con la firma ya conocida).
CREATE FUNCTION seguridad.FN_USUARIOS_ES_VISIBLE (@UUID_FIRMAS UNIQUEIDENTIFIER, @CORREO_NORMALIZADO NVARCHAR(256))
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS PERMITIDO
    WHERE @UUID_FIRMAS = CAST(SESSION_CONTEXT(N'UUID_FIRMAS') AS UNIQUEIDENTIFIER)
       OR (
            CAST(SESSION_CONTEXT(N'UUID_FIRMAS') AS UNIQUEIDENTIFIER) = '00000000-0000-0000-0000-000000000000'
            AND CAST(SESSION_CONTEXT(N'UUID_USUARIOS') AS UNIQUEIDENTIFIER) = '00000000-0000-0000-0000-000000000000'
            AND LEN(CAST(SESSION_CONTEXT(N'CORREO_INGRESO') AS NVARCHAR(256))) > 0
            AND @CORREO_NORMALIZADO = CAST(SESSION_CONTEXT(N'CORREO_INGRESO') AS NVARCHAR(256))
          );
GO
ALTER SECURITY POLICY seguridad.PS_FIRMAS
    ALTER FILTER PREDICATE seguridad.FN_USUARIOS_ES_VISIBLE(UUID_FIRMAS, CORREO_NORMALIZADO) ON dbo.T_USUARIOS;
