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
- Operadores y Unidad Vehicular con alta, búsqueda y edición desde la ficha del permisionario. El encabezado muestra el nombre y el número económico del titular.
- Fotografía en las fichas de permisionario, operador y unidad. La carga admite JPG y PNG de hasta 5 MB después de guardar el registro. Las fotografías se guardan en `fotografias` dentro del repositorio privado y se incluyen en su respaldo.
- Siete tipos de documentos del formulario SCAN. Vista previa de carga y apertura de versiones históricas. La versión con numeración más alta es la actual. En PostgreSQL se guardan metadatos. Los PDF se escriben en subcarpetas por ficha, con una clave distinta por versión.
- Usuarios independientes. Consulta puede leer fichas y PDF. Captura puede modificarlos. Administración puede configurar cuentas y consultar la bitácora. Tres intentos fallidos bloquean hasta desbloqueo administrativo. Los cambios de credencial o perfil invalidan sesiones.
- `domicilios.json` incluye una muestra. Sustituye sus filas por el catálogo autorizado de estado, municipio o alcaldía, colonia y código postal. Cada código postal asociado a una colonia ocupa una fila. Reinicia la aplicación para recargar el catálogo.
- La base comienza vacía. La importación de SQL Server y los PDF históricos, las cesiones, sustituciones, captura directa desde cámara y carátula quedan para sus módulos respectivos.

## Emitir y verificar carátulas

En la ficha de **Permisionarios**, abre **Carátula**. Para emitir, la ficha debe estar activa y tener una fotografía. Cuando haya unidades activas, selecciona cuál irá en el documento. Si el permisionario carece de unidades activas, los datos del vehículo aparecen como **S/D**. Los perfiles que pueden editar Permisionarios pueden emitir. Los perfiles que pueden consultar la ficha pueden descargar emisiones previas.

El PDF tamaño carta conserva el folio, la fotografía y los datos de la fecha de emisión. Se guarda en el directorio privado `caratulas` dentro del repositorio de archivos. PostgreSQL conserva los metadatos, la huella SHA-256 del original y la huella del código aleatorio de verificación. Respalda ese directorio junto con los demás documentos y PostgreSQL.

Para escanear el QR desde otro equipo o un teléfono, configura la URL HTTPS real del sistema, accesible desde esos dispositivos, **antes de emitir documentos**. En un servidor se configura mediante `Caratula__UrlBase`. Para desarrollo en Windows se puede usar `dotnet user-secrets set "Caratula:UrlBase" "https://dominio-institucional.example"` con la URL real. El valor de desarrollo por defecto es `http://localhost:5080`, que solo sirve para verificar en el mismo equipo. Los QR emitidos conservan la URL original.

El verificador público muestra el folio, la fecha y si el titular o la unidad está dado de baja. Al seleccionar un PDF en el verificador, el navegador calcula su huella localmente y verifica si coincide con el archivo emitido. En papel, coteja los datos y la fotografía con el PDF original, accesible mediante una sesión autorizada, porque un QR puede copiarse de una impresión a otra.

## Bajas e historial de expedientes

Cada permisionario admite hasta **tres operadores activos**. Las altas simultáneas se serializan por permisionario. Dar de baja un operador libera un lugar. Los registros que ya superen el límite se conservan y las nuevas altas se habilitan cuando queden menos de tres operadores activos.

Las bajas de operadores y unidades conservan la ficha, su fotografía y el historial para consulta. Cada alta, edición y baja de permisionario, operador o unidad guarda una versión con fecha y usuario. Consulta las versiones desde **Historial de la ficha**. Los nuevos reemplazos de fotografía también guardan la imagen anterior. Los PDF mantienen su historial en **Documentos**.

Al aplicar la migración, la aplicación guarda una copia inicial de los registros existentes e interpreta como bajas las fechas de baja de operadores que ya se cumplieron. Las versiones sustituidas antes de la actualización se pueden recuperar solo si existen en un respaldo anterior.

## Reportes

