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
                sys.path.append(exeDir);
                sys.path.append(Path.Combine(exeDir, "python"));

                dynamic xpscal = Py.Import("XPSCAL");
                dynamic xpsfit = Py.Import("XPSFIT");
                dynamic json = Py.Import("json");

                // --- 1. Atomic % 計算 ---
                string rsfPath = GetConfigPath("RSF.json");
                List<double> atomicPercents = new List<double>();

                if (File.Exists(rsfPath))
                {
                    string rsfContent = File.ReadAllText(rsfPath);
                    dynamic rsfList = json.loads(rsfContent);

                    // Atomic%計算 (Python)
                    dynamic apResult = xpscal.atomic_percent(XData, YData, Tags, rsfList);

                    long count = apResult.__len__();
                    for (int i = 0; i < count; i++)
                    {
                        atomicPercents.Add((double)apResult[i].As<double>());
                    }
                }
                else
                {
                    // RSFがない場合は全部0.0で埋める
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
                    double atVal = atomicPercents[i]; // このタグのAtomic%

                    // まず「全体(Total)」の行を追加
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

                    // --- ピークフィッティング実行 ---
                    if (peakDb != null)
                    {
                        // このタグ用の設定を抽出 (Pythonのリスト内包表記的な処理)
                        dynamic targetConfig = new PyList();
                        foreach (dynamic p in peakDb)
                        {
                            if (p["level"].ToString() == tag)
                            {
                                targetConfig.append(p);
                            }
                        }

                        // 設定がある場合のみフィッティング
                        if (targetConfig.__len__() > 0)
                        {
                            // 1. バックグラウンド計算 & 除去
                            // shirley_baseline(x, y) -> y_base, xmin, xmax
                            dynamic bgRes = xpscal.shirley_baseline(XData[i], YData[i]);
                            dynamic yBg = bgRes[0];

                            // Pythonのnumpy配列同士の引き算はC#からは直接しにくいので、
                            // 計算済みの y_pure を得るか、Python側で処理させるのが楽ですが、
                            // ここでは簡易的に「y - yBg」を想定します。
                            // 正確にはXPSFIT.perform_fittingに渡す前に引き算が必要です。
                            // ★簡略化のため、Pythonで一時的に計算させます
                            dynamic np = Py.Import("numpy");
                            dynamic yPure = np.array(YData[i]) - np.array(yBg);

                            // マイナス値を0にクリップ
                            yPure[yPure < 0] = 0;

                            // 2. Fitting実行
                            // perform_fitting(x, y, config, verbose) -> (peaks, y_total)
                            dynamic fitRes = xpsfit.perform_fitting(XData[i], yPure, targetConfig, false);

                            if (fitRes != null && fitRes[0] != null)
                            {
                                dynamic peaks = fitRes[0];
                                long pCount = peaks.__len__();

                                for (int k = 0; k < pCount; k++)
                                {
                                    dynamic p = peaks[k];
                                    // 結果を行に追加
                                    rows.Add(new AnalysisResultRow
                                    {
                                        Spectrum = "", // 2行目以降は空欄で見やすく
                                        Component = p["name"].ToString(),
                                        Position = $"{p["center"]:F2}",
                                        FWHM = $"{p["fwhm"]:F2}",
                                        Area = $"{p["area"]:F0}",
                                        AreaRatio = $"{p["ratio"]:F1}",
                                        AtomicPer = "" // 成分ごとのAtomic%は定義が難しいので空欄
                                    });
                                }
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