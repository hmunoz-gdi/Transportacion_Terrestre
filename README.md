# Transportación Terrestre

Primera versión para ASP.NET Core 10 y PostgreSQL 17. Abre esta carpeta en VSCodium.

## Iniciar en Windows

1. En pgAdmin, crea una base vacía llamada `transportacion_terrestre`. Concede a la cuenta de la aplicación permisos para crear las tablas en esa base.
2. Desde la terminal PowerShell de VSCodium configura la conexión y el administrador inicial. Cambia los valores entre `< >`.

```powershell
dotnet --list-sdks
dotnet user-secrets set "ConnectionStrings:Transportacion" "Host=localhost;Port=5432;Database=transportacion_terrestre;Username=<usuario>;Password=<contraseña>"
dotnet user-secrets set "Documentos:Raiz" "C:\DatosTransportacion\PDF"
dotnet user-secrets set "Inicial:Usuario" "admin.transportacion"
dotnet user-secrets set "Inicial:Contrasena" "<contraseña nueva de al menos 12 caracteres>"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --urls http://localhost:5080
```

3. Abre <http://localhost:5080> e inicia sesión. Después de crear el administrador, puedes retirar sus valores iniciales:

```powershell
dotnet user-secrets remove "Inicial:Contrasena"
dotnet user-secrets remove "Inicial:Usuario"
```

En servidor configura HTTPS y proporciona `ConnectionStrings__Transportacion` y `Documentos__Raiz` mediante la configuración del servicio. Usa una ruta absoluta privada fuera de `wwwroot`, local o UNC, con acceso para la cuenta que ejecuta ASP.NET Core. Respalda la base y esa carpeta de forma coordinada. Una carga con fallo después de escribir el archivo puede dejar un archivo huérfano, que debe reconciliarse con las claves registradas antes de limpiar el repositorio.

## Crear la base y el esquema desde pgAdmin

1. Abre **Query Tool** en la base de mantenimiento `postgres` con una cuenta que pueda crear bases. Abre y ejecuta `database/01-crear-base.sql` con autocommit habilitado. Si `transportacion_terrestre` ya existe, omite este paso.
2. Actualiza el árbol de bases y abre **otro Query Tool** en `transportacion_terrestre`. En una instalación nueva, abre y ejecuta `database/02-crear-esquema.sql`. Usa la cuenta de la aplicación para que sea propietaria de las tablas o concede a esa cuenta los permisos correspondientes.
3. Completa la configuración de conexión y del administrador inicial descrita arriba. La instalación registra la migración de Entity Framework, y el primer arranque crea los perfiles y la cuenta inicial de la aplicación.

El segundo script se genera desde las migraciones del proyecto para PostgreSQL. La carpeta de PDF se configura por separado al arrancar la aplicación.

## Recuperar el acceso de administrador

Desde la carpeta del proyecto, con la misma configuración de conexión del arranque, ejecuta en una terminal interactiva:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run -- restablecer-admin admin.transportacion
```

Escribe y confirma la contraseña nueva. La terminal oculta lo que tecleas. El comando requiere acceso a la base, opera sobre una cuenta con perfil Administrador, desbloquea la cuenta y deja un registro en la bitácora.

## Primera versión

- Permisionarios con búsqueda, alta, edición, baja lógica, datos de contacto y domicilio. Las rutas de edición usan el ID de la ficha.
- Taxistas y unidades con registro básico desde una ficha.
- Siete tipos de documentos del formulario SCAN. Vista previa de carga y apertura de versiones históricas. La versión con numeración más alta es la actual. En PostgreSQL se guardan metadatos. Los PDF se escriben en subcarpetas por ficha, con una clave distinta por versión.
- Usuarios independientes. Consulta puede leer fichas y PDF. Captura puede modificarlos. Administración puede configurar cuentas y consultar la bitácora. Tres intentos fallidos bloquean hasta desbloqueo administrativo. Los cambios de credencial o perfil invalidan sesiones.
- `domicilios.json` incluye una muestra. Sustituye sus filas por el catálogo autorizado de estado, municipio o alcaldía, colonia y código postal. Cada código postal asociado a una colonia ocupa una fila. Reinicia la aplicación para recargar el catálogo.
- La base comienza vacía. La importación de SQL Server y los PDF históricos, las cesiones, sustituciones, captura de fotografía y carátula quedan para sus módulos respectivos.

## Definir perfiles

En **Usuarios → Definir perfiles**, selecciona **Nuevo perfil**, escribe su nombre y marca las operaciones en el árbol. Luego vuelve a **Usuarios** y asigna el perfil al crear una cuenta o al cambiar una existente.

Los permisos de cambio incluyen la consulta del módulo. Los módulos de la ficha requieren consultar Permisionarios. Los perfiles del sistema mantienen sus permisos y los perfiles personalizados se guardan en las tablas de Identity. Al guardar cambios en un perfil se invalidan las sesiones de las cuentas asignadas.

## Agregar módulos

Cada módulo declara sus rutas en `Modules/` con un método `Mapear`, registrado desde `Program.cs`. Registra los permisos del módulo en `Modules/Perfiles.cs` y usa esas políticas o `Admin` en las rutas correspondientes y `Acceso.Registrar` para las operaciones que deban quedar en bitácora. `RegistroDb.cs` contiene el modelo. La migración inicial está en `Migraciones/`. Para ampliar el esquema, agrega una migración con `dotnet ef migrations add NombreDelCambio --output-dir Migraciones`. La aplicación aplica migraciones pendientes al arrancar.
