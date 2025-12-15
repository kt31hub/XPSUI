using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Python.Runtime;

namespace XPSUI
{
    public class XpsEngine
    {
        // --- データ保持エリア ---
        public List<string> Tags { get; private set; } = new List<string>();
        public List<double[]> XData { get; private set; } = new List<double[]>();
        public List<double[]> YData { get; private set; } = new List<double[]>();

        // 設定ファイル名
        private const string PathConfigFileName = "path.json";
        private const string ShiftSettingFileName = "shift_setting.json";

        // ---------------------------------------------------------
        // 1. Python初期化・設定関連
        // ---------------------------------------------------------
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
                if (!silent) throw ex; // 手動時はエラーを上に投げる
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

        // ---------------------------------------------------------
        // 2. データ読み込み (Python連携)
        // ---------------------------------------------------------
        public void LoadData(string filePath)
        {
            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;

                // Pythonパス設定
                sys.path.append(exeDir);
                sys.path.append(Path.Combine(exeDir, "python")); // pythonフォルダ

                dynamic xpsasc = Py.Import("XPSASC");
                dynamic result = xpsasc.load_allspe(filePath);

                var pyTags = result[0];
                var pyX = result[1];
                var pyY = result[2];

                // データを一新
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

        // ---------------------------------------------------------
        // 3. 帯電補正 (Python連携)
        // ---------------------------------------------------------
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
                dynamic sys = Py.Import("sys");
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                sys.path.append(exeDir);
                sys.path.append(Path.Combine(exeDir, "python"));

                dynamic xpscal = Py.Import("XPSCAL");

                dynamic result = xpscal.shift(
                    Tags, XData, YData,
                    settings.XMin, settings.XMax, settings.ShiftPeakCenter
                );

                var pyX = result[0];
                var pyY = result[1];

                // データを更新
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
        // ---------------------------------------------------------
        // 4. 解析実行 (Atomic % + Peak Fitting)
        // ---------------------------------------------------------
        public List<AnalysisResultRow> RunAnalysis()
        {
            var rows = new List<AnalysisResultRow>();

            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;

                // ★あえてチェックせずに追記する (これでエラー回避)
                sys.path.append(exeDir);
                sys.path.append(Path.Combine(exeDir, "python"));

                dynamic xpscal = Py.Import("XPSCAL");
                dynamic xpsfit = Py.Import("XPSFIT");
                dynamic json = Py.Import("json");
                dynamic np = Py.Import("numpy"); // NumPy必須

                // --- 1. Atomic % 計算 ---
                string rsfPath = GetConfigPath("RSF.json");
                List<double> atomicPercents = new List<double>();

                if (File.Exists(rsfPath))
                {
                    string rsfContent = File.ReadAllText(rsfPath);
                    dynamic rsfList = json.loads(rsfContent);

                    dynamic apResult = xpscal.atomic_percent(XData, YData, Tags, rsfList);

                    long count = apResult.__len__();
                    for (int i = 0; i < count; i++)
                    {
                        atomicPercents.Add((double)apResult[i].As<double>());
                    }
                }
                else
                {
                    for (int i = 0; i < Tags.Count; i++) atomicPercents.Add(0.0);
                }

                // --- 2. ピークフィッティング & 結果結合 ---
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
                            if (p["level"].ToString() == tag)
                            {
                                targetConfig.append(p);
                            }
                        }

                        if (targetConfig.__len__() > 0)
                        {
                            try
                            {
                                // 1. バックグラウンド計算
                                dynamic bgRes = xpscal.shirley_baseline(XData[i], YData[i]);
                                dynamic yBg = bgRes[0];

                                // 2. NumPy計算 (ここが修正ポイント！)
                                dynamic yRaw = np.array(YData[i]);
                                dynamic yBase = np.array(yBg);

                                // 引き算: np.subtractを使うと確実
                                dynamic yPure = np.subtract(yRaw, yBase);

                                // ★以前のエラー箇所: yPure[yPure < 0] = 0;
                                // ↓
                                // ★修正後: np.maximum を使う
                                yPure = np.maximum(yPure, 0.0);

                                // 3. Fitting実行
                                dynamic fitRes = xpsfit.perform_fitting(XData[i], yPure, targetConfig, false);

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
                            catch (Exception)
                            {
                                // エラー時はスキップして続行
                            }
                        }
                    }
                }
            }
            return rows;
        }

        // ---------------------------------------------------------
        // ヘルパー
        // ---------------------------------------------------------
        private string GetConfigPath(string fileName)
        {
            string myDoc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = Path.Combine(myDoc, "XPSUI_setting");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return Path.Combine(folder, fileName);
        }

        // 内部クラス
        private class PathConfig { public string PythonDllPath { get; set; } = ""; }
        public class AnalysisResultRow
        {
            public string Spectrum { get; set; }   // スペクトル名 (例: C1s)
            public string Component { get; set; }  // 成分名 (例: C-C, Total)
            public string Position { get; set; }   // ピーク位置 (eV)
            public string FWHM { get; set; }       // 半値幅
            public string Area { get; set; }       // 面積
            public string AreaRatio { get; set; }  // 面積比 (%)
            public string AtomicPer { get; set; }  // 原子数濃度 (%)
        }
    }
}