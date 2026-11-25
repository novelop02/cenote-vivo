using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CenoteVivo.EditorTools
{
    /// <summary>
    /// Entradas para línea de comandos (Unity -batchmode -executeMethod ...).
    ///   CenoteVivo.EditorTools.CenoteVivoBatch.ConstruirEscenas  → configura Android y arma las 3 escenas
    ///   CenoteVivo.EditorTools.CenoteVivoBatch.CompilarAPK       → genera Builds/CenoteVivoAR.apk
    /// También están en el menú "Cenote Vivo".
    /// </summary>
    public static class CenoteVivoBatch
    {
        const string RutaAPK = "Builds/CenoteVivoAR.apk";

        public static void ConstruirEscenas()
        {
            CenoteVivoBuilder.ConfigurarAndroid();
            CenoteVivoBuilder.ConstruirEscenas();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Arma las escenas y compila el APK en una sola pasada.</summary>
        public static void ConstruirYCompilar()
        {
            ConstruirEscenas();
            CompilarAPK();
        }

        [MenuItem("Cenote Vivo/3. Compilar APK (AR)", priority = 3)]
        public static void CompilarAPK()
        {
            CenoteVivoBuilder.ConfigurarAndroid();
            var escenas = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(RutaAPK));
            var opciones = new BuildPlayerOptions
            {
                scenes = escenas,
                locationPathName = RutaAPK,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            var reporte = BuildPipeline.BuildPlayer(opciones);
            var r = reporte.summary;
            Debug.Log($"[Cenote Vivo] Compilación: {r.result}, {r.totalSize / (1024f * 1024f):0.0} MB, {r.totalErrors} errores → {RutaAPK}");
            if (Application.isBatchMode) EditorApplication.Exit(r.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