Desde el menú lateral **Reportes**, filtra por número económico, agrupación, módulo, estado y rangos inclusivos de alta y baja. Todos los filtros llenados se combinan. Las fechas se presentan en hora de Ciudad de México. Para operadores, se usa la fecha de alta capturada en su ficha cuando está disponible. Para los demás registros se usa el evento de alta del historial. Los registros anteriores sin fecha de alta conocida se muestran como **Sin fecha registrada**.

Los perfiles personalizados requieren **Reportes → Consultar** y el permiso de consulta del módulo cuyos datos se mostrarán.

## Reglas de captura

En fichas de padrón, **Enter** mueve el foco al siguiente campo. En observaciones, **Mayús+Enter** inserta un salto de línea. Los datos de texto del padrón se guardan en mayúsculas. Los teléfonos y correos son opcionales. Los correos se capturan, guardan y muestran con su escritura original.

| Campo de Permisionarios | Tipo y longitud |
|---|---|
| Económico | De 1 a 10 letras A–Z, dígitos o guiones. Conserva ceros iniciales. |
| Nombre y apellidos | Hasta 90 caracteres por campo. Letras con acentos, espacios, puntos, apóstrofos y guiones. El nombre es obligatorio. |
| Agrupación | Selector de 11 opciones. Una agrupación anterior se conserva si ya estaba registrada en la ficha. |
| RFC | 13 caracteres para persona física y 12 para persona moral. Prefijo alfabético fiscal, seis dígitos de fecha y tres caracteres alfanuméricos. |
| Fecha de nacimiento y vigencia | Fecha de calendario. El nacimiento admite fechas hasta el día de captura. |
| Estado, municipio o alcaldía, colonia y código postal | Catálogo en cascada. El código postal proviene del catálogo. |
| Calle | Hasta 150 caracteres, incluidos números y signos. |
| Número de permiso | Hasta 50 letras, dígitos, espacios, punto, diagonal, guion y guion bajo. |
| Teléfonos | Hasta 10 filas con 10 dígitos ASCII por fila. |
| Correos | Hasta 10 filas de 254 caracteres por dirección, con formato de correo. |
| Observaciones | Hasta 1,000 caracteres, con saltos de línea. |
| Fotografía | Archivo JPG o PNG, hasta 5 MB. Se carga después de guardar la ficha. |

La base de RFC sugerida para persona física utiliza nombre, ambos apellidos y fecha de nacimiento. Es una ayuda de captura. Confirma el RFC completo y el sufijo con la constancia fiscal.

En **Operadores**, nombre, apellidos y campos descriptivos admiten hasta 150 caracteres, licencia hasta 50, CURP hasta 18, código postal de catálogo y observaciones hasta 1,000. En **Unidad Vehicular**, modelo, marca, tipo y referencias documentales admiten hasta 150 caracteres, placa hasta 50 y año de tenencia hasta cuatro. Las vigencias se capturan como fechas y las condiciones de seguro, factura, revista, tenencia y sustitución como casillas.

## Definir perfiles

En **Usuarios → Definir perfiles**, selecciona **Nuevo perfil**, escribe su nombre y marca las operaciones en el árbol. Luego vuelve a **Usuarios** y asigna el perfil al crear una cuenta o al cambiar una existente.

Los permisos de cambio incluyen la consulta del módulo. Los módulos de la ficha requieren consultar Permisionarios. Los perfiles del sistema mantienen sus permisos y los perfiles personalizados se guardan en las tablas de Identity. Al guardar cambios en un perfil se invalidan las sesiones de las cuentas asignadas.

## Agregar módulos

Cada módulo declara sus rutas en `Modules/` con un método `Mapear`, registrado desde `Program.cs`. Registra los permisos del módulo en `Modules/Perfiles.cs` y usa esas políticas o `Admin` en las rutas correspondientes y `Acceso.Registrar` para las operaciones que deban quedar en bitácora. `RegistroDb.cs` contiene el modelo. La migración inicial está en `Migraciones/`. Para ampliar el esquema, agrega una migración con `dotnet ef migrations add NombreDelCambio --output-dir Migraciones`. La aplicación aplica migraciones pendientes al arrancar.
