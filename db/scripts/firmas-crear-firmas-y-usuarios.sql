IF OBJECT_ID(N'[__EFMigrationsHistory_Firmas]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory_Firmas] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory_Firmas] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261018224515_CrearFirmasYUsuarios'
)
BEGIN
    DECLARE @historyTableSchema nvarchar(max) = QUOTENAME(SCHEMA_NAME())
    EXEC(N'CREATE TABLE [T_FIRMAS] (
        [UUID_FIRMAS] uniqueidentifier NOT NULL,
        [NOMBRE] nvarchar(200) NOT NULL,
        [FECHA_CREACION] datetime2(3) NOT NULL,
        [FECHA_MODIFICACION] datetime2(3) NULL,
        [PERIODO_FIN] datetime2(3) GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
        [PERIODO_INICIO] datetime2(3) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
        [UUID_USUARIOS_CREACION] uniqueidentifier NOT NULL,
        [UUID_USUARIOS_MODIFICACION] uniqueidentifier NULL,
        CONSTRAINT [PK_T_FIRMAS] PRIMARY KEY ([UUID_FIRMAS]),
        PERIOD FOR SYSTEM_TIME([PERIODO_INICIO], [PERIODO_FIN])
    ) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = ' + @historyTableSchema + N'.[T_FIRMAS_HISTORIAL]))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261018224515_CrearFirmasYUsuarios'
)
BEGIN
    DECLARE @historyTableSchema1 nvarchar(max) = QUOTENAME(SCHEMA_NAME())
    EXEC(N'CREATE TABLE [T_USUARIOS] (
        [UUID_USUARIOS] uniqueidentifier NOT NULL,
        [UUID_FIRMAS] uniqueidentifier NOT NULL,
        [NOMBRE] nvarchar(200) NOT NULL,
        [CORREO] nvarchar(256) NOT NULL,
        [CORREO_NORMALIZADO] nvarchar(256) NOT NULL,
        [ROL] varchar(20) NOT NULL,
        [ACTIVO] bit NOT NULL,
        [FECHA_CREACION] datetime2(3) NOT NULL,
        [FECHA_MODIFICACION] datetime2(3) NULL,
        [PERIODO_FIN] datetime2(3) GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
        [PERIODO_INICIO] datetime2(3) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
        [UUID_USUARIOS_CREACION] uniqueidentifier NOT NULL,
        [UUID_USUARIOS_MODIFICACION] uniqueidentifier NULL,
        CONSTRAINT [PK_T_USUARIOS] PRIMARY KEY ([UUID_USUARIOS]),
        CONSTRAINT [CK_T_USUARIOS_ROL] CHECK (ROL IN (''ADMINISTRADOR'')),
        CONSTRAINT [FK_T_USUARIOS_T_FIRMAS] FOREIGN KEY ([UUID_FIRMAS]) REFERENCES [T_FIRMAS] ([UUID_FIRMAS]) ON DELETE NO ACTION,
        PERIOD FOR SYSTEM_TIME([PERIODO_INICIO], [PERIODO_FIN])
    ) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = ' + @historyTableSchema1 + N'.[T_USUARIOS_HISTORIAL]))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261018224515_CrearFirmasYUsuarios'
)
BEGIN
    CREATE INDEX [IX_T_USUARIOS_UUID_FIRMAS] ON [T_USUARIOS] ([UUID_FIRMAS]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261018224515_CrearFirmasYUsuarios'
)
BEGIN
    CREATE UNIQUE INDEX [UX_T_USUARIOS_CORREO_NORMALIZADO] ON [T_USUARIOS] ([CORREO_NORMALIZADO]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261018224515_CrearFirmasYUsuarios'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Firmas] ([MigrationId], [ProductVersion])
    VALUES (N'20261018224515_CrearFirmasYUsuarios', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025015830_AgregarSeguridadPorFilaDeFirmas'
)
BEGIN
    EXEC(N'ALTER TABLE [T_FIRMAS] ADD CONSTRAINT [CK_T_FIRMAS_UUID_FIRMAS] CHECK (UUID_FIRMAS <> ''00000000-0000-0000-0000-000000000000'')');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025015830_AgregarSeguridadPorFilaDeFirmas'
)
BEGIN
    CREATE SCHEMA seguridad AUTHORIZATION dbo;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025015830_AgregarSeguridadPorFilaDeFirmas'
)
BEGIN
    -- La fila es de la firma de la sesión.
    CREATE FUNCTION seguridad.FN_FIRMAS_ES_DE_LA_FIRMA (@UUID_FIRMAS UNIQUEIDENTIFIER)
    RETURNS TABLE
    WITH SCHEMABINDING
    AS
    RETURN
        SELECT 1 AS PERMITIDO
        WHERE @UUID_FIRMAS = CAST(SESSION_CONTEXT(N'UUID_FIRMAS') AS UNIQUEIDENTIFIER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025015830_AgregarSeguridadPorFilaDeFirmas'
)
BEGIN
    CREATE SECURITY POLICY seguridad.PS_FIRMAS
        ADD FILTER PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS,
        ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS AFTER INSERT,
        ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS AFTER UPDATE,
        ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS BEFORE UPDATE,
        ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS BEFORE DELETE,
        ADD FILTER PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS_HISTORIAL,
        ADD FILTER PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS,
        ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS AFTER INSERT,
        ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS AFTER UPDATE,
        ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS BEFORE UPDATE,
        ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS BEFORE DELETE,
        ADD FILTER PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS_HISTORIAL
    WITH (STATE = ON, SCHEMABINDING = ON);

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025015830_AgregarSeguridadPorFilaDeFirmas'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Firmas] ([MigrationId], [ProductVersion])
    VALUES (N'20261025015830_AgregarSeguridadPorFilaDeFirmas', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171352_AgregarCredencialesAUsuarios'
)
BEGIN
    ALTER TABLE [T_USUARIOS] ADD [BLOQUEO_HASTA] datetime2(3) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171352_AgregarCredencialesAUsuarios'
)
BEGIN
    ALTER TABLE [T_USUARIOS] ADD [CLAVE_HASH] varchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171352_AgregarCredencialesAUsuarios'
)
BEGIN
    ALTER TABLE [T_USUARIOS] ADD [CODIGOS_RECUPERACION_HASH] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171352_AgregarCredencialesAUsuarios'
)
BEGIN
    ALTER TABLE [T_USUARIOS] ADD [INTENTOS_FALLIDOS] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171352_AgregarCredencialesAUsuarios'
)
BEGIN
    ALTER TABLE [T_USUARIOS] ADD [SECRETO_TOTP] varchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171352_AgregarCredencialesAUsuarios'
)
BEGIN
    ALTER TABLE [T_USUARIOS] ADD [SEGUNDO_FACTOR_ACTIVO] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171352_AgregarCredencialesAUsuarios'
)
BEGIN
    ALTER TABLE [T_USUARIOS] ADD [SELLO_SEGURIDAD] uniqueidentifier NOT NULL DEFAULT (NEWID());
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171352_AgregarCredencialesAUsuarios'
)
BEGIN
    ALTER TABLE [T_USUARIOS] ADD [TOTP_ULTIMO_PASO] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171352_AgregarCredencialesAUsuarios'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Firmas] ([MigrationId], [ProductVersion])
    VALUES (N'20261025171352_AgregarCredencialesAUsuarios', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171406_AgregarIngresoPorCorreoEnSeguridadPorFila'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171406_AgregarIngresoPorCorreoEnSeguridadPorFila'
)
BEGIN
    ALTER SECURITY POLICY seguridad.PS_FIRMAS
        ALTER FILTER PREDICATE seguridad.FN_USUARIOS_ES_VISIBLE(UUID_FIRMAS, CORREO_NORMALIZADO) ON dbo.T_USUARIOS;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Firmas]
    WHERE [MigrationId] = N'20261025171406_AgregarIngresoPorCorreoEnSeguridadPorFila'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Firmas] ([MigrationId], [ProductVersion])
    VALUES (N'20261025171406_AgregarIngresoPorCorreoEnSeguridadPorFila', N'10.0.13');
END;

COMMIT;
GO

