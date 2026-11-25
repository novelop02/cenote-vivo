// Archivo temporal de verificación: se borra al terminar.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CenoteVivo.Herramientas;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace CenoteVivo.EditorTools
{
    [InitializeOnLoad]
    static class DisparadorTemporal
    {
        const string Resultado = "Temp/CenoteVivo_resultado.txt";
        static double siguiente;
        static MedidorRendimiento medidor;
        static string faseAnterior = "";
        static int inicioFase = -1;
        static StringBuilder informe;

        static DisparadorTemporal() => EditorApplication.update += Revisar;

        static void Revisar()
        {
            string etiqueta = SessionState.GetString("cv_medir", "");
            if (etiqueta != "" && EditorApplication.isPlaying)
            {
                if (medidor == null && Time.frameCount > 5)
                {
                    ProfilerDriver.ClearAllFrames();
                    ProfilerDriver.profileEditor = false;
                    ProfilerDriver.enabled = true;
                    informe = new StringBuilder();
                    faseAnterior = "";
                    medidor = new GameObject("[Medidor]").AddComponent<MedidorRendimiento>();
                    medidor.Configurar(etiqueta, Path.GetFullPath("Temp"));
                }
                else if (medidor != null)
                {
                    var fase = medidor.FaseActual;
                    if (fase != faseAnterior)
                    {
                        if (faseAnterior == "") inicioFase = ProfilerDriver.lastFrameIndex;
                        else Analizar(faseAnterior, inicioFase, ProfilerDriver.lastFrameIndex);
                        faseAnterior = fase;
                    }
                    if (medidor.Terminado)
                    {
                        File.WriteAllText($"Temp/operaciones_{etiqueta}.txt", informe.ToString());
                        File.WriteAllText(Resultado, "MEDICION OK " + medidor.Resumen);
                        ProfilerDriver.enabled = false;
                        SessionState.SetString("cv_medir", "");
                        medidor = null;
                        EditorApplication.isPlaying = false;
                    }
                }
                return;
            }

            if (EditorApplication.timeSinceStartup < siguiente) return;
            siguiente = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying) return;

            if (File.Exists("Temp/CenoteVivo_construir.txt"))
            {
                File.Delete("Temp/CenoteVivo_construir.txt");
                try { CenoteVivoBuilder.ConstruirEscenas(); File.WriteAllText(Resultado, "OK"); }
                catch (Exception e) { File.WriteAllText(Resultado, "ERROR " + e); }
            }
            else if (File.Exists("Temp/CenoteVivo_medir.txt"))
            {
                var nombre = File.ReadAllText("Temp/CenoteVivo_medir.txt").Trim();
                File.Delete("Temp/CenoteVivo_medir.txt");
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/CenoteVivo/Scenes/CenoteVivo.unity");
                SessionState.SetString("cv_medir", nombre);
                medidor = null;
                EditorApplication.isPlaying = true;
            }
        }

        static void Analizar(string fase, int desde, int hasta)
        {
            desde = Math.Max(desde + 1, ProfilerDriver.firstFrameIndex);
            hasta = Math.Min(hasta, ProfilerDriver.lastFrameIndex);
            var propio = new Dictionary<string, double>();
            var bloques = new Dictionary<string, double>();
            int cuadros = 0;
            var hijos = new List<int>();
            for (int f = desde; f <= hasta; f++)
            {
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                                                                       HierarchyFrameDataView.columnTotalTime, false))
                {
                    if (v == null || !v.valid) continue;
                    v.GetItemChildren(v.GetRootItemID(), hijos);
                    int loop = hijos.FirstOrDefault(id => v.GetItemName(id) == "PlayerLoop");
                    if (loop == 0) continue;
                    cuadros++;
                    var directos = new List<int>();
                    v.GetItemChildren(loop, directos);
                    foreach (var id in directos)
                        Sumar(bloques, v.GetItemName(id), v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime));
                    var pila = new Stack<int>();
                    pila.Push(loop);
                    var tmp = new List<int>();
                    while (pila.Count > 0)
                    {
                        int id = pila.Pop();
                        Sumar(propio, v.GetItemName(id), v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime));
                        v.GetItemChildren(id, tmp);
                        foreach (var c in tmp) pila.Push(c);
                    }
                }
            }
            informe.AppendLine($"=== {fase} ({cuadros} cuadros analizados del Profiler)");
            if (cuadros == 0) return;
            informe.AppendLine("  Bloques de PlayerLoop (ms):");
            foreach (var kv in bloques.OrderByDescending(k => k.Value).Take(3))
                informe.AppendLine($"    {kv.Value / cuadros:0.000}  {kv.Key}");
            informe.AppendLine("  Operaciones más costosas (ms):");
            foreach (var kv in propio.OrderByDescending(k => k.Value).Take(6))
                informe.AppendLine($"    {kv.Value / cuadros:0.000}  {kv.Key}");
        }

        static void Sumar(Dictionary<string, double> d, string k, double v)
        {
            d.TryGetValue(k, out var a);
            d[k] = a + v;
        }
    }
}
