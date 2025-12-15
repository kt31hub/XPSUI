using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Python.Runtime;

namespace XPSUI
{
    public class XpsEngine
    {
        public List<string> Tags { get; private set; } = new List<string>();
        public List<double[]> XData { get; private set; } = new List<double[]>();
        public List<double[]> YData { get; private set; } = new List<double[]>();

        private const string PathConfigFileName = "path.json";
        private const string ShiftSettingFileName = "shift_setting.json";

        // --- 1. Python初期化 ---
        public bool TryInitializePython(bool silent)
        {
            if (PythonEngine.IsInitialized) return true;
            try
            {
                string dllPath = LoadPythonPath();
                if (!string.IsNullOrEmpty(dllPath) && File.Exists(dllPath))
                {
                    Runtime.PythonDLL = dllPath;
                }
                PythonEngine.Initialize();
                PythonEngine.BeginAllowThreads();
                return true;
            }
            catch (Exception ex)
            {
                if (!silent) throw ex;
                return false;
            }
        }

        public void SavePythonPath(string path)
        {
            var data = new PathConfig { PythonDllPath = path };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetConfigPath(PathConfigFileName), json);
        }

        private string LoadPythonPath()
        {
            try
            {
                string path = GetConfigPath(PathConfigFileName);
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize<PathConfig>(json);
                    return data?.PythonDllPath;
                }
            }
            catch { }
            return null;
        }

        // --- ヘルパー: パス設定と計算関数定義 ---
        private void PreparePythonEnvironment()
        {
            // 1. パス設定 (ここは以前動いていた C#方式 に戻す！)
            dynamic sys = Py.Import("sys");
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string pyDir = Path.Combine(exeDir, "python");

            sys.path.append(exeDir);
            sys.path.append(pyDir);

            // 2. 計算用関数だけを定義する (文字列処理の問題が起きないように独立させる)
            // これで C# 側の array index エラーを回避します
            string calcCode = @"
import numpy as np
def calc_pure_signal(y_raw, y_bg):
    y_r = np.array(y_raw)
    y_b = np.array(y_bg)
    y_pure = y_r - y_b
    y_pure[y_pure < 0] = 0.0
    return y_pure
";
            PythonEngine.Exec(calcCode);
        }

        // --- 2. データ読み込み ---
        public void LoadData(string filePath)
        {
            using (Py.GIL())
            {
                PreparePythonEnvironment(); // ★修正版呼び出し

                dynamic xpsasc = Py.Import("XPSASC");
                dynamic result = xpsasc.load_allspe(filePath);

                var pyTags = result[0];
                var pyX = result[1];
                var pyY = result[2];

                Tags.Clear();
                XData.Clear();
                YData.Clear();

                long count = pyTags.__len__();
                for (int i = 0; i < count; i++)
                {
                    Tags.Add(pyTags[i].ToString());
                    XData.Add((double[])pyX[i].As<double[]>());
                    YData.Add((double[])pyY[i].As<double[]>());
                }
            }
        }

        // --- 3. 帯電補正 ---
        public AppSettings LoadShiftSettings()
        {
            string path = GetConfigPath(ShiftSettingFileName);
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch { }
            }
            return new AppSettings();
        }

        public void ApplyShift(AppSettings settings)
        {
            using (Py.GIL())
            {
                PreparePythonEnvironment();

                dynamic xpscal = Py.Import("XPSCAL");
                dynamic result = xpscal.shift(
                    Tags, XData, YData,
                    settings.XMin, settings.XMax, settings.ShiftPeakCenter
                );

                var pyX = result[0];
                var pyY = result[1];

                XData.Clear();
                YData.Clear();

                long count = pyX.__len__();
                for (int i = 0; i < count; i++)
                {
                    XData.Add((double[])pyX[i].As<double[]>());
                    YData.Add((double[])pyY[i].As<double[]>());
                }
            }
        }

        // --- 4. 解析実行 ---
        public List<AnalysisResultRow> RunAnalysis()
        {
            var rows = new List<AnalysisResultRow>();
            if (Tags.Count == 0) return rows;

            using (Py.GIL())
            {
                PreparePythonEnvironment();

                // 定義した関数を使うために __main__ をインポート
                dynamic main = Py.Import("__main__");

                dynamic xpscal = Py.Import("XPSCAL");
                dynamic xpsfit = Py.Import("XPSFIT");
                dynamic json = Py.Import("json");

                // --- Atomic % ---
                string rsfPath = GetConfigPath("RSF.json");
                List<double> atomicPercents = new List<double>();

                if (File.Exists(rsfPath))
                {
                    string rsfContent = File.ReadAllText(rsfPath);
                    dynamic rsfList = json.loads(rsfContent);
                    dynamic apResult = xpscal.atomic_percent(XData, YData, Tags, rsfList);
                    long count = apResult.__len__();
                    for (int i = 0; i < count; i++) atomicPercents.Add((double)apResult[i].As<double>());
                }
                else
                {
                    for (int i = 0; i < Tags.Count; i++) atomicPercents.Add(0.0);
                }

                // --- Fitting ---
                string peakFitPath = GetConfigPath("peakfit.json");
                dynamic peakDb = null;
                if (File.Exists(peakFitPath))
                {
                    peakDb = json.loads(File.ReadAllText(peakFitPath));
                }

                for (int i = 0; i < Tags.Count; i++)
                {
                    string tag = Tags[i];
                    double atVal = atomicPercents[i];

                    rows.Add(new AnalysisResultRow
                    {
                        Spectrum = tag,
                        Component = "(Total)",
                        Position = "-",
                        FWHM = "-",
                        Area = "-",
                        AreaRatio = "100.0",
                        AtomicPer = atVal > 0 ? $"{atVal:F2}" : "-"
                    });

                    if (peakDb != null)
                    {
                        dynamic targetConfig = new PyList();
                        foreach (dynamic p in peakDb)
                        {
                            if (p["level"].ToString() == tag) targetConfig.append(p);
                        }

                        if (targetConfig.__len__() > 0)
                        {
                            try
                            {
                                dynamic np = Py.Import("numpy");
                                dynamic x_np = np.array(XData[i]);
                                dynamic y_np = np.array(YData[i]);

                                dynamic bgRes = xpscal.shirley_baseline(x_np, y_np);
                                dynamic yBg = bgRes[0];

                                // ★ここでPython関数を使って安全に計算★
                                dynamic yPure = main.calc_pure_signal(YData[i], yBg);

                                dynamic fitRes = xpsfit.perform_fitting(x_np, yPure, targetConfig, false);

                                if (fitRes != null && fitRes[0] != null)
                                {
                                    dynamic peaks = fitRes[0];
                                    long pCount = peaks.__len__();
                                    for (int k = 0; k < pCount; k++)
                                    {
                                        dynamic p = peaks[k];
                                        rows.Add(new AnalysisResultRow
                                        {
                                            Spectrum = "",
                                            Component = p["name"].ToString(),
                                            Position = $"{p["center"]:F2}",
                                            FWHM = $"{p["fwhm"]:F2}",
                                            Area = $"{p["area"]:F0}",
                                            AreaRatio = $"{p["ratio"]:F1}",
                                            AtomicPer = ""
                                        });
                                    }
                                }
                            }
                            catch (Exception) { }
                        }
                    }
                }
            }
            return rows;
        }

        private string GetConfigPath(string fileName)
        {
            string myDoc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = Path.Combine(myDoc, "XPSUI_setting");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return Path.Combine(folder, fileName);
        }

        private class PathConfig { public string PythonDllPath { get; set; } = ""; }
        public class AnalysisResultRow
        {
            public string Spectrum { get; set; }
            public string Component { get; set; }
            public string Position { get; set; }
            public string FWHM { get; set; }
            public string Area { get; set; }
            public string AreaRatio { get; set; }
            public string AtomicPer { get; set; }
        }
    }
}