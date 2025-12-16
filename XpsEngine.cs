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
            File.WriteAllText(GetWritableConfigPath(PathConfigFileName), json);
        }

        private string LoadPythonPath()
        {
            try
            {
                string path = FindConfigPath(PathConfigFileName);
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

        // --- ヘルパー: パス設定 ---
        private void PreparePythonEnvironment()
        {
            dynamic sys = Py.Import("sys");
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string pyDir = Path.Combine(exeDir, "python");
            sys.path.append(exeDir);
            sys.path.append(pyDir);
        }

        // --- 2. データ読み込み ---
        public void LoadData(string filePath)
        {
            using (Py.GIL())
            {
                PreparePythonEnvironment();
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
            string path = FindConfigPath(ShiftSettingFileName);
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

                dynamic xpscal = Py.Import("XPSCAL");
                dynamic xpsfit = Py.Import("XPSFIT");
                dynamic json = Py.Import("json");
                dynamic np = Py.Import("numpy");

                // ① Atomic % 計算
                var atomicPercents = CalculateAtomicPercents(xpscal, json);

                // PeakDBロード
                dynamic peakDb = null;
                string peakFitPath = FindConfigPath("peakfit.json");
                if (File.Exists(peakFitPath))
                {
                    peakDb = json.loads(File.ReadAllText(peakFitPath));
                }

                // ② タグごとに処理
                for (int i = 0; i < Tags.Count; i++)
                {
                    string tag = Tags[i];
                    double atVal = (i < atomicPercents.Count) ? atomicPercents[i] : 0.0;

                    // Total行を追加
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

                    // スキップ条件 (Python仕様準拠)
                    if (i == 0) continue;          // Survey (Su1s) はスキップ
                    if (tag == "CuLMM") continue;  // CuLMM はスキップ
                    if (peakDb == null) continue;

                    // Fitting実行
                    var fitRows = PerformPeakFitting(i, peakDb, xpscal, xpsfit, np);
                    rows.AddRange(fitRows);
                }
            }
            return rows;
        }

        // --- ヘルパー: Atomic % 計算 ---
        private List<double> CalculateAtomicPercents(dynamic xpscal, dynamic json)
        {
            var results = new List<double>();
            try
            {
                string rsfPath = FindConfigPath("RSF.json");
                dynamic rsfList = new PyList();

                if (File.Exists(rsfPath))
                {
                    try
                    {
                        string rsfContent = File.ReadAllText(rsfPath);
                        rsfList = json.loads(rsfContent);
                    }
                    catch { }
                }

                dynamic apResult = xpscal.atomic_percent(XData, YData, Tags, rsfList);

                long count = apResult.__len__();
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        double val = (double)apResult[i].As<double>();
                        if (double.IsNaN(val) || double.IsInfinity(val)) val = 0.0;
                        results.Add(val);
                    }
                    catch
                    {
                        results.Add(0.0);
                    }
                }
            }
            catch
            {
                results.Clear();
                for (int i = 0; i < Tags.Count; i++) results.Add(0.0);
            }
            return results;
        }

        // --- ヘルパー: Peak Fitting ---
        private List<AnalysisResultRow> PerformPeakFitting(int index, dynamic peakDb, dynamic xpscal, dynamic xpsfit, dynamic np)
        {
            var fitRows = new List<AnalysisResultRow>();
            try
            {
                string tag = Tags[index];
                dynamic targetConfig = new PyList();
                foreach (dynamic p in peakDb)
                {
                    if (p["level"].ToString() == tag) targetConfig.append(p);
                }

                if (targetConfig.__len__() > 0)
                {
                    // 1. バックグラウンド
                    dynamic bgRes = xpscal.shirley_baseline(XData[index], YData[index]);
                    dynamic yBg = bgRes[0];

                    // 2. NumPy計算 & マイナスカット
                    dynamic yRaw = np.array(YData[index]);
                    dynamic yBase = np.array(yBg);
                    dynamic yPure = np.subtract(yRaw, yBase);
                    yPure = np.maximum(yPure, 0.0);

                    // 3. Fitting
                    dynamic fitRes = xpsfit.perform_fitting(XData[index], yPure, targetConfig, false);

                    if (fitRes != null && fitRes[0] != null)
                    {
                        dynamic peaks = fitRes[0];
                        long pCount = peaks.__len__();
                        for (int k = 0; k < pCount; k++)
                        {
                            dynamic p = peaks[k];
                            fitRows.Add(new AnalysisResultRow
                            {
                                Spectrum = "",
                                Component = p["name"].ToString(),
                                Position = $"{p["center"]:F2}",
                                FWHM = $"{p["fwhm"]:F2}",
                                Area = $"{p["area"]:F1}",
                                AreaRatio = $"{p["ratio"]:F1}",
                                AtomicPer = ""
                            });
                        }
                    }
                }
            }
            catch { }
            return fitRows;
        }

        // --- パス解決ロジック (ここを強化！) ---
        private string FindConfigPath(string fileName)
        {
            // 1. システム標準のドキュメントフォルダ (OneDriveの場合あり)
            string myDoc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string path1 = Path.Combine(myDoc, "XPSUI_setting", fileName);
            if (File.Exists(path1)) return path1;

            // 2. ユーザープロファイル直下の Documents (ローカル強制)
            // C:\Users\kaito\Documents\XPSUI_setting\...
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string path2 = Path.Combine(userProfile, "Documents", "XPSUI_setting", fileName);
            if (File.Exists(path2)) return path2;

            // 3. EXEと同じフォルダ
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string path3 = Path.Combine(exeDir, fileName);
            if (File.Exists(path3)) return path3;

            // 見つからなかった場合は標準パスを返す
            return path1;
        }

        private string GetWritableConfigPath(string fileName)
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