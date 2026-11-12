METHOS
================
Editor unificado para paquetes de interfaz de Lineage II Interlude (.u y .xdat).

INSTALACIÓN
-----------
El proyecto genera un único instalador de Windows x64: Methos-Setup.exe.
Instala la aplicación principal, el módulo XDAT integrado en la ventana de Methos,
el runtime UCC de Interlude y los componentes necesarios. No hace falta instalar
.NET, Python ni UCC por separado.

INTERFAZ
--------
Al abrir Methos se muestran dos módulos:
- ARCHIVOS U: descompilar y compilar paquetes Unreal .u.
- ARCHIVOS XDAT: trabajar con archivos .xdat desde la misma ventana principal.

ESTRUCTURA DE TRABAJO
---------------------
Methos crea junto al ejecutable esta estructura:

Compilados/
  U/
    Archivo/       -> paquetes .u importados y paquete compilado actual
    Decopilado/    -> fuentes .uc extraídas y editables
  XDAT/
    Archivo/       -> archivos .xdat importados y paquete compilado actual
    Decopilado/    -> ventanas y accesos directos XML extraídos y editables

FLUJO DE TRABAJO
----------------
1. IMPORTAR
   - En ARCHIVOS U, pulsa CARGAR ARCHIVO y selecciona el .u.
   - En ARCHIVOS XDAT, importa el .xdat desde su módulo.
   - Methos copia el archivo a Compilados/U/Archivo o Compilados/XDAT/Archivo.
     El archivo que seleccionaste en su ubicación original no se modifica.

2. DESCOMPILAR / DECOPILAR
   - Selecciona el paquete importado y ejecuta DESCOMPILAR.
   - Methos limpia la carpeta Decopilado del módulo antes de extraer.
   - El resultado nuevo queda en Compilados/U/Decopilado o
     Compilados/XDAT/Decopilado. El contenido anterior de Archivo no se borra.

3. EDITAR
   - Abre Decopilado desde Methos.
   - Puedes modificar los .uc o .xml con tu editor externo habitual.
   - Methos no necesita que vuelvas a importar los archivos editados: al compilar
     lee el contenido que exista en Decopilado en ese momento.

4. COMPILAR
   - Selecciona el paquete base y pulsa COMPILAR.
   - Methos valida el resultado antes de publicarlo.
   - Guarda copias de seguridad del contenido anterior de Archivo.
   - Sustituye el contenido de Archivo por el nuevo paquete compilado.
   - No borra Decopilado: tus fuentes editadas permanecen disponibles.

SEGURIDAD DE LOS ARCHIVOS
-------------------------
- Los archivos originales que están fuera de Compilados nunca se modifican.
- Una extracción nueva limpia solamente Decopilado; no borra el paquete fuente.
- Una compilación se prepara y valida en un archivo temporal antes de sustituir
  Archivo.
- Si falla la publicación de un paquete .u o .xdat, Methos intenta restaurar el
  contenido anterior desde una copia independiente de recuperación.
- Si la restauración automática también falla, la copia de recuperación se
  conserva para permitir una recuperación posterior.
- Los respaldos U se guardan en:
  %LOCALAPPDATA%\Methos U Editor\Backups
- Los respaldos XDAT se guardan en:
  %LOCALAPPDATA%\Methos\Backups\XDAT
- La caché de fuentes U se guarda fuera de las carpetas de trabajo, asociada al
  SHA-256 del paquete correspondiente.

COMPILACIÓN DE ARCHIVOS U
-------------------------
Para Interface.u de Lineage II Interlude se utiliza el compilador UCC incluido
en el instalador. Se incluyen 169 fuentes .uc de referencia y el runtime
necesario; no hay que descargar ni configurar UCC manualmente.

Las pruebas automatizadas verifican la compilación de las clases, la estructura
del paquete, las tablas Name/Import/Export y un cambio real de bytecode en una
función de prueba. El paquete recompilado no tiene por qué ser idéntico byte por
byte al original: UCC vuelve a serializar el paquete.

Para otros paquetes .u, el motor nativo de Methos admite actualmente un
subconjunto limitado de UnrealScript. No debe interpretarse como compatibilidad
completa con todas las clases o construcciones del lenguaje.

COMPILACIÓN DE ARCHIVOS XDAT
----------------------------
XDAT extrae las ventanas y accesos directos XML y reconstruye el paquete usando
el contenido actual de Decopilado. Las pruebas incluyen modificar un XML,
recompilarlo y comprobar que el nuevo valor queda dentro del XDAT.

Methos reconoce variantes documentadas de cifrado Lineage II y utiliza el motor
integrado para los protocolos que tiene soportados. Si el cifrado no se reconoce,
la versión no está soportada o no se puede verificar el resultado, el proceso se
detiene sin modificar el original. No se debe asumir compatibilidad con claves
privadas o cifrados personalizados no identificados.

VALIDACIÓN Y ALCANCE
-------------------
Las compilaciones automatizadas comprueban:
- Instalación silenciosa y arranque de Methos.
- Arranque del módulo XDAT integrado.
- Presencia del motor de cifrado XDAT y runtime UCC.
- Creación de las cuatro carpetas Compilados.
- Compilación de las fuentes UCC y pruebas de modificación/reconstrucción.
- Extracción, modificación, reconstrucción y recuperación segura de XDAT.

Estas pruebas automáticas no sustituyen una comprobación final dentro del cliente
real de Lineage II Interlude. Antes de usar un paquete modificado en el juego,
conserva el archivo original y valida el resultado en una copia de prueba del
cliente.
