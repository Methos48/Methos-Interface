METHOS U EDITOR
================

Carpetas de trabajo
- Documentos\Methos U Editor\u completo
- Documentos\Methos U Editor\u por partes

DESCOMPILAR
1. Agrega uno o varios archivos .u.
2. Selecciónalos en la lista.
3. Pulsa DESCOMPILAR.
4. Las clases .uc se guardan en "u por partes", dentro de una carpeta con el nombre del paquete.
5. El programa verifica la estructura de las fuentes y registra las reparaciones de llaves que pueda demostrar con seguridad.
   La lectura/descompilación general de UnrealScript todavía utiliza UELib como lector de compatibilidad; el descompilador nativo completo sigue en desarrollo.

COMPILAR
- La acción COMPILAR ya no ejecuta ni solicita UCC.exe.
- Utiliza el motor nativo Methos para reconstruir el paquete y reemplazar bytecode de funciones existentes.
- En esta etapa inicial, el compilador solo admite funciones cuyo cuerpo contiene una única instrucción return con un literal compatible (enteros, flotantes, booleanos, cadenas, nombres, self/none o return;).
- Si una clase, función o expresión queda fuera de ese subconjunto, la compilación se detiene y el .u de destino no se reemplaza.
- Todavía no se admiten clases nuevas ni el lenguaje UnrealScript completo (variables, llamadas, asignaciones, condiciones, bucles, estados completos y propiedades).
- El programa verifica el paquete reconstruido con el analizador nativo y vuelve a abrirlo con UELib antes de guardarlo.
- Si ya existe un archivo de destino, se crea un backup.

IMPORTANTE
Este motor está en desarrollo y aún no es un compilador/descompilador completo para Lineage II Interlude. No uses el resultado como reemplazo de tu Interface.u de juego hasta que las pruebas específicas del cliente estén terminadas. Conserva siempre una copia original.
